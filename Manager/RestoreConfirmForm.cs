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
        // (#250 PR3b) この DB 世代とペアになるアセット manifest 情報。null = この世代にアセット控えが無い
        // (旧 safety_*.db や PR1 以前のバックアップ等)。BackupSectionPanel が時刻ペアリングで解決して渡す。
        private readonly AssetSnapshotInfo _pairedSnapshot;

        /// <summary>(#250 PR3b) アセット控えとのペアを与える ctor。<paramref name="pairedSnapshot"/> が非 null のとき
        /// 「ゲームファイルも一緒に復元する」を選べる (既定 ON)。null のときチェックボックスは無効化する。</summary>
        public RestoreConfirmForm(BackupCatalogEntry entry, AssetSnapshotInfo pairedSnapshot)
        {
            InitializeComponent();
            _entry = entry;
            _pairedSnapshot = pairedSnapshot;
        }

        /// <summary>(#250 PR3b) ユーザーが「ゲームファイルも一緒に復元する」を選んだか。アセット控えが無い世代では
        /// チェックボックスは無効なので常に false。呼び出し側はこの値が true のときだけ AssetRestoreService を回す。</summary>
        public bool RestoreAssets => chkRestoreAssets.Enabled && chkRestoreAssets.Checked;

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

            // (#250 PR3b) アセット (games/・guide/) 復元の可否を表示。ペアになる控えがあれば既定 ON、無ければ無効化。
            if (_pairedSnapshot != null)
            {
                chkRestoreAssets.Enabled = true;
                chkRestoreAssets.Checked = true; // 控えがあるなら「DB と一致するゲーム内容に戻す」が既定として安全。
                string host = string.IsNullOrEmpty(_pairedSnapshot.Host) ? "(不明)" : _pairedSnapshot.Host;
                lblAssetInfo.ForeColor = System.Drawing.Color.DimGray;
                lblAssetInfo.Text =
                    $"対応するゲームファイルの控え: {_pairedSnapshot.StartedAtLocal:yyyy/MM/dd HH:mm:ss}" +
                    $" / 取得PC: {host} / {_pairedSnapshot.FileCount} 個のファイル\n" +
                    "チェックを外すとデータベースだけを復元します (ゲームファイルは現在のまま)。";
            }
            else
            {
                chkRestoreAssets.Checked = false;
                chkRestoreAssets.Enabled = false;
                lblAssetInfo.ForeColor = System.Drawing.Color.DimGray;
                lblAssetInfo.Text = "この世代にはゲームファイルの控えがありません。データベースのみ復元します。";
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
