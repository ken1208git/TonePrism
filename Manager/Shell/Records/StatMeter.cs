using System;
using System.Windows;
using System.Windows.Media;

namespace TonePrism.Manager.Shell.Records
{
    /// <summary>
    /// (#464) 「上限のある比率」を 1 つ見せるメーター (270° の円弧)。平均★ (5 点満点) や
    /// アンケート回答率のように**分母が実在する**指標にだけ使う。
    ///
    /// **分母が無い指標にこれを使ってはいけない。** 弧は「上限のうちどこまで」を意味するので、
    /// プレイ回数や総プレイ時間のように上限の無い値に当てると、存在しない目標を発明することになる。
    /// そちらは数値そのもの (ヒーロー数値 / スタットタイル) で見せる。
    ///
    /// 目盛りの無い残り部分 (トラック) は**同じ色の薄い段**にする。別の灰色を使うと 2 色に見えて
    /// 「2 値の円グラフ」に読めてしまうため。中央の文字は本コントロールでは描かず、呼び出し側が
    /// 文字色トークンで重ねる (色は marker が担い、文字は文字色のまま、という原則)。
    /// </summary>
    public sealed class StatMeter : FrameworkElement
    {
        private double _value;
        private double _max = 1;
        private Color _hue = Color.FromRgb(0x39, 0x87, 0xE5);
        private double _thickness = 9;

        /// <summary>現在値。<see cref="Max"/> を超える値は満杯として描く。</summary>
        public double Value
        {
            get { return _value; }
            set { _value = value; InvalidateVisual(); }
        }

        /// <summary>上限 (満杯にあたる値)。0 以下は 1 として扱う (0 除算回避)。</summary>
        public double Max
        {
            get { return _max; }
            set { _max = value <= 0 ? 1 : value; InvalidateVisual(); }
        }

        /// <summary>指標の色。トラックはこの色の薄い段を自動で作る。</summary>
        public Color Hue
        {
            get { return _hue; }
            set { _hue = value; InvalidateVisual(); }
        }

        public double Thickness
        {
            get { return _thickness; }
            set { _thickness = value; InvalidateVisual(); }
        }

        // 下を開けた 270°。真円 (360°) にすると円グラフに見えるので、あえて開ける。
        private const double StartAngle = 135;
        private const double SweepAngle = 270;

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;

            double radius = (Math.Min(w, h) - _thickness) / 2;
            if (radius <= 0) return;
            var center = new Point(w / 2, h / 2);

            var trackPen = new Pen(new SolidColorBrush(Color.FromArgb(0x3A, _hue.R, _hue.G, _hue.B)), _thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };
            var fillPen = new Pen(new SolidColorBrush(_hue), _thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };

            dc.DrawGeometry(null, trackPen, Arc(center, radius, StartAngle, SweepAngle));

            double ratio = Math.Max(0, Math.Min(1, _value / _max));
            // 0 でも点として出さない (0 を「ごくわずかある」と読ませないため)。
            if (ratio > 0) dc.DrawGeometry(null, fillPen, Arc(center, radius, StartAngle, SweepAngle * ratio));
        }

        /// <summary>時計回りの円弧。角度は「真下 = 90°」の画面座標系 (y 下向き) で数える。</summary>
        private static Geometry Arc(Point center, double radius, double startDeg, double sweepDeg)
        {
            var geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                Point from = OnCircle(center, radius, startDeg);
                ctx.BeginFigure(from, false, false);
                // ArcTo は 180° を超えると大弧の指定が要る。
                ctx.ArcTo(OnCircle(center, radius, startDeg + sweepDeg),
                    new Size(radius, radius), 0,
                    sweepDeg > 180, SweepDirection.Clockwise, true, false);
            }
            geo.Freeze();
            return geo;
        }

        private static Point OnCircle(Point center, double radius, double deg)
        {
            double rad = deg * Math.PI / 180;
            return new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
        }
    }
}
