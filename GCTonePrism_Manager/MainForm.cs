using System;
using System.Drawing;
using System.IO;
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
            
            // ウィンドウサイズを少し広げる（バージョンカラム追加のため）
            if (this.Width < 1100) this.Width = 1100;

            dbManager = new DatabaseManager();
        }

        /// <summary>
        /// フォームロード時の処理
        /// </summary>
        private void MainForm_Load(object sender, EventArgs e)
        {
            // 起動時に同時使用に関する警告を表示
            MessageBox.Show(
                "【重要】管理ソフトは必ず「1台のPC」だけで起動してください。\n\n複数のPCで同時に管理ソフトを開くと、データの保存に失敗したり、最悪の場合ファイルが破損して全てのデータが失われる可能性があります。\n（ランチャーは複数のPCで同時に動かしても大丈夫です）",
                "同時起動に関する注意",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

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
                // データベースが存在する場合でも、バージョンアップやカラム追加のために初期化（マイグレーション）を実行する
                // これを行わないと、新しく追加されたカラム（supported_connectionなど）が反映されずにエラーになる
                dbManager.InitializeDatabase();

                // ゲーム一覧を読み込む
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

            // 非表示にするカラム
            string[] hiddenColumns = { 
                "Description", "Genre", "MinPlayers", "MaxPlayers", 
                "Difficulty", "PlayTime", "ControllerSupport", "ThumbnailPath", 
                "BackgroundPath", "ExecutablePath", "Controls", 
                "KeyMapping", "Developers", "DisplayOrder", "SupportedConnection", "Arguments" 
            };

            foreach (var columnName in hiddenColumns)
            {
                if (dgvGames.Columns[columnName] != null)
                {
                    dgvGames.Columns[columnName].Visible = false;
                }
            }

            // 製作者情報カラムを追加（まだ存在しない場合）
            if (dgvGames.Columns["DevelopersDisplay"] == null)
            {
                var developersColumn = new DataGridViewTextBoxColumn
                {
                    Name = "DevelopersDisplay",
                    HeaderText = "製作者",
                    DataPropertyName = "DevelopersDisplay",
                    ReadOnly = true
                };
                
                dgvGames.Columns.Add(developersColumn);
            }

            // バージョンカラムを追加
            if (dgvGames.Columns["Version"] == null)
            {
                var versionColumn = new DataGridViewTextBoxColumn
                {
                    Name = "Version",
                    HeaderText = "バージョン",
                    DataPropertyName = "Version",
                    ReadOnly = true
                };
                
                dgvGames.Columns.Add(versionColumn);
            }

            // カラムの順序を設定（ゲームID → タイトル → リリース年 → 製作者 → バージョン → ランチャー表示）
            if (dgvGames.Columns["GameId"] != null)
                dgvGames.Columns["GameId"].DisplayIndex = 0;
            if (dgvGames.Columns["Title"] != null)
                dgvGames.Columns["Title"].DisplayIndex = 1;
            if (dgvGames.Columns["ReleaseYear"] != null)
                dgvGames.Columns["ReleaseYear"].DisplayIndex = 2;
            if (dgvGames.Columns["DevelopersDisplay"] != null)
                dgvGames.Columns["DevelopersDisplay"].DisplayIndex = 3;
            if (dgvGames.Columns["Version"] != null)
                dgvGames.Columns["Version"].DisplayIndex = 4;
            if (dgvGames.Columns["IsVisible"] != null)
                dgvGames.Columns["IsVisible"].DisplayIndex = 5;

            // 表示するカラムのヘッダーテキストを日本語に設定
            if (dgvGames.Columns["GameId"] != null)
                dgvGames.Columns["GameId"].HeaderText = "ゲームID";
            if (dgvGames.Columns["Title"] != null)
                dgvGames.Columns["Title"].HeaderText = "タイトル";
            if (dgvGames.Columns["ReleaseYear"] != null)
                dgvGames.Columns["ReleaseYear"].HeaderText = "リリース年";
            if (dgvGames.Columns["IsVisible"] != null)
                dgvGames.Columns["IsVisible"].HeaderText = "ランチャー表示";
            if (dgvGames.Columns["DevelopersDisplay"] != null)
                dgvGames.Columns["DevelopersDisplay"].HeaderText = "製作者";
            if (dgvGames.Columns["Version"] != null)
                dgvGames.Columns["Version"].HeaderText = "バージョン";

            // カラム幅の自動調整
            dgvGames.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvGames.Columns["GameId"].FillWeight = 80;
            dgvGames.Columns["Title"].FillWeight = 200;
            dgvGames.Columns["ReleaseYear"].FillWeight = 80;
            dgvGames.Columns["DevelopersDisplay"].FillWeight = 150;
            dgvGames.Columns["Version"].FillWeight = 80;
            dgvGames.Columns["IsVisible"].FillWeight = 100; // ランチャー表示のカラム幅を少し太く
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
            catch (System.Data.SQLite.SQLiteException ex)
            {
                string errorMessage = DatabaseManager.GetUserFriendlyErrorMessage(ex);
                MessageBox.Show(
                    $"表示順序の更新に失敗しました。\n\n{errorMessage}",
                    "データベースエラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
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
                catch (System.Data.SQLite.SQLiteException ex)
                {
                    string errorMessage = DatabaseManager.GetUserFriendlyErrorMessage(ex);
                    MessageBox.Show(
                        $"ゲームの削除に失敗しました。\n\n{errorMessage}",
                        "データベースエラー",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
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
        /// バージョンアップボタンクリック
        /// </summary>
        private void btnVersionUp_Click(object sender, EventArgs e)
        {
            if (dgvGames.SelectedRows.Count == 0)
            {
                MessageBox.Show(
                    "バージョンアップするゲームを選択してください。",
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

            // 現在のバージョン（最新のバージョン or 初期登録）を取得
            var latestVersion = dbManager.GetLatestVersion(game.GameId);
            string currentVersion = latestVersion?.Version ?? "1.0.0";

            using (var form = new VersionUpForm(game, currentVersion, latestVersion))
            {
                if (form.ShowDialog() == DialogResult.OK && form.NewVersion != null)
                {
                    try
                    {
                        // 新しいバージョンのフォルダを作成（vX.Y.Z 形式）
                        string versionDir = PathManager.GetVersionFolder(game.GameId, form.NewVersion.Version);
                        Directory.CreateDirectory(versionDir);
                        
                        // ソースフォルダ全体をコピー
                        CopyDirectory(form.SourceFolderPath, versionDir);
                        
                        // バージョン情報にコピー後の実行ファイルパスを設定（相対パス: vX.Y.Z/relative/path）
                        string versionFolderName = form.NewVersion.Version.StartsWith("v") ? form.NewVersion.Version : "v" + form.NewVersion.Version;
                        string relativePath = Path.Combine(versionFolderName, form.RelativeExecutablePath);
                        form.NewVersion.ExecutablePath = relativePath;
                        

                        // データベースに保存
                        dbManager.AddGameVersion(form.NewVersion);

                        // アクティブバージョンにするか確認
                        var activationResult = MessageBox.Show(
                            $"バージョン {form.NewVersion.Version} を現在のバージョン（アクティブ）として設定しますか？\n\n「いいえ」を選択した場合、バージョンは作成されますが、ランチャーで起動するバージョンは変更されません。",
                            "アクティブバージョンの確認",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question);

                        if (activationResult == DialogResult.Yes)
                        {
                            // メインのゲーム情報も更新（最新バージョンに合わせる）
                            form.UpdatedGameInfo.ExecutablePath = relativePath;
                            dbManager.UpdateGame(form.UpdatedGameInfo);
                        }
                        
                        MessageBox.Show(
                            $"ゲーム「{game.Title}」のバージョン {form.NewVersion.Version} を追加しました。",
                            "成功",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        
                        LoadGames();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            $"バージョンアップに失敗しました。\n\n{ex.Message}",
                            "エラー",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                }
            }
        }

        /// <summary>
        /// ファイル名として使用可能な文字列に変換
        /// </summary>
        private string CleanFileName(string fileName)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c, '_');
            }
            return fileName;
        }

        /// <summary>
        /// バージョンフォルダかどうかを判定（v + 数字 で始まるか）
        /// </summary>
        private bool IsVersionFolder(string folderName)
        {
            if (string.IsNullOrEmpty(folderName)) return false;
            // "v" または "V" で始まり、その次が数字である
            if (!folderName.StartsWith("v", StringComparison.OrdinalIgnoreCase)) return false;
            if (folderName.Length < 2) return false;
            return char.IsDigit(folderName[1]);
        }

        /// <summary>
        /// パスを正規化（\\?\ プレフィックスの除去と絶対パス化）
        /// </summary>
        private string NormalizePath(string path)
        {
            if (path.StartsWith(@"\\?\")) path = path.Substring(4);
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        /// <summary>
        /// ディレクトリを再帰的にコピー
        /// </summary>
        private void CopyDirectory(string sourceDir, string destDir)
        {
            // コピー先のパスが長い場合、\\?\ プレフィックスを付加して長いパスに対応
            string safeDestDir = destDir;
            if (safeDestDir.Length >= 240 && !safeDestDir.StartsWith(@"\\?\"))
            {
                safeDestDir = @"\\?\" + safeDestDir;
            }

            Directory.CreateDirectory(safeDestDir);
            
            // 無限ループ防止のため、絶対パスを正規化して比較
            string fullSourceDir = NormalizePath(sourceDir);
            string fullDestDir = NormalizePath(destDir);

            // コピー先がコピー元の中にある場合（親子関係）、コピー元がコピー先の中にある場合（逆親子）をガード
            if (fullDestDir.StartsWith(fullSourceDir, StringComparison.OrdinalIgnoreCase))
            {
                // コピー先がコピー元のサブフォルダの場合、その中身をコピーしようとすると無限ループになる可能性があるため注意が必要
                // ただし、destDir自体を除外すればよい
            }

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(file);
                string destFile = Path.Combine(safeDestDir, fileName);
                string sourceFile = file;

                // 長いパス対応
                if (destFile.Length >= 240 && !destFile.StartsWith(@"\\?\")) destFile = @"\\?\" + destFile;
                if (sourceFile.Length >= 240 && !sourceFile.StartsWith(@"\\?\")) sourceFile = @"\\?\" + sourceFile;

                File.Copy(sourceFile, destFile, true);
            }
            
            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string folderName = Path.GetFileName(subDir);
                string fullSubPath = NormalizePath(subDir);

                // ガード処理:
                // 1. コピー先ディレクトリ自体を除外（無限再帰防止）
                //    または コピー先がサブディレクトリの中にある場合も除外
                if (fullSubPath.Equals(fullDestDir, StringComparison.OrdinalIgnoreCase) || 
                    fullDestDir.StartsWith(fullSubPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // 2. バージョン管理フォルダ（vX.Y.Z）を除外
                //    コピー元として「ゲームのルートフォルダ」を選択した場合、
                //    その中の既存バージョンフォルダ（v1.0.0など）をコピーしないようにする。
                if (IsVersionFolder(folderName))
                {
                    continue;
                }

                string destSubDir = Path.Combine(safeDestDir, folderName);
                CopyDirectory(subDir, destSubDir);
            }
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
                
                // データベースバージョン情報を取得
                int targetVersion = dbManager.GetTargetDatabaseVersion();
                int actualVersion = dbManager.GetActualDatabaseVersion();
                
                versionInfo += $"\n\nデータベース\n";
                versionInfo += $"構造バージョン: v{actualVersion} (ターゲット: v{targetVersion})";
                
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

