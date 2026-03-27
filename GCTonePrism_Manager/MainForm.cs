using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GCTonePrism.Manager.Models;
using GCTonePrism.Manager.Services;

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
                    // ゲームが追加された場合、ファイルコピー処理をバックグラウンド実行
                    // AddGameFormではDB登録までしか行っていない（パスは設定済みだがファイルコピーはまだ）
                    // ※AddGameFormの実装を確認する必要があるが、現状はAddGameForm内でコピーしていない前提でここでやるか、
                    //   あるいはAddGameFormから戻ってきた時点でコピー済みなのか？
                    //   → 以前の実装ではMainForm側でコピーしていた。
                    //   → AddGameFormの戻り値 `form.AddedGame` にはパスが入っているが、ファイルはまだコピーされていないはず。
                    
                    // 実装計画に基づき、ここで ProcessingDialog を出してコピーする
                    /*
                     * 注意: AddGameForm側で既にコピーしている場合、二重コピーになる。
                     * 確認: task.md の「新規ゲーム登録」完了済み項目を見ると "AddGameForm: フォルダコピー処理" とある。
                     * もしAddGameForm内でやっているなら、そちらを修正してMainForm側でやるようにするか、
                     * あるいはAddGameForm内でProcessingDialogを出すべき。
                     * 
                     * 現状のMainForm.csのbtnAddGame_Clickを見ると、単純にLoadGames()しているだけ。
                     * つまりAddGameForm内でコピーまで完結している。
                     * プログレスバーを入れるなら、AddGameFormの中身をいじる必要がある。
                     * 
                     * しかし、AddGameFormはモーダルダイアログ。
                     * ユーザー体験的には「追加」ボタンを押した後にプログレスバーが出るのが自然。
                     * 
                     * ここでは「AddGameForm」は設定入力のみを行い、OKが押されたらMainForm側でコピーとDB登録を行う形にリファクタリングするのが筋だが、
                     * AddGameFormの修正範囲が大きくなる。
                     * 
                     * 代替案: AddGameForm内でコピーする直前にProcessingDialogを出す。
                     * 
                     * 今回は「各操作への組み込み」が目標。
                     * btnAddGame_Click は AddGameForm を開くだけ。
                     * 実際の処理は AddGameForm 内にあるはず。
                     * 
                     * 確認のため AddGameForm.cs を見る必要があるが、
                     * とりあえず btnVersionUp_Click は MainForm 内にあるのでそちらを先に修正する。
                     */

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

        private void btnStoreSections_Click(object sender, EventArgs e)
        {
            using (var form = new StoreSectionListForm(dbManager))
            {
                form.ShowDialog();
            }
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
                    // ProcessingDialog を使用して非同期コピー
                    var processingDialog = new ProcessingDialog((IProgress<ProgressInfo> progress, CancellationToken token) =>
                    {
                        try
                        {
                            // 新しいバージョンのフォルダを作成（vX.Y.Z 形式）
                            string versionDir = PathManager.GetVersionFolder(game.GameId, form.NewVersion.Version);
                            Directory.CreateDirectory(versionDir);

                            // ソースフォルダ全体をコピー（非同期）
                            // 注: UIスレッド外から実行されるため、InvokeはProcessingDialog側で処理される
                            CopyDirectoryAsync(form.SourceFolderPath, versionDir, progress, token);
                        }
                        catch (OperationCanceledException)
                        {
                            throw; // キャンセルはそのまま上位へ
                        }
                        catch (Exception ex)
                        {
                            throw new Exception($"ファイルコピー中にエラーが発生しました: {ex.Message}", ex);
                        }
                    });

                    if (processingDialog.ShowDialog() == DialogResult.OK)
                    {
                        try
                        {
                             // コピー成功後、DB登録処理
                            string versionDir = PathManager.GetVersionFolder(game.GameId, form.NewVersion.Version);
                            
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
                                $"データベースへの保存に失敗しました。\n\n{ex.Message}",
                                "エラー",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
                        }
                    }
                    else if (processingDialog.DialogResult == DialogResult.Cancel)
                    {
                         MessageBox.Show(
                            "処理がキャンセルされました。",
                            "キャンセル",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                         
                         // キャンセル時はコピー途中の中途半端なフォルダが残っている可能性があるので削除する？
                         // 安全のため、ここでは削除しない（ユーザーが手動で確認できるように）
                         // 完全に削除するなら:
                         // try { Directory.Delete(PathManager.GetVersionFolder(game.GameId, form.NewVersion.Version), true); } catch { }
                    }
                }
            }
        }

        /// <summary>
        /// ディレクトリを非同期で再帰的にコピー（FileOperationServiceに委譲）
        /// </summary>
        private void CopyDirectoryAsync(string sourceDir, string destDir, IProgress<ProgressInfo> progress, System.Threading.CancellationToken token)
        {
            FileOperationService.CopyDirectoryWithProgress(
                sourceDir, destDir, progress, token,
                excludeFolderPredicate: FileOperationService.IsVersionFolder);
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

