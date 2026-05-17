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

        public MainForm()
        {
            InitializeComponent();
            dbManager = new DatabaseManager();

            _gameSectionPanel = new GameSectionPanel { Dock = DockStyle.Fill };
            _storeSectionPanel = new StoreSectionPanel { Dock = DockStyle.Fill };
            _settingsSectionPanel = new SettingsSectionPanel { Dock = DockStyle.Fill };
            _backupSectionPanel = new BackupSectionPanel { Dock = DockStyle.Fill };
            _logSectionPanel = new LogSectionPanel { Dock = DockStyle.Fill };

            _gameSectionPanel.StatusChanged += (msg) => UpdateStatusBar(msg);
            _settingsSectionPanel.DatabaseReset += OnDatabaseReset;
            _backupSectionPanel.DatabaseChanged += OnDatabaseRestored;

            tabGame.Controls.Add(_gameSectionPanel);
            tabStore.Controls.Add(_storeSectionPanel);
            tabBackup.Controls.Add(_backupSectionPanel);
            tabLog.Controls.Add(_logSectionPanel);
            tabSettings.Controls.Add(_settingsSectionPanel);
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            MessageBox.Show(
                "【重要】管理ソフトは必ず「1台のPC」だけで起動してください。\n\n複数のPCで同時に管理ソフトを開くと、データの保存に失敗したり、最悪の場合ファイルが破損して全てのデータが失われる可能性があります。\n（ランチャーは複数のPCで同時に動かしても大丈夫です）",
                "同時起動に関する注意",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

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

            _gameSectionPanel.LoadGames();
            UpdateStatusBar();

            // 起動時に自動バックアップが必要なら走らせる（バックグラウンドで非ブロッキング）
            StartAutoBackupIfDue();
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
    }
}
