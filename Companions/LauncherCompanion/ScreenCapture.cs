using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TonePrism.LauncherCompanion
{
    /// <summary>
    /// プライマリスクリーンを 1 枚 PNG にキャプチャする (中断オーバーレイ #30 のすりガラス背景用)。
    /// 中断メニューは透明窓でゲームを透かすが、Godot はゲーム (別プロセス) の画素を blur できないため、
    /// 「メニューを出す直前のゲーム画面」をここで撮って Godot に渡し、Godot 側でぼかす。
    ///
    /// DPI 非対応プロセスなので高 DPI では論理解像度で撮れる場合があるが、どのみち blur するため許容
    /// (Godot 側で content-scale に引き伸ばされて少しソフトになるだけ)。
    /// </summary>
    internal static class ScreenCapture
    {
        /// <summary>
        /// 仮想スクリーン座標 (x,y) から w×h の領域を PNG 保存する。Launcher が「ランチャーのある画面」の
        /// rect (DisplayServer の物理座標) を渡す前提。companion は DPI-aware (Program 起動時に設定) なので
        /// Godot の物理座標とこの CopyFromScreen の座標が一致する。マルチモニターは仮想座標で指定可。
        /// </summary>
        // ぼかし背景用なので低解像度で十分。フル解像度 PNG 保存は高解像度で遅く、Launcher 側の
        // 待ち時間 timeout を招くため、最大幅まで縮小して保存し高速化する。
        private const int MaxWidth = 720;

        public static bool CaptureRegion(int x, int y, int w, int h, string path)
        {
            try
            {
                if (w <= 0 || h <= 0) return false;
                using (var full = new Bitmap(w, h, PixelFormat.Format24bppRgb))
                {
                    using (var g = Graphics.FromImage(full))
                    {
                        g.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
                    }
                    int dstW = Math.Min(w, MaxWidth);
                    int dstH = (int)((long)h * dstW / w);
                    if (dstH < 1) dstH = 1;
                    if (dstW >= w)
                    {
                        full.Save(path, ImageFormat.Png); // 既に十分小さい
                    }
                    else
                    {
                        using (var small = new Bitmap(dstW, dstH, PixelFormat.Format24bppRgb))
                        {
                            using (var g2 = Graphics.FromImage(small))
                            {
                                g2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                                g2.DrawImage(full, 0, 0, dstW, dstH);
                            }
                            small.Save(path, ImageFormat.Png);
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("[capture] スクリーンキャプチャ失敗: " + path, ex);
                return false;
            }
        }
    }
}
