using System;
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

        public MainForm()
        {
            InitializeComponent();
            dbManager = new DatabaseManager();

            _gameSectionPanel = new GameSectionPanel { Dock = DockStyle.Fill };
            _storeSectionPanel = new StoreSectionPanel { Dock = DockStyle.Fill };
            _settingsSectionPanel = new SettingsSectionPanel { Dock = DockStyle.Fill };

            _gameSectionPanel.StatusChanged += (msg) => UpdateStatusBar(msg);
            _settingsSectionPanel.DatabaseReset += OnDatabaseReset;

            tabGame.Controls.Add(_gameSectionPanel);
            tabStore.Controls.Add(_storeSectionPanel);
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

            _gameSectionPanel.LoadGames();
            UpdateStatusBar();
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
