using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GCTonePrism.Manager.Controls;
using GCTonePrism.Manager.Services;

namespace GCTonePrism.Manager
{
    /// <summary>
    /// メインフォーム - タブナビゲーション
    /// </summary>
    public partial class MainForm : Form
    {
        private DatabaseManager dbManager;
        private ManagerSessionService _sessionService;

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

            _gameSectionPanel.StatusChanged += (msg) => UpdateStatusBar(msg);
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
            if (_sessionService == null)
            {
                // session service 未初期化 (= MainForm_Load 完了前 / Initialize 失敗時) は fail-soft で
                // OK 返却、編集操作を block しない。
                return DialogResult.OK;
            }
            var others = _sessionService.DetectOtherActiveSessions();
            if (others.Count == 0) return DialogResult.OK;
            return SessionConflictDialog.Show(
                this, SessionConflictDialogContext.EditOperation, others, operationDescription);
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // (#178 (b)) アップデート完了直後の起動 (= sentinel あり) は、まず「✓ アップデート完了」
            // MessageBox を表示。sentinel なし path は何もしない。
            // (#178 (c) / #179) 旧「同時起動に関する注意」MessageBox は撤廃、代わりに ManagerSessionService
            // で他 PC で稼働中の Manager を自動検出 → 検出時のみ SessionConflictDialog (Startup context)
            // を表示する設計に移行。sentinel 有無に関係なく session 初期化 + check を実行。
            TryShowUpdateCompletedDialog();

            // (#179) ManagerSessionService 初期化 (stale cleanup + self row 登録 + heartbeat thread 起動)
            //   - DB schema migration が完了している必要があるため、dbManager 初期化後に呼ぶ
            //   - DB 不到達等は service 内 fail-soft (= Logger.Error + heartbeat 不在で継続)
            _sessionService = new ManagerSessionService(dbManager.ManagerSessionRepository);
            _sessionService.Initialize();

            // 起動時 check: 他 PC で active session を検出 → SessionConflictDialog (Startup)。
            // Cancel で Manager 終了 (self row delete 経由で clean exit trail を残す)。
            var otherSessionsAtStartup = _sessionService.DetectOtherActiveSessions();
            if (otherSessionsAtStartup.Count > 0)
            {
                var dialogResult = SessionConflictDialog.Show(
                    this, SessionConflictDialogContext.Startup, otherSessionsAtStartup);
                if (dialogResult == DialogResult.Cancel)
                {
                    Logger.Info("[MainForm] user が SessionConflictDialog (Startup) で Cancel 選択、Manager 終了");
                    _sessionService.Shutdown();
                    // Application.Exit ではなく Close で FormClosing を確実に走らせる (= Logger.Shutdown 含む)
                    BeginInvoke(new Action(() => Close()));
                    return;
                }
                Logger.Info("[MainForm] user が SessionConflictDialog (Startup) で OK 選択、起動を続行");
            }

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
                UpdateStatusBar("データベース未初期化");
                return;
            }

            // DB確認後にパネルを初期化（DB存在前のアクセスを防止）
            _gameSectionPanel.Initialize(dbManager);
            _storeSectionPanel.Initialize(dbManager);
            _settingsSectionPanel.Initialize(dbManager);

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
            _logSectionPanel.Initialize(PathManager.BaseDirectory);
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
        /// `%TEMP%/GCTonePrism_update_*` を全部削除。今 update 中 (= Manager 起動中に Updater spawn 直後)
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

                UpdateStatusBar("自動バックアップを実行中...");
                BackupResult result = await Task.Run(() =>
                    dbManager.BackupService.RunAutoBackupIfDue(null, CancellationToken.None));

                if (result == null) return;

                if (result.IsSuccess)
                {
                    UpdateStatusBar($"自動バックアップ完了: {System.IO.Path.GetFileName(result.FilePath)}");
                    _backupSectionPanel.RefreshDisplay();
                }
                else if (result.IsFailed)
                {
                    UpdateStatusBar($"自動バックアップ失敗: {result.Message}");
                }
                // IsSkipped はそのまま（他PCで実行済み等、特に通知不要）
            }
            catch (Exception ex)
            {
                Logger.Error("[MainForm] StartAutoBackupIfDue エラー", ex);
                UpdateStatusBar($"自動バックアップエラー: {ex.Message}");
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

        private void UpdateStatusBar(string additionalInfo = null)
        {
            string dbStatus = dbManager.DatabaseExists() ? "接続済み" : "未接続";
            string gameInfo = additionalInfo ?? $"ゲーム数: {_gameSectionPanel.GameCount}件";
            lblStatus.Text = $"データベース: {dbStatus} | {gameInfo}";
        }

        // (#178 (b)) アップデート完了通知 dialog。2 invariant:
        // (1) sentinel ファイルは読込直後の `finally` で必ず削除 (parse 成功 / 失敗問わず、永続再表示バグ防止)。
        // (2) 起動時 dialog 数は常に 1 つに保つため、本 dialog 表示 (= true 返却) 時は caller が
        //     「同時起動に関する注意」MessageBox を skip する排他置換。仕様: SPECIFICATION.md §3.7.3。

        /// <summary>
        /// sentinel ファイル `<install>/.update_completed` を読んで完了 dialog を表示。
        /// 表示した場合 true (caller は同時起動注意 MessageBox を skip)、表示しなかった場合 false。
        /// </summary>
        private bool TryShowUpdateCompletedDialog()
        {
            string sentinelPath = System.IO.Path.Combine(PathManager.BaseDirectory, ".update_completed");
            if (!System.IO.File.Exists(sentinelPath)) return false;

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
                // parse 失敗 / newVersion 不在は dialog 出さず終了、caller は通常の同時起動注意 MessageBox を表示。
                return false;
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
            return true;
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
