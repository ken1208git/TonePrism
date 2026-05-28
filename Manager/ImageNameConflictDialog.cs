using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TonePrism.Manager
{
    /// <summary>
    /// (累積監査 round 3) EditGameForm でゲームフォルダ外の画像を選択した際、コピー先 (v{version}/<filename>)
    /// に同名ファイルが既に存在する場合に user に rename を促す dialog。
    ///
    /// 古いバージョンが古い画像ファイルを参照している可能性があるため、上書き / 既存削除は禁止 (= 古い
    /// バージョンの参照を壊さない方針、user 合意済)。default 提案名は `<base>_2.<ext>` 形式で先に
    /// 衝突しない名前まで自動 increment するが、user が TextBox で自由に編集可能。
    ///
    /// Designer は使わずコードで UI を組む (RestoreReportForm 等と同方針)。
    /// </summary>
    public class ImageNameConflictDialog : Form
    {
        private readonly string _sourcePath;
        private readonly string _existingPath;
        private readonly string _destinationFolder;

        private TextBox _txtNewFileName;
        private Label _lblDestinationPreview;
        private Label _lblValidationError;
        private Button _btnOk;
        private Button _btnCancel;

        private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();
        private static readonly string[] AllowedExtensions = { ".png", ".jpg", ".jpeg", ".bmp" };

        /// <summary>
        /// user が決定した新しいファイル名 (拡張子込み)。OK 押下後のみ有効。
        /// </summary>
        public string ResolvedFileName { get; private set; }

        /// <param name="sourcePath">user が選択した元画像のフルパス。</param>
        /// <param name="destinationFolder">コピー先フォルダ (= v{version}/ のフルパス)。</param>
        /// <param name="suggestedFileName">初期提案名 (= 衝突しない最初の名前、caller が事前計算)。</param>
        public ImageNameConflictDialog(string sourcePath, string destinationFolder, string suggestedFileName)
        {
            _sourcePath = sourcePath ?? "";
            _destinationFolder = destinationFolder ?? "";
            string sourceFileName = Path.GetFileName(_sourcePath);
            _existingPath = Path.Combine(_destinationFolder, sourceFileName);
            BuildUi(suggestedFileName ?? sourceFileName);
            UpdateDestinationPreview();
        }

        private void BuildUi(string initialFileName)
        {
            Text = "同名ファイルがあります";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(540, 320);

            var lblHeadline = new Label
            {
                Text = "コピー先に同名のファイルが既にあります",
                Location = new Point(12, 12),
                AutoSize = true,
                Font = new Font(Font.FontFamily, 10f, FontStyle.Bold),
                ForeColor = Color.DarkGoldenrod
            };

            var lblInfo = new Label
            {
                Text = "上書きせず別名で保存します。ファイル名は下の欄で編集できます。",
                Location = new Point(12, 40),
                Size = new Size(516, 36)
            };

            var lblExisting = new Label
            {
                Text = "既に存在するファイル:",
                Location = new Point(12, 84),
                AutoSize = true,
                Font = new Font(Font.FontFamily, 9f, FontStyle.Bold)
            };
            var txtExisting = new TextBox
            {
                Text = _existingPath,
                Location = new Point(12, 104),
                Size = new Size(516, 22),
                ReadOnly = true,
                Font = new Font(FontFamily.GenericMonospace, 9f)
            };

            var lblSource = new Label
            {
                Text = "コピー元:",
                Location = new Point(12, 132),
                AutoSize = true,
                Font = new Font(Font.FontFamily, 9f, FontStyle.Bold)
            };
            var txtSource = new TextBox
            {
                Text = _sourcePath,
                Location = new Point(12, 152),
                Size = new Size(516, 22),
                ReadOnly = true,
                Font = new Font(FontFamily.GenericMonospace, 9f)
            };

            var lblNewName = new Label
            {
                Text = "保存ファイル名:",
                Location = new Point(12, 180),
                AutoSize = true,
                Font = new Font(Font.FontFamily, 9f, FontStyle.Bold)
            };
            _txtNewFileName = new TextBox
            {
                Text = initialFileName,
                Location = new Point(12, 200),
                Size = new Size(516, 22),
                Font = new Font(FontFamily.GenericMonospace, 9f)
            };
            _txtNewFileName.TextChanged += (s, e) => UpdateDestinationPreview();

            _lblDestinationPreview = new Label
            {
                Text = "",
                Location = new Point(12, 228),
                Size = new Size(516, 18),
                Font = new Font(FontFamily.GenericMonospace, 8.5f),
                ForeColor = Color.DimGray
            };

            _lblValidationError = new Label
            {
                Text = "",
                Location = new Point(12, 248),
                Size = new Size(516, 18),
                ForeColor = Color.Firebrick,
                Font = new Font(Font.FontFamily, 9f, FontStyle.Bold)
            };

            _btnOk = new Button
            {
                Text = "OK",
                Location = new Point(352, 280),
                Size = new Size(84, 28),
                DialogResult = DialogResult.None
            };
            _btnOk.Click += BtnOk_Click;

            _btnCancel = new Button
            {
                Text = "キャンセル",
                Location = new Point(444, 280),
                Size = new Size(84, 28),
                DialogResult = DialogResult.Cancel
            };

            Controls.AddRange(new Control[]
            {
                lblHeadline, lblInfo,
                lblExisting, txtExisting,
                lblSource, txtSource,
                lblNewName, _txtNewFileName,
                _lblDestinationPreview, _lblValidationError,
                _btnOk, _btnCancel
            });

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;
        }

        private void UpdateDestinationPreview()
        {
            string name = _txtNewFileName?.Text ?? "";
            string preview = string.IsNullOrEmpty(_destinationFolder)
                ? name
                : Path.Combine(_destinationFolder, name);
            if (_lblDestinationPreview != null)
            {
                _lblDestinationPreview.Text = "保存先: " + preview;
            }
            // validation error はリアルタイム表示しない (= OK 押下時にまとめて出す)、ただし
            // 「保存先が衝突したまま」は色で示すと user 親切。本実装では OK 押下時 validate のみ。
            if (_lblValidationError != null)
            {
                _lblValidationError.Text = "";
            }
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            string name = (_txtNewFileName.Text ?? "").Trim();

            if (string.IsNullOrEmpty(name))
            {
                ShowError("ファイル名を入力してください。");
                return;
            }

            if (name.IndexOfAny(InvalidNameChars) >= 0)
            {
                ShowError("ファイル名に使用できない文字が含まれています (\\ / : * ? \" < > | など)。");
                return;
            }

            // 拡張子チェック (画像系のみ許可)。caller が UpdateThumbnailPreview で読むため image でないと破綻。
            string ext = Path.GetExtension(name).ToLowerInvariant();
            bool extOk = false;
            foreach (var allowed in AllowedExtensions)
            {
                if (ext == allowed) { extOk = true; break; }
            }
            if (!extOk)
            {
                ShowError("拡張子は .png / .jpg / .jpeg / .bmp のいずれかにしてください。");
                return;
            }

            // 再衝突 check (= user が別名にしたつもりが別の既存ファイルと衝突した場合)
            string destinationPath = Path.Combine(_destinationFolder, name);
            if (File.Exists(destinationPath))
            {
                ShowError("その名前のファイルもコピー先に既にあります。別の名前にしてください。");
                _txtNewFileName.SelectAll();
                _txtNewFileName.Focus();
                return;
            }

            ResolvedFileName = name;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void ShowError(string message)
        {
            _lblValidationError.Text = message;
        }

        /// <summary>
        /// 衝突しない最初の候補名を生成する static helper。caller が dialog を出す前に呼ぶ。
        /// `cover.png` → `cover_2.png` → `cover_3.png` ... と increment、見つかったら return。
        /// </summary>
        public static string SuggestNonConflictingFileName(string destinationFolder, string originalFileName)
        {
            if (string.IsNullOrEmpty(destinationFolder) || string.IsNullOrEmpty(originalFileName))
            {
                return originalFileName ?? "";
            }
            string baseName = Path.GetFileNameWithoutExtension(originalFileName);
            string ext = Path.GetExtension(originalFileName);
            for (int i = 2; i < 1000; i++)
            {
                string candidate = baseName + "_" + i + ext;
                if (!File.Exists(Path.Combine(destinationFolder, candidate)))
                {
                    return candidate;
                }
            }
            // 1000 連続衝突は通常運用ではあり得ないが、安全側で GUID suffix にして必ず一意化
            return baseName + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ext;
        }
    }
}
