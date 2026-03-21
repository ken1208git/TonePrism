using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GCTonePrism.Manager.Models;
using GCTonePrism.Manager.Services;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace GCTonePrism.Manager
{
    public partial class VersionUpForm : Form
    {
        private string gameId;
        private string currentVersion;
        private string selectedFolderPath;
        private List<DeveloperInfo> developers;
        private DeveloperListManager devListManager;
        private GameVersion baseVersion; // コピー元のバージョン情報
        private GameInfo originalGameInfo; // 元のゲーム情報

        /// <summary>
        /// 作成された新しいバージョン情報
        /// </summary>
        public GameVersion NewVersion { get; private set; }

        /// <summary>
        /// 選択されたゲームフォルダのパス（コピー元）
        /// </summary>
        public string SourceFolderPath { get; private set; }

        /// <summary>
        /// 選択された実行ファイルの相対パス（フォルダ内での相対パス）
        /// </summary>
        public string RelativeExecutablePath { get; private set; }

        /// <summary>
        /// 更新されたゲーム情報（タイトル、説明など）
        /// </summary>
        public GameInfo UpdatedGameInfo { get; private set; }

        /// <summary>
        /// 更新された製作者リスト
        /// </summary>
        public List<DeveloperInfo> UpdatedDevelopers => developers;

        public VersionUpForm(GameInfo gameInfo, string currentVersion, GameVersion baseVersion = null)
        {
            InitializeComponent();
            this.originalGameInfo = gameInfo;
            this.gameId = gameInfo.GameId;
            this.currentVersion = currentVersion;
            this.baseVersion = baseVersion;
            developers = new List<DeveloperInfo>();
            
            lblCurrentVersion.Text = currentVersion;
            NewVersion = null;
        }

        private void VersionUpForm_Load(object sender, EventArgs e)
        {
            // 難易度のコンボボックスを初期化
            cmbDifficulty.Items.Add("1 - 易しい");
            cmbDifficulty.Items.Add("2 - 普通");
            cmbDifficulty.Items.Add("3 - 難しい");
            
            // プレイ時間のコンボボックスを初期化
            cmbPlayTime.Items.Add("1 - ～5分");
            cmbPlayTime.Items.Add("2 - 5分～15分");
            cmbPlayTime.Items.Add("3 - 15分以上");

            // 通信プレイ対応のコンボボックスを初期化
            cmbSupportedConnection.Items.Add("なし（1台で遊ぶ）");
            cmbSupportedConnection.Items.Add("ローカル通信（部室のLANで対戦）");
            cmbSupportedConnection.Items.Add("オンライン通信（インターネット対戦）");

            // ジャンルチェックボックスリストを初期化
            clbGenre.Items.Clear();
            foreach (var genre in GenreList.AvailableGenres)
            {
                clbGenre.Items.Add(genre, false);
            }

            if (baseVersion != null)
            {
                // 既存のバージョンがある場合はコピーする
                txtTitle.Text = baseVersion.Title;
                txtDescription.Text = baseVersion.Description; // ゲーム説明文をコピー
                txtArguments.Text = baseVersion.Arguments;
                txtUpdateNote.Text = ""; // 更新内容は空欄にする（ユーザー要望）
                
                // コンボボックス等の選択
                if (baseVersion.Difficulty.HasValue && baseVersion.Difficulty.Value >= 1 && baseVersion.Difficulty.Value <= 3)
                    cmbDifficulty.SelectedIndex = baseVersion.Difficulty.Value - 1;
                else
                    cmbDifficulty.SelectedIndex = 1;

                if (baseVersion.PlayTime.HasValue && baseVersion.PlayTime.Value >= 1 && baseVersion.PlayTime.Value <= 3)
                    cmbPlayTime.SelectedIndex = baseVersion.PlayTime.Value - 1;
                else
                    cmbPlayTime.SelectedIndex = 1;

                numMinPlayers.Value = baseVersion.MinPlayers ?? 1;
                numMaxPlayers.Value = baseVersion.MaxPlayers ?? 1;
                chkControllerSupport.Checked = baseVersion.ControllerSupport;
                
                if (baseVersion.SupportedConnection >= 0 && baseVersion.SupportedConnection < cmbSupportedConnection.Items.Count)
                    cmbSupportedConnection.SelectedIndex = baseVersion.SupportedConnection;
                else
                    cmbSupportedConnection.SelectedIndex = 0;

                // ジャンルのチェック
                if (baseVersion.Genre != null)
                {
                    for (int i = 0; i < clbGenre.Items.Count; i++)
                    {
                        string genre = clbGenre.Items[i].ToString();
                        if (baseVersion.Genre.Contains(genre))
                        {
                            clbGenre.SetItemChecked(i, true);
                        }
                    }
                }

                // 製作者情報のコピー (Deep Copy)
                if (baseVersion.Developers != null)
                {
                    foreach (var dev in baseVersion.Developers)
                    {
                        developers.Add(new DeveloperInfo
                        {
                            // IDはコピーしない（新規作成のため）
                            GameId = dev.GameId,
                            LastName = dev.LastName,
                            FirstName = dev.FirstName,
                            Grade = dev.Grade
                        });
                    }
                }

                // パス情報は初期値としてセットしておく（変更なければそのまま使われる可能性があるが、通常は新しいフォルダになるので再設定推奨ではある）
                // ただし、VersionUpFormのロジック上、SourceFolderPath（コピー元）が設定されると、そこからの相対パスとして扱われる。
                // ここでは表示のみ行っておく。
                // 実際にはユーザーが「フォルダ選択」を行うことが前提のフローになっているため、
                // パスだけ入れてもコピー元フォルダが決まらないと意味がないかもしれない。
                // しかし「あらゆる要素」をコピペという要望なので入れておく。
                txtExecutablePath.Text = baseVersion.ExecutablePath;
                txtThumbnailPath.Text = baseVersion.ThumbnailPath;
                txtBackgroundPath.Text = baseVersion.BackgroundPath;
            }
            else
            {
                // デフォルト値
                cmbDifficulty.SelectedIndex = 1;
                cmbPlayTime.SelectedIndex = 1;
                cmbSupportedConnection.SelectedIndex = 0;
                chkControllerSupport.Checked = false;
                numMinPlayers.Value = 1;
                numMaxPlayers.Value = 1;
            }

            // 製作者情報のDataGridViewを初期化
            InitializeDevelopersGrid();
        }

        private void InitializeDevelopersGrid()
        {
            devListManager = new DeveloperListManager(dgvDevelopers, developers);
            devListManager.InitializeGrid();
        }

        private void btnSelectGameFolder_Click(object sender, EventArgs e)
        {
            using (var dialog = new CommonOpenFileDialog())
            {
                dialog.IsFolderPicker = true;
                dialog.Title = "新しいバージョンのゲームフォルダを選択してください";

                if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    selectedFolderPath = dialog.FileName;
                    txtGameFolder.Text = selectedFolderPath;
                    SourceFolderPath = selectedFolderPath;
                    
                    // フォルダ選択後、ファイルのパスをクリアして自動検出
                    txtExecutablePath.Text = "";
                    txtThumbnailPath.Text = "";
                    txtBackgroundPath.Text = "";
                    RelativeExecutablePath = "";
                    
                    AutoDetectFiles();
                }
            }
        }

        /// <summary>
        /// ファイルを自動検出
        /// </summary>
        private void AutoDetectFiles()
        {
            if (string.IsNullOrEmpty(selectedFolderPath) || !Directory.Exists(selectedFolderPath))
            {
                return;
            }

            // 実行ファイルを自動検出
            var exeFiles = Directory.GetFiles(selectedFolderPath, "*.exe", SearchOption.AllDirectories);
            if (exeFiles.Length > 0)
            {
                var excludedPatterns = new[]
                {
                    ".console.exe",
                    "UnityCrashHandler64.exe",
                    "UnityCrashHandler32.exe",
                    "UnityCrashHandler.exe",
                    "CrashHandler.exe",
                    "CrashHandler64.exe",
                    "CrashHandler32.exe"
                };

                var preferredExeFiles = exeFiles
                    .Where(file =>
                    {
                        string fileName = Path.GetFileName(file);
                        return !excludedPatterns.Any(pattern =>
                            fileName.EndsWith(pattern, StringComparison.OrdinalIgnoreCase));
                    })
                    .ToList();
                
                if (preferredExeFiles.Count > 0)
                {
                    txtExecutablePath.Text = preferredExeFiles[0];
                    RelativeExecutablePath = preferredExeFiles[0].Substring(selectedFolderPath.Length).TrimStart(Path.DirectorySeparatorChar);
                }
                else if (exeFiles.Length > 0)
                {
                    txtExecutablePath.Text = exeFiles[0];
                    RelativeExecutablePath = exeFiles[0].Substring(selectedFolderPath.Length).TrimStart(Path.DirectorySeparatorChar);
                }
            }

            // サムネイル画像を自動検出
            var thumbnailPatterns = new[] { "thumbnail.png", "thumb.png", "thumb.jpg", "icon.png", "icon.jpg" };
            foreach (var pattern in thumbnailPatterns)
            {
                var files = Directory.GetFiles(selectedFolderPath, pattern, SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    txtThumbnailPath.Text = files[0];
                    break;
                }
            }

            // 背景画像を自動検出
            var backgroundPatterns = new[] { "background.png", "background.jpg", "bg.png", "bg.jpg", "preview.png", "preview.jpg" };
            foreach (var pattern in backgroundPatterns)
            {
                var files = Directory.GetFiles(selectedFolderPath, pattern, SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    txtBackgroundPath.Text = files[0];
                    break;
                }
            }
        }

        private void btnSelectExecutable_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(selectedFolderPath))
            {
                MessageBox.Show("先にゲームフォルダを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var dialog = new OpenFileDialog())
            {
                dialog.InitialDirectory = selectedFolderPath;
                dialog.Filter = "実行ファイル (*.exe)|*.exe|すべてのファイル (*.*)|*.*";
                dialog.Title = "実行ファイルを選択してください";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    string selectedPath = dialog.FileName;
                    
                    if (!selectedPath.StartsWith(selectedFolderPath, StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show(
                            "実行ファイルはゲームフォルダ内から選択してください。",
                            "入力エラー",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }
                    
                    txtExecutablePath.Text = selectedPath;
                    RelativeExecutablePath = selectedPath.Substring(selectedFolderPath.Length).TrimStart(Path.DirectorySeparatorChar);
                }
            }
        }

        private void btnSelectThumbnail_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(selectedFolderPath))
            {
                MessageBox.Show("先にゲームフォルダを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var dialog = new OpenFileDialog())
            {
                dialog.InitialDirectory = selectedFolderPath;
                dialog.Filter = "画像ファイル (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|すべてのファイル (*.*)|*.*";
                dialog.Title = "サムネイル画像を選択";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtThumbnailPath.Text = dialog.FileName;
                    UpdateThumbnailPreview();
                }
            }
        }

        private void btnSelectBackground_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(selectedFolderPath))
            {
                MessageBox.Show("先にゲームフォルダを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var dialog = new OpenFileDialog())
            {
                dialog.InitialDirectory = selectedFolderPath;
                dialog.Filter = "画像ファイル (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|すべてのファイル (*.*)|*.*";
                dialog.Title = "背景画像を選択";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtBackgroundPath.Text = dialog.FileName;
                    UpdateBackgroundPreview();
                }
            }
        }

        private void btnAddDeveloper_Click(object sender, EventArgs e) => devListManager.Add();
        private void btnEditDeveloper_Click(object sender, EventArgs e) => devListManager.Edit();
        private void btnDeleteDeveloper_Click(object sender, EventArgs e) => devListManager.Delete();

        private void btnOK_Click(object sender, EventArgs e)
        {
            if (ValidateInput())
            {
                // バージョン情報を作成
                NewVersion = new GameVersion
                {
                    GameId = gameId,
                    Version = txtNextVersion.Text.Trim(),
                    ExecutablePath = "", // コピー後に設定
                    Arguments = txtArguments.Text.Trim(),
                    Description = txtDescription.Text.Trim(), // 説明文
                    UpdateNote = txtUpdateNote.Text.Trim(), // 更新内容
                    
                    Title = txtTitle.Text.Trim(),
                    Genre = GetSelectedGenres(),
                    MinPlayers = (int)numMinPlayers.Value,
                    MaxPlayers = (int)numMaxPlayers.Value,
                    Difficulty = GetDifficultyValue(),
                    PlayTime = GetPlayTimeValue(),
                    ControllerSupport = chkControllerSupport.Checked,
                    SupportedConnection = cmbSupportedConnection.SelectedIndex,
                    
                    // サムネイルと背景はコピー後に相対パスになる可能性があるが、
                    // ここではとりあえずフォームの値をセットし、MainFormで処理するか、
                    // 相対パス計算ロジックをここに入れるか。
                    // 既存ロジック(UpdatedGameInfo)ではMainFormではなくForm内で計算していた(lines 394+)
                    // NewVersionにも同じロジックを適用する必要がある。
                    // ここでは空文字または絶対パスを入れておき、後続の処理で書き換える。
                    ThumbnailPath = txtThumbnailPath.Text,
                    BackgroundPath = txtBackgroundPath.Text,
                    
                    Developers = new List<DeveloperInfo>(developers), // リストをコピー
                    
                    RegisteredAt = DateTime.Now
                };

                // 更新されたゲーム情報を作成
                // 更新されたゲーム情報を作成（既存の情報をベースにする）
                UpdatedGameInfo = new GameInfo
                {
                    GameId = originalGameInfo.GameId,
                    Title = txtTitle.Text.Trim(),
                    Description = txtDescription.Text.Trim(),
                    Genre = GetSelectedGenres(),
                    
                    // フォームにない項目は既存の値を引き継ぐ
                    ReleaseYear = originalGameInfo.ReleaseYear,
                    DisplayOrder = originalGameInfo.DisplayOrder,
                    IsVisible = originalGameInfo.IsVisible, // 表示設定は維持
                    
                    MinPlayers = (int)numMinPlayers.Value,
                    MaxPlayers = (int)numMaxPlayers.Value,
                    Difficulty = GetDifficultyValue(),
                    PlayTime = GetPlayTimeValue(),
                    ControllerSupport = chkControllerSupport.Checked,
                    SupportedConnection = cmbSupportedConnection.SelectedIndex,
                    Developers = new List<DeveloperInfo>(developers), // 追加
                    
                    // Controls, KeyMappingなども引き継ぐ
                    Controls = originalGameInfo.Controls,
                    KeyMapping = originalGameInfo.KeyMapping,
                    
                    Version = NewVersion.Version // 新しいバージョンをアクティブなバージョンとして設定
                };

                // サムネイルと背景のパスを計算（コピー後に相対パスになる）
                if (!string.IsNullOrEmpty(txtThumbnailPath.Text) && File.Exists(txtThumbnailPath.Text))
                {
                    string relativePath = txtThumbnailPath.Text;
                    if (relativePath.StartsWith(selectedFolderPath))
                    {
                        relativePath = relativePath.Substring(selectedFolderPath.Length).TrimStart(Path.DirectorySeparatorChar);
                    }
                    UpdatedGameInfo.ThumbnailPath = relativePath;
                    NewVersion.ThumbnailPath = relativePath;
                }
                if (!string.IsNullOrEmpty(txtBackgroundPath.Text) && File.Exists(txtBackgroundPath.Text))
                {
                    string relativePath = txtBackgroundPath.Text;
                    if (relativePath.StartsWith(selectedFolderPath))
                    {
                        relativePath = relativePath.Substring(selectedFolderPath.Length).TrimStart(Path.DirectorySeparatorChar);
                    }
                    UpdatedGameInfo.BackgroundPath = relativePath;
                    NewVersion.BackgroundPath = relativePath;
                }
                
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private List<string> GetSelectedGenres()
        {
            var selectedGenres = new List<string>();
            for (int i = 0; i < clbGenre.Items.Count; i++)
            {
                if (clbGenre.GetItemChecked(i))
                {
                    selectedGenres.Add(clbGenre.Items[i].ToString());
                }
            }
            return selectedGenres;
        }

        private int GetDifficultyValue()
        {
            if (cmbDifficulty.SelectedIndex >= 0)
            {
                return cmbDifficulty.SelectedIndex + 1;
            }
            return 2; // デフォルト: 普通
        }

        private int GetPlayTimeValue()
        {
            if (cmbPlayTime.SelectedIndex >= 0)
            {
                return cmbPlayTime.SelectedIndex + 1;
            }
            return 2; // デフォルト: 5分～15分
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private bool ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(txtNextVersion.Text))
            {
                MessageBox.Show("新しいバージョンを入力してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtNextVersion.Focus();
                return false;
            }

            if (txtNextVersion.Text.Trim() == currentVersion)
            {
                MessageBox.Show("現在のバージョンと同じバージョンは指定できません。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtNextVersion.Focus();
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtGameFolder.Text))
            {
                MessageBox.Show("ゲームフォルダを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                btnSelectGameFolder.Focus();
                return false;
            }

            if (!Directory.Exists(txtGameFolder.Text))
            {
                MessageBox.Show("選択されたゲームフォルダが見つかりません。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                btnSelectGameFolder.Focus();
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtTitle.Text))
            {
                MessageBox.Show("タイトルを入力してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtTitle.Focus();
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtExecutablePath.Text))
            {
                MessageBox.Show("実行ファイルを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                btnSelectExecutable.Focus();
                return false;
            }

            if (!File.Exists(txtExecutablePath.Text))
            {
                MessageBox.Show("選択された実行ファイルが見つかりません。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                btnSelectExecutable.Focus();
                return false;
            }

            return true;
        }

        private void UpdateThumbnailPreview() => ImagePreviewHelper.UpdatePreview(picThumbnailPreview, txtThumbnailPath.Text);
        private void UpdateBackgroundPreview() => ImagePreviewHelper.UpdatePreview(picBackgroundPreview, txtBackgroundPath.Text);

        /// <summary>
        /// テスト起動ボタンクリック
        /// </summary>
        private void btnTestRun_Click(object sender, EventArgs e)
        {
            try
            {
                string exePath = txtExecutablePath.Text.Trim();
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    MessageBox.Show("実行ファイルが指定されていません。", "テスト起動", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                
                if (!File.Exists(exePath))
                {
                    MessageBox.Show($"実行ファイルが見つかりません:\n{exePath}", "テスト起動", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = txtArguments.Text ?? "",
                    WorkingDirectory = Path.GetDirectoryName(exePath),
                    UseShellExecute = true
                };
                
                System.Diagnostics.Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"実行ファイルの起動に失敗しました:\n{ex.Message}", "テスト起動", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
