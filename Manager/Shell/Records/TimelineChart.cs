using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TonePrism.Manager.Models;

namespace TonePrism.Manager.Shell.Records
{
    /// <summary>
    /// (#464) 1 日ぶんの時間帯グラフ (15 分刻み) を滑らかな面グラフで描く軽量コントロール。
    ///
    /// **チャートライブラリを足さずに自前で描く理由**: 必要なのは「1 本の折れ線を塗る」だけで、
    /// 依存を 1 つ増やすと配布 zip・オフライン更新・ライセンス表記まで波及する (Manager は単一 exe を
    /// SMB 越しに置き換える配布形態)。`OnRender` 数十行で足りるものに依存は増やさない。
    ///
    /// 線は Catmull-Rom スプラインを 3 次ベジェに変換して滑らかにする。折れ線のままだと 15 分刻みの
    /// ギザギザが目立ち、来場の波という「読み取りたい形」が見えないため。
    /// </summary>
    public sealed class TimelineChart : FrameworkElement
    {
        private static readonly Brush LineBrush = new SolidColorBrush(Color.FromRgb(0x6C, 0xB6, 0xFF));
        private static readonly Brush AxisBrush = new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x6A));
        private static readonly Brush LabelBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6));
        private static readonly Brush PeakBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xA0, 0x3E));
        private static readonly Brush EmptyBrush = new SolidColorBrush(Color.FromRgb(0x77, 0x7D, 0x83));
        private static readonly Pen LinePen = MakePen(LineBrush, 2.0);
        private static readonly Pen AxisPen = MakePen(AxisBrush, 1.0);
        private static readonly Pen GridPen = MakePen(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), 1.0);
        private static readonly Pen PeakPen = MakePen(PeakBrush, 1.5);

        private static readonly Brush AreaBrush = MakeAreaBrush();

        private const double PadLeft = 34;
        private const double PadRight = 12;
        private const double PadTop = 14;
        private const double PadBottom = 22;

        private PlayStatsTimeline _timeline;

        /// <summary>描画対象の 1 日。null / 全 0 なら「記録なし」を出す。</summary>
        public PlayStatsTimeline Timeline
        {
            get { return _timeline; }
            set { _timeline = value; InvalidateVisual(); }
        }

        private static Pen MakePen(Brush b, double thickness)
        {
            var p = new Pen(b, thickness);
            p.Freeze();
            return p;
        }

        private static Brush MakeAreaBrush()
        {
            var g = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
            };
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0x66, 0x6C, 0xB6, 0xFF), 0));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0x08, 0x6C, 0xB6, 0xFF), 1));
            g.Freeze();
            return g;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double w = ActualWidth, h = ActualHeight;
            if (w <= PadLeft + PadRight || h <= PadTop + PadBottom) return;

            PlayStatsTimeline t = _timeline;
            if (t == null || t.Buckets == null || t.PeakCount <= 0)
            {
                DrawCentered(dc, "この日の記録はありません", w, h);
                return;
            }

            // 記録のある範囲だけを描く。会期は日中だけなので 0 時から描くと左半分が空になる。
            int first = -1, last = -1;
            for (int i = 0; i < t.Buckets.Length; i++)
            {
                if (t.Buckets[i] <= 0) continue;
                if (first < 0) first = i;
                last = i;
            }
            if (first < 0) { DrawCentered(dc, "この日の記録はありません", w, h); return; }

            // 前後に少し余白バケットを足して、山が枠に張り付かないようにする。
            int perBucket = PlayStatsTuning.TimelineBucketMinutes;
            int pad = 60 / perBucket;                        // 1 時間ぶん
            first = Math.Max(0, first - pad);
            last = Math.Min(t.Buckets.Length - 1, last + pad);
            int n = last - first + 1;
            if (n < 2) { DrawCentered(dc, "この日の記録はありません", w, h); return; }

            double plotW = w - PadLeft - PadRight;
            double plotH = h - PadTop - PadBottom;
            double maxY = Math.Max(1, t.PeakCount);

            Func<int, double> xAt = i => PadLeft + plotW * (i - first) / (n - 1);
            Func<double, double> yAt = v => PadTop + plotH * (1 - v / maxY);

            // --- 目盛り (横線 + 値) ---
            int steps = maxY <= 4 ? (int)maxY : 4;
            for (int s = 0; s <= steps; s++)
            {
                double v = maxY * s / steps;
                double y = yAt(v);
                dc.DrawLine(s == 0 ? AxisPen : GridPen, new Point(PadLeft, y), new Point(w - PadRight, y));
                FormattedText ft = Text(Math.Round(v).ToString(CultureInfo.InvariantCulture), 10, LabelBrush);
                dc.DrawText(ft, new Point(PadLeft - 6 - ft.Width, y - ft.Height / 2));
            }

            // --- 面 + 線 ---
            var points = new Point[n];
            for (int i = 0; i < n; i++) points[i] = new Point(xAt(first + i), yAt(t.Buckets[first + i]));

            StreamGeometry line = BuildSmoothPath(points, false, 0);
            StreamGeometry area = BuildSmoothPath(points, true, PadTop + plotH);
            dc.DrawGeometry(AreaBrush, null, area);
            dc.DrawGeometry(null, LinePen, line);

            // --- ピーク ---
            int peakIdx = first;
            for (int i = first; i <= last; i++) if (t.Buckets[i] > t.Buckets[peakIdx]) peakIdx = i;
            var peak = new Point(xAt(peakIdx), yAt(t.Buckets[peakIdx]));
            dc.DrawEllipse(PeakBrush, PeakPen, peak, 3.5, 3.5);
            string peakLabel = string.Format("{0} 　{1} プレイ",
                t.Date.AddMinutes(peakIdx * perBucket).ToString("HH:mm"), t.Buckets[peakIdx]);
            FormattedText pf = Text(peakLabel, 11, PeakBrush);
            double px = Math.Min(Math.Max(PadLeft, peak.X - pf.Width / 2), w - PadRight - pf.Width);
            dc.DrawText(pf, new Point(px, Math.Max(0, peak.Y - pf.Height - 7)));

            // --- 時刻の目盛り (1 時間ごと、狭ければ 2 時間ごと) ---
            int perHour = 60 / perBucket;
            int hourStep = plotW / Math.Max(1, (double)n / perHour) < 46 ? 2 : 1;
            for (int i = first; i <= last; i++)
            {
                int minuteOfDay = i * perBucket;
                if (minuteOfDay % (60 * hourStep) != 0) continue;
                double x = xAt(i);
                dc.DrawLine(GridPen, new Point(x, PadTop), new Point(x, PadTop + plotH));
                FormattedText ft = Text((minuteOfDay / 60).ToString("00") + ":00", 10, LabelBrush);
                dc.DrawText(ft, new Point(x - ft.Width / 2, PadTop + plotH + 5));
            }
        }

        private void DrawCentered(DrawingContext dc, string text, double w, double h)
        {
            FormattedText ft = Text(text, 12, EmptyBrush);
            dc.DrawText(ft, new Point((w - ft.Width) / 2, (h - ft.Height) / 2));
        }

        private FormattedText Text(string s, double size, Brush brush)
        {
            return new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Yu Gothic UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }

        /// <summary>
        /// Catmull-Rom を 3 次ベジェに変換して滑らかな経路を作る。<paramref name="close"/> が true なら
        /// <paramref name="baseline"/> まで下ろして閉じ、面塗り用の形にする。
        /// </summary>
        private static StreamGeometry BuildSmoothPath(Point[] p, bool close, double baseline)
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

                    // Catmull-Rom (張力 1/6) → ベジェ制御点。値が負に沈まないよう Y をクランプする
                    // (プレイ数は 0 未満にならないのに、スプラインの overshoot で軸下に潜るのを防ぐ)。
                    var c1 = new Point(p1.X + (p2.X - p0.X) / 6.0, p1.Y + (p2.Y - p0.Y) / 6.0);
                    var c2 = new Point(p2.X - (p3.X - p1.X) / 6.0, p2.Y - (p3.Y - p1.Y) / 6.0);
                    double floor = Math.Max(p1.Y, p2.Y);
                    if (p1.Y == p2.Y) { c1.Y = Math.Min(c1.Y, floor); c2.Y = Math.Min(c2.Y, floor); }
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
