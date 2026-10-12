namespace AionDpsMeter.Services.Services.Session
{
    /// <summary>
    /// Estimates time to kill a target from how fast its HP has dropped over a recent window.
    /// HP-based rather than damage-based, so it also counts unidentified players, DoTs and
    /// target healing. Falls back to party DPS until there is enough HP history.
    /// </summary>
    public sealed class TimeToKillEstimator
    {
        private static readonly TimeSpan Window = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan MinSampleSpan = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan MaxEstimate = TimeSpan.FromHours(1);

        private readonly Queue<(DateTime At, long Hp)> samples = new();
        private int currentTargetId = -1;
        private long lastHp = -1;

        public TimeSpan? Estimate(int targetId, long hpCurrent, long hpTotal, double partyDps, DateTime now)
        {
            if (targetId != currentTargetId) Reset(targetId);
            if (hpTotal <= 0 || hpCurrent <= 0) return null;

            // HP going back up (heal, reset, evade) makes the old samples meaningless.
            if (lastHp >= 0 && hpCurrent > lastHp) samples.Clear();

            if (hpCurrent != lastHp)
            {
                samples.Enqueue((now, hpCurrent));
                lastHp = hpCurrent;
            }

            while (samples.Count > 1 && now - samples.Peek().At > Window)
                samples.Dequeue();

            double hpPerSecond = 0;
            var (oldestAt, oldestHp) = samples.Peek();
            var span = now - oldestAt;
            if (span >= MinSampleSpan && oldestHp > hpCurrent)
                hpPerSecond = (oldestHp - hpCurrent) / span.TotalSeconds;
            else if (double.IsFinite(partyDps) && partyDps > 0)
                hpPerSecond = partyDps;

            if (hpPerSecond <= 0) return null;

            var seconds = hpCurrent / hpPerSecond;
            if (seconds > MaxEstimate.TotalSeconds) return null;

            return TimeSpan.FromSeconds(seconds);
        }

        public void Reset(int targetId = -1)
        {
            samples.Clear();
            currentTargetId = targetId;
            lastHp = -1;
        }
    }
}
