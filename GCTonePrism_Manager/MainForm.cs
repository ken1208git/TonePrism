using System;
using System.Drawing;
using System.Reflection;
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
        /// データベースリセットメニュークリック
        /// </summary>
        private void menuItemResetDatabase_Click(object sender, EventArgs e)
        {
            // カスタム確認フォームを表示（ランダム文字列入力、ボタンが逃げる）
            using (var confirmForm = new ResetDatabaseConfirmForm())
            {
                if (confirmForm.ShowDialog() == DialogResult.Yes)
                {
                    try
                    {
                        // データベースをリセット
                        dbManager.ResetDatabase();

                        MessageBox.Show(
                            "データベースのリセットが完了しました。",
                            "成功",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);

                        // 一覧を更新
                        LoadGames();
                        UpdateStatusBar();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            $"データベースのリセットに失敗しました。\n\n{ex.Message}",
                            "エラー",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                }
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
                
                // デバッグ: コンソールに詳細情報を出力（相対パスを表示）
                Console.WriteLine("\n=== データベース確認 ===");
                foreach (var game in games)
                {
                    Console.WriteLine($"ゲームID: {game.GameId}");
                    Console.WriteLine($"タイトル: {game.Title}");
                    
                    // 実行ファイルパス（データベースに保存されている相対パスを表示）
                    string executablePath = game.ExecutablePath ?? "(未設定)";
                    if (!string.IsNullOrEmpty(executablePath) && System.IO.Path.IsPathRooted(executablePath))
                    {
                        // 絶対パスの場合は警告を表示
                        Console.WriteLine($"実行ファイル: {executablePath} [警告: 絶対パスが保存されています]");
                    }
                    else
                    {
                        Console.WriteLine($"実行ファイル: {executablePath} [相対パス]");
                    }
                    
                    // サムネイルパス
                    string thumbnailPath = string.IsNullOrEmpty(game.ThumbnailPath) ? "(未設定)" : game.ThumbnailPath;
                    if (!string.IsNullOrEmpty(game.ThumbnailPath) && System.IO.Path.IsPathRooted(game.ThumbnailPath))
                    {
                        Console.WriteLine($"サムネイル: {thumbnailPath} [警告: 絶対パスが保存されています]");
                    }
                    else
                    {
                        Console.WriteLine($"サムネイル: {thumbnailPath} {(string.IsNullOrEmpty(game.ThumbnailPath) ? "" : "[相対パス]")}");
                    }
                    
                    // 背景パス
                    string backgroundPath = string.IsNullOrEmpty(game.BackgroundPath) ? "(未設定)" : game.BackgroundPath;
                    if (!string.IsNullOrEmpty(game.BackgroundPath) && System.IO.Path.IsPathRooted(game.BackgroundPath))
                    {
                        Console.WriteLine($"背景: {backgroundPath} [警告: 絶対パスが保存されています]");
                    }
                    else
                    {
                        Console.WriteLine($"背景: {backgroundPath} {(string.IsNullOrEmpty(game.BackgroundPath) ? "" : "[相対パス]")}");
                    }
                    
                    Console.WriteLine($"表示: {game.IsVisible}");
                    Console.WriteLine($"表示順: {game.DisplayOrder}");
                    Console.WriteLine("---");
                }
                Console.WriteLine($"合計: {games.Count}件\n");
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
            dgvGames.Columns["IsVisible"].HeaderText = "ランチャー表示";

            // 非表示にするカラム
            string[] hiddenColumns = { 
                "Description", "Genre", "MinPlayers", "MaxPlayers", 
                "Difficulty", "PlayTime", "ControllerSupport", "ThumbnailPath", 
                "BackgroundPath", "ExecutablePath", "Controls", 
                "KeyMapping", "Developers", "DisplayOrder" 
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
            dgvGames.Columns["GameId"].FillWeight = 100;
            dgvGames.Columns["Title"].FillWeight = 250;
            dgvGames.Columns["ReleaseYear"].FillWeight = 80;
            dgvGames.Columns["IsVisible"].FillWeight = 120;
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

        private int dragRowIndex = -1;

        /// <summary>
        /// ドラッグ開始（マウスダウン時）
        /// </summary>
        private void dgvGames_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                DataGridView.HitTestInfo hitTest = dgvGames.HitTest(e.X, e.Y);
                if (hitTest.Type == DataGridViewHitTestType.Cell || hitTest.Type == DataGridViewHitTestType.RowHeader)
                {
                    dragRowIndex = hitTest.RowIndex;
                    if (dragRowIndex >= 0 && dragRowIndex < dgvGames.Rows.Count)
                    {
                        dgvGames.DoDragDrop(dragRowIndex, DragDropEffects.Move);
                    }
                }
            }
        }

        /// <summary>
        /// ドラッグオーバー時（ドロップ可能かチェック）
        /// </summary>
        private void dgvGames_DragOver(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Move;
        }

        /// <summary>
        /// ドロップ時（行を移動してdisplay_orderを更新）
        /// </summary>
        private void dgvGames_DragDrop(object sender, DragEventArgs e)
        {
            try
            {
                Point clientPoint = dgvGames.PointToClient(new Point(e.X, e.Y));
                DataGridView.HitTestInfo hitTest = dgvGames.HitTest(clientPoint.X, clientPoint.Y);

                if (hitTest.RowIndex >= 0 && hitTest.RowIndex < dgvGames.Rows.Count)
                {
                    int dropRowIndex = hitTest.RowIndex;

                    if (dragRowIndex >= 0 && dragRowIndex != dropRowIndex && dragRowIndex < dgvGames.Rows.Count)
                    {
                        // データソースからGameInfoを取得
                        var sourceGame = dgvGames.Rows[dragRowIndex].DataBoundItem as GameInfo;
                        var games = dbManager.GetAllGames();

                        if (sourceGame != null)
                        {
                            // 行を移動
                            games.RemoveAt(dragRowIndex);
                            games.Insert(dropRowIndex, sourceGame);

                            // display_orderを更新（0から始まる連番）
                            for (int i = 0; i < games.Count; i++)
                            {
                                games[i].DisplayOrder = i;
                                dbManager.UpdateGame(games[i]);
                            }

                            // 一覧を再読み込み
                            LoadGames();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"表示順序の更新に失敗しました。\n\n{ex.Message}",
                    "エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                dragRowIndex = -1;
            }
        }

        /// <summary>
        /// ゲーム追加ボタンクリック
        /// </summary>
        private void btnAddGame_Click(object sender, EventArgs e)
        {
            using (var form = new AddGameForm(dbManager))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    // ゲームが追加された場合、一覧を更新
                    LoadGames();
                    MessageBox.Show(
                        $"ゲーム「{form.AddedGame.Title}」を追加しました。",
                        "成功",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
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

            var selectedGame = dgvGames.SelectedRows[0].DataBoundItem as GameInfo;
            if (selectedGame == null) return;

            // データベースから最新のゲーム情報を取得
            var game = dbManager.GetGameById(selectedGame.GameId);
            if (game == null)
            {
                MessageBox.Show(
                    "選択されたゲームが見つかりません。",
                    "エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            using (var form = new EditGameForm(dbManager, game))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    // ゲームが更新された場合、一覧を更新
                    LoadGames();
                    MessageBox.Show(
                        $"ゲーム「{form.EditedGame.Title}」を更新しました。",
                        "成功",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
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

        /// <summary>
        /// バージョン情報メニュークリック
        /// </summary>
        private void menuItemVersionInfo_Click(object sender, EventArgs e)
        {
            try
            {
                // アセンブリ情報を取得
                Assembly assembly = Assembly.GetExecutingAssembly();
                AssemblyName assemblyName = assembly.GetName();
                Version version = assemblyName.Version;

                // アセンブリの詳細情報を取得
                string productName = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "GCTonePrism 管理ソフト";
                string copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";
                string company = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "";

                // バージョン情報を構築
                string versionInfo = $"バージョン情報\n\n";
                versionInfo += $"製品名: {productName}\n";
                versionInfo += $"バージョン: {version.Major}.{version.Minor}.{version.Build}";
                if (version.Revision > 0)
                {
                    versionInfo += $".{version.Revision}";
                }
                versionInfo += "\n\n";
                
                if (!string.IsNullOrEmpty(company))
                {
                    versionInfo += $"会社: {company}\n";
                }
                if (!string.IsNullOrEmpty(copyright))
                {
                    versionInfo += $"{copyright}\n";
                }

                // メッセージボックスで表示
                MessageBox.Show(
                    versionInfo,
                    "バージョン情報",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"バージョン情報の取得に失敗しました。\n\n{ex.Message}",
                    "エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}

