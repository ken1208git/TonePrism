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
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // (#178 (b)) アップデート完了直後の起動 (= sentinel あり) は、通常の「同時起動に関する注意」
            // MessageBox を「✓ アップデート完了」MessageBox に **置換** する設計。起動 dialog 数は変わらず 1 つ、
            // user は完了 feedback を確実に受け取る。同時起動注意は次回 (sentinel なし) 起動から通常表示。
            if (!TryShowUpdateCompletedDialog())
            {
                MessageBox.Show(
                    "【重要】管理ソフトは必ず「1台のPC」だけで起動してください。\n\n複数のPCで同時に管理ソフトを開くと、データの保存に失敗したり、最悪の場合ファイルが破損して全てのデータが失われる可能性があります。\n（ランチャーは複数のPCで同時に動かしても大丈夫です）",
                    "同時起動に関する注意",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
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
            try
            {
                string json = System.IO.File.ReadAllText(sentinelPath, System.Text.Encoding.UTF8);
                var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                var dto = ser.Deserialize<UpdateCompletedSentinel>(json);
                if (dto != null) newVersion = dto.NewVersion;
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

            Services.Logger.Info("[MainForm] update_completed dialog 表示: v" + newVersion);
            MessageBox.Show(
                "アップデートが完了しました。\n\n" +
                "  新しいバージョン: v" + newVersion + "\n\n" +
                "新しい管理ソフトが起動しています。",
                "✓ アップデート完了",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return true;
        }

        private sealed class UpdateCompletedSentinel
        {
            /// <summary>
            /// 完了時刻 (ISO 8601 UTC、例: "2026-05-18T14:30:45Z")。**forensic 用** — File Explorer で
            /// sentinel ファイルを直接開いた時に時刻が読めるためだけに書き出す。consumer
            /// (`TryShowUpdateCompletedDialog`) は本 field を読み取らない (dialog 文言は `NewVersion`
            /// のみで構成)。`JavaScriptSerializer` の case-insensitive deserialize により JSON 上は
            /// `completedAt` (camelCase) でも `CompletedAt` (PascalCase) でも互換的に受理する。
            /// </summary>
            public string CompletedAt { get; set; }
            /// <summary>新バージョン (例: "0.9.2")。consumer はこの値を dialog 文言に embed する。</summary>
            public string NewVersion { get; set; }
        }
    }
}
