using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using GCTonePrism.Manager.Models;
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
        }

        /// <summary>
        /// フォームロード時の処理
        /// </summary>
        private void AddGameForm_Load(object sender, EventArgs e)
        {
            // 難易度のコンボボックスを初期化
            cmbDifficulty.Items.Add("1 - 易しい");
            cmbDifficulty.Items.Add("2 - 普通");
            cmbDifficulty.Items.Add("3 - 難しい");
            cmbDifficulty.SelectedIndex = 1; // デフォルト: 普通

            // プレイ時間のコンボボックスを初期化
            cmbPlayTime.Items.Add("1 - ～5分");
            cmbPlayTime.Items.Add("2 - 5分～15分");
            cmbPlayTime.Items.Add("3 - 15分以上");
            cmbPlayTime.SelectedIndex = 1; // デフォルト: 5分～15分

            // デフォルト値の設定
            chkControllerSupport.Checked = false;
            numMinPlayers.Value = 1;
            numMaxPlayers.Value = 1;

            // ジャンルフィールドのプレースホルダー処理
            txtGenre.Enter += (s, args) =>
            {
                if (txtGenre.Text == "（カンマ区切りで複数入力可）")
                {
                    txtGenre.Text = "";
                }
            };
            txtGenre.Leave += (s, args) =>
            {
                if (string.IsNullOrWhiteSpace(txtGenre.Text))
                {
                    txtGenre.Text = "（カンマ区切りで複数入力可）";
                }
            };

            // 初期状態では警告を非表示、OKボタンを有効化
            lblGameIdWarning.Visible = false;
            btnOK.Enabled = true;

            // リリース年の初期値を今年に設定
            numReleaseYear.Value = DateTime.Now.Year;
        }

        /// <summary>
        /// ファイルを自動検出
        /// </summary>
        private void AutoDetectFiles()
        {
            if (string.IsNullOrEmpty(sourceGameFolder) || !Directory.Exists(sourceGameFolder))
            {
                return;
            }

            // 実行ファイルを自動検出
            var exeFiles = Directory.GetFiles(sourceGameFolder, "*.exe", SearchOption.AllDirectories);
            if (exeFiles.Length > 0)
            {
                // .console.exeを除外し、それ以外のexeファイルを優先
                var preferredExeFiles = exeFiles
                    .Where(file => !file.EndsWith(".console.exe", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                
                if (preferredExeFiles.Count > 0)
                {
                    // .console.exe以外のexeファイルがあれば、その中から最初のものを選択
                    txtExecutablePath.Text = preferredExeFiles[0];
                }
                else
                {
                    // .console.exeしかない場合は、それを使用
                    txtExecutablePath.Text = exeFiles[0];
                }
            }

            // サムネイル画像を自動検出
            var thumbnailPatterns = new[] { "thumbnail.png", "thumb.png", "thumb.jpg", "icon.png", "icon.jpg" };
            foreach (var pattern in thumbnailPatterns)
            {
                var files = Directory.GetFiles(sourceGameFolder, pattern, SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    txtThumbnailPath.Text = files[0];
                    break;
                }
            }

            // 背景画像/動画を自動検出
            var backgroundPatterns = new[] { "background.png", "background.jpg", "bg.png", "bg.jpg", "preview.png", "preview.jpg" };
            foreach (var pattern in backgroundPatterns)
            {
                var files = Directory.GetFiles(sourceGameFolder, pattern, SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    txtBackgroundPath.Text = files[0];
                    break;
                }
            }
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
                }
            }
        }

        /// <summary>
        /// ゲームフォルダをコピー
        /// </summary>
        private void CopyGameFolder(string gameId)
        {
            destinationGameFolder = PathManager.GetGameFolder(gameId);

            // 既に存在する場合はエラー
            if (Directory.Exists(destinationGameFolder))
            {
                throw new Exception($"ゲームフォルダ '{destinationGameFolder}' は既に存在します。");
            }

            // ゲームフォルダを作成
            Directory.CreateDirectory(destinationGameFolder);

            // フォルダ内のすべてのファイルとサブフォルダをコピー
            CopyDirectoryRecursive(sourceGameFolder, destinationGameFolder);
        }

        /// <summary>
        /// ディレクトリを再帰的にコピー
        /// </summary>
        private void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
                CopyDirectoryRecursive(subDir, destSubDir);
            }
        }

        /// <summary>
        /// 絶対パスから相対パスを取得（.NET Framework 4.8対応）
        /// </summary>
        private string GetRelativePath(string fromPath, string toPath)
        {
            if (string.IsNullOrEmpty(fromPath) || string.IsNullOrEmpty(toPath))
            {
                return toPath;
            }

            fromPath = Path.GetFullPath(fromPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            toPath = Path.GetFullPath(toPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(fromPath, toPath, StringComparison.OrdinalIgnoreCase))
            {
                return ".";
            }

            if (!toPath.StartsWith(fromPath, StringComparison.OrdinalIgnoreCase))
            {
                return toPath;
            }

            string relativePath = toPath.Substring(fromPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.IsNullOrEmpty(relativePath) ? "." : relativePath;
        }

        /// <summary>
        /// コピー元のパスをコピー先の絶対パスに変換
        /// </summary>
        private string ConvertSourcePathToDestinationPath(string sourcePath, string gameId)
        {
            if (string.IsNullOrEmpty(sourcePath))
            {
                return null;
            }

            string destinationGameFolder = PathManager.GetGameFolder(gameId);
            
            if (sourcePath.StartsWith(destinationGameFolder, StringComparison.OrdinalIgnoreCase))
            {
                // 既にコピー先フォルダ内のパス
                return sourcePath;
            }
            else if (sourcePath.StartsWith(sourceGameFolder, StringComparison.OrdinalIgnoreCase))
            {
                // コピー元フォルダ内のパス → コピー先の絶対パスに変換
                string relativePath = GetRelativePath(sourceGameFolder, sourcePath);
                return Path.Combine(destinationGameFolder, relativePath);
            }
            else
            {
                // その他のパスはそのまま（将来的にはエラーにするか検討）
                return sourcePath;
            }
        }

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
                // ゲームフォルダをコピー
                CopyGameFolder(gameId);

                // コピー元のパスをコピー先のパスに変換
                string executableAbsolutePath = ConvertSourcePathToDestinationPath(txtExecutablePath.Text.Trim(), gameId);
                string thumbnailAbsolutePath = string.IsNullOrWhiteSpace(txtThumbnailPath.Text) ? null : ConvertSourcePathToDestinationPath(txtThumbnailPath.Text.Trim(), gameId);
                string backgroundAbsolutePath = string.IsNullOrWhiteSpace(txtBackgroundPath.Text) ? null : ConvertSourcePathToDestinationPath(txtBackgroundPath.Text.Trim(), gameId);

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
                    ControllerSupport = chkControllerSupport.Checked,
                    ThumbnailPath = thumbnailAbsolutePath,
                    BackgroundPath = backgroundAbsolutePath,
                    ExecutablePath = executableAbsolutePath,
                    DisplayOrder = dbManager.GetMinDisplayOrder() - 1, // 既存の最小値より1小さい値（一番上に配置）
                    IsVisible = true, // 新規追加のゲームは常にランチャーに表示
                    Controls = null, // 後で実装
                    KeyMapping = null // 後で実装
                };

                // ジャンルを処理（カンマ区切り）
                if (!string.IsNullOrWhiteSpace(txtGenre.Text) && 
                    !txtGenre.Text.Contains("（カンマ区切りで複数入力可）"))
                {
                    game.Genre = txtGenre.Text.Split(',').Select(g => g.Trim()).Where(g => !string.IsNullOrEmpty(g)).ToList();
                }

                // データベースに追加
                dbManager.AddGame(game);

                AddedGame = game;
                DialogResult = DialogResult.OK;
                Close();
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
            var validPattern = new Regex(@"^[a-zA-Z0-9_-]+$");
            if (!validPattern.IsMatch(txtGameId.Text.Trim()))
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
    }
}

