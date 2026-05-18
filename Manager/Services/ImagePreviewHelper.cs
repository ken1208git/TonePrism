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
        /// </summary>
        public static void UpdatePreview(PictureBox pictureBox, string path, string baseFolder = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    pictureBox.Image = null;
                    return;
                }

                string resolvedPath = path.Trim();
                if (!Path.IsPathRooted(resolvedPath) && !string.IsNullOrEmpty(baseFolder))
                {
                    resolvedPath = Path.Combine(baseFolder, resolvedPath);
                }

                if (File.Exists(resolvedPath))
                {
                    using (var stream = new FileStream(resolvedPath, FileMode.Open, FileAccess.Read))
                    {
                        using (var ms = new MemoryStream())
                        {
                            stream.CopyTo(ms);
                            ms.Position = 0;
                            pictureBox.Image = Image.FromStream(ms);
                        }
                    }
                }
                else
                {
                    pictureBox.Image = null;
                }
            }
            catch
            {
                pictureBox.Image = null;
            }
        }
    }
}
