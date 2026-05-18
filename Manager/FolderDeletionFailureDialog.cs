using System;
using System.Windows.Forms;

namespace TonePrism.Manager
{
    /// <summary>
    /// フォルダ削除に失敗した時の対応ダイアログ。
    /// 失敗フォルダのパスと例外メッセージを表示し、ユーザーに「再試行」「諦める」を選ばせる。
    /// 主因は Launcher など他プロセスがフォルダ内のファイルを掴んでいるケース。
    /// その場合はユーザーが Launcher を閉じてから「再試行」を押すだけで解消する。
    /// (#122 Group C 実装)
    /// </summary>
    public partial class FolderDeletionFailureDialog : Form
    {
        private readonly string _folderPath;
        private readonly Exception _lastError;

        public FolderDeletionFailureDialog(string folderPath, Exception lastError)
        {
            InitializeComponent();
            _folderPath = folderPath ?? string.Empty;
            _lastError = lastError;
        }

        private void FolderDeletionFailureDialog_Load(object sender, EventArgs e)
        {
            txtFolderPath.Text = _folderPath;
            txtErrorDetail.Text = _lastError != null ? _lastError.Message : "(詳細不明)";
        }

        private void btnRetry_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Retry;
            Close();
        }

        private void btnGiveUp_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Ignore;
            Close();
        }
    }
}
