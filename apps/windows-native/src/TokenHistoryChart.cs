using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace UsageApp.Native
{
    internal sealed class TokenHistoryChart : Control
    {
        private readonly List<TokenUsageDailyBucket> buckets =
            new List<TokenUsageDailyBucket>();

        public TokenHistoryChart()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.SupportsTransparentBackColor
                    | ControlStyles.UserPaint,
                true);
            DoubleBuffered = true;
            BackColor = Color.Transparent;
            ForeColor = NativePalette.Primary;
            TabStop = false;
            AccessibleRole = AccessibleRole.Graphic;
            AccessibleName = "Tokens by day chart";
            AccessibleDescription = "No daily token history is available.";
        }

        public void SetData(IList<TokenUsageDailyBucket> source)
        {
            buckets.Clear();
            Dictionary<string, long> byDate =
                new Dictionary<string, long>(StringComparer.Ordinal);
            if (source != null)
            {
                foreach (TokenUsageDailyBucket bucket in source)
                {
                    if (bucket == null
                        || string.IsNullOrEmpty(bucket.StartDate)
                        || bucket.Tokens < 0)
                    {
                        continue;
                    }
                    byDate[bucket.StartDate] = bucket.Tokens;
                }
            }
            foreach (KeyValuePair<string, long> entry in byDate)
            {
                buckets.Add(new TokenUsageDailyBucket
                {
                    StartDate = entry.Key,
                    Tokens = entry.Value
                });
            }
            buckets.Sort(delegate(
                TokenUsageDailyBucket left,
                TokenUsageDailyBucket right)
            {
                return string.CompareOrdinal(left.StartDate, right.StartDate);
            });
            AccessibleDescription = DescribeData(buckets);
            if (IsHandleCreated)
            {
                AccessibilityNotifyClients(
                    AccessibleEvents.DescriptionChange,
                    -1);
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            base.OnPaint(eventArgs);
            Graphics graphics = eventArgs.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint =
                System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int left = NativeDrawing.Dpi(this, 58);
            int right = NativeDrawing.Dpi(this, 18);
            int top = NativeDrawing.Dpi(this, 18);
            int bottom = NativeDrawing.Dpi(this, 40);
            Rectangle plot = new Rectangle(
                left,
                top,
                Math.Max(1, Width - left - right),
                Math.Max(1, Height - top - bottom));

            if (buckets.Count == 0)
            {
                TextRenderer.DrawText(
                    graphics,
                    "No daily token history was returned for this range.",
                    Font,
                    ClientRectangle,
                    NativePalette.Muted,
                    TextFormatFlags.HorizontalCenter
                        | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.WordBreak
                        | TextFormatFlags.NoPadding);
                return;
            }

            List<ChartBucket> bars = Aggregate(buckets, plot.Width);
            long maximum = 1;
            foreach (ChartBucket bar in bars)
            {
                maximum = Math.Max(maximum, bar.Tokens);
            }

            DrawGrid(graphics, plot, maximum);
            DrawBars(graphics, plot, bars, maximum);
            DrawDateLabels(graphics, plot, bars);
        }

        private List<ChartBucket> Aggregate(
            IList<TokenUsageDailyBucket> source,
            int plotWidth)
        {
            int minimumBarWidth = NativeDrawing.Dpi(this, 5);
            int maximumBars = Math.Max(1, plotWidth / Math.Max(1, minimumBarWidth));
            int groupSize = Math.Max(
                1,
                (int)Math.Ceiling(source.Count / (double)maximumBars));
            List<ChartBucket> result = new List<ChartBucket>();
            for (int index = 0; index < source.Count; index += groupSize)
            {
                int end = Math.Min(source.Count, index + groupSize);
                long total = 0;
                for (int item = index; item < end; item++)
                {
                    try
                    {
                        total = checked(total + source[item].Tokens);
                    }
                    catch (OverflowException)
                    {
                        total = long.MaxValue;
                    }
                }
                result.Add(new ChartBucket(
                    source[index].StartDate,
                    source[end - 1].StartDate,
                    total));
            }
            return result;
        }

        private void DrawGrid(Graphics graphics, Rectangle plot, long maximum)
        {
            using (Pen line = new Pen(NativePalette.Border, 1.0f))
            using (Font labelFont = ScaledFont(FontStyle.Regular))
            {
                line.DashStyle = DashStyle.Dot;
                for (int step = 0; step <= 3; step++)
                {
                    int y = plot.Bottom - (plot.Height * step / 3);
                    graphics.DrawLine(line, plot.Left, y, plot.Right, y);
                    long value = step == 3
                        ? maximum
                        : (maximum / 3) * step
                            + ((maximum % 3) * step) / 3;
                    string text = CompactNumber(value);
                    Rectangle labelBounds = new Rectangle(
                        0,
                        y - NativeDrawing.Dpi(this, 9),
                        plot.Left - NativeDrawing.Dpi(this, 8),
                        NativeDrawing.Dpi(this, 18));
                    TextRenderer.DrawText(
                        graphics,
                        text,
                        labelFont,
                        labelBounds,
                        NativePalette.Muted,
                        TextFormatFlags.Right
                            | TextFormatFlags.VerticalCenter
                            | TextFormatFlags.NoPadding);
                }
            }
        }

        private void DrawBars(
            Graphics graphics,
            Rectangle plot,
            IList<ChartBucket> bars,
            long maximum)
        {
            float slot = plot.Width / (float)Math.Max(1, bars.Count);
            float inset = Math.Max(1.0f, slot * 0.16f);
            for (int index = 0; index < bars.Count; index++)
            {
                double ratio = maximum <= 0
                    ? 0
                    : Math.Max(0, Math.Min(1, bars[index].Tokens / (double)maximum));
                if (ratio <= 0)
                {
                    continue;
                }
                float height = Math.Max(
                    NativeDrawing.Dpi(this, 2),
                    (float)(plot.Height * ratio));
                RectangleF bar = new RectangleF(
                    plot.Left + index * slot + inset,
                    plot.Bottom - height,
                    Math.Max(1.0f, slot - inset * 2),
                    height);
                using (Brush fill = CreateBarBrush(bar))
                {
                    graphics.FillRectangle(fill, bar);
                }
            }
        }

        private Brush CreateBarBrush(RectangleF bar)
        {
            if (SystemInformation.HighContrast || bar.Height < 2)
            {
                return new SolidBrush(NativePalette.Accent);
            }
            return new LinearGradientBrush(
                bar,
                NativePalette.Accent,
                NativePalette.AccentPurple,
                LinearGradientMode.Vertical);
        }

        private void DrawDateLabels(
            Graphics graphics,
            Rectangle plot,
            IList<ChartBucket> bars)
        {
            using (Font labelFont = ScaledFont(FontStyle.Regular))
            {
                string first = ShortDate(bars[0].StartDate);
                string last = ShortDate(bars[bars.Count - 1].EndDate);
                string middle = ShortDate(
                    bars[bars.Count / 2].StartDate);
                int y = plot.Bottom + NativeDrawing.Dpi(this, 10);
                int labelWidth = Math.Max(
                    NativeDrawing.Dpi(this, 74),
                    plot.Width / 4);
                DrawDateLabel(
                    graphics,
                    first,
                    labelFont,
                    new Rectangle(plot.Left, y, labelWidth, NativeDrawing.Dpi(this, 20)),
                    TextFormatFlags.Left);
                if (bars.Count > 2)
                {
                    DrawDateLabel(
                        graphics,
                        middle,
                        labelFont,
                        new Rectangle(
                            plot.Left + (plot.Width - labelWidth) / 2,
                            y,
                            labelWidth,
                            NativeDrawing.Dpi(this, 20)),
                        TextFormatFlags.HorizontalCenter);
                }
                DrawDateLabel(
                    graphics,
                    last,
                    labelFont,
                    new Rectangle(
                        plot.Right - labelWidth,
                        y,
                        labelWidth,
                        NativeDrawing.Dpi(this, 20)),
                    TextFormatFlags.Right);
            }
        }

        private static void DrawDateLabel(
            Graphics graphics,
            string text,
            Font font,
            Rectangle bounds,
            TextFormatFlags alignment)
        {
            TextRenderer.DrawText(
                graphics,
                text,
                font,
                bounds,
                NativePalette.Muted,
                alignment
                    | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.NoPadding);
        }

        private Font ScaledFont(FontStyle style)
        {
            Font source = Font ?? SystemFonts.MessageBoxFont;
            return new Font(
                source.FontFamily,
                Math.Max(7.0f, source.SizeInPoints * 0.94f),
                style,
                GraphicsUnit.Point);
        }

        private static string CompactNumber(long value)
        {
            if (value >= 1000000000)
            {
                return (value / 1000000000.0).ToString("0.#", CultureInfo.CurrentCulture)
                    + "B";
            }
            if (value >= 1000000)
            {
                return (value / 1000000.0).ToString("0.#", CultureInfo.CurrentCulture)
                    + "M";
            }
            if (value >= 1000)
            {
                return (value / 1000.0).ToString("0.#", CultureInfo.CurrentCulture)
                    + "K";
            }
            return value.ToString("N0", CultureInfo.CurrentCulture);
        }

        private static string ShortDate(string value)
        {
            DateTime parsed;
            return DateTime.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out parsed)
                    ? parsed.ToString("MMM d", CultureInfo.CurrentCulture)
                    : value;
        }

        private static string DescribeData(IList<TokenUsageDailyBucket> source)
        {
            if (source == null || source.Count == 0)
            {
                return "No daily token history is available for the selected range.";
            }
            long total = 0;
            TokenUsageDailyBucket peak = null;
            foreach (TokenUsageDailyBucket bucket in source)
            {
                try
                {
                    total = checked(total + bucket.Tokens);
                }
                catch (OverflowException)
                {
                    total = long.MaxValue;
                }
                if (peak == null || bucket.Tokens > peak.Tokens)
                {
                    peak = bucket;
                }
            }
            return string.Format(
                CultureInfo.CurrentCulture,
                "{0} daily values from {1} through {2}, totaling {3} tokens. Peak {4} tokens on {5}.",
                source.Count,
                source[0].StartDate,
                source[source.Count - 1].StartDate,
                total.ToString("N0", CultureInfo.CurrentCulture),
                peak == null
                    ? "0"
                    : peak.Tokens.ToString("N0", CultureInfo.CurrentCulture),
                peak == null ? "an unavailable date" : peak.StartDate);
        }

        private sealed class ChartBucket
        {
            public ChartBucket(string startDate, string endDate, long tokens)
            {
                StartDate = startDate;
                EndDate = endDate;
                Tokens = tokens;
            }

            public string StartDate { get; private set; }
            public string EndDate { get; private set; }
            public long Tokens { get; private set; }
        }
    }
}
