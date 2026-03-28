using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GCTonePrism.Manager.Models;
using GCTonePrism.Manager.Services;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace GCTonePrism.Manager
{
    /// <summary>
    /// ゲーム追加フォーム
    /// </summary>
    public partial class AddGameForm : Form
    {
        private DatabaseManager dbManager;
        private string sourceGameFolder;
        private string destinationGameFolder;
        private List<DeveloperInfo> developers;
        private DeveloperListManager devListManager;
        private Label lblArgumentsPlaceholder;

        /// <summary>
        /// 追加されたゲーム情報（OKボタンがクリックされた場合のみ設定される）
        /// </summary>
        public GameInfo AddedGame { get; private set; }

        public AddGameForm(DatabaseManager dbManager)
        {
            InitializeComponent();
            this.dbManager = dbManager;
            AddedGame = null;
            sourceGameFolder = null;
            destinationGameFolder = null;
            developers = new List<DeveloperInfo>();
        }

        /// <summary>
        /// フォームロード時の処理
        /// </summary>
        private void AddGameForm_Load(object sender, EventArgs e)
        {
            // コンボボックスを初期化
            GameFormHelper.InitializeDifficultyCombo(cmbDifficulty);
            GameFormHelper.InitializePlayTimeCombo(cmbPlayTime);
            GameFormHelper.InitializeConnectionCombo(cmbSupportedConnection);

            // 起動オプションのプレースホルダー設定
            lblArgumentsPlaceholder = GameFormHelper.SetupArgumentsPlaceholder(txtArguments, this);

            // デフォルト値の設定
            chkControllerSupport.Checked = false;
            numMinPlayers.Value = 1;
            numMaxPlayers.Value = 1;

            
            // ジャンルチェックボックスリストを初期化
            clbGenre.Items.Clear();
            foreach (var genre in GenreList.AvailableGenres)
            {
                clbGenre.Items.Add(genre, false);
            }

            btnOK.Enabled = true;

            // リリース年の初期値を今年に設定
            numReleaseYear.Value = DateTime.Now.Year;

            // バージョンの初期値を設定
            txtVersion.Text = "v1.0.0";

            // 製作者情報のDataGridViewを初期化
            InitializeDevelopersGrid();
        }

        /// <summary>
        /// 製作者情報のDataGridViewを初期化
        /// </summary>
        private void InitializeDevelopersGrid()
        {
            devListManager = new DeveloperListManager(dgvDevelopers, developers);
            devListManager.InitializeGrid();
        }

        /// <summary>
        /// ファイルを自動検出
        /// </summary>
        private void AutoDetectFiles()
        {
            var (exePath, thumbPath, bgPath) = GameFormHelper.AutoDetectFiles(sourceGameFolder);
            if (exePath != null) txtExecutablePath.Text = exePath;
            if (thumbPath != null) txtThumbnailPath.Text = thumbPath;
            if (bgPath != null) txtBackgroundPath.Text = bgPath;
        }


        /// <summary>
        /// ゲームフォルダ選択ボタンクリック
        /// </summary>
        private void btnSelectGameFolder_Click(object sender, EventArgs e)
        {
            using (var dialog = new CommonOpenFileDialog())
            {
                dialog.IsFolderPicker = true;
                dialog.Title = "ゲームフォルダを選択してください";
                dialog.Multiselect = false;

                // 現在のパスが設定されている場合は、そこから開始
                if (!string.IsNullOrEmpty(txtGameFolder.Text) && Directory.Exists(txtGameFolder.Text))
                {
                    dialog.InitialDirectory = txtGameFolder.Text;
                }

                if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    sourceGameFolder = dialog.FileName;
                    txtGameFolder.Text = sourceGameFolder;
                    
                    // 自動検出を実行
                    AutoDetectFiles();
                }
            }
        }

        /// <summary>
        /// 実行ファイル選択ボタンクリック（コピー先フォルダから選択）
        /// </summary>
        private void btnSelectExecutable_Click(object sender, EventArgs e)
        {
            string searchFolder = sourceGameFolder;
            if (string.IsNullOrEmpty(searchFolder))
            {
                MessageBox.Show("先にゲームフォルダを選択してください。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var dialog = new OpenFileDialog())
            {
                dialog.InitialDirectory = searchFolder;
                dialog.Filter = "実行ファイル (*.exe)|*.exe|すべてのファイル (*.*)|*.*";
                dialog.Title = "実行ファイルを選択（コピー元フォルダ内から選択）";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtExecutablePath.Text = dialog.FileName;
                }
            }
        }

        /// <summary>
        /// サムネイル画像選択ボタンクリック（コピー先フォルダから選択）
        /// </summary>
        private void btnSelectThumbnail_Click(object sender, EventArgs e)
        {
            string searchFolder = sourceGameFolder;
            if (string.IsNullOrEmpty(searchFolder))
            {
                MessageBox.Show("先にゲームフォルダを選択してください。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var dialog = new OpenFileDialog())
            {
                dialog.InitialDirectory = searchFolder;
                dialog.Filter = "画像ファイル (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|すべてのファイル (*.*)|*.*";
                dialog.Title = "サムネイル画像を選択（コピー元フォルダ内から選択）";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtThumbnailPath.Text = dialog.FileName;
                    UpdateThumbnailPreview();
                }
            }
        }

        /// <summary>
        /// 背景画像選択ボタンクリック（コピー先フォルダから選択）
        /// </summary>
        private void btnSelectBackground_Click(object sender, EventArgs e)
        {
            string searchFolder = sourceGameFolder;
            if (string.IsNullOrEmpty(searchFolder))
            {
                MessageBox.Show("先にゲームフォルダを選択してください。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var dialog = new OpenFileDialog())
            {
                dialog.InitialDirectory = searchFolder;
                dialog.Filter = "画像ファイル (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|すべてのファイル (*.*)|*.*";
                dialog.Title = "背景画像を選択（コピー元フォルダ内から選択）";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtBackgroundPath.Text = dialog.FileName;
                    UpdateBackgroundPreview();
                }
            }
        }

        /// <summary>
        /// ゲームフォルダをコピー（バージョンフォルダ構造）
        /// </summary>
        private void CopyGameFolder(string gameId, string version, IProgress<ProgressInfo> progress, System.Threading.CancellationToken token)
        {
            // ゲームベースフォルダを作成
            string gameBaseFolder = PathManager.GetGameFolder(gameId);
            if (!Directory.Exists(gameBaseFolder))
            {
                Directory.CreateDirectory(gameBaseFolder);
            }
            
            // バージョンフォルダにコピー
            destinationGameFolder = PathManager.GetVersionFolder(gameId, version);

            // 既に存在する場合はエラー
            if (Directory.Exists(destinationGameFolder))
            {
                throw new Exception($"バージョンフォルダ '{destinationGameFolder}' は既に存在します。");
            }

            // バージョンフォルダを作成
            Directory.CreateDirectory(destinationGameFolder);
            
            // FileOperationServiceに委譲してコピー
            FileOperationService.CopyDirectoryWithProgress(sourceGameFolder, destinationGameFolder, progress, token);
        }


        // パス変換はPathConversionHelperに委譲

        /// <summary>
        /// OKボタンクリック
        /// </summary>
        private void btnOK_Click(object sender, EventArgs e)
        {
            // バリデーション
            if (!ValidateInput())
            {
                return;
            }

            string gameId = txtGameId.Text.Trim();

            try
            {
                // 初期バージョン番号
                string version = txtVersion.Text.Trim();
                
                // ProcessingDialog を使用して非同期コピー
                var processingDialog = new ProcessingDialog((IProgress<ProgressInfo> progress, CancellationToken token) =>
                {
                    try
                    {
                        CopyGameFolder(gameId, version, progress, token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"ファイルコピー中にエラーが発生しました: {ex.Message}", ex);
                    }
                });

                if (processingDialog.ShowDialog() != DialogResult.OK)
                {
                    // キャンセルまたはエラー時
                    if (processingDialog.DialogResult == DialogResult.Cancel)
                    {
                        MessageBox.Show("処理がキャンセルされました。", "キャンセル", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    
                    // コピー失敗時のロールバック（フォルダ削除）
                    if (!string.IsNullOrEmpty(destinationGameFolder) && Directory.Exists(destinationGameFolder))
                    {
                        try { Directory.Delete(destinationGameFolder, true); } catch { }
                    }
                    return;
                }

                destinationGameFolder = PathManager.GetVersionFolder(gameId, version);

                // コピー元のパスをコピー先の絶対パスに変換
                string executableAbsolutePath = PathConversionHelper.ConvertSourceToDestination(txtExecutablePath.Text.Trim(), sourceGameFolder, destinationGameFolder);
                string thumbnailAbsolutePath = string.IsNullOrWhiteSpace(txtThumbnailPath.Text) ? null : PathConversionHelper.ConvertSourceToDestination(txtThumbnailPath.Text.Trim(), sourceGameFolder, destinationGameFolder);
                string backgroundAbsolutePath = string.IsNullOrWhiteSpace(txtBackgroundPath.Text) ? null : PathConversionHelper.ConvertSourceToDestination(txtBackgroundPath.Text.Trim(), sourceGameFolder, destinationGameFolder);

                // デバッグログ（開発時のみ）
                Console.WriteLine($"[AddGameForm] コピー先フォルダ: {destinationGameFolder}");
                Console.WriteLine($"[AddGameForm] 実行ファイル絶対パス: {executableAbsolutePath}");
                Console.WriteLine($"[AddGameForm] サムネイル絶対パス: {thumbnailAbsolutePath}");
                Console.WriteLine($"[AddGameForm] 背景絶対パス: {backgroundAbsolutePath}");

                // コピー後にコピー先フォルダ（games/{game_id}/）からの相対パスに変換
                string executablePath = PathConversionHelper.ToRelativePathAfterCopy(executableAbsolutePath, destinationGameFolder);
                string thumbnailPath = PathConversionHelper.ToRelativePathAfterCopy(thumbnailAbsolutePath, destinationGameFolder);
                string backgroundPath = PathConversionHelper.ToRelativePathAfterCopy(backgroundAbsolutePath, destinationGameFolder);

                // デバッグログ（開発時のみ）
                Console.WriteLine($"[AddGameForm] 実行ファイル相対パス: {executablePath}");
                Console.WriteLine($"[AddGameForm] サムネイル相対パス: {thumbnailPath}");
                Console.WriteLine($"[AddGameForm] 背景相対パス: {backgroundPath}");

                // 起動オプション
                string arguments = txtArguments.Text;

                // GameInfoオブジェクトを作成
                var game = new GameInfo
                {
                    GameId = gameId,
                    Title = txtTitle.Text.Trim(),
                    Description = string.IsNullOrWhiteSpace(txtDescription.Text) ? null : txtDescription.Text.Trim(),
                    ReleaseYear = numReleaseYear.Value > 0 ? (int?)numReleaseYear.Value : null,
                    MinPlayers = numMinPlayers.Value > 0 ? (int?)numMinPlayers.Value : null,
                    MaxPlayers = numMaxPlayers.Value > 0 ? (int?)numMaxPlayers.Value : null,
                    Difficulty = cmbDifficulty.SelectedIndex >= 0 ? cmbDifficulty.SelectedIndex + 1 : (int?)null,
                    PlayTime = cmbPlayTime.SelectedIndex >= 0 ? cmbPlayTime.SelectedIndex + 1 : (int?)null,
                    SupportedConnection = cmbSupportedConnection.SelectedIndex >= 0 ? cmbSupportedConnection.SelectedIndex : 0,
                    ControllerSupport = chkControllerSupport.Checked,
                    ThumbnailPath = thumbnailPath,
                    BackgroundPath = backgroundPath,
                    ExecutablePath = executablePath,
                    Arguments = arguments,
                    DisplayOrder = dbManager.GetMinDisplayOrder() - 1, // 既存の最小値より1小さい値（一番上に配置）
                    IsVisible = true, // 新規追加のゲームは常にランチャーに表示
                    Controls = null, // 後で実装
                    KeyMapping = null, // 後で実装
                    Version = txtVersion.Text.Trim() // 初期バージョンを設定
                };

                // ジャンルを処理
                game.Genre = GameFormHelper.GetSelectedGenres(clbGenre);

                // 製作者情報を設定
                game.Developers = developers;

                // データベースに追加
                dbManager.AddGame(game);

                // 初期バージョン情報を追加
                var initialVersion = new GameVersion
                {
                    GameId = game.GameId,
                    Version = txtVersion.Text.Trim(),
                    ExecutablePath = game.ExecutablePath,
                    Description = "初期バージョン",
                    RegisteredAt = DateTime.Now
                };
                dbManager.AddGameVersion(initialVersion);

                AddedGame = game;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (System.Data.SQLite.SQLiteException ex)
            {
                // エラーが発生した場合、コピーしたフォルダを削除（ロールバック）
                if (!string.IsNullOrEmpty(destinationGameFolder) && Directory.Exists(destinationGameFolder))
                {
                    try
                    {
                        Directory.Delete(destinationGameFolder, true);
                    }
                    catch
                    {
                        // 削除に失敗しても続行
                    }
                }

                string errorMessage = DatabaseManager.GetUserFriendlyErrorMessage(ex);
                MessageBox.Show(
                    $"ゲームの追加に失敗しました。\n\n{errorMessage}",
                    "データベースエラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                // エラーが発生した場合、コピーしたフォルダを削除（ロールバック）
                if (!string.IsNullOrEmpty(destinationGameFolder) && Directory.Exists(destinationGameFolder))
                {
                    try
                    {
                        Directory.Delete(destinationGameFolder, true);
                    }
                    catch
                    {
                        // 削除に失敗しても続行
                    }
                }

                MessageBox.Show(
                    $"ゲームの追加に失敗しました。\n\n{ex.Message}",
                    "エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// キャンセルボタンクリック
        /// </summary>
        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        /// <summary>
        /// 入力値のバリデーション
        /// </summary>
        private bool ValidateInput()
        {
            // ゲームID
            if (string.IsNullOrWhiteSpace(txtGameId.Text))
            {
                MessageBox.Show("ゲームIDを入力してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtGameId.Focus();
                return false;
            }

            // ゲームIDの文字種チェック（英数字と一部の記号のみ許可）
            if (!GameFormHelper.IsValidGameId(txtGameId.Text))
            {
                MessageBox.Show("ゲームIDは英数字、アンダースコア（_）、ハイフン（-）のみ使用できます。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtGameId.Focus();
                return false;
            }

            // タイトル
            if (string.IsNullOrWhiteSpace(txtTitle.Text))
            {
                MessageBox.Show("タイトルを入力してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtTitle.Focus();
                return false;
            }

            // ゲームフォルダ（テキストボックスから取得）
            string gameFolderPath = txtGameFolder.Text.Trim();
            if (string.IsNullOrEmpty(gameFolderPath))
            {
                MessageBox.Show("ゲームフォルダを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                btnSelectGameFolder.Focus();
                return false;
            }

            if (!Directory.Exists(gameFolderPath))
            {
                MessageBox.Show("選択されたゲームフォルダが見つかりません。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                btnSelectGameFolder.Focus();
                return false;
            }

            // sourceGameFolderを更新
            sourceGameFolder = gameFolderPath;

            // 実行ファイルパス
            if (string.IsNullOrWhiteSpace(txtExecutablePath.Text))
            {
                MessageBox.Show("実行ファイルを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                btnSelectExecutable.Focus();
                return false;
            }

            // 実行ファイルが選択されたフォルダ内にあるか確認（ゲームフォルダが選択されている場合のみ）
            if (!string.IsNullOrEmpty(sourceGameFolder) && 
                !txtExecutablePath.Text.StartsWith(sourceGameFolder, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("実行ファイルは選択されたゲームフォルダ内のファイルを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                btnSelectExecutable.Focus();
                return false;
            }

            if (!File.Exists(txtExecutablePath.Text))
            {
                MessageBox.Show("選択された実行ファイルが見つかりません。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                btnSelectExecutable.Focus();
                return false;
            }

            // サムネイル画像パスのチェック（指定されている場合）
            if (!string.IsNullOrWhiteSpace(txtThumbnailPath.Text))
            {
                if (!File.Exists(txtThumbnailPath.Text))
                {
                    MessageBox.Show("選択されたサムネイル画像が見つかりません。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    btnSelectThumbnail.Focus();
                    return false;
                }
                if (!string.IsNullOrEmpty(sourceGameFolder) && 
                    !txtThumbnailPath.Text.StartsWith(sourceGameFolder, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("サムネイル画像は選択されたゲームフォルダ内のファイルを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    btnSelectThumbnail.Focus();
                    return false;
                }
            }

            // 背景画像パスのチェック（指定されている場合）
            if (!string.IsNullOrWhiteSpace(txtBackgroundPath.Text))
            {
                if (!File.Exists(txtBackgroundPath.Text))
                {
                    MessageBox.Show("選択された背景画像が見つかりません。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    btnSelectBackground.Focus();
                    return false;
                }
                if (!string.IsNullOrEmpty(sourceGameFolder) && 
                    !txtBackgroundPath.Text.StartsWith(sourceGameFolder, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("背景画像は選択されたゲームフォルダ内のファイルを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    btnSelectBackground.Focus();
                    return false;
                }
            }

            // ゲームIDの重複チェック
            var existingGame = dbManager.GetGameById(txtGameId.Text.Trim());
            if (existingGame != null)
            {
                MessageBox.Show("このゲームIDは既に使用されています。別のIDを入力してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtGameId.Focus();
                return false;
            }

            return true;
        }

        private void btnAddDeveloper_Click(object sender, EventArgs e) => devListManager.Add();
        private void btnEditDeveloper_Click(object sender, EventArgs e) => devListManager.Edit();
        private void btnDeleteDeveloper_Click(object sender, EventArgs e) => devListManager.Delete();

        private void UpdateThumbnailPreview() => ImagePreviewHelper.UpdatePreview(picThumbnailPreview, txtThumbnailPath.Text);
        private void UpdateBackgroundPreview() => ImagePreviewHelper.UpdatePreview(picBackgroundPreview, txtBackgroundPath.Text);

        private void btnTestRun_Click(object sender, EventArgs e) =>
            GameFormHelper.TestRunGame(txtExecutablePath.Text.Trim(), txtArguments.Text);
    }
}

