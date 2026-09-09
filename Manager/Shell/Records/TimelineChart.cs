using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TonePrism.Manager.Models;

namespace TonePrism.Manager.Shell.Records
{
    /// <summary>
    /// (#464) 「いつ遊ばれたか」の時系列を柱グラフで描く軽量コントロール。
    ///
    /// **描くのは常に 1 本の連続した並び**。日ごとに 1 枚ずつ描く作りだと、記録が増えるほど
    /// グラフの枚数が際限なく増えた。刻み (15 分 / 時 / 日 / 週 / 月 / 年) は
    /// <see cref="PlayStatsTrend"/> が期間の長さから決めており、本数は常に有界。
    ///
    /// **柱で描く。折れ線ではない。** これは区間ごとの件数であって、連続量を一定間隔で測った値では
    /// ない。線でつなぐと点と点の間を補間することになり、記録に無い山や谷を描く。柱なら 1 本が
    /// 「この区間に N 回」という完結した事実になる。
    ///
    /// **チャートライブラリを足さずに自前で描く理由**: 必要なのは柱を並べるだけで、依存を 1 つ
    /// 増やすと配布 zip・オフライン更新・ライセンス表記まで波及する (Manager は単一 exe を
    /// SMB 越しに置き換える配布形態)。`OnRender` 数十行で足りるものに依存は増やさない。
    /// </summary>
    public sealed class TimelineChart : FrameworkElement
    {
        private static readonly Brush AxisBrush = new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x6A));
        private static readonly Brush LabelBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6));
        /// <summary>日付の目盛り用 (時刻の目盛りより一段明るくして日の境目を分からせる)。</summary>
        private static readonly Brush LabelStrongBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0xDD, 0xE2));
        private static readonly Brush PeakBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xA0, 0x3E));
        private static readonly Brush EmptyBrush = new SolidColorBrush(Color.FromRgb(0x77, 0x7D, 0x83));
        private static readonly Pen AxisPen = MakePen(AxisBrush, 1.0);
        private static readonly Pen GridPen = MakePen(new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF)), 1.0);

        // 柱は塗りで描く。ピークだけ明るくして直接ラベルと対応させる。
        private static readonly Brush ColumnBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x39, 0x87, 0xE5)));
        private static readonly Brush PeakColumnBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x6C, 0xA8, 0xF0)));

        /// <summary>柱と柱のすき間 (px)。隣同士がくっつくと 1 本の帯に見えて境目が読めない。</summary>
        private const double ColumnGap = 2;

        /// <summary>
        /// 柱の最大幅 (px)。記録が少ない期間は 1 本あたりの枠が広がり、1 件の柱が画面幅いっぱいの
        /// 塊になってしまう。上限を置くと、柱は細いまま時刻の位置に立つ。
        /// </summary>
        private const double MaxColumnWidth = 26;

        private const double PadLeft = 34;
        private const double PadRight = 12;
        private const double PadTop = 18;
        private const double PadBottom = 22;

        private PlayStatsTrend _trend;

        /// <summary>描画対象。null / 全 0 なら「記録なし」を出す。</summary>
        public PlayStatsTrend Trend
        {
            get { return _trend; }
            set { _trend = value; InvalidateVisual(); }
        }

        private static Brush Freeze(Brush b)
        {
            b.Freeze();
            return b;
        }

        private static Pen MakePen(Brush b, double thickness)
        {
            var p = new Pen(b, thickness);
            p.Freeze();
            return p;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double w = ActualWidth, h = ActualHeight;
            if (w <= PadLeft + PadRight || h <= PadTop + PadBottom) return;

            PlayStatsTrend t = _trend;
            if (t == null || !t.HasData)
            {
                DrawCentered(dc, "この期間の記録はありません", w, h);
                return;
            }

            List<PlayStatsTrendPoint> pts = t.Points;
            int n = pts.Count;
            double plotW = w - PadLeft - PadRight;
            double plotH = h - PadTop - PadBottom;
            double maxY = Math.Max(1, t.PeakCount);
            double baseY = PadTop + plotH;

            Func<double, double> yAt = v => PadTop + plotH * (1 - v / maxY);

            // --- 目盛り (横線 + 値) ---
            int steps = maxY <= 4 ? (int)maxY : 4;
            for (int i = 0; i <= steps; i++)
            {
                double v = maxY * i / steps;
                double y = yAt(v);
                dc.DrawLine(i == 0 ? AxisPen : GridPen, new Point(PadLeft, y), new Point(w - PadRight, y));
                FormattedText ft = Text(Math.Round(v).ToString(CultureInfo.InvariantCulture), 10, LabelBrush);
                dc.DrawText(ft, new Point(PadLeft - 6 - ft.Width, y - ft.Height / 2));
            }

            // --- 柱 ---
            double slot = plotW / n;
            double colW = Math.Max(1, Math.Min(MaxColumnWidth, slot - ColumnGap));
            for (int i = 0; i < n; i++)
            {
                int value = pts[i].Count;
                if (value <= 0) continue;   // 0 は描かない (0 と「ごくわずか」を見分けるため)
                double top = yAt(value);
                double x = PadLeft + slot * i + (slot - colW) / 2;
                double radius = Math.Min(3, colW / 2);
                dc.DrawRoundedRectangle(i == t.PeakIndex ? PeakColumnBrush : ColumnBrush, null,
                    new Rect(x, top, colW, Math.Max(1, baseY - top)), radius, radius);
            }

            // --- ピークの直接ラベル (数字を置くのはここだけ) ---
            if (t.PeakIndex >= 0 && t.PeakIndex < n)
            {
                double peakX = PadLeft + slot * t.PeakIndex + slot / 2;
                double peakY = yAt(t.PeakCount);
                string label = pts[t.PeakIndex].Start.ToString(t.PeakLabelFormat, CultureInfo.CurrentCulture)
                    + "　" + t.PeakCount + " プレイ";
                FormattedText pf = Text(label, 11, PeakBrush);
                double px = Math.Min(Math.Max(PadLeft, peakX - pf.Width / 2), w - PadRight - pf.Width);
                dc.DrawText(pf, new Point(px, Math.Max(0, peakY - pf.Height - 5)));
            }

            DrawTicks(dc, t, pts, PadLeft, plotW, baseY, slot);
        }

        /// <summary>
        /// 横軸の目盛り。**入るぶんだけ間引く** — 本数ぶん全部ラベルを置くと必ず重なる。
        /// </summary>
        private void DrawTicks(DrawingContext dc, PlayStatsTrend t, List<PlayStatsTrendPoint> pts,
            double left, double plotW, double baseY, double slot)
        {
            int n = pts.Count;
            FormattedText sample = Text(pts[0].Start.ToString(t.TickFormat, CultureInfo.CurrentCulture), 10, LabelBrush);
            double need = sample.Width + 18;
            int every = Math.Max(1, (int)Math.Ceiling(need / Math.Max(1, slot)));

            bool clock = t.Unit == PlayStatsTrendUnit.Minute15 || t.Unit == PlayStatsTrendUnit.Hour;

            // **目盛りの位相を日の変わり目に合わせる。** 等間隔に置くだけだと 0 時が間引きに
            // 当たらず、下の「0 時は日付を出す」分岐に一度も入らないことがある。そうなると
            // 「16:00 … 14:00」と時刻だけが並び、2 日ぶんであることが読めない。
            int start = 0;
            if (clock)
            {
                for (int i = 0; i < n; i++)
                {
                    if (pts[i].Start.TimeOfDay != TimeSpan.Zero) continue;
                    start = i % every;
                    break;
                }
            }

            for (int i = start; i < n; i += every)
            {
                double x = left + slot * i + slot / 2;
                dc.DrawLine(GridPen, new Point(x, PadTop), new Point(x, baseY));

                // 時刻きざみで日をまたぐとき、0 時の目盛りは時刻ではなく日付にする。
                // 「16:00 … 00:00 … 14:00」だと 2 日分だと分からない。
                bool dayStart = clock && pts[i].Start.TimeOfDay == TimeSpan.Zero;
                string label = dayStart
                    ? pts[i].Start.ToString("M/d", CultureInfo.CurrentCulture)
                    : pts[i].Start.ToString(t.TickFormat, CultureInfo.CurrentCulture);
                FormattedText ft = Text(label, 10, dayStart ? LabelStrongBrush : LabelBrush);
                dc.DrawText(ft, new Point(x - ft.Width / 2, baseY + 5));
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
    }
}
