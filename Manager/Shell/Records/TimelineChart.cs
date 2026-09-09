using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TonePrism.Manager.Models;

namespace TonePrism.Manager.Shell.Records
{
    /// <summary>
    /// (#464) 1 日ぶんの時間帯グラフ (15 分刻み) を面グラフで描く軽量コントロール。
    ///
    /// **チャートライブラリを足さずに自前で描く理由**: 必要なのは「1 本の折れ線を塗る」だけで、
    /// 依存を 1 つ増やすと配布 zip・オフライン更新・ライセンス表記まで波及する (Manager は単一 exe を
    /// SMB 越しに置き換える配布形態)。`OnRender` 数十行で足りるものに依存は増やさない。
    ///
    /// **柱で描く。折れ線ではない。** これは「15 分ごとに何回遊ばれたか」= ビンに入った件数で、
    /// 連続量を一定間隔で測った値ではない。線でつなぐと点と点の間を補間することになり、記録に無い
    /// 山や谷を描く (スプラインを外したのと同じ理由が、折れ線そのものにも当てはまる)。柱なら 1 本が
    /// 「この 15 分に N 回」という完結した事実になる。
    ///
    /// ページ内の他の棒 (★内訳 / プレイ時間分布 / ランキング / 切り口別) と同じ語彙になる利点もある。
    /// </summary>
    public sealed class TimelineChart : FrameworkElement
    {
            private static readonly Brush AxisBrush = new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x6A));
        private static readonly Brush LabelBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6));
        private static readonly Brush PeakBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xA0, 0x3E));
        private static readonly Brush EmptyBrush = new SolidColorBrush(Color.FromRgb(0x77, 0x7D, 0x83));
        // 柱は塗りで描く (線ではない)。ピークだけ明るくして直接ラベルと対応させる。
        private static readonly Brush ColumnBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x39, 0x87, 0xE5)));
        private static readonly Brush PeakColumnBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x6C, 0xA8, 0xF0)));
        /// <summary>柱と柱のすき間 (px)。隣同士がくっつくと 1 本の帯に見えて境目が読めない。</summary>
        private const double ColumnGap = 2;

        /// <summary>
        /// 柱の最大幅 (px)。記録が少ない日は 1 本あたりの枠が広がり、1 件の柱が画面幅いっぱいの
        /// 塊になってしまう (会期 1 日目がまさにそれで、1 プレイが巨大な四角に見えた)。
        /// 上限を置くと、柱は細いまま時刻の位置に立つ。
        /// </summary>
        private const double MaxColumnWidth = 26;
        private static readonly Pen AxisPen = MakePen(AxisBrush, 1.0);
        private static readonly Pen GridPen = MakePen(new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF)), 1.0);

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

            // --- 柱 ---
            int peakIdx = first;
            for (int i = first; i <= last; i++) if (t.Buckets[i] > t.Buckets[peakIdx]) peakIdx = i;

            double slot = plotW / n;
            double colW = Math.Max(1, Math.Min(MaxColumnWidth, slot - ColumnGap));
            double baseY = PadTop + plotH;
            for (int i = first; i <= last; i++)
            {
                int value = t.Buckets[i];
                if (value <= 0) continue;   // 0 は描かない (0 と「ごくわずか」を見分けるため)
                double top = yAt(value);
                double x = PadLeft + slot * (i - first) + (slot - colW) / 2;
                double radius = Math.Min(3, colW / 2);
                dc.DrawRoundedRectangle(i == peakIdx ? PeakColumnBrush : ColumnBrush, null,
                    new Rect(x, top, colW, Math.Max(1, baseY - top)), radius, radius);
            }

            // --- ピークの直接ラベル (数字を置くのはここだけ) ---
            double peakX = PadLeft + slot * (peakIdx - first) + slot / 2;
            double peakY = yAt(t.Buckets[peakIdx]);
            string peakLabel = string.Format("{0} 　{1} プレイ",
                t.Date.AddMinutes(peakIdx * perBucket).ToString("HH:mm"), t.Buckets[peakIdx]);
            FormattedText pf = Text(peakLabel, 11, PeakBrush);
            double px = Math.Min(Math.Max(PadLeft, peakX - pf.Width / 2), w - PadRight - pf.Width);
            dc.DrawText(pf, new Point(px, Math.Max(0, peakY - pf.Height - 5)));

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
    }
}
