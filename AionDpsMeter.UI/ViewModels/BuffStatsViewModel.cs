using AionDpsMeter.Services.Models;

namespace AionDpsMeter.UI.ViewModels
{
    public sealed class BuffStatsViewModel : ViewModelBase
    {
        private readonly BuffStats _stats;
        private readonly double? _uptimePercentage;

        /// <param name="fightDuration">Same window the buff time was measured over (fight start to last hit).</param>
        public BuffStatsViewModel(BuffStats stats, TimeSpan fightDuration)
        {
            _stats = stats;
            _uptimePercentage = fightDuration > TimeSpan.Zero
                ? Math.Min(100, stats.EffectiveDurationSeconds / fightDuration.TotalSeconds * 100)
                : null;
        }

        public int    BuffId                => _stats.BuffId;
        public string BuffName              => _stats.BuffName;
        public string? BuffIcon             => _stats.BuffIcon;
        public bool   HasBuffIcon           => !string.IsNullOrEmpty(_stats.BuffIcon);
        public string Description           => _stats.Description;
        public int    ApplicationCount      => _stats.ApplicationCount;
        public string ApplicationCountText  => $"x{_stats.ApplicationCount}";
        public string DurationFormatted     => _stats.DurationFormatted;
        public double EffectiveDurationSec  => _stats.EffectiveDurationSeconds;
        // "01:23 (64% of the fight)", or just the time for fights saved before buff % existed.
        public string UptimeWithPercent     => _uptimePercentage is { } p
            ? $"{_stats.DurationFormatted} ({p:0}% of the fight)"
            : _stats.DurationFormatted;
        public string CompactUptimeText     => _uptimePercentage is { } p
            ? $"{_stats.DurationFormatted} {p:0}%"
            : _stats.DurationFormatted;


        public string TooltipText
        {
            get
            {
                var desc = string.IsNullOrWhiteSpace(_stats.Description)
                    ? _stats.BuffName
                    : _stats.Description;
                return $"{desc}\nUptime: {UptimeWithPercent}  (×{_stats.ApplicationCount})";
            }
        }
    }
}
