using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace TonePrism.Manager.Shell.Records
{
    /// <summary>
    /// (#464) ヒーロー数値に添える小さな推移グラフ。軸も目盛りも凡例も持たない
    /// (単系列なので凡例は不要、値は隣の数値が持っている)。
    ///
    /// **直接ラベルはピークだけ**。点ごとに数字を置くと読まれないうえ画面が荒れる。
    /// **柱で描く** — これは時間帯ごとの件数 (ビンに入った数) で、線でつなぐと点の間を補間して
    /// 記録に無い形を描くことになる。<see cref="TimelineChart"/> と同じ理由。
    /// <see cref="TimelineChart"/> は日ごとの詳細を軸つきで見せる別物で、こちらは「形」だけを伝える。
    /// </summary>
    public sealed class Sparkline : FrameworkElement
    {
        private int[] _values;
        private Color _hue = Color.FromRgb(0x39, 0x87, 0xE5);
        private Func<int, string> _labelAt;

        /// <summary>描画するバケット列。先頭・末尾の 0 は自動で切り詰める。</summary>
        public int[] Values
        {
            get { return _values; }
            set { _values = value; InvalidateVisual(); }
        }

        public Color Hue
        {
            get { return _hue; }
            set { _hue = value; InvalidateVisual(); }
        }

        /// <summary>ピークの直接ラベル (index → 表示文字列)。null ならラベルを出さない。</summary>
        public Func<int, string> LabelAt
        {
            get { return _labelAt; }
            set { _labelAt = value; InvalidateVisual(); }
        }

        private const double PadTop = 16;
        private const double PadBottom = 4;
        /// <summary>柱と柱のすき間 (px)。くっつくと 1 本の帯に見えて境目が読めない。</summary>
        private const double ColumnGap = 2;
        /// <summary>
        /// 柱の最大幅 (px)。上限を厳しくすると、時間帯が 10 本ほどしかないこの帯では柱が細く離れて
        /// 並び、間が抜けて見える。幅いっぱいに近い柱の方が「1 日のかたち」として読める。
        /// </summary>
        private const double MaxColumnWidth = 64;

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            int[] v = _values;
            double w = ActualWidth, h = ActualHeight;
            if (v == null || v.Length == 0 || w <= 2 || h <= PadTop + PadBottom) return;

            int first = -1, last = -1;
            for (int i = 0; i < v.Length; i++)
            {
                if (v[i] <= 0) continue;
                if (first < 0) first = i;
                last = i;
            }
            if (first < 0 || last <= first) return;

            int n = last - first + 1;
            double plotH = h - PadTop - PadBottom;
            double max = 0;
            for (int i = first; i <= last; i++) max = Math.Max(max, v[i]);
            if (max <= 0) return;

            int peak = 0;
            for (int i = 1; i < n; i++) if (v[first + i] > v[first + peak]) peak = i;

            var normal = new SolidColorBrush(Color.FromArgb(0xB0, _hue.R, _hue.G, _hue.B));
            var accent = new SolidColorBrush(_hue);
            normal.Freeze();
            accent.Freeze();

            double slot = w / n;
            double colW = Math.Max(1, Math.Min(MaxColumnWidth, slot - ColumnGap));
            double baseY = PadTop + plotH;
            double peakX = slot * peak + slot / 2;
            double peakTop = baseY;

            for (int i = 0; i < n; i++)
            {
                int value = v[first + i];
                if (value <= 0) continue;   // 0 は描かない
                double top = PadTop + plotH * (1 - value / max);
                double x = slot * i + (slot - colW) / 2;
                double radius = Math.Min(3, colW / 2);
                dc.DrawRoundedRectangle(i == peak ? accent : normal, null,
                    new Rect(x, top, colW, Math.Max(1, baseY - top)), radius, radius);
                if (i == peak) peakTop = top;
            }

            if (_labelAt != null)
            {
                string text = _labelAt(first + peak);
                if (!string.IsNullOrEmpty(text))
                {
                    FormattedText ft = new FormattedText(text, CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight, new Typeface("Yu Gothic UI"), 10.5,
                        new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6)),
                        VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    double x = Math.Min(Math.Max(0, peakX - ft.Width / 2), w - ft.Width);
                    dc.DrawText(ft, new Point(x, Math.Max(0, peakTop - ft.Height - 4)));
                }
            }
        }
    }
}
