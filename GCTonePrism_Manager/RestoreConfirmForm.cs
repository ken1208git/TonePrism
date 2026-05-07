using System;
using System.IO;
using System.Windows.Forms;
using GCTonePrism.Manager.Models;

namespace GCTonePrism.Manager
{
    /// <summary>
    /// データベース復元の確認ダイアログ。確認コード入力で誤操作を防止する。
    /// </summary>
    public partial class RestoreConfirmForm : Form
    {
        private string _confirmationCode;
        private readonly Random _random = new Random();
        private readonly BackupLogEntry _entry;

        public RestoreConfirmForm(BackupLogEntry entry)
        {
            InitializeComponent();
            _entry = entry;
        }

        private void RestoreConfirmForm_Load(object sender, EventArgs e)
        {
            _confirmationCode = GenerateConfirmationCode();
            lblConfirmationCode.Text = $"確認コード: {_confirmationCode}";

            if (_entry != null)
            {
                string sizeStr = _entry.FileSizeBytes.HasValue ? FormatBytes(_entry.FileSizeBytes.Value) : "-";
                lblTargetFile.Text =
                    $"対象: {Path.GetFileName(_entry.FilePath ?? "")}\n" +
                    $"作成日時: {_entry.StartedAtLocal:yyyy/MM/dd HH:mm:ss}\n" +
                    $"サイズ: {sizeStr}\n" +
                    $"フルパス: {_entry.FilePath}";
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
