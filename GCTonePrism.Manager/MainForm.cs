using System;
using System.Windows.Forms;
using GCTonePrism.Manager.Models;

namespace GCTonePrism.Manager
{
    /// <summary>
    /// メインフォーム - ゲーム管理画面
    /// </summary>
    public partial class MainForm : Form
    {
        private DatabaseManager dbManager;

        public MainForm()
        {
            InitializeComponent();
            dbManager = new DatabaseManager();
        }

        /// <summary>
        /// フォームロード時の処理
        /// </summary>
        private void MainForm_Load(object sender, EventArgs e)
        {
            // データベースが存在するか確認
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
                }
            }
            else
            {
                // データベースが存在する場合、ゲーム一覧を読み込む
                LoadGames();
            }

            UpdateStatusBar();
        }

        /// <summary>
        /// データベース初期化ボタンクリック
        /// </summary>
        private void btnInitDatabase_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "データベースを初期化します。\n既存のデータは保持されます。\nよろしいですか？",
                "データベース初期化",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                InitializeDatabase();
            }
        }

        /// <summary>
        /// データベースを初期化
        /// </summary>
        private void InitializeDatabase()
        {
            try
            {
                // 必要なディレクトリを作成
                PathManager.EnsureDirectoriesExist();

                // データベース初期化
                dbManager.InitializeDatabase();

                MessageBox.Show(
                    "データベースの初期化が完了しました。",
                    "成功",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                LoadGames();
                UpdateStatusBar();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"データベースの初期化に失敗しました。\n\n{ex.Message}",
                    "エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// ゲーム一覧を読み込む
        /// </summary>
        private void LoadGames()
        {
            try
            {
                var games = dbManager.GetAllGames();
                
                // DataGridViewにバインド
                dgvGames.DataSource = null;
                dgvGames.DataSource = games;

                // カラムの表示設定
                ConfigureDataGridView();

                UpdateStatusBar();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"ゲーム一覧の読み込みに失敗しました。\n\n{ex.Message}",
                    "エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// DataGridViewの表示設定
        /// </summary>
        private void ConfigureDataGridView()
        {
            if (dgvGames.Columns.Count == 0) return;

            // 表示するカラムを設定
            dgvGames.Columns["GameId"].HeaderText = "ゲームID";
            dgvGames.Columns["Title"].HeaderText = "タイトル";
            dgvGames.Columns["ReleaseYear"].HeaderText = "リリース年";
            dgvGames.Columns["Difficulty"].HeaderText = "難易度";
            dgvGames.Columns["IsVisible"].HeaderText = "表示";
            dgvGames.Columns["DisplayOrder"].HeaderText = "表示順";

            // 非表示にするカラム
            string[] hiddenColumns = { 
                "Description", "Genre", "MinPlayers", "MaxPlayers", 
                "PlayTime", "ControllerSupport", "ThumbnailPath", 
                "BackgroundPath", "ExecutablePath", "Controls", 
                "KeyMapping", "Developers" 
            };

            foreach (var columnName in hiddenColumns)
            {
                if (dgvGames.Columns[columnName] != null)
                {
                    dgvGames.Columns[columnName].Visible = false;
                }
            }

            // カラム幅の自動調整
            dgvGames.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvGames.Columns["GameId"].FillWeight = 80;
            dgvGames.Columns["Title"].FillWeight = 150;
            dgvGames.Columns["ReleaseYear"].FillWeight = 60;
            dgvGames.Columns["Difficulty"].FillWeight = 50;
            dgvGames.Columns["IsVisible"].FillWeight = 40;
            dgvGames.Columns["DisplayOrder"].FillWeight = 60;
        }

        /// <summary>
        /// ステータスバーを更新
        /// </summary>
        private void UpdateStatusBar()
        {
            string dbStatus = dbManager.DatabaseExists() ? "接続済み" : "未接続";
            int gameCount = dgvGames.Rows.Count;
            
            lblStatus.Text = $"データベース: {dbStatus} | ゲーム数: {gameCount}件";
        }

        /// <summary>
        /// ゲーム追加ボタンクリック
        /// </summary>
        private void btnAddGame_Click(object sender, EventArgs e)
        {
            MessageBox.Show(
                "ゲーム追加フォームは次のステップで実装します。",
                "情報",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        /// <summary>
        /// ゲーム編集ボタンクリック
        /// </summary>
        private void btnEditGame_Click(object sender, EventArgs e)
        {
            if (dgvGames.SelectedRows.Count == 0)
            {
                MessageBox.Show(
                    "編集するゲームを選択してください。",
                    "情報",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            MessageBox.Show(
                "ゲーム編集フォームは次のステップで実装します。",
                "情報",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        /// <summary>
        /// ゲーム削除ボタンクリック
        /// </summary>
        private void btnDeleteGame_Click(object sender, EventArgs e)
        {
            if (dgvGames.SelectedRows.Count == 0)
            {
                MessageBox.Show(
                    "削除するゲームを選択してください。",
                    "情報",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var selectedGame = dgvGames.SelectedRows[0].DataBoundItem as GameInfo;
            if (selectedGame == null) return;

            var result = MessageBox.Show(
                $"ゲーム「{selectedGame.Title}」を削除しますか？\nこの操作は取り消せません。",
                "削除確認",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                try
                {
                    dbManager.DeleteGame(selectedGame.GameId);
                    MessageBox.Show(
                        "ゲームを削除しました。",
                        "成功",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    
                    LoadGames();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"ゲームの削除に失敗しました。\n\n{ex.Message}",
                        "エラー",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>
        /// 更新ボタンクリック
        /// </summary>
        private void btnRefresh_Click(object sender, EventArgs e)
        {
            LoadGames();
        }
    }
}

