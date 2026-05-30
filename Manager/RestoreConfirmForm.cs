using System;
using System.IO;
using System.Windows.Forms;
using TonePrism.Manager.Models;

namespace TonePrism.Manager
{
    /// <summary>
    /// データベース復元の確認ダイアログ。確認コード入力で誤操作を防止する。
    /// </summary>
    public partial class RestoreConfirmForm : Form
    {
        private string _confirmationCode;
        // (累積監査 round 4 Low-26) instance-shared Random で連打時の seed 衝突を防ぐ。
        // 旧実装は GenerateConfirmationCode 内で毎回 `new Random()` を作っていたため、user の高速連打で
        // 同 1ms 内に複数回呼ばれると同一 seed = 同一コード再出 → 「なぜ同じ？」混乱の UX bug があった。
        // 宣言済の `_random` field を実際に再利用するよう GenerateConfirmationCode を instance method 化。
        private readonly Random _random = new Random();
        private readonly BackupCatalogEntry _entry;

        public RestoreConfirmForm(BackupCatalogEntry entry)
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
                // カタログ entry の FilePath は走査で得た現在の絶対パスそのもの (プロジェクト移動後も
                // 常に現在の保存先を走査するため、昔の絶対パスが出る問題は構造的に発生しない)。
                string resolvedPath = _entry.FilePath;
                lblTargetFile.Text =
                    $"対象: {Path.GetFileName(resolvedPath)}\n" +
                    $"作成日時: {_entry.StartedAtLocal:yyyy/MM/dd HH:mm:ss}\n" +
                    $"サイズ: {FormatBytes(_entry.FileSizeBytes)}\n" +
                    $"フルパス: {resolvedPath}";
            }
        }

        private string GenerateConfirmationCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            char[] code = new char[4];
            for (int i = 0; i < 4; i++)
            {
                code[i] = chars[_random.Next(chars.Length)];
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
