namespace AionDpsMeter.Services.Services.Session
{
    public sealed class HistorySessionListItem
    {
        // A fight counts as a clear when the boss's HP hit 0 at the last hit, or the party dealt (nearly)
        // its whole HP (fights saved before the end HP was kept; a few hits can be lost).
        public const double KillDamageShare = 0.97;

        public Guid SessionId { get; init; }
        public int TargetId { get; init; }
        public string TargetName { get; init; } = string.Empty;
        public long TargetHpTotal { get; init; }
        /// <summary>Boss HP at the last hit; null when unknown (older fights, or no HP update seen).</summary>
        public long? TargetHpRemaining { get; init; }
        public DateTime SessionStart { get; init; }
        public DateTime SessionEnd { get; init; }
        public SessionState State { get; init; }
        public long TotalDamage { get; init; }
        public int PlayerCount { get; init; }

        public TimeSpan Duration => SessionEnd > SessionStart ? SessionEnd - SessionStart : TimeSpan.Zero;

        public bool IsClear => TargetHpTotal > 0
            && (TargetHpRemaining == 0 || TotalDamage >= TargetHpTotal * KillDamageShare);

        /// <summary>Boss HP left at the end, 0-100; null when the boss's HP is unknown.</summary>
        public double? RemainingHpPercent
        {
            get
            {
                if (TargetHpTotal <= 0) return null;
                if (IsClear) return 0;
                // Damage we saw is a lower bound on what the boss lost (hits out of range are missed), and the
                // HP at the last hit can lag the final hit, so both over-estimate what's left: take the smaller.
                double byDamage = Math.Max(0, 1 - (double)TotalDamage / TargetHpTotal) * 100;
                if (TargetHpRemaining is not { } hp || hp < 0) return byDamage;
                return Math.Min(byDamage, Math.Min(100, (double)hp / TargetHpTotal * 100));
            }
        }
    }
}
