using System;
using System.IO;
using System.Windows.Forms;

namespace GCTonePrism.Manager
{
    /// <summary>
    /// ゲーム削除確認フォーム。
    /// DB 削除のみ / DB + games/{gameId}/ フォルダ削除 を選択させる。
    /// チェックボックスはデフォルト OFF（誤操作で物理削除しないため）。
    /// </summary>
    public partial class DeleteGameConfirmForm : Form
    {
        private readonly string _gameTitle;
        private readonly string _gameId;
        private readonly string _gameFolderPath;
        private readonly bool _folderExists;

        public bool DeleteFolder { get; private set; }

        public DeleteGameConfirmForm(string gameTitle, string gameId, string gameFolderPath)
        {
            InitializeComponent();
            _gameTitle = gameTitle ?? string.Empty;
            _gameId = gameId ?? string.Empty;
            _gameFolderPath = gameFolderPath ?? string.Empty;
            _folderExists = !string.IsNullOrEmpty(gameFolderPath) && Directory.Exists(gameFolderPath);
        }

        private void DeleteGameConfirmForm_Load(object sender, EventArgs e)
        {
            lblTitle.Text = $"ゲーム「{_gameTitle}」を削除しますか？";
            lblGameId.Text = $"Game ID: {_gameId}";

            chkDeleteFolder.Checked = false;
            txtFolderPath.Text = _gameFolderPath;

            if (_folderExists)
            {
                chkDeleteFolder.Enabled = true;
                lblFolderStatus.Text = "（このフォルダ内のファイルがディスクから物理的に削除されます）";
                lblFolderStatus.ForeColor = System.Drawing.Color.DarkRed;
            }
            else
            {
                chkDeleteFolder.Enabled = false;
                lblFolderStatus.Text = "（フォルダが見つかりません。DB のみ削除します）";
                lblFolderStatus.ForeColor = System.Drawing.Color.Gray;
            }
        }

        private void btnDelete_Click(object sender, EventArgs e)
        {
            DeleteFolder = _folderExists && chkDeleteFolder.Checked;

            if (DeleteFolder)
            {
                var result = MessageBox.Show(
                    $"ゲームフォルダ\n  {_gameFolderPath}\nをディスクから完全に削除します。\n\nこの操作は取り消せません。本当に実行してよろしいですか？",
                    "最終確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (result != DialogResult.Yes)
                {
                    return;
                }
            }

            DialogResult = DialogResult.Yes;
            Close();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
