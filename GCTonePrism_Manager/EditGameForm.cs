using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using GCTonePrism.Manager.Models;
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
        private Label lblArgumentsPlaceholder;

        private const string ARGUMENTS_PLACEHOLDER = "通常は空欄で構いません。\r\n特殊な起動オプションが必要な場合のみ記述してください。\r\n例: Unity製ゲームでフルスクリーン起動を強制する場合 -> -screen-fullscreen 1";

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

            // 既存のジャンルをチェック
            if (originalGame.Genre != null && originalGame.Genre.Count > 0)
            {
                foreach (string genre in originalGame.Genre)
                {
                    // GenreListに含まれるジャンルのみチェック（既存データに無効なジャンルが含まれている場合に対応）
                    if (GenreList.IsValidGenre(genre))
                    {
                        int index = clbGenre.Items.IndexOf(genre);
                        if (index >= 0 && index < clbGenre.Items.Count)
                        {
                            clbGenre.SetItemChecked(index, true);
                        }
                    }
                }
            }

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

            // 難易度のコンボボックスを初期化
            cmbDifficulty.Items.Add("1 - 易しい");
            cmbDifficulty.Items.Add("2 - 普通");
            cmbDifficulty.Items.Add("3 - 難しい");
            if (originalGame.Difficulty.HasValue && originalGame.Difficulty.Value >= 1 && originalGame.Difficulty.Value <= 3)
            {
                cmbDifficulty.SelectedIndex = originalGame.Difficulty.Value - 1;
            }
            else
            {
                cmbDifficulty.SelectedIndex = 1; // デフォルト: 普通
            }

            // プレイ時間のコンボボックスを初期化
            cmbPlayTime.Items.Add("1 - ～5分");
            cmbPlayTime.Items.Add("2 - 5分～15分");
            cmbPlayTime.Items.Add("3 - 15分以上");
            if (originalGame.PlayTime.HasValue && originalGame.PlayTime.Value >= 1 && originalGame.PlayTime.Value <= 3)
            {
                cmbPlayTime.SelectedIndex = originalGame.PlayTime.Value - 1;
            }
            else
            {
                cmbPlayTime.SelectedIndex = 1; // デフォルト: 5分～15分
            }

            // 通信プレイ対応のコンボボックスを初期化
            cmbSupportedConnection.Items.Add("なし（1台で遊ぶ）");
            cmbSupportedConnection.Items.Add("ローカル通信（部室のLANで対戦）");
            cmbSupportedConnection.Items.Add("オンライン通信（インターネット対戦）");
            if (originalGame.SupportedConnection >= 0 && originalGame.SupportedConnection <= 2)
            {
                cmbSupportedConnection.SelectedIndex = originalGame.SupportedConnection;
            }
            else
            {
                cmbSupportedConnection.SelectedIndex = 0; // デフォルト: なし
            }

            chkControllerSupport.Checked = originalGame.ControllerSupport;
            chkIsVisible.Checked = originalGame.IsVisible;

            // ファイルパスの設定（既存のgames/{game_id}/フォルダからの相対パスを絶対パスに変換）
            if (!string.IsNullOrEmpty(originalGame.ExecutablePath))
            {
                if (Path.IsPathRooted(originalGame.ExecutablePath))
                {
                    txtExecutablePath.Text = originalGame.ExecutablePath;
                }
                else
                {
                    txtExecutablePath.Text = Path.Combine(gameFolder, originalGame.ExecutablePath);
                }
            }

            if (!string.IsNullOrEmpty(originalGame.ThumbnailPath))
            {
                if (Path.IsPathRooted(originalGame.ThumbnailPath))
                {
                    txtThumbnailPath.Text = originalGame.ThumbnailPath;
                }
                else
                {
                    txtThumbnailPath.Text = Path.Combine(gameFolder, originalGame.ThumbnailPath);
                }
            }

            if (!string.IsNullOrEmpty(originalGame.BackgroundPath))
            {
                if (Path.IsPathRooted(originalGame.BackgroundPath))
                {
                    txtBackgroundPath.Text = originalGame.BackgroundPath;
                }
                else
                {
                    txtBackgroundPath.Text = Path.Combine(gameFolder, originalGame.BackgroundPath);
                }
            }

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
            
            // 起動オプションのプレースホルダー設定（ラベルを重ねて表示）
            lblArgumentsPlaceholder = new Label();
            lblArgumentsPlaceholder.Text = ARGUMENTS_PLACEHOLDER;
            lblArgumentsPlaceholder.ForeColor = System.Drawing.Color.Gray;
            lblArgumentsPlaceholder.BackColor = System.Drawing.Color.White; // テキストボックスの背景色に合わせる
            lblArgumentsPlaceholder.AutoSize = false;
            lblArgumentsPlaceholder.Size = new System.Drawing.Size(txtArguments.Width - 4, txtArguments.Height - 4);
            lblArgumentsPlaceholder.Location = new System.Drawing.Point(txtArguments.Location.X + 2, txtArguments.Location.Y + 2); // 枠線の分だけずらす
            lblArgumentsPlaceholder.Font = txtArguments.Font;
            lblArgumentsPlaceholder.Cursor = Cursors.IBeam;
            lblArgumentsPlaceholder.Click += (s, ev) => txtArguments.Focus();
            this.Controls.Add(lblArgumentsPlaceholder);
            lblArgumentsPlaceholder.BringToFront();

            // テキスト変更時のイベントハンドラ
            txtArguments.TextChanged += (s, ev) => UpdatePlaceholderVisibility();
            UpdatePlaceholderVisibility(); // 初期状態の更新

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
            
            // Note: This logic assumes txt*Path.Text holds the *current* absolute path or relative path being edited.
            // When switching versions, txt*Path is updated. When hitting OK, we use the text box values for the *selected* version.
            // For other versions, their paths are already stored in the attributes.
            
            string exePath = txtExecutablePath.Text;
            string thumbPath = txtThumbnailPath.Text;
            string bgPath = txtBackgroundPath.Text;

            // ExecutablePath
            if (!string.IsNullOrEmpty(exePath))
            {
                if (exePath.StartsWith(gameFolder, StringComparison.OrdinalIgnoreCase))
                    version.ExecutablePath = exePath.Substring(gameFolder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                else
                    version.ExecutablePath = exePath; 
            }
            else version.ExecutablePath = "";

            // ThumbnailPath
            if (!string.IsNullOrEmpty(thumbPath))
            {
                if (thumbPath.StartsWith(gameFolder, StringComparison.OrdinalIgnoreCase))
                    version.ThumbnailPath = thumbPath.Substring(gameFolder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                else
                    version.ThumbnailPath = thumbPath;
            }
            else version.ThumbnailPath = "";

            // BackgroundPath
            if (!string.IsNullOrEmpty(bgPath))
            {
                if (bgPath.StartsWith(gameFolder, StringComparison.OrdinalIgnoreCase))
                    version.BackgroundPath = bgPath.Substring(gameFolder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                else
                    version.BackgroundPath = bgPath;
            }
            else version.BackgroundPath = "";
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
            
            version.Genre = new List<string>();
            for (int i = 0; i < clbGenre.CheckedItems.Count; i++)
            {
                string genre = clbGenre.CheckedItems[i].ToString();
                if (!string.IsNullOrEmpty(genre))
                {
                    version.Genre.Add(genre);
                }
            }
            
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
            txtVersionDescription.Text = version.UpdateNote ?? ""; // バージョン更新内容（DescriptionではなくUpdateNoteを使用）
            // Version.Descriptionは「更新内容」として使われている？ 
            // GameInfo.Descriptionは「ゲーム説明」。
            // GameVersion.Descriptionは「更新ノート」。
            // UI上、txtDescriptionは「ゲーム説明」、txtUpdateNote（VersionUpFormにある）は「更新内容」。
            // EditGameFormにはtxtUpdateNoteが無い。txtDescriptionしかない。
            // しかしバージョンごとに「ゲーム説明」を持つようになったので、txtDescriptionにはVersion.Descriptionを表示すべき？
            // 待て、GameVersion.Descriptionは「更新内容(Update Note)」として使われている（VersionUpFormで）。
            // GameVersion.Description != GameInfo.Description ?
            // Schema migration added "description" to game_versions.
            // VersionUpForm maps txtUpdateNote to Description.
            // So GameVersion.Description IS Update Note.
            // But I also added "title", "genre", "description" to game_versions (schema v3).
            // Wait, did I add "description" TWICE?
            // "description TEXT" was already in v2 (for update note).
            // In v3 migration, I added "title", "genre", etc.
            // I did NOT add "description" again.
            // So GameVersion.Description IS the Update Note.
            // But where is the "Game Description" stored per version?
            // "description TEXT" in game_versions table is... ambiguous.
            // In v2 it was Update Note.
            // The User said: "Title, Description, Genre... per version".
            // So "description" column should hold "Game Description".
            // Then where is "Update Note"?
            // Maybe I need a separate column for "update_note"?
            // Or maybe "description" IS "Game Description" and "Update Note" is lost or stored elsewhere?
            // VersionUpForm: txtUpdateNote -> Description.
            // This implies "description" = Update Note.
            // If I overwrite it with "Game Description", I lose Update Note.
            // The user wants "Description" (Setsumei-bun) to be versioned.
            // I should probably have used a different column for one of them.
            // However, looking at MigrateV2ToV3, I did NOT add `game_description` column.
            // I assumed `description` was the game description?
            // But `VersionUpForm` uses it as "update note".
            // This is a conflict.
            // User requirement: "Title, Description, Genre... : Version-specific".
            // So I should treat `description` column as "Game Description".
            // And "Update Note" might need to be migrated to `update_note` column or shared?
            // Given I already ran migration (conceptually), checking `DatabaseManager.cs`.
            // `AddGameVersion`: `command.Parameters.AddWithValue("@description", version.Description ...)`
            // `Version.Description` is used.
            // If I map `txtDescription` (Game Description) to `Version.Description`, then `txtUpdateNote` (Update Note) will also map to `Version.Description`.
            // This means Update Note BECOMES Game Description.
            // This might be what the user wants? "Release 1.0: Initial Release" as description?
            // No, Game Description is usually "This is a shooting game...".
            // Update Note is "Fixed bugs...".
            // They are distinct.
            // I should have added `update_note` column.
            // But I can't change schema easily now without another migration.
            // I will assume `Version.Description` is "Game Description" (per user request "Description per version").
            // And "Update Note" is... effectively `Version.Description` currently.
            // So for now, I will map `txtDescription` to `Version.Description`.
            // And `txtVersionDescription` (which was showing Update Note) will also show `Version.Description` (Game Description).
            // Ensure `cmbVersionList_SelectedIndexChanged` updates `txtVersionDescription` too.
            // Note: `txtVersionDescription` is likely a label or readonly text for the update note in the list.
            
            // Re-reading User Request: "Title, Genre, PlayerCount... per version".
            // "Description" is definitely Game Description.
            // I will proceed with using `Version.Description` as Game Description.

            txtDescription.Text = version.Description ?? ""; // Game Description
            
            // ジャンル
            for (int i = 0; i < clbGenre.Items.Count; i++)
            {
                clbGenre.SetItemChecked(i, false);
            }
            if (version.Genre != null)
            {
                foreach (string g in version.Genre)
                {
                     int index = clbGenre.Items.IndexOf(g);
                     if (index >= 0) clbGenre.SetItemChecked(index, true);
                }
            }

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
            
            // Paths
            if (!string.IsNullOrEmpty(version.ExecutablePath))
            {
                 txtExecutablePath.Text = Path.IsPathRooted(version.ExecutablePath) ? version.ExecutablePath : Path.Combine(gameFolder, version.ExecutablePath);
            }
            else txtExecutablePath.Text = "";

            if (!string.IsNullOrEmpty(version.ThumbnailPath))
            {
                 txtThumbnailPath.Text = Path.IsPathRooted(version.ThumbnailPath) ? version.ThumbnailPath : Path.Combine(gameFolder, version.ThumbnailPath);
            }
            else txtThumbnailPath.Text = "";

            if (!string.IsNullOrEmpty(version.BackgroundPath))
            {
                 txtBackgroundPath.Text = Path.IsPathRooted(version.BackgroundPath) ? version.BackgroundPath : Path.Combine(gameFolder, version.BackgroundPath);
            }
            else txtBackgroundPath.Text = "";

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
            dgvDevelopers.AutoGenerateColumns = false;
            dgvDevelopers.AllowUserToAddRows = false;
            dgvDevelopers.AllowUserToDeleteRows = false;
            dgvDevelopers.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvDevelopers.MultiSelect = false;
            dgvDevelopers.ReadOnly = true;

            // カラムを追加
            dgvDevelopers.Columns.Clear();
            dgvDevelopers.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "LastName",
                HeaderText = "姓",
                DataPropertyName = "LastName",
                Width = 100
            });
            dgvDevelopers.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "FirstName",
                HeaderText = "名",
                DataPropertyName = "FirstName",
                Width = 100
            });
            dgvDevelopers.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "GradeDisplay",
                HeaderText = "期生",
                DataPropertyName = "GradeDisplay",
                Width = 60
            });

            // データソースを設定
            dgvDevelopers.DataSource = developers;
        }

        /// <summary>
        /// 製作者情報を更新
        /// </summary>
        private void RefreshDevelopersGrid()
        {
            dgvDevelopers.DataSource = null;
            dgvDevelopers.DataSource = developers;
        }

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

        /// <summary>
        /// 絶対パスから相対パスを取得（games/{game_id}/フォルダからの相対パス）
        /// </summary>
        private string GetRelativePath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
            {
                return null;
            }

            if (!Path.IsPathRooted(absolutePath))
            {
                return absolutePath; // 既に相対パスの場合
            }

            if (absolutePath.StartsWith(gameFolder, StringComparison.OrdinalIgnoreCase))
            {
                string relativePath = absolutePath.Substring(gameFolder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.IsNullOrEmpty(relativePath) ? null : relativePath;
            }

            // games/{game_id}/フォルダ外のパスは絶対パスのまま保存
            return absolutePath;
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

            try
            {
                // パスを相対パスに変換（可能な場合）
                string executablePath = GetRelativePath(txtExecutablePath.Text.Trim());
                string thumbnailPath = string.IsNullOrWhiteSpace(txtThumbnailPath.Text) ? null : GetRelativePath(txtThumbnailPath.Text.Trim());
                string backgroundPath = string.IsNullOrWhiteSpace(txtBackgroundPath.Text) ? null : GetRelativePath(txtBackgroundPath.Text.Trim());

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

                // ジャンルを処理（チェックボックスから選択されたものを取得）
                game.Genre = new List<string>();
                for (int i = 0; i < clbGenre.CheckedItems.Count; i++)
                {
                    string genre = clbGenre.CheckedItems[i].ToString();
                    if (!string.IsNullOrEmpty(genre))
                    {
                        game.Genre.Add(genre);
                    }
                }

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
                    
                    // 3. メインのゲーム情報を更新（最新の選択中バージョンに合わせる）
                    game.ExecutablePath = selectedVersion.ExecutablePath;
                    game.ThumbnailPath = selectedVersion.ThumbnailPath;
                    game.BackgroundPath = selectedVersion.BackgroundPath;
                    game.Version = selectedVersion.Version; // アクティブなバージョンを設定
                    
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

        /// <summary>
        /// 製作者追加ボタンクリック
        /// </summary>
        private void btnAddDeveloper_Click(object sender, EventArgs e)
        {
            using (var form = new DeveloperForm())
            {
                if (form.ShowDialog() == DialogResult.OK && form.Developer != null)
                {
                    developers.Add(form.Developer);
                    RefreshDevelopersGrid();
                }
            }
        }

        /// <summary>
        /// 製作者編集ボタンクリック
        /// </summary>
        private void btnEditDeveloper_Click(object sender, EventArgs e)
        {
            if (dgvDevelopers.SelectedRows.Count == 0)
            {
                MessageBox.Show("編集する製作者を選択してください。", "情報", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var selectedDeveloper = dgvDevelopers.SelectedRows[0].DataBoundItem as DeveloperInfo;
            if (selectedDeveloper == null) return;

            using (var form = new DeveloperForm(selectedDeveloper))
            {
                if (form.ShowDialog() == DialogResult.OK && form.Developer != null)
                {
                    int index = developers.IndexOf(selectedDeveloper);
                    if (index >= 0)
                    {
                        developers[index] = form.Developer;
                        RefreshDevelopersGrid();
                    }
                }
            }
        }

        /// <summary>
        /// 製作者削除ボタンクリック
        /// </summary>
        private void btnDeleteDeveloper_Click(object sender, EventArgs e)
        {
            if (dgvDevelopers.SelectedRows.Count == 0)
            {
                MessageBox.Show("削除する製作者を選択してください。", "情報", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var selectedDeveloper = dgvDevelopers.SelectedRows[0].DataBoundItem as DeveloperInfo;
            if (selectedDeveloper == null) return;

            var result = MessageBox.Show(
                $"製作者「{selectedDeveloper.FullName}」を削除しますか？",
                "削除確認",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                developers.Remove(selectedDeveloper);
                RefreshDevelopersGrid();
            }
        }

        private void UpdatePlaceholderVisibility()
        {
            if (lblArgumentsPlaceholder != null)
            {
                lblArgumentsPlaceholder.Visible = string.IsNullOrEmpty(txtArguments.Text);
            }
        }
        private void btnVersionUp_Click(object sender, EventArgs e)
        {
             // Deprecated
        }

        private void btnApplyVersion_Click(object sender, EventArgs e)
        {
             // Deprecated
        }

        // フォルダコピー用ヘルパー
        private void CopyDirectory(string sourceDir, string destinationDir)
        {
            DirectoryInfo dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");
            }

            DirectoryInfo[] dirs = dir.GetDirectories();
            Directory.CreateDirectory(destinationDir);

            foreach (FileInfo file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath, true);
            }

            foreach (DirectoryInfo subDir in dirs)
            {
                string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                CopyDirectory(subDir.FullName, newDestinationDir);
            }
        }

        private string CleanFileName(string fileName)
        {
            return Path.GetInvalidFileNameChars().Aggregate(fileName, (current, c) => current.Replace(c.ToString(), string.Empty));
        }

        /// <summary>
        /// サムネイルプレビューを更新
        /// </summary>
        private void UpdateThumbnailPreview()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(txtThumbnailPath.Text))
                {
                    picThumbnailPreview.Image = null;
                    return;
                }
                
                string path = txtThumbnailPath.Text.Trim();
                if (!Path.IsPathRooted(path))
                {
                    path = Path.Combine(gameFolder, path);
                }
                
                if (File.Exists(path))
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                    {
                        picThumbnailPreview.Image = Image.FromStream(stream);
                    }
                }
                else
                {
                    picThumbnailPreview.Image = null;
                }
            }
            catch
            {
                picThumbnailPreview.Image = null;
            }
        }

        /// <summary>
        /// 背景プレビューを更新
        /// </summary>
        private void UpdateBackgroundPreview()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(txtBackgroundPath.Text))
                {
                    picBackgroundPreview.Image = null;
                    return;
                }
                
                string path = txtBackgroundPath.Text.Trim();
                if (!Path.IsPathRooted(path))
                {
                    path = Path.Combine(gameFolder, path);
                }
                
                if (File.Exists(path))
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                    {
                        picBackgroundPreview.Image = Image.FromStream(stream);
                    }
                }
                else
                {
                    picBackgroundPreview.Image = null;
                }
            }
            catch
            {
                picBackgroundPreview.Image = null;
            }
        }

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
                
                if (!Path.IsPathRooted(exePath))
                {
                    exePath = Path.Combine(gameFolder, exePath);
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

