using AionDpsMeter.Services.Models;

namespace AionDpsMeter.UI.ViewModels
{
    /// <summary>Uptime text for the player breakdown and history (not shown on the main meter).</summary>
    internal static class UptimeFormatter
    {
        public static string Display(PlayerStats stats)
            => stats.UptimePercentage is { } uptime ? DamageFormatter.FormatRate(uptime) : "—";

        public static string Tooltip(PlayerStats stats, double? bestForClass)
        {
            if (stats.UptimePercentage is null) return "No uptime data (fight saved before uptime tracking)";

            var lines = new List<string>
            {
                $"Active {DamageFormatter.FormatDuration(TimeSpan.FromSeconds(stats.ActiveSeconds))} of " +
                $"{DamageFormatter.FormatDuration(TimeSpan.FromSeconds(stats.ActiveSeconds + stats.IdleSeconds))} " +
                $"(idle {FormatIdle(stats.IdleSeconds)})",
                "Downtime isn't counted: stretches when nobody, or only one player of a group, was hitting the boss.",
            };
            if (bestForClass is { } best)
            {
                var className = string.IsNullOrEmpty(stats.ClassName) ? "this class" : stats.ClassName;
                lines.Add($"Best {className} on this boss: {DamageFormatter.FormatRate(best)}");
            }
            return string.Join(Environment.NewLine, lines);
        }

        public static string FormatIdle(double seconds)
            => seconds < 60 ? $"{seconds:0}s" : DamageFormatter.FormatDuration(TimeSpan.FromSeconds(seconds));
    }
}
