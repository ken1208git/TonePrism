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

            var pts = new Point[n];
            for (int i = 0; i < n; i++)
            {
                pts[i] = new Point(
                    w * i / (n - 1),
                    PadTop + plotH * (1 - v[first + i] / max));
            }

            var area = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            area.GradientStops.Add(new GradientStop(Color.FromArgb(0x59, _hue.R, _hue.G, _hue.B), 0));
            area.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, _hue.R, _hue.G, _hue.B), 1));
            area.Freeze();

            var pen = new Pen(new SolidColorBrush(_hue), 2);   // 細いマーク
            pen.Freeze();

            dc.DrawGeometry(area, null, Path(pts, true, PadTop + plotH));
            dc.DrawGeometry(null, pen, Path(pts, false, 0));

            // ピークだけ点 + ラベル。
            int peak = 0;
            for (int i = 1; i < n; i++) if (v[first + i] > v[first + peak]) peak = i;
            var peakPoint = pts[peak];
            dc.DrawEllipse(new SolidColorBrush(_hue), null, peakPoint, 4, 4);   // >= 8px マーカー

            if (_labelAt != null)
            {
                string text = _labelAt(first + peak);
                if (!string.IsNullOrEmpty(text))
                {
                    FormattedText ft = new FormattedText(text, CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight, new Typeface("Yu Gothic UI"), 10.5,
                        new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6)),
                        VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    double x = Math.Min(Math.Max(0, peakPoint.X - ft.Width / 2), w - ft.Width);
                    dc.DrawText(ft, new Point(x, Math.Max(0, peakPoint.Y - ft.Height - 6)));
                }
            }
        }

        /// <summary>Catmull-Rom を 3 次ベジェに変換した滑らかな経路。<paramref name="close"/> で面塗り用に閉じる。</summary>
        private static Geometry Path(Point[] p, bool close, double baseline)
        {
            var geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                ctx.BeginFigure(p[0], close, close);
                for (int i = 0; i < p.Length - 1; i++)
                {
                    Point p0 = i == 0 ? p[0] : p[i - 1];
                    Point p1 = p[i];
                    Point p2 = p[i + 1];
                    Point p3 = i + 2 < p.Length ? p[i + 2] : p[i + 1];
                    var c1 = new Point(p1.X + (p2.X - p0.X) / 6.0, p1.Y + (p2.Y - p0.Y) / 6.0);
                    var c2 = new Point(p2.X - (p3.X - p1.X) / 6.0, p2.Y - (p3.Y - p1.Y) / 6.0);
                    // 値は 0 未満にならないので、スプラインの行き過ぎで軸下に潜らせない。
                    if (p1.Y == p2.Y)
                    {
                        double floor = Math.Max(p1.Y, p2.Y);
                        c1.Y = Math.Min(c1.Y, floor);
                        c2.Y = Math.Min(c2.Y, floor);
                    }
                    ctx.BezierTo(c1, c2, p2, true, false);
                }
                if (close)
                {
                    ctx.LineTo(new Point(p[p.Length - 1].X, baseline), false, false);
                    ctx.LineTo(new Point(p[0].X, baseline), false, false);
                }
            }
            geo.Freeze();
            return geo;
        }
    }
}
