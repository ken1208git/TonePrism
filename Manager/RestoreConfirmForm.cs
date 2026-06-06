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

        /// <summary>(#250 PR3b) アセット控えとのペアを与える ctor。<paramref name="pairedSnapshot"/> が非 null のとき、
        /// 確認ダイアログは「games/guide もこの時点に戻す（＝以後に追加・変更したファイルは削除される）」旨を赤字で警告し、
        /// null のときは「DB のみ復元」を表示する。**アセットも戻すか否かの判断は呼出側 (BackupSectionPanel) が同じ
        /// pairedSnapshot から行う**＝真実の源は 1 つ (チェックボックスは round2 で廃止＝一貫時点復元に一本化)。本フォームは
        /// 確認コードと警告表示に徹し、復元可否のフラグは持たない。
        /// </summary>
        public RestoreConfirmForm(BackupCatalogEntry entry, AssetSnapshotInfo pairedSnapshot)
        {
            InitializeComponent();
            _entry = entry;
            _pairedSnapshot = pairedSnapshot;
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

            // (#250 PR3b round2) チェックボックスは廃止し「復元＝その時点に丸ごと戻す」に一本化 (ユーザー判断)。
            // 控えがあれば games/guide も戻る＝**この時点より後に追加したゲーム等は削除される**旨を赤字で強く明示する。
            // 破壊的だが、BackupSectionPanel が reconcile 前に現在の状態を自動退避するので「やり直せる」点も併記。
            if (_pairedSnapshot != null)
            {
                string host = string.IsNullOrEmpty(_pairedSnapshot.Host) ? "(不明)" : _pairedSnapshot.Host;
                lblAssetInfo.ForeColor = System.Drawing.Color.DarkRed;
                lblAssetInfo.Text =
                    "⚠ ゲームの登録情報（データベース）だけでなく、\n" +
                    "　 ゲームファイル本体や初回説明の画像も、この時点の内容に戻します。\n" +
                    "　 この時点より後に追加・変更したファイルはディスクから削除されます。\n" +
                    $"　 戻す控え: {_pairedSnapshot.StartedAtLocal:yyyy/MM/dd HH:mm:ss} / 取得PC: {host} / {_pairedSnapshot.FileCount} ファイル\n" +
                    "　 ※ 削除の前に現在の状態を自動退避するので、履歴から戻してやり直せます。";
            }
            else
            {
                lblAssetInfo.ForeColor = System.Drawing.Color.DimGray;
                lblAssetInfo.Text =
                    "この世代にはゲームファイル本体や初回説明の画像の控えがありません。\n" +
                    "ゲームの登録情報（データベース）だけを復元します（ディスク上のファイルは現在のまま）。";
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
