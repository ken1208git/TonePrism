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
            // コンボボックスを初期化（選択値はbaseVersion読み込み時に設定）
            GameFormHelper.InitializeDifficultyCombo(cmbDifficulty);
            GameFormHelper.InitializePlayTimeCombo(cmbPlayTime);
            GameFormHelper.InitializeConnectionCombo(cmbSupportedConnection);

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
                if (baseVersion.PlayTime.HasValue && baseVersion.PlayTime.Value >= 1 && baseVersion.PlayTime.Value <= 3)
                    cmbPlayTime.SelectedIndex = baseVersion.PlayTime.Value - 1;

                numMinPlayers.Value = baseVersion.MinPlayers ?? 1;
                numMaxPlayers.Value = baseVersion.MaxPlayers ?? 1;
                chkControllerSupport.Checked = baseVersion.ControllerSupport;

                if (baseVersion.SupportedConnection >= 0 && baseVersion.SupportedConnection < cmbSupportedConnection.Items.Count)
                    cmbSupportedConnection.SelectedIndex = baseVersion.SupportedConnection;

                // ジャンルのチェック
                GameFormHelper.SetSelectedGenres(clbGenre, baseVersion.Genre);

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
            var (exePath, thumbPath, bgPath) = GameFormHelper.AutoDetectFiles(selectedFolderPath);
            if (exePath != null)
            {
                txtExecutablePath.Text = exePath;
                RelativeExecutablePath = PathConversionHelper.ToRelativePath(selectedFolderPath, exePath);
            }
            if (thumbPath != null) txtThumbnailPath.Text = thumbPath;
            if (bgPath != null) txtBackgroundPath.Text = bgPath;
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
                    Genre = GameFormHelper.GetSelectedGenres(clbGenre),
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
                    Genre = GameFormHelper.GetSelectedGenres(clbGenre),
                    
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
                    string relativePath = PathConversionHelper.ToRelativePath(selectedFolderPath, txtThumbnailPath.Text);
                    UpdatedGameInfo.ThumbnailPath = relativePath;
                    NewVersion.ThumbnailPath = relativePath;
                }
                if (!string.IsNullOrEmpty(txtBackgroundPath.Text) && File.Exists(txtBackgroundPath.Text))
                {
                    string relativePath = PathConversionHelper.ToRelativePath(selectedFolderPath, txtBackgroundPath.Text);
                    UpdatedGameInfo.BackgroundPath = relativePath;
                    NewVersion.BackgroundPath = relativePath;
                }
                
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private int GetDifficultyValue()
        {
            return cmbDifficulty.SelectedIndex >= 0 ? cmbDifficulty.SelectedIndex + 1 : 2;
        }

        private int GetPlayTimeValue()
        {
            return cmbPlayTime.SelectedIndex >= 0 ? cmbPlayTime.SelectedIndex + 1 : 2;
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

        private void btnTestRun_Click(object sender, EventArgs e) =>
            GameFormHelper.TestRunGame(txtExecutablePath.Text.Trim(), txtArguments.Text);
    }
}
