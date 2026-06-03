using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using TonePrism.Manager.Models;
using TonePrism.Manager.Services;

namespace TonePrism.Manager
{
    /// <summary>
    /// (#253) イントロガイドのスライド 1 枚を編集する Form (`StoreSectionForm` をミラー、画像ピッカー付き)。
    /// UI は `ImageNameConflictDialog` と同じくコード組み (Designer なし)。
    /// 本文 (空可=image-only) / 画像 (任意=text-only) / 表示 ON-OFF を編集する。
    /// 画像は OK 時に `IntroGuideAssetHelper.ImportImage` で `guide/` へコピーし、DB には相対パスを保存。
    /// </summary>
    public class IntroSlideEditForm : Form
    {
        private readonly DatabaseManager _dbManager;
        private readonly IntroSlide _slide;
        private readonly bool _isNew;

        // 画像の編集状態: 新規選択した絶対パス (null=変更なし)、クリア指示 (true=画像を外す)。
        private string _pendingImageAbsolute;
        private bool _clearImage;

        /// <summary>(#295) この編集で guide/ に新規画像ファイルを書いたか。操作単位バックアップで「ゲーム本体
        /// (games/guide) も控えるか」を caller が判定するのに読む。本文/表示のみの編集や既存画像の再利用なら
        /// false = DB だけ控え、重い games/guide 走査を skip できる。
        /// (round8 #3) **将来の縛り**: 本フラグは「guide/ に新規画像を書いた (createdNewFile)」だけを true にする。
        /// 現状 edit パスは画像クリア/差替でも guide/ の旧画像実体を**物理削除しない**ため、ディスク不変 = false で整合する。
        /// もし将来この編集経路に orphan 掃除 (クリア/差替時の旧画像 `IntroGuideAssetHelper` 削除等) を足すなら、
        /// その**削除でも AssetsChangedOnDisk=true** にしないと、guide/ が変わったのに次のアセット操作まで控えに反映されない
        /// silent drift になる (スライド削除パスの imageRemoved 追跡と対称に保つこと)。</summary>
        public bool AssetsChangedOnDisk { get; private set; }

        private TextBox _txtBody;
        private TextBox _txtImage;
        private PictureBox _preview;
        private CheckBox _chkVisible;

        public IntroSlideEditForm(DatabaseManager dbManager, IntroSlide slide = null)
        {
            _dbManager = dbManager;
            if (slide != null)
            {
                _slide = slide;
                _isNew = false;
            }
            else
            {
                _slide = new IntroSlide { DisplayOrder = _dbManager.GetMaxIntroSlideDisplayOrder() + 1 };
                _isNew = true;
            }
            BuildUi();
            LoadFromSlide();
        }

        private void BuildUi()
        {
            Text = _isNew ? "初回説明 スライドの追加" : "初回説明 スライドの編集";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(468, 470);

            Controls.Add(new Label { Text = "本文（スライドに表示する文章。画像のみのスライドなら空でも可）", Location = new Point(12, 10), AutoSize = true });
            _txtBody = new TextBox { Location = new Point(12, 30), Size = new Size(444, 120), Multiline = true, ScrollBars = ScrollBars.Vertical };
            Controls.Add(_txtBody);

            Controls.Add(new Label { Text = "画像（任意。選ぶと guide フォルダにコピーされます）", Location = new Point(12, 160), AutoSize = true });
            _txtImage = new TextBox { Location = new Point(12, 180), Size = new Size(300, 23), ReadOnly = true };
            Controls.Add(_txtImage);
            var btnSelect = new Button { Text = "選択...", Location = new Point(318, 179), Size = new Size(68, 25) };
            btnSelect.Click += OnSelectImage;
            Controls.Add(btnSelect);
            var btnClear = new Button { Text = "クリア", Location = new Point(390, 179), Size = new Size(66, 25) };
            btnClear.Click += OnClearImage;
            Controls.Add(btnClear);

            _preview = new PictureBox { Location = new Point(12, 210), Size = new Size(444, 170), BorderStyle = BorderStyle.Fixed3D, SizeMode = PictureBoxSizeMode.Zoom };
            Controls.Add(_preview);

            _chkVisible = new CheckBox { Text = "表示する", Location = new Point(12, 396), AutoSize = true, Checked = true };
            Controls.Add(_chkVisible);

            var btnOk = new Button { Text = "OK", Location = new Point(290, 432), Size = new Size(80, 27), DialogResult = DialogResult.None };
            btnOk.Click += OnOk;
            Controls.Add(btnOk);
            var btnCancel = new Button { Text = "キャンセル", Location = new Point(376, 432), Size = new Size(80, 27), DialogResult = DialogResult.Cancel };
            Controls.Add(btnCancel);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        private void LoadFromSlide()
        {
            _txtBody.Text = _slide.BodyText ?? "";
            _chkVisible.Checked = _slide.IsVisible;
            if (!string.IsNullOrWhiteSpace(_slide.ImagePath))
            {
                _txtImage.Text = _slide.ImagePath;
                SetPreview(IntroGuideAssetHelper.ToAbsolute(_slide.ImagePath));
            }
        }

        private void OnSelectImage(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                // Launcher の Image.load_from_file が読める形式に限定する。gif は Manager のプレビュー
                // (GDI+) では表示できても Launcher では読めず、来場者画面で画像が出ない (Codex 指摘)。
                // png/jpg/jpeg/bmp は Manager プレビュー・Launcher 読込の両方に対応。
                dlg.Filter = "画像ファイル (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|すべてのファイル (*.*)|*.*";
                dlg.Title = "スライド画像を選択";
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    // 「すべてのファイル (*.*)」で gif 等の Launcher 非対応形式も選べてしまうため、
                    // 拡張子を明示検証して弾く (Codex 指摘)。Manager プレビューは出ても来場者画面で
                    // 表示されない silent な失敗を防ぐ。対応は png/jpg/jpeg/bmp。
                    string ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
                    if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".bmp")
                    {
                        MessageBox.Show(this,
                            "この形式の画像は来場者画面（Launcher）で表示できません。\n\n" +
                            "png / jpg / jpeg / bmp のいずれかを選んでください。",
                            "未対応の画像形式", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    _pendingImageAbsolute = dlg.FileName;
                    _clearImage = false;
                    _txtImage.Text = Path.GetFileName(dlg.FileName) + "（保存時に取り込み）";
                    SetPreview(dlg.FileName);
                }
            }
        }

        private void OnClearImage(object sender, EventArgs e)
        {
            _pendingImageAbsolute = null;
            _clearImage = true;
            _txtImage.Text = "";
            SetPreview(null);
        }

        private void SetPreview(string absPath)
        {
            var old = _preview.Image;
            _preview.Image = null;
            if (old != null) old.Dispose();
            if (string.IsNullOrEmpty(absPath) || !File.Exists(absPath)) return;
            try
            {
                // ファイルロックを避けるためバイト列経由で読む。
                _preview.Image = Image.FromStream(new MemoryStream(File.ReadAllBytes(absPath)));
            }
            catch
            {
                // 壊れた画像 / 非対応形式は preview 無しで黙って続行 (保存自体は妨げない)。
            }
        }

        private void OnOk(object sender, EventArgs e)
        {
            bool bodyEmpty = string.IsNullOrWhiteSpace(_txtBody.Text);
            bool willHaveImage = _pendingImageAbsolute != null
                || (!_clearImage && !string.IsNullOrWhiteSpace(_slide.ImagePath));
            if (bodyEmpty && !willHaveImage)
            {
                MessageBox.Show(this, "本文か画像のどちらかは入力してください。", "入力エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // (#179) DB write 直前の session conflict check。
            string opLabel = _isNew ? "初回説明 スライド追加" : "初回説明 スライド編集";
            if (SessionConflictHelper.CheckBeforeWrite(this, opLabel) == DialogResult.Cancel)
            {
                return; // 編集画面に戻る
            }

            // 画像取り込み (guide/ へコピー → 相対パス)。失敗時は保存せず編集画面に戻る。
            // (#274 review #3) 本 OnOk で新規にコピーした画像の相対パスを控え、後段の DB write が失敗したら
            // orphan として guide/ に残らないよう best-effort で削除する。
            string newlyImportedRel = null;
            try
            {
                if (_pendingImageAbsolute != null)
                {
                    bool createdNewFile;
                    _slide.ImagePath = IntroGuideAssetHelper.ImportImage(_pendingImageAbsolute, out createdNewFile);
                    // 既存 guide/ 画像を再利用した場合 (createdNewFile == false) は、保存失敗時の orphan 掃除で
                    // その既存ファイル (他スライドが参照しているかもしれない) を誤って消さないよう追跡しない。
                    // 新規にコピーしたときだけ「保存失敗なら消す対象」として控える。
                    newlyImportedRel = createdNewFile ? _slide.ImagePath : null;
                    if (createdNewFile) AssetsChangedOnDisk = true; // (#295) guide/ に新規画像を書いた = ゲーム本体側が変わった
                }
                else if (_clearImage)
                {
                    _slide.ImagePath = null;
                }
                // それ以外は既存 ImagePath を維持。
            }
            catch (Exception ex)
            {
                Logger.Error("[IntroSlideEditForm] 画像取り込み失敗", ex);
                MessageBox.Show(this, "画像の取り込みに失敗しました: " + ex.Message, "エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _slide.BodyText = _txtBody.Text.Trim();
            _slide.IsVisible = _chkVisible.Checked;

            try
            {
                if (_isNew) _dbManager.AddIntroSlide(_slide);
                else _dbManager.UpdateIntroSlide(_slide);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                Logger.Error("[IntroSlideEditForm] スライド保存失敗", ex);
                // (#274 review #3) 保存失敗時、この OnOk でコピーした新規画像は DB 参照が付かない orphan に
                // なるため best-effort で削除する (削除失敗は握り潰し)。既存画像 (newlyImportedRel == null) は触らない。
                if (newlyImportedRel != null)
                {
                    try { IntroGuideAssetHelper.DeleteImage(PathManager.GuideFolder, newlyImportedRel); } catch { /* best-effort */ }
                }
                MessageBox.Show(this, "保存に失敗しました: " + ex.Message, "エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
