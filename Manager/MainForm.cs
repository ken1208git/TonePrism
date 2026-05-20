using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TonePrism.Manager.Controls;
using TonePrism.Manager.Models;
using TonePrism.Manager.Services;

namespace TonePrism.Manager
{
    /// <summary>
    /// メインフォーム - タブナビゲーション
    /// </summary>
    public partial class MainForm : Form
    {
        private DatabaseManager dbManager;
        private ManagerSessionService _sessionService;
        // (#179 PR3b) Launcher LAN-wide session 検出機構。`manager_sessions` table と非対称の
        // JSON drop folder 方式 (= SPEC §3.8.7)、polling-only で DB write ゼロ。
        private LauncherSessionService _launcherSessionService;

        private GameSectionPanel _gameSectionPanel;
        private StoreSectionPanel _storeSectionPanel;
        private SettingsSectionPanel _settingsSectionPanel;
        private BackupSectionPanel _backupSectionPanel;
        private LogSectionPanel _logSectionPanel;
        private UpdateSectionPanel _updateSectionPanel;

        public MainForm()
        {
            InitializeComponent();
            dbManager = new DatabaseManager();

            _gameSectionPanel = new GameSectionPanel { Dock = DockStyle.Fill };
            _storeSectionPanel = new StoreSectionPanel { Dock = DockStyle.Fill };
            _settingsSectionPanel = new SettingsSectionPanel { Dock = DockStyle.Fill };
            _backupSectionPanel = new BackupSectionPanel { Dock = DockStyle.Fill };
            _logSectionPanel = new LogSectionPanel { Dock = DockStyle.Fill };
            _updateSectionPanel = new UpdateSectionPanel { Dock = DockStyle.Fill };

            // (#170 followup) GameSectionPanel から送られる msg は常に「ゲーム数: N 件」で UpdateStatusBar()
            // の default と同義のため引数なし版に統一。旧 signature `UpdateStatusBar(string)` は廃止。
            _gameSectionPanel.StatusChanged += (msg) => UpdateStatusBar();
            _settingsSectionPanel.DatabaseReset += OnDatabaseReset;
            _backupSectionPanel.DatabaseChanged += OnDatabaseRestored;

            tabGame.Controls.Add(_gameSectionPanel);
            tabStore.Controls.Add(_storeSectionPanel);
            tabBackup.Controls.Add(_backupSectionPanel);
            tabLog.Controls.Add(_logSectionPanel);
            tabUpdate.Controls.Add(_updateSectionPanel);
            tabSettings.Controls.Add(_settingsSectionPanel);

            // (#179) ManagerSessionService の shutdown (self row delete + heartbeat 停止) を form 終了時に発火
            this.FormClosed += MainForm_FormClosed;
            // (#170 followup round 2 review L-3) backup status auto-revert timer の cleanup は Dispose 前に
            // 走らせたいので FormClosing を hook (= FormClosed は Dispose 後で意味薄)。
            this.FormClosing += MainForm_FormClosing;
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // (#170 followup round 2 review L-3 + round 3 review L-1) backup status auto-revert timer の
            // cleanup を **FormClosing** (= Dispose 前、message pump 走行中) で実行。
            // 別 handler が `e.Cancel=true` を set した場合 (= 将来「保存して閉じますか?」confirm dialog 追加時)
            // は timer を生かしたまま return、form が継続生存して `UpdateBackupStatus` 呼出時に新 timer 作成可能。
            //
            // (round 4 review L-3) **handler subscription 順序の前提**: `e.Cancel=true` を判定する handler
            // (= 「保存して閉じますか?」等) は **本 handler より先に subscribe** された場合のみ「timer を
            // 生かしたまま return」が成立する。`+=` の subscription 順で event 発火するため、後付け時は
            // ctor (L57-60) の `FormClosing` hook 行より前に新 handler の `+=` を追加すること。
            if (e.Cancel) return;
            try
            {
                if (_backupStatusClearTimer != null)
                {
                    _backupStatusClearTimer.Stop();
                    _backupStatusClearTimer.Dispose();
                    _backupStatusClearTimer = null;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("[MainForm] FormClosing で _backupStatusClearTimer cleanup 失敗: " + ex.Message);
            }
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            try
            {
                if (_sessionService != null)
                {
                    _sessionService.Shutdown();
                    _sessionService = null;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("[MainForm] FormClosed で ManagerSessionService Shutdown 失敗 (stale cleanup に委ねる): " + ex.Message);
            }

            // (#179 PR3b) LauncherSessionService も同様に Shutdown 呼出 + null 化。polling-only service
            // なので Shutdown は no-op (= 自 PC に self file なし)、対称化のため API は呼ぶ。
            try
            {
                if (_launcherSessionService != null)
                {
                    _launcherSessionService.Shutdown();
                    _launcherSessionService = null;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("[MainForm] FormClosed で LauncherSessionService Shutdown 失敗: " + ex.Message);
            }

        }

        /// <summary>
        /// (#179 / #178 (c)) 編集操作 (DB write) 前に他 PC active session を check して、検出時に
        /// SessionConflictDialog (EditOperation context) を表示する。各 SectionPanel が save handler 直前で
        /// 本 method を呼び出して結果を判定する。
        /// </summary>
        /// <param name="operationDescription">操作内容 (例: "ゲーム編集"、"ストア section 編集")、dialog 文言に embed される。</param>
        /// <returns>
        /// `DialogResult.OK` = 操作続行 (= 検出なし or user が「このまま保存する」選択)、
        /// `DialogResult.Cancel` = 操作中止 (= user が「保存を中止する」選択)。
        /// </returns>
        public DialogResult CheckSessionConflictBeforeWrite(string operationDescription)
        {
            // (#179 round 7 M-1) `_sessionService == null` (= MainForm_Load 完了前) と
            // `_sessionService.IsInitialized == false` (= Initialize 失敗で heartbeat 機構なし) の両方を
            // fail-soft で OK 返却。後者を guard しないと毎 click ごとに `DetectOtherActiveSessions` →
            // `ExecuteWithRetry` (busy_timeout=10000ms × maxRetries=3 = 最大 ~30 秒 block) を空振りし、
            // 全 13 SectionPanel callsite で click 毎 UI freeze + Warn log noise を踏む path だった。
            // Initialize 失敗は SPEC §3.8.5「致命傷、検出機能は以降一切働かない」と整合するため早期 OK。
            if (_sessionService == null || !_sessionService.IsInitialized)
            {
                return DialogResult.OK;
            }
            var others = _sessionService.DetectOtherActiveSessions();

            // (#179 PR3b) Launcher 検出を同時に取得 (= SPEC §3.8.7 merge 表示)。
            // LauncherSessionService が未 init / Initialize 失敗時は空 list で fail-soft、Manager 単独
            // 検出 path に倒れる (= 13 callsite 側 code 変更ゼロ、内部 merge で API contract 据置)。
            IReadOnlyList<LauncherSessionInfo> launcherOthers;
            if (_launcherSessionService != null && _launcherSessionService.IsInitialized)
            {
                launcherOthers = _launcherSessionService.DetectActiveLauncherSessions();
            }
            else
            {
                launcherOthers = new List<LauncherSessionInfo>();
            }

            if (others.Count == 0 && launcherOthers.Count == 0) return DialogResult.OK;
            return SessionConflictDialog.Show(
                this, SessionConflictDialogContext.EditOperation, others, launcherOthers, operationDescription);
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // (#168 brand rename ハード切替 guard) 旧版 (GCTonePrism) install を検出した場合、
            // user が誤って新 zip を旧 install dir に展開した想定。`prism.db` (旧 DB filename) が
            // 残置されているが `toneprism.db` (新 DB filename) は存在しない、というのが旧版痕跡の
            // 一意 marker。fresh install (= 何もない) と区別するため両方を check する。検出時は
            // 警告 + 即時 process kill で、誤った混在状態で先に進む path を物理的に塞ぐ。
            //
            // **設計意図 (= 一過性 / 再利用前提なし)**: 本 guard は #168 brand rename transition 専用、
            // prism.db → toneprism.db 跨ぎ install の fail-safe。trigger 条件 (`prism.db` 残置 +
            // `toneprism.db` 不在) は brand rename specific で、健全 install では永久に発火しない
            // (= v0.12.0 以降では dead code に近い)。将来別 transition (e.g. toneprism.db → 別 schema)
            // では別 marker / 別 guard を新設する設計、本 guard を流用しない。
            //
            // **Environment.Exit(1) 採用根拠**: `Application.Exit()` だと WinForms message loop が
            // queued message を消化してから停止する設計のため、guard return 後も Form Show / 子
            // control 初期化 event が走り得る race window 残存 (= MainForm 未 Visible なので実害は
            // 薄いが defensive)。`Environment.Exit(1)` で process 即時 kill、queued message 一切
            // 処理されない厳密 fence。
            //
            // **dev 環境 false-positive 注意**: PathManager.FindBaseDirectory は priority-1
            // (`toneprism.db` 探索) → priority-2 (`.git` 探索) → priority-3 (Manager+Launcher
            // sibling) の順で BaseDirectory を resolve。開発者が repo root に過去の test artifact
            // `prism.db` を残置していた場合、priority-1 hit せず priority-2 で repo root が選ばれ
            // → 本 guard が trigger する path あり。回避は手動で `prism.db` 削除。MessageBox 文言は
            // 説明的なので開発者が原因に気付きやすい想定 (= 受容範囲、別 issue 化なし)。
            string oldDb = Path.Combine(PathManager.BaseDirectory, "prism.db");
            string newDb = Path.Combine(PathManager.BaseDirectory, "toneprism.db");
            if (File.Exists(oldDb) && !File.Exists(newDb))
            {
                MessageBox.Show(
                    "旧版 (GCTonePrism) の install が検出されました。\n\n" +
                    "新版は完全 rename (GCTonePrism → TonePrism) を伴う破壊的変更を含むため、" +
                    "自動更新できません。\n\n" +
                    "最新の TonePrism_vX.Y.Z.zip を新しいフォルダに解凍し、Install.bat を実行してください。",
                    "旧版検出 — 手動再 install が必要です",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                Environment.Exit(1);
                return;
            }

            // (#178 (b)) アップデート完了直後の起動 (= sentinel あり) は、まず「✓ アップデート完了」
            // MessageBox を表示。sentinel なし path は何もしない。
            // (#178 (c) / #179) 旧「同時起動に関する注意」MessageBox は撤廃、代わりに ManagerSessionService
            // で他 PC で稼働中の Manager を自動検出 → 検出時のみ SessionConflictDialog (Startup context)
            // を表示する設計に移行。session 初期化 + check は **dbManager.InitializeDatabase() で
            // schema migration (v12 → v13) が完了した後** に行う (= round 1 C-1 fix)。旧実装は migration
            // 前に Initialize を呼んでいて、v12 → v13 初回 upgrade で `no such table: manager_sessions`
            // で session 機構が silent に永久 disabled になる bug があった。
            TryShowUpdateCompletedDialog();

            // (#201, v0.15.0) ログ保存先 setting auto-migrate 完了 (v0.14.0 → v0.15.0 移行) の一回限り通知。
            // Program.Main の TryAutoMigrateLegacyLogPath が migration 完了時に `<install>/.logs_root_migrated`
            // sentinel file を書出している。本関数で sentinel を読込 → 部員向け subdir 構造説明 MessageBox →
            // sentinel を削除する flow。次回起動以降は sentinel 不在で発火しない。
            TryShowLogsRootMigratedDialog();

            // (R3 review M-2) Updater log absorb は ContinueLoadAfterSessionCheck 経由に移設済 (= session
            // conflict check 通過後)。MainForm_Load の早期で absorb すると同一 PC で複数 Manager が同時起動
            // した極端 case で `.absorbed` の append race + 重複 absorb が発生し得る path があった (実害は
            // Manager log file 間の重複 entry のみで file 内競合は無いが、構造的に bound するため移設)。
            //
            // (R4 review M-2) **skip path (dbReady=false / session conflict Cancel) の整理**:
            //   - dbReady=false (= DB 初期化を user が拒否 / 不存在 path) → MainForm_Load early return →
            //     ContinueLoadAfterSessionCheck 呼ばれず → absorb 走らない
            //   - session conflict Cancel → BeginInvoke dialog で Cancel → ContinueLoadAfterSessionCheck
            //     呼ばれず → absorb 走らない
            //   両 path とも次回 Manager 起動で `.absorbed` の未含有 entry として picked up される
            //   idempotent 設計のため、当該 startup の Manager log には Updater event が遅延反映されるが、
            //   永久に失われることはない (= CleanupOldLogs の「全起動 path 必達」設計とは要件が異なる、
            //   absorb は idempotent + deferred OK の弱い保証で十分)。

            bool dbReady = false;

            if (!dbManager.DatabaseExists())
            {
                var result = MessageBox.Show(
                    "データベースが見つかりません。\n新規作成しますか？",
                    "データベース初期化",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    InitializeDatabase();
                    dbReady = true;
                }
            }
            else if (!dbManager.TablesExist())
            {
                var result = MessageBox.Show(
                    "データベーステーブルが見つかりません。\n初期化しますか？",
                    "データベース初期化",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    InitializeDatabase();
                    dbReady = true;
                }
            }
            else
            {
                dbManager.InitializeDatabase();
                dbReady = true;
            }

            if (!dbReady)
            {
                // (round 1 L-3) DB 未初期化 path では session service も起動しない (= heartbeat 不在で
                // form を user 手動 × で閉じるまで no-op、FormClosed で _sessionService=null なので
                // Shutdown も skip される clean path)。
                // (#170 followup) DB 未初期化は左 zone 占有 (= ゲーム数取得不可、データベース状態を伝える)
                lblStatus.Text = "データベース未初期化";
                return;
            }

            // (#179、round 1 C-1 fix) DB schema migration が確実に完了したため、ManagerSessionService を
            // ここで初期化 + 起動時 check を実行。Initialize 内の SQL (`DELETE FROM manager_sessions ...` /
            // `INSERT OR REPLACE INTO manager_sessions ...`) は v13 schema に依存。
            _sessionService = new ManagerSessionService(dbManager.ManagerSessionRepository);
            _sessionService.Initialize();

            // (#179 PR3b) Launcher session 検出機構も同時に初期化。JSON drop folder 方式で SPEC §6.5
            // 「Launcher は SQLite に直接 write しない」原則を遵守、Manager 側は polling-only で DB write
            // ゼロ。`PathManager.LauncherSessionsFolder` (= `<install>/responses/launcher_sessions/`) を
            // SoT として両 component が同 path を別実装 (C# / GDScript) で resolve する。詳細 SPEC §3.8.7。
            _launcherSessionService = new LauncherSessionService(PathManager.LauncherSessionsFolder);
            _launcherSessionService.Initialize();

            // 起動時 check: 他 PC で active session を検出 → SessionConflictDialog (Startup)。
            // Cancel で Manager 終了 (self row delete 経由で clean exit trail を残す)。
            //
            // (#186 round 3) **gate 維持 + taskbar entry 確保の両立** のため chain pattern を採用:
            //   - 検出時: `BeginInvoke` で dialog 表示を defer (= MainForm の Show 完了後に dialog が
            //     owner-modal child で開く → taskbar entry あり、他 window click で裏に行ける、natural
            //     WinForms 挙動)
            //   - **panel.Initialize / LoadGames / RegisterUnknownSafetyFiles / CleanupStaleBackupEntries /
            //     StartAutoBackupIfDue / CleanupZombieStagings / StartBackgroundUpdateCheckIfDue は全部
            //     `ContinueLoadAfterSessionCheck` に切出し**、conflict 検出時は MainForm_Load 自体は
            //     return して残り init は実行しない (= gate)。
            //   - OK 押下時のみ `ContinueLoadAfterSessionCheck` を chain で起動 → 旧実装の gate 意味論
            //     を完全に維持。Cancel 時は panel init / backup / update check すべて skip して Close。
            //
            // round 1 (`MessageBoxOptions.DefaultDesktopOnly`) は user feedback「常時最前面うざい」で撤回。
            // round 2 (`BeginInvoke` defer のみ) は MainForm_Load の後続処理が dialog より先に走る regression
            // が PR #189 reviewer High で指摘されたため、本 round 3 で chain pattern に拡張。
            //
            // (#179 PR3b) Manager 検出 + Launcher 検出を merge して dialog 表示判定。両方が空の場合のみ
            // ContinueLoadAfterSessionCheck を直接呼出 (= 競合なし path)、どちらか 1 件以上で dialog 表示。
            //
            // Initialize は sync のまま (= heartbeat thread 起動を最速で)、Detect も sync で他 PC 検出結果
            // を握ってから dialog 表示部分のみ defer。
            var otherSessionsAtStartup = _sessionService.DetectOtherActiveSessions();
            // (#179 PR3b round 3 L-4) `CheckSessionConflictBeforeWrite` 側と pattern を対称化、
            // `_launcherSessionService.IsInitialized` を明示 check (= defense-in-depth、Initialize 失敗
            // path で fail-soft empty を確実に返す)。`_launcherSessionService` は直前に Initialize 済で
            // null になる path は無いが、guard を 2 箇所で揃えて読み手の認知 load を下げる。
            IReadOnlyList<LauncherSessionInfo> launcherSessionsAtStartup;
            if (_launcherSessionService != null && _launcherSessionService.IsInitialized)
            {
                launcherSessionsAtStartup = _launcherSessionService.DetectActiveLauncherSessions();
            }
            else
            {
                launcherSessionsAtStartup = new List<LauncherSessionInfo>();
            }
            if (otherSessionsAtStartup.Count > 0 || launcherSessionsAtStartup.Count > 0)
            {
                BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // (PR #189 round 4 L-1 表現訂正) race の正確 timing は
                        // 「`MainForm_Load` 完了 → `MainForm.Show` 完了 → message pump が本 BeginInvoke
                        // action を pick up するまでの数 ms」に user が × で閉じた case。
                        // MainForm_Load 実行中は MainForm 未表示で × clickable な window がないため、
                        // 「Load 中 ×」ではない。_sessionService が null になってる可能性 (= FormClosed
                        // 経由 Shutdown が deferred action 起動前に走った race) も同時 guard、defense-
                        // in-depth で残置。
                        if (IsDisposed || Disposing || _sessionService == null) return;

                        var dialogResult = SessionConflictDialog.Show(
                            this, SessionConflictDialogContext.Startup, otherSessionsAtStartup, launcherSessionsAtStartup);
                        if (dialogResult == DialogResult.Cancel)
                        {
                            Logger.Info("[MainForm] user が SessionConflictDialog (Startup) で Cancel 選択、Manager 終了");
                            // (PR #184 round 1 M-4) FormClosed 経由 Shutdown と二重呼出にならないよう、
                            // Cancel path では Shutdown 直接呼出後に _sessionService = null を set。
                            _sessionService.Shutdown();
                            _sessionService = null;
                            // (#179 PR3b) LauncherSessionService も同様に Shutdown + null 化。polling-only
                            // なので Shutdown は no-op だが対称化のため呼ぶ + FormClosed 経由二重呼出予防。
                            if (_launcherSessionService != null)
                            {
                                _launcherSessionService.Shutdown();
                                _launcherSessionService = null;
                            }
                            // (PR #189 round 4 L-2 復活) `Application.Exit` ではなく `Close` で
                            // FormClosing → FormClosed chain を確実に走らせる (= Logger.Shutdown +
                            // FormClosed 経由の cleanup 一式)。round 3 で旧 `BeginInvoke(...Close())`
                            // 二重 defer を素の `Close()` に短絡化した際に同 rationale が消えていたため
                            // 復活。`Application.Exit` の方が直感的だが FormClosing が走らない drift
                            // path があるため `Close()` を選ぶ。
                            Close();
                            return;
                        }
                        Logger.Info("[MainForm] user が SessionConflictDialog (Startup) で OK 選択、起動を続行");
                        // (#186 round 3) OK path: 残り init を chain で起動 (= gate を通過した時点で
                        // 初めて panel init / backup / update check 等が走る)。
                        ContinueLoadAfterSessionCheck();
                    }
                    catch (Exception ex)
                    {
                        // deferred action 内例外は Application.ThreadException 経由で UI thread crash
                        // 経路を踏むため、Logger.Error で握り潰す (FormClosed handler の Shutdown catch と
                        // 同方針、PR #189 round 3 reviewer Low 指摘)。
                        Logger.Error("[MainForm] Startup dialog deferred action で例外", ex);
                    }
                }));
                // (#186 round 3) gate 維持のため、conflict 検出時は MainForm_Load 自体ここで return。
                // dialog の OK/Cancel 判定後に ContinueLoadAfterSessionCheck が chain で呼ばれる。
                return;
            }

            // 競合なし: 残り init を直接呼出 (= 旧実装と同じ sync 起動 path、user 視点で挙動不変)。
            ContinueLoadAfterSessionCheck();
        }

        /// <summary>
        /// (#186 round 3) `MainForm_Load` の session check 通過後に呼ばれる残り init 処理。
        /// 旧実装は `MainForm_Load` 内に inline されていたが、Startup SessionConflictDialog の
        /// `BeginInvoke` defer (= MainForm.Show 完了後に dialog を出す taskbar entry 確保策) と
        /// 「Cancel 時は panel init / backup / update check すべて skip する gate 意味論」を両立する
        /// ため、chain pattern (= dialog OK 時に本 method を呼出、Cancel 時は呼出しない) で gate を維持。
        ///
        /// 競合なし path も本 method を直接呼んで code path を 1 本化。
        /// </summary>
        private void ContinueLoadAfterSessionCheck()
        {
            // DB確認後にパネルを初期化（DB存在前のアクセスを防止）
            _gameSectionPanel.Initialize(dbManager);
            _storeSectionPanel.Initialize(dbManager);
            _settingsSectionPanel.Initialize(dbManager);

            // Updater log の post-hoc filtered absorb (= SPEC §3.6 Companions ログ管理規約)。
            // 直前のアップデートサイクル中に Updater が書き出した log のうち Warn/Error + 主要 milestone
            // を Manager log に embed する。Manager GUI の log viewer から 3 component に収束させる方針の
            // 一環 (= Updater 用 tab を増やさず、Updater 由来の重要 event を Manager log の一部として閲覧)。
            // 例外は内部で握り潰し済、Manager 起動を阻害しない。
            //
            // **呼出位置**: session conflict check 通過後 = ContinueLoadAfterSessionCheck 内 (= R3 review
            // M-2 対応で MainForm_Load 早期から移設)。同一 PC 複数 Manager 同時起動時の `.absorbed` 競合を
            // 構造的に bound する目的。Cancel path で skip されるが next start で idempotent に picked up。
            Services.UpdaterLogAbsorber.AbsorbPendingLogs();

            // (#170 followup round 2 review H-1) CleanupOldLogs は **Program.Main に移動済**。
            // 旧実装は本 MainForm 経路で呼出していたが、dbReady=false / SessionConflictDialog Cancel
            // 等の early-return path で到達不能になる silent regression があり、Program.Main で
            // SQLite 直接 read 経由で呼ぶ形に変更。本 MainForm 内で再呼出は不要。

            // 旧バージョンが root 直下に作っていた safety ファイルを backups/safety/ へ一度きり移動
            try
            {
                int movedSafety = Services.BackupService.MigrateLegacySafetyFilesToSafetyFolder(
                    System.IO.Path.GetDirectoryName(PathManager.DatabasePath));
                if (movedSafety > 0)
                {
                    Logger.Info($"[MainForm] 旧 safety ファイル {movedSafety} 件を backups/safety/ に移動しました");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("[MainForm] 旧 safety ファイル移動失敗", ex);
            }

            // 退避ファイルの未登録分を backup_log に登録（起動時に1回）
            RegisterUnknownSafetyFiles();

            // 起動時に古い in_progress 行を掃除（クラッシュ残骸 / 自己参照スナップショット由来）
            CleanupStaleBackupEntries();

            _backupSectionPanel.Initialize(dbManager);
            _logSectionPanel.Initialize(PathManager.LogsRootDirectory);
            _updateSectionPanel.Initialize(dbManager);

            _gameSectionPanel.LoadGames();
            UpdateStatusBar();

            // 起動時に自動バックアップが必要なら走らせる（バックグラウンドで非ブロッキング）
            StartAutoBackupIfDue();

            // 起動時に zombie staging dir (前回 update 失敗の残骸) を cleanup (#108 Phase 4)
            CleanupZombieStagings();

            // 起動時にバックグラウンドで GitHub Releases API を叩いてアップデート check (#108 Phase 4)
            // cache TTL 内なら HTTP を叩かない、起動を遅延させない fire-and-forget pattern
            StartBackgroundUpdateCheckIfDue();
        }

        /// <summary>
        /// 過去 run の失敗 / cancel で残った staging dir を起動時に best-effort 削除する (#108 Phase 4)。
        /// `%TEMP%/TonePrism_update_*` を全部削除。今 update 中 (= Manager 起動中に Updater spawn 直後)
        /// は normally 走らないので race condition なし。
        /// </summary>
        private void CleanupZombieStagings()
        {
            try
            {
                foreach (string dir in PathManager.EnumerateZombieStagings())
                {
                    try
                    {
                        System.IO.Directory.Delete(dir, recursive: true);
                        Services.Logger.Info("[MainForm] zombie staging 削除: " + dir);
                    }
                    catch (Exception ex)
                    {
                        Services.Logger.Warn("[MainForm] zombie staging 削除失敗: " + dir + " (" + ex.Message + ")");
                    }
                }
            }
            catch (Exception ex)
            {
                Services.Logger.Error("[MainForm] zombie staging cleanup エラー", ex);
            }
        }

        /// <summary>
        /// 起動時の background アップデート check (#108 Phase 4)。SettingsRepository の cache を参照し、
        /// TTL 超過なら GitHub Releases API を叩く。fire-and-forget で UI を遮らず、完了したら
        /// `_updateSectionPanel.OnCheckCompleted` で UI thread に marshal して反映。
        /// 新版検出 (Status=UpdateAvailable) 時は MessageBox で部員に通知してアップデートタブに誘導。
        /// 既にスキップ済 (Status=Skipped) / 最新 (UpToDate) / network 失敗時は通知しない。
        /// </summary>
        private async void StartBackgroundUpdateCheckIfDue()
        {
            try
            {
                if (dbManager == null) return;
                var checker = new Services.UpdateChecker(dbManager.SettingsRepository);
                // (#108 Phase 4 round 8 L-1) `Task.Run` 除去。`CheckAsync` は HttpClient.GetAsync 経由の
                // 真の async で UI thread をブロックしないため、`Task.Run` で thread pool 1 つ消費 +
                // 直ちに async state machine に戻る overhead が無駄だった。直接 await で同等動作 +
                // thread pool 節約。`StartAutoBackupIfDue` 側は synchronous な `BackupService.RunAutoBackupIfDue`
                // を呼ぶため `Task.Run` 必要で、対比的に本 path は不要。
                Models.UpdateCheckResult result = await checker.CheckAsync(System.Threading.CancellationToken.None);
                if (result == null || _updateSectionPanel == null) return;
                _updateSectionPanel.OnCheckCompleted(result);
                if (result.Status == Models.UpdateCheckStatus.UpdateAvailable
                    && result.Latest != null
                    && !string.IsNullOrEmpty(result.Latest.TagName))
                {
                    // (#108 Phase 4 round 2 codex P2 / round 3 M-1) per-tag notified marker で「同 tag は 1 回
                    // dialog 表示」に絞る。round 3 M-1 fix: 直接 SettingsRepository.SetString ではなく
                    // UpdateChecker.MarkNotified / GetNotifiedTag 経由で _settingsWriteLock 内で書込み
                    // (= M4 invariant 維持)。
                    string notifiedTag = checker.GetNotifiedTag();
                    if (string.Equals(notifiedTag, result.Latest.TagName, StringComparison.Ordinal))
                    {
                        Services.Logger.Info("[MainForm] UpdateAvailable 検出 (tag=" + result.Latest.TagName + ") だが notified marker 一致、通知 skip");
                    }
                    else
                    {
                        // (#108 Phase 4 round 4 codex P2 NEW) MarkNotified は MessageBox 表示成功時のみ。
                        // 旧実装は無条件 marker 更新で、UI error (BeginInvoke 失敗 / MessageBox 例外) で
                        // dialog 表示されなかった case でも marker set → 次回起動以降 永久 suppress に
                        // なる path があった。ShowUpdateAvailableNotification を bool 返却に変更、
                        // success のみ MarkNotified 経路に流す。
                        bool shown = ShowUpdateAvailableNotification(result);
                        if (shown)
                        {
                            // (#170 followup) MarkNotified は **意図的に CheckBeforeWrite を呼ばない**。
                            // 理由 3 つ: (1) auto background path (StartBackgroundUpdateCheckIfDue) からの
                            // 自動副作用で user 操作直接 trigger ではない、(2) 非破壊的な metadata write
                            // (= 同 tag 上書きでも実害ゼロ、SQLite WAL で atomic)、(3) 直前に通知 dialog を
                            // user が dismiss したばかりで、直後に「他 PC 競合中」popup を出すと UX 二重表示
                            // で煩雑。BackupService の `last_backup_at` 自動書込が同種 auto path で
                            // CheckBeforeWrite ではなく TryAcquireBackupLease の独自 fence を採用しているのと
                            // 同じ判断基準 (= auto path は popup ではなく atomic 性に依存)。本 PR の plan
                            // で当初追加候補だったが、background thread + auto side-effect + 非破壊性 という
                            // 3 constraint で意図的に skip と文書化。
                            checker.MarkNotified(result.Latest.TagName);
                        }
                        else
                        {
                            Services.Logger.Warn("[MainForm] UpdateAvailable 通知 dialog 表示に失敗、marker 更新 skip (次回起動で再通知)");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Services.Logger.Error("[MainForm] BackgroundUpdateCheck エラー", ex);
            }
        }

        /// <summary>
        /// 新バージョン検出時に MessageBox で通知して「アップデート」タブに誘導する (#108 Phase 4)。
        /// `Status=UpdateAvailable` の case でのみ呼ばれる (Skipped / UpToDate / 失敗時は呼ばれない、
        /// = 「スキップしたバージョンが新 release で更新されるまで再通知しない」semantic を上位で保証)。
        ///
        /// **戻り値 (round 4 codex P2 NEW + round 5 L-1)**: dialog が **実際に user に表示された** ら
        /// true、UI error (Invoke 失敗 / MessageBox 例外 / form 破棄) で表示前に early return した場合は
        /// false。caller (StartBackgroundUpdateCheckIfDue) は true 時のみ MarkNotified を呼ぶ責務、
        /// false 時は marker 更新 skip で次回起動で再通知する。
        ///
        /// round 5 L-1: 旧実装は async recursive call (BeginInvoke) で楽観的 true return していたが、
        /// 実際 caller `StartBackgroundUpdateCheckIfDue` は `await Task.Run` 後 SynchronizationContext
        /// 経由で UI thread に戻るため `InvokeRequired = false` 確定の dead path だった。defensive
        /// として残す場合は **synchronous Invoke** で recursive call の結果を caller に正確に返す形に。
        /// </summary>
        private bool ShowUpdateAvailableNotification(Models.UpdateCheckResult result)
        {
            if (InvokeRequired)
            {
                // (round 5 L-1) defensive path: BeginInvoke の楽観 true は嘘になるため Invoke で
                // synchronous に recursive call → result を caller に正確に返す。
                try
                {
                    return (bool)Invoke(new Func<Models.UpdateCheckResult, bool>(ShowUpdateAvailableNotification), result);
                }
                // (round 4 M-3) ObjectDisposedException : InvalidOperationException 派生関係のため
                // specific を先に置いて意図明示。
                catch (ObjectDisposedException) { return false; /* form 完全 Dispose 済 */ }
                catch (InvalidOperationException) { return false; /* form 破棄済み */ }
            }

            string currentLabel = result.Current == null ? "(不明)" : "v" + result.Current.ToString(3);
            string latestLabel = result.Latest.TagName;
            string message =
                "新しいバージョンが利用可能です。\n\n" +
                "現在のバージョン: " + currentLabel + "\n" +
                "最新のバージョン: " + latestLabel + "\n\n" +
                "「アップデート」タブを開いてリリースノートを確認しますか？\n" +
                "(あとで確認する場合は「いいえ」、このバージョンを無視するには「アップデート」タブの「このバージョンをスキップ」を押してください)";

            DialogResult dr;
            try
            {
                dr = MessageBox.Show(this, message, "アップデートの通知",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1);
            }
            catch (Exception ex)
            {
                Services.Logger.Error("[MainForm] ShowUpdateAvailableNotification エラー", ex);
                return false;
            }
            if (dr == DialogResult.Yes && tabControl1 != null)
            {
                try
                {
                    tabControl1.SelectedTab = tabUpdate;
                }
                catch (Exception ex)
                {
                    Services.Logger.Warn("[MainForm] tabUpdate 切替失敗: " + ex.Message);
                }
            }
            return true; // (round 4 codex P2 NEW) dialog 表示完了 = success
        }

        private void RegisterUnknownSafetyFiles()
        {
            try
            {
                int added = dbManager.BackupLogRepository.RegisterUnknownSafetyFiles(
                    dbManager.BackupService.GetSafetyDirectory(),
                    Environment.MachineName);
                if (added > 0)
                {
                    Logger.Info($"[MainForm] 退避ファイル {added} 件を backup_log に新規登録しました");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("[MainForm] 退避ファイル登録に失敗", ex);
            }
        }

        private void CleanupStaleBackupEntries()
        {
            try
            {
                // 起動時は10分以上経過した in_progress のみリコンサイル対象（実行中のバックアップに干渉しないため）
                var (success, failed) = dbManager.BackupLogRepository.ReconcileInProgressEntries(
                    "進行中状態のまま放置されたため自動的にfailed扱いにしました（Managerクラッシュ等でバックアップが完了しなかった可能性）",
                    thresholdSeconds: 600);
                if (success > 0 || failed > 0)
                {
                    Logger.Info($"[MainForm] 起動時リコンサイル: 成功化 {success} 件 / 失敗化 {failed} 件");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("[MainForm] バックアップ履歴の掃除に失敗しました", ex);
            }
        }

        /// <summary>
        /// 自動バックアップが期限到来していればバックグラウンドで実行する。
        /// UIをブロックせず、ステータスバーで進捗を伝える。
        /// </summary>
        private async void StartAutoBackupIfDue()
        {
            if (dbManager == null) return;

            try
            {
                if (!dbManager.BackupService.IsAutoBackupDue())
                {
                    return;
                }

                // (#170 followup) 右 zone (lblBackupStatus) で transient 表示、左 zone のゲーム数は維持。
                UpdateBackupStatus("自動バックアップ実行中...", System.Drawing.Color.DarkBlue, autoRevert: false);
                BackupResult result = await Task.Run(() =>
                    dbManager.BackupService.RunAutoBackupIfDue(null, CancellationToken.None));

                if (result == null) return;

                if (result.IsSuccess)
                {
                    UpdateBackupStatus(
                        $"✓ 自動バックアップ完了: {System.IO.Path.GetFileName(result.FilePath)}",
                        System.Drawing.Color.DarkGreen, autoRevert: true);
                    _backupSectionPanel.RefreshDisplay();
                }
                else if (result.IsFailed)
                {
                    UpdateBackupStatus($"✗ 自動バックアップ失敗: {result.Message}",
                        System.Drawing.Color.DarkRed, autoRevert: true);
                }
                else if (result.IsSkipped)
                {
                    // (#170 followup round 2 review #1) 他 PC で実行済 / disable 等で skip された場合、
                    // 直前の `UpdateBackupStatus("自動バックアップ実行中...", autoRevert: false)` で表示した
                    // indicator が永久残留する path を閉じる。empty string + autoRevert: true で
                    // 即時 clear (Timer の 7 秒経過で消える、または重複呼出時に旧 timer が dispose される
                    // ので問題なし)。
                    UpdateBackupStatus(string.Empty, System.Drawing.SystemColors.ControlText, autoRevert: true);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("[MainForm] StartAutoBackupIfDue エラー", ex);
                UpdateBackupStatus($"✗ 自動バックアップエラー: {ex.Message}",
                    System.Drawing.Color.DarkRed, autoRevert: true);
            }
        }

        private void InitializeDatabase()
        {
            try
            {
                PathManager.EnsureDirectoriesExist();
                dbManager.InitializeDatabase();

                MessageBox.Show(
                    "データベースの初期化が完了しました。",
                    "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"データベースの初期化に失敗しました。\n\n{ex.Message}",
                    "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnDatabaseReset()
        {
            _gameSectionPanel.LoadGames();
            _storeSectionPanel.LoadSections();
            UpdateStatusBar();
        }

        /// <summary>
        /// バックアップからの復元完了時。各パネルを再ロードする。
        /// </summary>
        private void OnDatabaseRestored()
        {
            try
            {
                // 復元によりスキーマやデータが変わっているので、必要なら再初期化
                dbManager.InitializeDatabase();

                // 復元時に作成された新しい safety ファイルを backup_log に登録（復元先には未登録）
                RegisterUnknownSafetyFiles();

                // 復元先のスナップショットには「バックアップ撮影時点で進行中だった自分自身の行」が
                // 含まれている自己参照ゴーストになる。各 in_progress 行について実ファイルが
                // 存在するか確認し、存在すれば success、存在しなければ failed としてリコンサイルする。
                try
                {
                    var (success, failed) = dbManager.BackupLogRepository.ReconcileInProgressEntries(
                        "バックアップファイルが見つかりませんでした（バックアップから復元時の自己参照スナップショット由来で、実ファイルも残っていない）");
                    if (success > 0 || failed > 0)
                    {
                        Logger.Info($"[MainForm] 復元後リコンサイル: 成功化 {success} 件 / 失敗化 {failed} 件");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("[MainForm] 復元後の履歴リコンサイルに失敗しました", ex);
                }

                _gameSectionPanel.LoadGames();
                _storeSectionPanel.LoadSections();
                _settingsSectionPanel.UpdateVersionInfo();
                UpdateStatusBar();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"復元後の再読み込みに失敗しました: {ex.Message}",
                    "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (tabControl1.SelectedTab == tabStore)
            {
                _storeSectionPanel.LoadSections();
            }
        }

        // (#170 followup) status bar の左 / 右 zone を分離。
        //   左 (lblStatus): データベース接続状態 + ゲーム数 = 永続表示
        //   右 (lblBackupStatus): 自動バックアップの transient 状態 = autoRevert=true なら 7 秒で消える
        // 旧実装は 1 ラベル 1 関数で、auto backup message が「ゲーム数」を上書きして元情報が一時消失していた。
        private System.Windows.Forms.Timer _backupStatusClearTimer;

        private void UpdateStatusBar()
        {
            if (dbManager == null) return;
            string dbStatus = dbManager.DatabaseExists() ? "接続済み" : "未接続";
            string gameInfo = $"ゲーム数: {_gameSectionPanel.GameCount}件";
            lblStatus.Text = $"データベース: {dbStatus} | {gameInfo}";
        }

        /// <summary>
        /// (#170 followup) status bar の右 zone (`lblBackupStatus`) に transient な backup 状態を表示する。
        /// 左 zone (`lblStatus` の「データベース | ゲーム数」) は変えない (= 旧 UpdateStatusBar(string) の
        /// 上書き問題への対処)。
        ///
        /// `autoRevert=true` で渡すと 7 秒後に自動 clear する Timer を仕掛ける (= 完了 / 失敗 message が
        /// 永続表示で stale になるのを防ぐ)。`autoRevert=false` は「実行中...」のような完了まで残したい
        /// 状態用、次の `UpdateBackupStatus` 呼出か手動 clear まで残る。
        ///
        /// Accessibility: color 情報を text prefix で補強する (`✓` / `✗`)、screen reader で color を
        /// 識別できない user 向けの fallback。
        /// </summary>
        private void UpdateBackupStatus(string message, System.Drawing.Color color, bool autoRevert)
        {
            // (#170 followup round 2) lblBackupStatus は Designer で Alignment=Right + AutoSize=true 設定済。
            // strip 右端から natural width 分を anchor 配置するため、Text / ForeColor 設定だけで OK。
            // (review M-2) 長文 message (= SQLite error / 長 path) で strip 幅超過 + left zone 重なりを防ぐため、
            // 表示 text を 80 文字に pre-truncate (= chevron に逃げる前の defense)。完全な message は呼出元
            // (e.g., 自動 backup 失敗 path) で Logger.Error として別途残るため UI で truncated でも debug 可能。
            lblBackupStatus.Text = TruncateForStatusBar(message);
            lblBackupStatus.ForeColor = color;

            // 既存 timer を破棄してから新規 (= 連続呼出時に古い timer が古い message を消すのを防ぐ)
            if (_backupStatusClearTimer != null)
            {
                _backupStatusClearTimer.Stop();
                _backupStatusClearTimer.Dispose();
                _backupStatusClearTimer = null;
            }
            if (autoRevert && !string.IsNullOrEmpty(message))
            {
                _backupStatusClearTimer = new System.Windows.Forms.Timer();
                _backupStatusClearTimer.Interval = 7000;
                _backupStatusClearTimer.Tick += BackupStatusClearTimer_Tick;
                _backupStatusClearTimer.Start();
            }
        }

        /// <summary>
        /// (#170 followup round 2 review M-2) status bar 右 zone 用の text truncation helper。
        /// 経験則上 80 chars で過半数の典型 message (= 自動 backup 失敗 SQLite error / 長 path) を救い、
        /// 極端 overflow は WinForms StatusStrip の chevron に逃げる設計。
        ///
        /// 注: 厳密な pixel 幅判定はしない (= 全角混じり / window resize / DPI scaling で精度差あり)。
        /// 完全な message は呼出元 (e.g., `StartAutoBackupIfDue` の `Logger.Error`) で別途 log に残るため
        /// UI で truncated でも debug 可能、本 helper は user 視覚的 alarm 用 carry-best-effort と位置付け。
        /// </summary>
        private static string TruncateForStatusBar(string message)
        {
            if (string.IsNullOrEmpty(message)) return string.Empty;
            const int MaxLen = 80;
            if (message.Length <= MaxLen) return message;
            // 先頭 50 文字 + "..." + 末尾 27 文字 で末尾 (ファイル名等) を維持
            return message.Substring(0, 50) + "..." + message.Substring(message.Length - 27);
        }

        private void BackupStatusClearTimer_Tick(object sender, EventArgs e)
        {
            lblBackupStatus.Text = string.Empty;
            if (_backupStatusClearTimer != null)
            {
                _backupStatusClearTimer.Stop();
                _backupStatusClearTimer.Dispose();
                _backupStatusClearTimer = null;
            }
        }

        // (#178 (b)) アップデート完了通知 dialog。invariant:
        //   sentinel ファイルは読込直後の `finally` で必ず削除 (parse 成功 / 失敗問わず、永続再表示バグ防止)。
        // 仕様: SPECIFICATION.md §3.7.3。
        //
        // (round 4 Medium-3) 旧 docstring の「同時起動注意 MessageBox との排他置換 → true/false 返却で
        // caller が gate」invariant は本 PR (#179 / #178 (c) で同時起動 MessageBox を撤廃) で消滅。
        // 戻り値を使う caller も居ない (MainForm_Load:104 の呼出は戻り値捨て) ため signature を void 化。

        /// <summary>
        /// sentinel ファイル `<install>/.update_completed` を読んで完了 dialog を表示。
        /// sentinel 不在時は no-op。仕様 SPECIFICATION.md §3.7.3 参照。
        /// </summary>
        private void TryShowUpdateCompletedDialog()
        {
            string sentinelPath = System.IO.Path.Combine(PathManager.BaseDirectory, ".update_completed");
            if (!System.IO.File.Exists(sentinelPath)) return;

            string newVersion = null;
            string completedAtRaw = null;
            try
            {
                string json = System.IO.File.ReadAllText(sentinelPath, System.Text.Encoding.UTF8);
                var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                var dto = ser.Deserialize<UpdateCompletedSentinel>(json);
                if (dto != null) { newVersion = dto.NewVersion; completedAtRaw = dto.CompletedAt; }
            }
            catch (Exception ex)
            {
                Services.Logger.Warn("[MainForm] update_completed sentinel parse 失敗 (dialog 表示 skip): " + ex.Message);
            }
            finally
            {
                // 読込結果に関わらず sentinel は必ず削除する (永続 dialog 再表示バグ防止)。
                try { System.IO.File.Delete(sentinelPath); }
                catch (Exception delEx) { Services.Logger.Warn("[MainForm] update_completed sentinel 削除失敗: " + delEx.Message); }
            }

            if (string.IsNullOrEmpty(newVersion))
            {
                // parse 失敗 / newVersion 不在は dialog 出さず終了 (sentinel は finally で削除済)。
                return;
            }

            // CompletedAt は writer が ISO 8601 UTC で書き出した値 ("yyyy-MM-ddTHH:mm:ssZ")。dialog では
            // user-friendly な local time format に変換 ("yyyy-MM-dd HH:mm")。parse 失敗時は空文字で fallback、
            // dialog 表示は version のみで継続 (= 完了時刻不在で dialog 出ない方が UX 悪なので silent fallback)。
            string completedAtLocal = string.Empty;
            if (!string.IsNullOrEmpty(completedAtRaw))
            {
                DateTime parsed;
                if (DateTime.TryParse(completedAtRaw,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                        out parsed))
                {
                    completedAtLocal = parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
                }
            }

            Services.Logger.Info("[MainForm] update_completed dialog 表示: Bundle v" + newVersion + " completedAt=" + (completedAtRaw ?? "(null)"));
            string body = "アップデートが完了しました。\n\n" +
                          "  Bundle バージョン: v" + newVersion + "\n";
            if (!string.IsNullOrEmpty(completedAtLocal))
            {
                body += "  完了時刻: " + completedAtLocal + "\n";
            }
            body += "\n新しい管理ソフトが起動しています。";
            MessageBox.Show(
                body,
                "✓ アップデート完了",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        /// <summary>
        /// (#201, v0.15.0) ログ保存先 setting auto-migrate 完了通知 dialog。
        /// `<install>/.logs_root_migrated` sentinel ファイル存在 check → 部員向け subdir 構造説明 MessageBox 表示
        /// → sentinel 削除。次回起動以降は sentinel 不在で発火しない。
        ///
        /// sentinel JSON schema: `{"migrated_from": "<oldValue>", "migrated_at": "<ISO 8601 UTC>"}`
        /// (= Program.TryAutoMigrateLegacyLogPath で書出済)
        /// 例外は内部で握り潰し (= Manager 起動を阻害しない)、parse 失敗時も sentinel は finally で削除する
        /// (永続 dialog 再表示バグ防止、TryShowUpdateCompletedDialog と同 pattern)。
        /// </summary>
        private void TryShowLogsRootMigratedDialog()
        {
            string sentinelPath = System.IO.Path.Combine(PathManager.BaseDirectory, ".logs_root_migrated");
            if (!System.IO.File.Exists(sentinelPath)) return;

            string migratedFrom = null;
            try
            {
                string json = System.IO.File.ReadAllText(sentinelPath, System.Text.Encoding.UTF8);
                var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                var dto = ser.Deserialize<LogsRootMigratedSentinel>(json);
                if (dto != null) migratedFrom = dto.MigratedFrom;
            }
            catch (Exception ex)
            {
                Services.Logger.Warn("[MainForm] logs_root_migrated sentinel parse 失敗 (dialog 表示 skip): " + ex.Message);
            }
            finally
            {
                // 読込結果に関わらず sentinel は必ず削除する (永続 dialog 再表示バグ防止)。
                try { System.IO.File.Delete(sentinelPath); }
                catch (Exception delEx) { Services.Logger.Warn("[MainForm] logs_root_migrated sentinel 削除失敗: " + delEx.Message); }
            }

            if (string.IsNullOrEmpty(migratedFrom))
            {
                // parse 失敗 / migratedFrom 不在は dialog 出さず終了 (sentinel は finally で削除済)。
                return;
            }

            Services.Logger.Info("[MainForm] logs_root_migrated dialog 表示: migratedFrom=" + migratedFrom);
            string body =
                "これまでのバージョンでは、Manager のログだけが指定したフォルダに保存されていました。\n" +
                "このバージョンから、Manager / Launcher / Updater など 全コンポーネントのログを\n" +
                "1 つのフォルダにまとめて保存できるようになりました。\n" +
                "\n" +
                "【設定の自動引き継ぎ】\n" +
                "これまでの設定値 (" + migratedFrom + ") はそのまま引き継がれましたが、\n" +
                "フォルダ構造が以下のように変わります:\n" +
                "\n" +
                "  " + migratedFrom + "\\\n" +
                "    ├ manager\\    ← Manager のログ\n" +
                "    ├ launcher\\   ← Launcher のログ (新規追加)\n" +
                "    └ updater\\    ← Updater のログ (新規追加)\n" +
                "\n" +
                "これまで " + migratedFrom + " 直下に保存されていた古いログファイルはそのまま残ります\n" +
                "(不要であれば手動で削除してください)。\n" +
                "\n" +
                "設定タブの「ログ」セクションから保存先を変更できます。";
            MessageBox.Show(
                this,
                body,
                "ログの保存先の構造が変わりました",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        /// <summary>
        /// `<install>/.logs_root_migrated` sentinel ファイルの JSON deserialize 用 DTO (#201, v0.15.0)。
        /// writer 側は Program.TryAutoMigrateLegacyLogPath、wire format は **真の camelCase**
        /// (`migratedFrom` / `migratedAt`)、`JavaScriptSerializer` の case-insensitive deserialize で
        /// PascalCase property と互換受理 (UpdateCompletedSentinel と同 pattern)。
        /// 注: 旧 R1 docstring で「camelCase」と称しつつ実態が snake_case (`migrated_from`) だった drift を
        /// R2 review Critical #1 で解消、JavaScriptSerializer は underscore stripping を行わないため
        /// snake_case 採用は silent dialog 不発火を招くことが判明。
        /// </summary>
        private sealed class LogsRootMigratedSentinel
        {
            /// <summary>migration 前の旧 `log_destination_path` 値 (= dialog に embed して user に表示する)。</summary>
            public string MigratedFrom { get; set; }
            /// <summary>migration 実行時刻 (ISO 8601 UTC、現状は dialog 表示には使わず audit trail 用)。</summary>
            public string MigratedAt { get; set; }
        }

        /// <summary>
        /// `<install>/.update_completed` sentinel ファイルの JSON deserialize 用 DTO。
        ///
        /// **Serializer 切替時の注意** (round 2 review fix Low-2): 本 class は PascalCase property を
        /// 持ち、JSON wire format は camelCase (writer 側 `UpdateSectionPanel.RunUpdateWorker` の anonymous
        /// type が camelCase で書出し)。現状の `System.Web.Script.Serialization.JavaScriptSerializer` は
        /// case-insensitive deserialize で互換性が成立しているが、将来 `System.Text.Json` 等の case-sensitive
        /// default serializer へ切替える場合、wire 名 mapping を別途設定する必要がある (例: `JsonPropertyName`
        /// attribute)。切替時は wire format との対応を再検証すること。
        /// </summary>
        private sealed class UpdateCompletedSentinel
        {
            /// <summary>
            /// アップデート完了時刻 (ISO 8601 UTC、例: "2026-05-18T14:30:45Z")。consumer
            /// (`TryShowUpdateCompletedDialog`) が `DateTime.TryParse` で読み取り、`ToLocalTime` →
            /// "yyyy-MM-dd HH:mm" 形式に変換して dialog 文言の「完了時刻」行に embed する。parse 失敗時は
            /// 空文字 fallback で時刻行を省略 (= 時刻不在で dialog 自体を skip するより UX 良の判断)。
            /// `JavaScriptSerializer` の case-insensitive deserialize により JSON 上は `completedAt`
            /// (camelCase、wire format) でも `CompletedAt` (PascalCase) でも互換的に受理。
            /// </summary>
            public string CompletedAt { get; set; }
            /// <summary>
            /// 新 Bundle バージョン (例: "0.3.2")。**Bundle 全体の version** (= GitHub Releases tag) で、
            /// Manager 単体 version (例: "0.9.2") ではない (writer 側 `targetVersion.ToString(3)` が
            /// `_currentResult.Latest.Version` = Bundle Version 由来のため)。dialog 文言「Bundle バージョン: v...」
            /// に embed して user に表示する。
            /// </summary>
            public string NewVersion { get; set; }
        }
    }
}
