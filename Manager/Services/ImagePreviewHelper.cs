using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// 画像プレビュー更新の共通ヘルパー。
    /// AddGameForm / EditGameForm のサムネイル・背景プレビューを共通化する。
    /// </summary>
    public static class ImagePreviewHelper
    {
        /// <summary>
        /// 指定パスの画像をPictureBoxに表示する。
        /// パスが相対パスの場合は baseFolderで解決する。
        ///
        /// (round 5 H3) Image は GDI+ unmanaged handle を保持するため、PictureBox.Image に新 Image を
        /// セットする前に旧 Image を必ず Dispose する。これを怠ると GC 任せの非決定 dispose になり、
        /// 9 時間連続展示で編集画面の画像切替を繰り返すと GDI handle 上限 (10,000) に達して WinForms
        /// 全体が描画不能になる経路があった。さらに `Image.FromStream` は MS docs 仕様で
        /// 「stream must remain open for the lifetime of the Image」 — 本実装は MemoryStream を using で
        /// 即解放しているため、`new Bitmap(image)` でクローン化してから代入し、stream lifetime と切り離す。
        /// </summary>
        public static void UpdatePreview(PictureBox pictureBox, string path, string baseFolder = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    SetImageWithDispose(pictureBox, null);
                    return;
                }

                string resolvedPath = path.Trim();
                if (!Path.IsPathRooted(resolvedPath) && !string.IsNullOrEmpty(baseFolder))
                {
                    resolvedPath = Path.Combine(baseFolder, resolvedPath);
                }

                if (File.Exists(resolvedPath))
                {
                    // (round 5 H3) FileStream → MemoryStream → Image.FromStream → new Bitmap でクローン化。
                    // Image.FromStream の戻り値は元 stream に依存するため、Bitmap でコピーしてから
                    // 元 Image / MemoryStream の両方を破棄する。これで PictureBox に渡る画像は stream 独立。
                    using (var stream = new FileStream(resolvedPath, FileMode.Open, FileAccess.Read))
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        ms.Position = 0;
                        using (var raw = Image.FromStream(ms))
                        {
                            var bmp = new Bitmap(raw);
                            SetImageWithDispose(pictureBox, bmp);
                        }
                    }
                }
                else
                {
                    SetImageWithDispose(pictureBox, null);
                }
            }
            catch
            {
                SetImageWithDispose(pictureBox, null);
            }
        }

        /// <summary>
        /// (round 5 H3) PictureBox.Image に新 Image をセットする際、旧 Image を確実に Dispose する。
        /// 旧値が null なら no-op、新値と同一インスタンスなら何もしない (defensive)。
        /// </summary>
        private static void SetImageWithDispose(PictureBox pictureBox, Image newImage)
        {
            var oldImage = pictureBox.Image;
            if (ReferenceEquals(oldImage, newImage)) return;
            pictureBox.Image = newImage;
            oldImage?.Dispose();
        }
    }
}
