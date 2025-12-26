using System;
using System.Collections.Generic;
using System.Data;
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

            if (originalGame.Genre != null && originalGame.Genre.Count > 0)
            {
                txtGenre.Text = string.Join(", ", originalGame.Genre);
            }
            else
            {
                txtGenre.Text = "（カンマ区切りで複数入力可）";
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

            // 製作者情報のDataGridViewを初期化
            InitializeDevelopersGrid();
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
                    ControllerSupport = chkControllerSupport.Checked,
                    ThumbnailPath = thumbnailPath,
                    BackgroundPath = backgroundPath,
                    ExecutablePath = executablePath,
                    DisplayOrder = originalGame.DisplayOrder, // 表示順序は変更しない（メイン画面のドラッグ&ドロップで変更）
                    IsVisible = chkIsVisible.Checked,
                    Controls = originalGame.Controls, // 後で実装
                    KeyMapping = originalGame.KeyMapping // 後で実装
                };

                // ジャンルを処理（カンマ区切り）
                if (!string.IsNullOrWhiteSpace(txtGenre.Text) && 
                    !txtGenre.Text.Contains("（カンマ区切りで複数入力可）"))
                {
                    game.Genre = txtGenre.Text.Split(',').Select(g => g.Trim()).Where(g => !string.IsNullOrEmpty(g)).ToList();
                }
                else
                {
                    game.Genre = new List<string>();
                }

                // 製作者情報は既存のものを保持
                game.Developers = originalGame.Developers ?? new List<DeveloperInfo>();

                // データベースを更新
                dbManager.UpdateGame(game);

                EditedGame = game;
                DialogResult = DialogResult.OK;
                Close();
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
    }
}

