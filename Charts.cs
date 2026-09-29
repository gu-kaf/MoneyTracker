using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace MoneyTracker
{
    /// <summary>图表基类：统一取色、统一文字渲染，主题切换后自动重画</summary>
    public abstract class ChartBase : FrameworkElement
    {
        protected Brush C(string key)
            => Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;

        protected Color Col(string key)
            => Application.Current?.TryFindResource(key + "Color") is Color c ? c : Colors.Gray;

        protected static readonly Color[] Palette =
        {
            Color.FromRgb(0x2F, 0x6F, 0xED), Color.FromRgb(0xE5, 0x48, 0x4D),
            Color.FromRgb(0x26, 0xA2, 0x69), Color.FromRgb(0xE0, 0x8A, 0x1F),
            Color.FromRgb(0x7C, 0x5C, 0xE0), Color.FromRgb(0x0F, 0xA4, 0x7F),
            Color.FromRgb(0xE0, 0x56, 0x8F), Color.FromRgb(0x4C, 0x9A, 0xFF),
            Color.FromRgb(0xC9, 0x50, 0x3F), Color.FromRgb(0x5B, 0x64, 0x72),
            Color.FromRgb(0x9B, 0x7A, 0x2F), Color.FromRgb(0x3D, 0x8B, 0x4F)
        };

        public static Color ColorAt(int i) => Palette[((i % Palette.Length) + Palette.Length) % Palette.Length];

        protected FormattedText Txt(string s, double size, Brush b, bool bold = false, bool rightAlign = false)
        {
            var ft = new FormattedText(s ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Microsoft YaHei UI, Segoe UI"), FontStyles.Normal,
                    bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
                size, b, VisualTreeHelper.GetDpi(this).PixelsPerDip)
            {
                TextAlignment = rightAlign ? TextAlignment.Right : TextAlignment.Left
            };
            return ft;
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            InvalidateVisual();
        }

        public new void InvalidateVisual() => base.InvalidateVisual();
    }

    /// <summary>纵向柱状图（月度趋势、每日金额都用它）</summary>
    public class BarChart : ChartBase
    {
        public List<(string label, double value)> Data = new List<(string, double)>();
        public List<(string label, double value)> Data2 = new List<(string, double)>();
        public string ColorKey1 = "ExpenseBrush";
        public string ColorKey2 = "IncomeBrush";
        public string ValueFormat = "N0";

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 40 || h < 40 || Data.Count == 0) return;

            var border = C("BorderBrush2");
            var sub = C("SubBrush");
            var text = C("TextBrush");
            var pen = new Pen(border, 1);
            pen.Freeze();

            double padL = 48, padR = 10, padT = 16, padB = 32;
            double plotW = w - padL - padR, plotH = h - padT - padB;
            if (plotW < 20 || plotH < 20) return;

            double max = Math.Max(1, Math.Max(
                Data.Count > 0 ? Data.Max(d => d.value) : 0,
                Data2.Count > 0 ? Data2.Max(d => d.value) : 0));

            // 横向网格 + 左侧刻度
            int lines = 4;
            for (int i = 0; i <= lines; i++)
            {
                double y = padT + plotH - plotH * i / lines;
                dc.DrawLine(pen, new Point(padL, y), new Point(w - padR, y));
                var lbl = Txt((max * i / lines).ToString("N0"), 10, sub);
                dc.DrawText(lbl, new Point(padL - 6 - lbl.Width, y - lbl.Height / 2));
            }

            int n = Data.Count;
            double slot = plotW / n;
            bool dual = Data2.Count == n && Data2.Any(d => d.value > 0);
            double bw = dual ? Math.Max(3, slot * 0.30) : Math.Max(4, slot * 0.55);

            for (int i = 0; i < n; i++)
            {
                double cx = padL + slot * i + slot / 2;

                void DrawBar(double val, double offset, Color col)
                {
                    if (val <= 0) return;
                    double bh = Math.Max(2, plotH * (val / max));
                    double x = cx - (dual ? bw + 1 : bw / 2) + offset;
                    var rect = new Rect(x, padT + plotH - bh, bw, bh);
                    var brush = new SolidColorBrush(col);
                    brush.Freeze();
                    dc.DrawRoundedRectangle(brush, null, rect, 3, 3);

                    if (n <= 14)
                    {
                        var vt = Txt(val.ToString(ValueFormat), 10, text, true);
                        dc.DrawText(vt, new Point(x + bw / 2 - vt.Width / 2, rect.Y - vt.Height - 2));
                    }
                }

                if (dual)
                {
                    DrawBar(Data[i].value, 0, Col(ColorKey1));
                    DrawBar(Data2[i].value, bw + 2, Col(ColorKey2));
                }
                else DrawBar(Data[i].value, 0, ColorAt(i));

                var lt = Txt(Data[i].label, 10, sub);
                if (lt.Width < slot - 2) dc.DrawText(lt, new Point(cx - lt.Width / 2, padT + plotH + 6));
                else if (n <= 8) dc.DrawText(lt, new Point(cx - lt.Width / 2, padT + plotH + 6));
            }
        }
    }

    /// <summary>横向条形图（分类排行）</summary>
    public class HBarChart : ChartBase
    {
        public List<(string label, double value)> Data = new List<(string, double)>();
        public bool ShowPercent = true;

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 60 || h < 40 || Data.Count == 0) return;

            var sub = C("SubBrush");
            var text = C("TextBrush");

            double padL = 76, padR = 84, padT = 4, padB = 4;
            double plotW = w - padL - padR;
            if (plotW < 30) return;

            int n = Data.Count;
            double rowH = (h - padT - padB) / n;
            double bh = Math.Min(20, rowH * 0.56);
            double max = Math.Max(1, Data.Max(d => d.value));
            double total = Data.Sum(d => d.value);

            for (int i = 0; i < n; i++)
            {
                double cy = padT + rowH * i + rowH / 2;
                var col = ColorAt(i);
                var brush = new SolidColorBrush(col);
                brush.Freeze();

                var name = Txt(Data[i].label, 12, text);
                dc.DrawText(name, new Point(padL - 10 - name.Width, cy - name.Height / 2));

                double bw = Math.Max(2, plotW * (Data[i].value / max));
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(46, col.R, col.G, col.B)), null,
                    new Rect(padL, cy - bh / 2, plotW, bh), bh / 2, bh / 2);
                dc.DrawRoundedRectangle(brush, null, new Rect(padL, cy - bh / 2, bw, bh), bh / 2, bh / 2);

                string right = ShowPercent && total > 0
                    ? Data[i].value.ToString("N0") + "　" + (Data[i].value * 100 / total).ToString("0.0") + "%"
                    : Data[i].value.ToString("N0");
                var vt = Txt(right, 11, sub);
                dc.DrawText(vt, new Point(w - padR + 8, cy - vt.Height / 2));
            }
        }
    }

    /// <summary>环形图 + 图例（支出占比）</summary>
    public class DonutChart : ChartBase
    {
        public List<(string label, double value)> Data = new List<(string, double)>();
        public string CenterTitle = "";
        public string CenterValue = "";

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 80 || h < 60 || Data.Count == 0) return;

            var sub = C("SubBrush");
            var text = C("TextBrush");

            double legendW = Math.Min(170, w * 0.42);
            double availW = w - legendW;
            double size = Math.Min(availW, h) - 16;
            if (size < 40) return;

            double cx = 8 + size / 2, cy = h / 2;
            double outer = size / 2, inner = outer * 0.62;
            double total = Data.Sum(d => d.value);
            if (total <= 0) return;

            double angle = -90;
            for (int i = 0; i < Data.Count; i++)
            {
                double sweep = Data[i].value / total * 360.0;
                var col = ColorAt(i);
                var brush = new SolidColorBrush(col);
                brush.Freeze();
                DrawArc(dc, brush, cx, cy, outer, inner, angle, sweep);
                angle += sweep;
            }

            if (!string.IsNullOrEmpty(CenterValue))
            {
                var cv = Txt(CenterValue, 15, text, true);
                var ct = Txt(CenterTitle, 10.5, sub);
                dc.DrawText(cv, new Point(cx - cv.Width / 2, cy - cv.Height / 2 - 2));
                dc.DrawText(ct, new Point(cx - ct.Width / 2, cy + cv.Height / 2 - 2));
            }

            // 图例
            double lx = availW + 6;
            double rowH = Math.Min(20, (h - 8) / Math.Max(1, Data.Count));
            for (int i = 0; i < Data.Count; i++)
            {
                double y = 8 + rowH * i;
                if (y + rowH > h) break;
                var col = ColorAt(i);
                var brush = new SolidColorBrush(col);
                brush.Freeze();
                dc.DrawRoundedRectangle(brush, null, new Rect(lx, y + rowH / 2 - 5, 10, 10), 3, 3);

                var name = Txt(Data[i].label, 11.5, text);
                double pct = Data[i].value * 100 / total;
                var pctT = Txt(pct.ToString("0.0") + "%", 11, sub);

                double nameMax = legendW - 34 - pctT.Width;
                string nm = Data[i].label;
                while (name.Width > nameMax && nm.Length > 1)
                {
                    nm = nm.Substring(0, nm.Length - 1);
                    name = Txt(nm + "…", 11.5, text);
                }
                dc.DrawText(name, new Point(lx + 16, y + rowH / 2 - name.Height / 2));
                dc.DrawText(pctT, new Point(w - pctT.Width - 2, y + rowH / 2 - pctT.Height / 2));
            }
        }

        private static void DrawArc(DrawingContext dc, Brush brush, double cx, double cy,
            double outer, double inner, double startAngle, double sweep)
        {
            if (sweep <= 0.01) return;
            sweep = Math.Min(sweep, 359.99);

            Point P(double r, double a)
            {
                double rad = a * Math.PI / 180.0;
                return new Point(cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));
            }

            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(P(outer, startAngle), true, true);
                g.ArcTo(P(outer, startAngle + sweep), new Size(outer, outer), 0,
                    sweep > 180, SweepDirection.Clockwise, true, false);
                g.LineTo(P(inner, startAngle + sweep), true, false);
                g.ArcTo(P(inner, startAngle), new Size(inner, inner), 0,
                    sweep > 180, SweepDirection.Counterclockwise, true, false);
            }
            geo.Freeze();
            dc.DrawGeometry(brush, null, geo);
        }
    }

    /// <summary>折线图（结余走势）</summary>
    public class LineChart : ChartBase
    {
        public List<(string label, double value)> Data = new List<(string, double)>();
        public string ColorKey = "AccentBrush";
        public bool FillArea = true;

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 40 || h < 40 || Data.Count < 2) return;

            var border = C("BorderBrush2");
            var sub = C("SubBrush");
            var text = C("TextBrush");
            var pen = new Pen(border, 1);
            pen.Freeze();

            double padL = 52, padR = 12, padT = 14, padB = 28;
            double plotW = w - padL - padR, plotH = h - padT - padB;
            if (plotW < 20 || plotH < 20) return;

            double min = Math.Min(0, Data.Min(d => d.value));
            double max = Math.Max(1, Data.Max(d => d.value));
            if (max - min < 1) max = min + 1;

            int lines = 4;
            for (int i = 0; i <= lines; i++)
            {
                double y = padT + plotH - plotH * i / lines;
                dc.DrawLine(pen, new Point(padL, y), new Point(w - padR, y));
                double val = min + (max - min) * i / lines;
                var lbl = Txt(val.ToString("N0"), 10, sub);
                dc.DrawText(lbl, new Point(padL - 6 - lbl.Width, y - lbl.Height / 2));
            }

            int n = Data.Count;
            double X(int i) => padL + plotW * i / (n - 1);
            double Y(double v) => padT + plotH - plotH * (v - min) / (max - min);

            var col = Col(ColorKey);
            var lineBrush = new SolidColorBrush(col);
            lineBrush.Freeze();
            var linePen = new Pen(lineBrush, 2.2) { LineJoin = PenLineJoin.Round };
            linePen.Freeze();

            if (FillArea)
            {
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(new Point(X(0), padT + plotH), true, true);
                    for (int i = 0; i < n; i++) g.LineTo(new Point(X(i), Y(Data[i].value)), true, false);
                    g.LineTo(new Point(X(n - 1), padT + plotH), true, false);
                }
                geo.Freeze();
                var fill = new LinearGradientBrush(
                    Color.FromArgb(70, col.R, col.G, col.B),
                    Color.FromArgb(6, col.R, col.G, col.B), 90);
                fill.Freeze();
                dc.DrawGeometry(fill, null, geo);
            }

            for (int i = 1; i < n; i++)
                dc.DrawLine(linePen, new Point(X(i - 1), Y(Data[i - 1].value)), new Point(X(i), Y(Data[i].value)));

            for (int i = 0; i < n; i++)
            {
                var dotBrush = new SolidColorBrush(col);
                dotBrush.Freeze();
                dc.DrawEllipse(dotBrush, null, new Point(X(i), Y(Data[i].value)), 3.2, 3.2);
            }

            for (int i = 0; i < n; i++)
            {
                if (n > 8 && i % 2 == 1) continue;
                var lt = Txt(Data[i].label, 10, sub);
                dc.DrawText(lt, new Point(X(i) - lt.Width / 2, padT + plotH + 5));
            }

            var last = Txt(Data[n - 1].value.ToString("N0"), 11, text, true);
            dc.DrawText(last, new Point(Math.Min(w - padR - last.Width, X(n - 1) + 6), Y(Data[n - 1].value) - last.Height - 4));
        }
    }
}