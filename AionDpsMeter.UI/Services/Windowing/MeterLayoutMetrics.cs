using AionDpsMeter.Services.Services.Settings;

namespace AionDpsMeter.UI.Services.Windowing
{
    /// <summary>
    /// How tall each meter layout's header and rows are, so switching layouts can resize the window to
    /// show about as many bars as before (and widen or narrow it to suit the layout).
    /// Measured from the page while a layout is on screen and saved; estimates until then.
    /// </summary>
    public sealed class MeterLayoutMetrics
    {
        public const int Standard = 1, Compact = 2, UltraCompact = 3, Horizontal = 4;

        /// <param name="Header">Space above the player list.</param>
        /// <param name="Pitch">Distance from one row to the next.</param>
        /// <param name="Bottom">Space below the list.</param>
        /// <param name="RowHeight">Height of one row (one slot in the horizontal layout).</param>
        public sealed record Metrics(double Header, double Pitch, double Bottom, double RowHeight);

        // Below the list there's normally only padding; more means the list doesn't fill the window.
        private const double MaxBottom = 12;

        private readonly IAppSettingsService settings;
        private readonly object gate = new();
        private readonly Dictionary<int, Metrics> measured = new();

        /// <summary>A layout was measured on screen (raised on the caller's thread).</summary>
        public event Action<int, Metrics>? Measured;

        public MeterLayoutMetrics(IAppSettingsService settings)
        {
            this.settings = settings;
            foreach (var style in new[] { Standard, Compact, UltraCompact, Horizontal })
            {
                if (settings.GetMeterLayoutMetrics(style) is [var h, var p, var b, var r] && p > 0 && r > 0)
                    measured[style] = new Metrics(h, p, b, r);
            }
        }

        public Metrics Get(int style)
        {
            lock (gate)
            {
                if (measured.TryGetValue(style, out var m)) return m;
            }
            return Estimate(style);
        }

        /// <summary>From the page. Rows only count with at least two on screen (to get the pitch).</summary>
        public void Report(int style, double header, double bottom, double pitch, double rowHeight)
        {
            if (header < 0) return;
            bottom = Math.Min(bottom, MaxBottom);

            Metrics next;
            bool save;
            lock (gate)
            {
                var previous = measured.TryGetValue(style, out var m) ? m : Estimate(style);
                // No players on screen: only the header was measured; rows keep what's known.
                if (rowHeight <= 0) (rowHeight, pitch) = (previous.RowHeight, previous.Pitch);
                // The horizontal layout's slots sit side by side, so its "pitch" is just the slot height.
                if (style == Horizontal) pitch = rowHeight;
                if (pitch <= 0) pitch = previous.Pitch;
                next = new Metrics(header, pitch, bottom, rowHeight);
                save = !measured.ContainsKey(style) || Differs(previous, next);
                if (!save) return;
                measured[style] = next;
            }
            settings.SetMeterLayoutMetrics(style, [next.Header, next.Pitch, next.Bottom, next.RowHeight]);
            Measured?.Invoke(style, next);
        }

        /// <summary>How many bars fit in a window this tall (fractional).</summary>
        public double BarsFitting(int style, double height)
        {
            var m = Get(style);
            return Math.Max(1, (height - m.Header - m.Bottom) / m.Pitch);
        }

        /// <summary>Window height that shows this many bars (the horizontal layout: one row of slots).</summary>
        public double HeightFor(int style, double bars)
        {
            var m = Get(style);
            return style == Horizontal
                ? m.Header + m.RowHeight + m.Bottom + 2
                : m.Header + bars * m.Pitch + m.Bottom;
        }

        /// <summary>
        /// Relative width of each layout: Ultra Compact is a bit narrower than Standard and Compact,
        /// Horizontal is a wide strip.
        /// </summary>
        public static double WidthFactor(int style) => style switch
        {
            UltraCompact => 0.8,
            Horizontal => 1.6,
            _ => 1.0,
        };

        private static bool Differs(Metrics a, Metrics b)
            => Math.Abs(a.Header - b.Header) > 1 || Math.Abs(a.Pitch - b.Pitch) > 0.5
               || Math.Abs(a.Bottom - b.Bottom) > 1 || Math.Abs(a.RowHeight - b.RowHeight) > 0.5;

        // Rough sizes from the stylesheets, until the layout has been on screen with players in it.
        private Metrics Estimate(int style)
        {
            double rowScale = settings.PlayerRowScale > 0 ? settings.PlayerRowScale : 1.0;
            return style switch
            {
                Standard => new Metrics(80, 46 * rowScale, 0, 41 * rowScale),
                UltraCompact => new Metrics(21, 17, 0, 16),
                Horizontal => new Metrics(21, 34, 0, 34),
                _ => new Metrics(69, 32, 0, 28),
            };
        }
    }
}
