using System;
using System.IO;
using System.Windows.Forms;

namespace GCTonePrism.Manager
{
    /// <summary>
    /// ゲーム削除確認フォーム。
    /// 削除実行時は常に「DB レコード + games/{gameId}/ フォルダ」をセットで削除する設計。
    /// フォルダが存在しない場合（手動削除済み等）は DB のみ削除する。
    /// 部員が見ても何が消えるか（特に「自分の開発フォルダは無事か」）を判断できるよう、
    /// 専門用語を避けて 1.〜 2. のリスト形式で明示する。
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
            lblGameId.Text = $"ゲームID: {_gameId}";

            txtFolderPath.Text = _gameFolderPath;

            // 1 番目（DB 側）の文言は状態によらず固定
            lblItem1Title.Text = "1. ゲーム情報・プレイ記録・アンケート回答";
            lblItem1Detail.Text = "    (製作者情報・バージョン情報も含む)";

            if (_folderExists)
            {
                lblHeader.Text = "以下の 2 つが削除されます:";
                lblHeader.ForeColor = System.Drawing.Color.DarkRed;

                lblItem2Title.Text = "2. ゲームファイル (実行ファイル・サムネイル・背景画像など)";
                lblItem2Title.ForeColor = System.Drawing.Color.DarkRed;

                lblItem2Note1.Visible = true;
                lblItem2Note2.Visible = true;
                lblItem2Note1.Text = "    ※ Manager にゲームを追加した際にコピーされたものです。";
                lblItem2Note2.Text = "       部員の開発フォルダには影響しません。";
            }
            else
            {
                lblHeader.Text = "削除されるもの:";
                lblHeader.ForeColor = System.Drawing.Color.DimGray;

                lblItem2Title.Text = "2. ゲームファイル — 既に手動削除済み (DB のみ削除します)";
                lblItem2Title.ForeColor = System.Drawing.Color.DimGray;

                lblItem2Note1.Visible = false;
                lblItem2Note2.Visible = false;
            }
        }

        private void btnDelete_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Yes;
            Close();
        }
    }
}
