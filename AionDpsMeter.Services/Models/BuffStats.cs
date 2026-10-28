namespace AionDpsMeter.Services.Models
{
   
    public sealed class BuffStats
    {
        public int BuffId { get; init; }
        public string BuffName { get; init; } = string.Empty;
        public string? BuffIcon { get; init; }
        public string Description { get; init; } = string.Empty;
        public int ApplicationCount { get; init; }

        public double EffectiveDurationSeconds { get; init; }

        // Share of the player's own active (attacking) time this buff was up. Null for fights saved before it existed.
        public double? ActiveUptimePercentage { get; init; }

        public string DurationFormatted
        {
            get
            {
                var ts = TimeSpan.FromSeconds(EffectiveDurationSeconds);
                if (ts.TotalHours >= 1)
                    return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
                return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
            }
        }
    }
}
