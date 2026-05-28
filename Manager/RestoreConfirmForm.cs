using System;
using System.IO;
using System.Windows.Forms;
using TonePrism.Manager.Models;
using TonePrism.Manager.Services;

namespace TonePrism.Manager
{
    /// <summary>
    /// データベース復元の確認ダイアログ。確認コード入力で誤操作を防止する。
    /// </summary>
    public partial class RestoreConfirmForm : Form
    {
        private string _confirmationCode;
        private readonly Random _random = new Random();
        private readonly BackupLogEntry _entry;
        private readonly string _dbPath;

        public RestoreConfirmForm(BackupLogEntry entry, string dbPath)
        {
            InitializeComponent();
            _entry = entry;
            _dbPath = dbPath;
        }

        private void RestoreConfirmForm_Load(object sender, EventArgs e)
        {
            _confirmationCode = GenerateConfirmationCode();
            lblConfirmationCode.Text = $"確認コード: {_confirmationCode}";

            if (_entry != null)
            {
                // (復元と表示で同一の解決パスを使う) 実復元時は BackupPathResolver で
                // relative_path から再計算した絶対パスを使うが、ここで _entry.FilePath を生表示
                // するとプロジェクト移動後に「昔の絶対パス」が出てユーザーを混乱させる。
                string resolvedPath = BackupPathResolver.ResolveAbsolutePath(_entry, _dbPath);
                string sizeStr = _entry.FileSizeBytes.HasValue ? FormatBytes(_entry.FileSizeBytes.Value) : "-";
                lblTargetFile.Text =
                    $"対象: {Path.GetFileName(resolvedPath)}\n" +
                    $"作成日時: {_entry.StartedAtLocal:yyyy/MM/dd HH:mm:ss}\n" +
                    $"サイズ: {sizeStr}\n" +
                    $"フルパス: {resolvedPath}";
            }
        }

        private static string GenerateConfirmationCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var random = new Random();
            char[] code = new char[4];
            for (int i = 0; i < 4; i++)
            {
                code[i] = chars[random.Next(chars.Length)];
            }
            return new string(code);
        }

        private void btnConfirm_Click(object sender, EventArgs e)
        {
            if (txtConfirmationCode.Text.Trim().ToUpper() != _confirmationCode.ToUpper())
            {
                MessageBox.Show("確認コードが正しくありません。", "エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                _confirmationCode = GenerateConfirmationCode();
                lblConfirmationCode.Text = $"確認コード: {_confirmationCode}";
                txtConfirmationCode.Clear();
                return;
            }
            DialogResult = DialogResult.Yes;
            Close();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return $"{kb:F1} KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return $"{mb:F2} MB";
            double gb = mb / 1024.0;
            return $"{gb:F2} GB";
        }
    }
}
