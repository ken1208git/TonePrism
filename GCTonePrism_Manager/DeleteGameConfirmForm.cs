using System;
using System.IO;
using System.Windows.Forms;

namespace GCTonePrism.Manager
{
    /// <summary>
    /// ゲーム削除確認フォーム。
    /// 削除実行時は常に「DB レコード + games/{gameId}/ フォルダ」をセットで削除する設計。
    /// フォルダが存在しない場合（手動削除済み等）は DB のみ削除する。
    /// 削除は不可逆操作のため、フォルダパスを表示して何が消えるかを明示する。
    /// </summary>
    public partial class DeleteGameConfirmForm : Form
    {
        private readonly string _gameTitle;
        private readonly string _gameId;
        private readonly string _gameFolderPath;
        private readonly bool _folderExists;

        /// <summary>
        /// 呼び出し側がフォルダ削除を実行すべきか。フォルダ存在時は true、不在時は false。
        /// </summary>
        public bool DeleteFolder => _folderExists;

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

            txtFolderPath.Text = _gameFolderPath;

            if (_folderExists)
            {
                lblFolderHeader.Text = "次のゲームフォルダもディスクから物理的に削除されます:";
                lblFolderHeader.ForeColor = System.Drawing.Color.DarkRed;
                lblFolderStatus.Text = "（この操作は取り消せません）";
                lblFolderStatus.ForeColor = System.Drawing.Color.DarkRed;
            }
            else
            {
                lblFolderHeader.Text = "ゲームフォルダの想定パス:";
                lblFolderHeader.ForeColor = System.Drawing.Color.DimGray;
                lblFolderStatus.Text = "（フォルダが見つからないため、DB のみ削除します）";
                lblFolderStatus.ForeColor = System.Drawing.Color.Gray;
            }
        }

        private void btnDelete_Click(object sender, EventArgs e)
        {
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
