using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using GCTonePrism.Manager.Models;
using GCTonePrism.Manager.Services;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace GCTonePrism.Manager
{
    /// <summary>
    /// ゲーム編集フォーム
    /// </summary>
    public partial class EditGameForm : Form
    {
        private DatabaseManager dbManager;
        private GameInfo originalGame;
        private string gameFolder;
        private List<DeveloperInfo> developers;
        private DeveloperListManager devListManager;
        private Label lblArgumentsPlaceholder;

        /// <summary>
        /// 編集されたゲーム情報（OKボタンがクリックされた場合のみ設定される）
        /// </summary>
        public GameInfo EditedGame { get; private set; }

        public EditGameForm(DatabaseManager dbManager, GameInfo game)
        {
            InitializeComponent();
            this.dbManager = dbManager;
            this.originalGame = game;
            this.gameFolder = PathManager.GetGameFolder(game.GameId);
            EditedGame = null;
            developers = new List<DeveloperInfo>();
        }

        /// <summary>
        /// フォームロード時の処理
        /// </summary>
        private void EditGameForm_Load(object sender, EventArgs e)
        {
            // ゲームIDは編集不可（Enabled = falseで選択も不可）
            txtGameId.Text = originalGame.GameId;

            // 既存の値を設定
            txtTitle.Text = originalGame.Title ?? "";
            txtDescription.Text = originalGame.Description ?? "";
            
            if (originalGame.ReleaseYear.HasValue)
            {
                numReleaseYear.Value = originalGame.ReleaseYear.Value;
            }
            else
            {
                numReleaseYear.Value = DateTime.Now.Year;
            }

            // ジャンルチェックボックスリストを初期化
            clbGenre.Items.Clear();
            foreach (var genre in GenreList.AvailableGenres)
            {
                clbGenre.Items.Add(genre, false);
            }
            GameFormHelper.SetSelectedGenres(clbGenre, originalGame.Genre);

            if (originalGame.MinPlayers.HasValue)
            {
                numMinPlayers.Value = originalGame.MinPlayers.Value;
            }
            else
            {
                numMinPlayers.Value = 1;
            }

            if (originalGame.MaxPlayers.HasValue)
            {
                numMaxPlayers.Value = originalGame.MaxPlayers.Value;
            }
            else
            {
                numMaxPlayers.Value = 1;
            }

            // コンボボックスを初期化
            GameFormHelper.InitializeDifficultyCombo(cmbDifficulty, originalGame.Difficulty);
            GameFormHelper.InitializePlayTimeCombo(cmbPlayTime, originalGame.PlayTime);
            GameFormHelper.InitializeConnectionCombo(cmbSupportedConnection, originalGame.SupportedConnection);

            chkControllerSupport.Checked = originalGame.ControllerSupport;
            chkIsVisible.Checked = originalGame.IsVisible;

            // ファイルパスの設定（相対パスを絶対パスに変換して表示）
            if (!string.IsNullOrEmpty(originalGame.ExecutablePath))
                txtExecutablePath.Text = PathConversionHelper.ToAbsolutePath(gameFolder, originalGame.ExecutablePath);
            if (!string.IsNullOrEmpty(originalGame.ThumbnailPath))
                txtThumbnailPath.Text = PathConversionHelper.ToAbsolutePath(gameFolder, originalGame.ThumbnailPath);
            if (!string.IsNullOrEmpty(originalGame.BackgroundPath))
                txtBackgroundPath.Text = PathConversionHelper.ToAbsolutePath(gameFolder, originalGame.BackgroundPath);

            // ゲームフォルダの表示（既存のgames/{game_id}/フォルダを表示、編集不可）
            txtGameFolder.Text = gameFolder;


            // 警告ラベルを非表示
            lblGameIdWarning.Visible = false;

            // 既存の製作者情報をコピー
            if (originalGame.Developers != null)
            {
                foreach (var dev in originalGame.Developers)
                {
                    developers.Add(new DeveloperInfo
                    {
                        Id = dev.Id,
                        GameId = dev.GameId,
                        LastName = dev.LastName,
                        FirstName = dev.FirstName,
                        Grade = dev.Grade
                    });
                }
            }

            // 起動オプションの設定
            if (!string.IsNullOrWhiteSpace(originalGame.Arguments))
            {
                txtArguments.Text = originalGame.Arguments;
            }
            
            // 起動オプションのプレースホルダー設定
            lblArgumentsPlaceholder = GameFormHelper.SetupArgumentsPlaceholder(txtArguments, this);

            // 製作者情報のDataGridViewを初期化
            InitializeDevelopersGrid();

            // バージョン情報を読み込み
            LoadVersions();

            // 旧ボタンを非表示
            btnApplyVersion.Visible = false;
            btnVersionUp.Visible = false;

            // ラベルのテキスト変更（適宜）
            // lblVersionUp.Visible = false; など
        }

        /// <summary>
        /// バージョン情報を読み込み
        /// </summary>
        private void LoadVersions()
        {
            try
            {
                var versions = dbManager.GetGameVersions(originalGame.GameId);
                cmbVersionList.Items.Clear();
                foreach (var v in versions)
                {
                    cmbVersionList.Items.Add(v);
                }
                
                if (originalGame.Version != null)
                {
                    // アクティブなバージョンを選択
                    foreach (var item in cmbVersionList.Items)
                    {
                        if (item is GameVersion v && v.Version == originalGame.Version)
                        {
                            cmbVersionList.SelectedItem = item;
                            break;
                        }
                    }
                    
                    // 見つからなかった場合（またはVersionが設定されていない場合）は先頭（最新）を選択
                    if (cmbVersionList.SelectedIndex == -1 && cmbVersionList.Items.Count > 0)
                    {
                        cmbVersionList.SelectedIndex = 0;
                    }
                }
                else if (cmbVersionList.Items.Count > 0)
                {
                    cmbVersionList.SelectedIndex = 0;
                }
                
                // 表示用にフォーマット
                cmbVersionList.DisplayMember = "Version"; // GameVersionクラスのToStringをオーバーライドするか、DisplayMemberを設定
            }
            catch (Exception ex)
            {
                Console.WriteLine($"バージョン情報の読み込みに失敗: {ex.Message}");
            }
        }


        /// <summary>
        /// バージョン選択変更時の処理 - 更新内容を表示
        /// </summary>
        private void cmbVersionList_SelectedIndexChanged(object sender, EventArgs e)
        {
            // まず現在の入力内容を保存（選択変更前のアイテムに対して）
            // しかしSelectedIndexChanged発火時点ではSelectedItemは既に新しいものになっている
            // したがって、前回の選択アイテムを保持しておく必要があるか、
            // または「変更前」イベントがないため、工夫が必要。
            // 簡易的に、LoadGameDataForVersionの冒頭で「現在表示中のデータ」を「直前に選択されていたバージョン」に保存する…のは難しい（直前のバージョンがどれかわからない）
            
            // アプローチ:
            // メンバ変数 `currentDisplayingVersion` を用意し、LoadGameDataForVersionで更新する。
            // その前に `SaveGameDataToVersion(currentDisplayingVersion)` を呼ぶ。
            
            if (currentDisplayingVersion != null)
            {
                SaveGameDataToVersion(currentDisplayingVersion);
            }

            if (cmbVersionList.SelectedItem is GameVersion selectedVersion)
            {
                LoadGameDataForVersion(selectedVersion);
            }
        }

        private GameVersion currentDisplayingVersion = null;

        /// <summary>
        /// バージョンオブジェクトのパスを相対パスに変換・適用する
        /// </summary>
        private void ApplyRelativePaths(GameVersion version)
        {
            if (version == null) return;

            version.ExecutablePath = !string.IsNullOrEmpty(txtExecutablePath.Text)
                ? PathConversionHelper.ToRelativePath(gameFolder, txtExecutablePath.Text) : "";
            version.ThumbnailPath = !string.IsNullOrEmpty(txtThumbnailPath.Text)
                ? PathConversionHelper.ToRelativePath(gameFolder, txtThumbnailPath.Text) : "";
            version.BackgroundPath = !string.IsNullOrEmpty(txtBackgroundPath.Text)
                ? PathConversionHelper.ToRelativePath(gameFolder, txtBackgroundPath.Text) : "";
        }

        private void SaveGameDataToVersion(GameVersion version)
        {
            if (version == null) return;

            // Title
            version.Title = txtTitle.Text.Trim();
            // Descriptionは "Game Description"
            version.Description = string.IsNullOrWhiteSpace(txtDescription.Text) ? null : txtDescription.Text.Trim();
            // UpdateNote
            version.UpdateNote = string.IsNullOrWhiteSpace(txtVersionDescription.Text) ? null : txtVersionDescription.Text.Trim();
            
            // Version Name (Rename)
            if (!string.IsNullOrWhiteSpace(txtVersionName.Text))
            {
                version.Version = txtVersionName.Text.Trim();
            }
            
            version.Genre = GameFormHelper.GetSelectedGenres(clbGenre);
            
            version.MinPlayers = (int)numMinPlayers.Value;
            version.MaxPlayers = (int)numMaxPlayers.Value;
            version.Difficulty = cmbDifficulty.SelectedIndex >= 0 ? cmbDifficulty.SelectedIndex + 1 : (int?)null;
            version.PlayTime = cmbPlayTime.SelectedIndex >= 0 ? cmbPlayTime.SelectedIndex + 1 : (int?)null;
            version.ControllerSupport = chkControllerSupport.Checked;
            version.SupportedConnection = cmbSupportedConnection.SelectedIndex >= 0 ? cmbSupportedConnection.SelectedIndex : 0;
            
            // Developers
            version.Developers = new List<DeveloperInfo>(developers);

            // Paths: ApplyRelativePaths handles reading from text boxes and converting to relative if possible
            ApplyRelativePaths(version);
        }

        private void LoadGameDataForVersion(GameVersion version)
        {
            if (version == null) return;

            // 基本情報の読み込み
            txtVersionName.Text = version.Version ?? ""; // バージョン名を表示
            txtTitle.Text = version.Title ?? "";
            txtDescription.Text = version.Description ?? "";
            txtVersionDescription.Text = version.UpdateNote ?? "";

            // game_versions.description = ゲーム説明文（バージョンごと）
            // game_versions.update_note = 更新内容（バージョンごと）
            txtDescription.Text = version.Description ?? "";
            
            // ジャンル
            GameFormHelper.SetSelectedGenres(clbGenre, version.Genre);

            // 数値系
            if (version.MinPlayers.HasValue) numMinPlayers.Value = version.MinPlayers.Value;
            if (version.MaxPlayers.HasValue) numMaxPlayers.Value = version.MaxPlayers.Value;
            
            // Difficulty (1-3)
            if (version.Difficulty.HasValue && version.Difficulty >= 1 && version.Difficulty <= 3)
                cmbDifficulty.SelectedIndex = version.Difficulty.Value - 1;
            else cmbDifficulty.SelectedIndex = 1;

            // PlayTime (1-3)
            if (version.PlayTime.HasValue && version.PlayTime >= 1 && version.PlayTime <= 3)
                cmbPlayTime.SelectedIndex = version.PlayTime.Value - 1;
            else cmbPlayTime.SelectedIndex = 1;

            // Connection
            if (version.SupportedConnection >= 0 && version.SupportedConnection <= 2)
                cmbSupportedConnection.SelectedIndex = version.SupportedConnection;
            else cmbSupportedConnection.SelectedIndex = 0;

            chkControllerSupport.Checked = version.ControllerSupport;
            
            // Paths（相対パスを絶対パスに変換して表示）
            txtExecutablePath.Text = !string.IsNullOrEmpty(version.ExecutablePath)
                ? PathConversionHelper.ToAbsolutePath(gameFolder, version.ExecutablePath) : "";
            txtThumbnailPath.Text = !string.IsNullOrEmpty(version.ThumbnailPath)
                ? PathConversionHelper.ToAbsolutePath(gameFolder, version.ThumbnailPath) : "";
            txtBackgroundPath.Text = !string.IsNullOrEmpty(version.BackgroundPath)
                ? PathConversionHelper.ToAbsolutePath(gameFolder, version.BackgroundPath) : "";

            // Developers
            developers.Clear();
            if (version.Developers != null)
            {
                foreach(var d in version.Developers) developers.Add(d);
            }
            RefreshDevelopersGrid();
            
            txtVersionDescription.Text = version.UpdateNote ?? "";
            txtVersionDescription.ReadOnly = false; // 編集可能にする
            
            // Update image previews
            UpdateThumbnailPreview();
            UpdateBackgroundPreview();
        }


        /// <summary>
        /// 製作者情報のDataGridViewを初期化
        /// </summary>
        private void InitializeDevelopersGrid()
        {
            devListManager = new DeveloperListManager(dgvDevelopers, developers);
            devListManager.InitializeGrid();
        }

        private void RefreshDevelopersGrid() => devListManager.Refresh();

        /// <summary>
        /// 実行ファイル選択ボタンクリック（既存のgames/{game_id}/フォルダ内から選択）
        /// </summary>
        private void btnSelectExecutable_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.InitialDirectory = gameFolder;
                dialog.Filter = "実行ファイル (*.exe)|*.exe|すべてのファイル (*.*)|*.*";
                dialog.Title = "実行ファイルを選択（ゲームフォルダ内から選択）";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtExecutablePath.Text = dialog.FileName;
                }
            }
        }

        /// <summary>
        /// サムネイル画像選択ボタンクリック（既存のgames/{game_id}/フォルダ内から選択）
        /// </summary>
        private void btnSelectThumbnail_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.InitialDirectory = gameFolder;
                dialog.Filter = "画像ファイル (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|すべてのファイル (*.*)|*.*";
                dialog.Title = "サムネイル画像を選択（ゲームフォルダ内から選択）";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtThumbnailPath.Text = dialog.FileName;
                    UpdateThumbnailPreview();
                }
            }
        }

        /// <summary>
        /// 背景画像選択ボタンクリック（既存のgames/{game_id}/フォルダ内から選択）
        /// </summary>
        private void btnSelectBackground_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.InitialDirectory = gameFolder;
                dialog.Filter = "画像ファイル (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|すべてのファイル (*.*)|*.*";
                dialog.Title = "背景画像を選択（ゲームフォルダ内から選択）";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtBackgroundPath.Text = dialog.FileName;
                    UpdateBackgroundPreview();
                }
            }
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

            try
            {
                // パスを相対パスに変換（可能な場合）
                string executablePath = PathConversionHelper.ToRelativePath(gameFolder, txtExecutablePath.Text.Trim());
                string thumbnailPath = string.IsNullOrWhiteSpace(txtThumbnailPath.Text) ? null : PathConversionHelper.ToRelativePath(gameFolder, txtThumbnailPath.Text.Trim());
                string backgroundPath = string.IsNullOrWhiteSpace(txtBackgroundPath.Text) ? null : PathConversionHelper.ToRelativePath(gameFolder, txtBackgroundPath.Text.Trim());

                // 起動オプション
                string arguments = txtArguments.Text;

                // GameInfoオブジェクトを作成（既存の値をベースに）
                var game = new GameInfo
                {
                    GameId = originalGame.GameId, // ゲームIDは変更不可
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
                    DisplayOrder = originalGame.DisplayOrder, // 表示順序は変更しない（メイン画面のドラッグ&ドロップで変更）
                    IsVisible = chkIsVisible.Checked,
                    Controls = originalGame.Controls, // 後で実装
                    KeyMapping = originalGame.KeyMapping // 後で実装
                };

                // ジャンルを処理
                game.Genre = GameFormHelper.GetSelectedGenres(clbGenre);

                // 製作者情報は既存のものを保持
                game.Developers = originalGame.Developers ?? new List<DeveloperInfo>();

                // 選択中のバージョン
                var selectedVersion = cmbVersionList.SelectedItem as GameVersion;

                if (selectedVersion == null)
                {
                    // バージョンが無い場合は従来通りの更新（本来ありえないが）
                    dbManager.UpdateGame(game);
                }
                else
                {
                    // 1. 現在表示中の内容を、選択中のバージョンオブジェクトに反映
                    SaveGameDataToVersion(selectedVersion);
                    
                    // パス関連: 相対パス化ロジック (現在の選択中バージョンに対してのみ適用)
                    // 他のバージョンは既にロード済みまたは編集済みで、その時点でパスは保持されているはず
                    ApplyRelativePaths(selectedVersion);

                    // 2. 全てのバージョンをデータベースに保存
                    // これにより、切り替えた別のバージョンの変更も保存される
                    foreach (var item in cmbVersionList.Items)
                    {
                        if (item is GameVersion v)
                        {
                            dbManager.UpdateGameVersion(v);
                        }
                    }
                    
                    // 3. メインのゲーム情報を更新（選択中バージョンの全フィールドを反映）
                    game.Title = selectedVersion.Title ?? game.Title;
                    game.Description = selectedVersion.Description;
                    game.Genre = selectedVersion.Genre ?? game.Genre;
                    game.MinPlayers = selectedVersion.MinPlayers;
                    game.MaxPlayers = selectedVersion.MaxPlayers;
                    game.Difficulty = selectedVersion.Difficulty;
                    game.PlayTime = selectedVersion.PlayTime;
                    game.ControllerSupport = selectedVersion.ControllerSupport;
                    game.SupportedConnection = selectedVersion.SupportedConnection;
                    game.ExecutablePath = selectedVersion.ExecutablePath;
                    game.ThumbnailPath = selectedVersion.ThumbnailPath;
                    game.BackgroundPath = selectedVersion.BackgroundPath;
                    game.Arguments = selectedVersion.Arguments;
                    game.Version = selectedVersion.Version;
                    
                    dbManager.UpdateGame(game);
                }

                EditedGame = game;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (System.Data.SQLite.SQLiteException ex)
            {
                string errorMessage = DatabaseManager.GetUserFriendlyErrorMessage(ex);
                MessageBox.Show(
                    $"ゲームの更新に失敗しました。\n\n{errorMessage}",
                    "データベースエラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"ゲームの更新に失敗しました。\n\n{ex.Message}",
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
            // タイトル
            if (string.IsNullOrWhiteSpace(txtTitle.Text))
            {
                MessageBox.Show("タイトルを入力してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtTitle.Focus();
                return false;
            }

            // 実行ファイルパス
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

            // 実行ファイルがゲームフォルダ内にあるか確認
            if (!txtExecutablePath.Text.StartsWith(gameFolder, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("実行ファイルはゲームフォルダ内のファイルを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                if (!txtThumbnailPath.Text.StartsWith(gameFolder, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("サムネイル画像はゲームフォルダ内のファイルを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                if (!txtBackgroundPath.Text.StartsWith(gameFolder, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("背景画像はゲームフォルダ内のファイルを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    btnSelectBackground.Focus();
                    return false;
                }
            }

            return true;
        }

        private void btnAddDeveloper_Click(object sender, EventArgs e) => devListManager.Add();
        private void btnEditDeveloper_Click(object sender, EventArgs e) => devListManager.Edit();
        private void btnDeleteDeveloper_Click(object sender, EventArgs e) => devListManager.Delete();

        private void btnVersionUp_Click(object sender, EventArgs e)
        {
             // Deprecated
        }

        private void btnApplyVersion_Click(object sender, EventArgs e)
        {
             // Deprecated
        }

        private void UpdateThumbnailPreview() => ImagePreviewHelper.UpdatePreview(picThumbnailPreview, txtThumbnailPath.Text, gameFolder);
        private void UpdateBackgroundPreview() => ImagePreviewHelper.UpdatePreview(picBackgroundPreview, txtBackgroundPath.Text, gameFolder);

        private void btnTestRun_Click(object sender, EventArgs e) =>
            GameFormHelper.TestRunGame(txtExecutablePath.Text.Trim(), txtArguments.Text, gameFolder);
    }
}

