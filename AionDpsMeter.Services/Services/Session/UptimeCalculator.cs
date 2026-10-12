using AionDpsMeter.Core.Models;

namespace AionDpsMeter.Services.Services.Session
{
    /// <summary>
    /// Damage uptime: each direct hit counts its player as active for a short window after it.
    /// Stretches where nobody in the party was hitting (untargetable phases, cutscenes) are left
    /// out, so uptime = player active time / party active time and nobody is penalised for downtime.
    /// While attacking, a Gladiator lands a hit at least every second (701 of 717 gaps under 0.5 s),
    /// so longer gaps are moving, dodging, being dead or idling.
    /// </summary>
    public static class UptimeCalculator
    {
        public static readonly TimeSpan ActiveWindow = TimeSpan.FromSeconds(1.5);

        public static List<DateTime> ActiveHitTimes(IEnumerable<PlayerDamage> hits)
        {
            var times = hits.Where(h => !h.IsDot && h.SourceSummon is null).Select(h => h.DateTime).ToList();
            times.Sort();
            return times;
        }

        /// <summary>Time covered by [t, t + window] for the given sorted hit times, never past <paramref name="end"/>.</summary>
        public static TimeSpan ActiveTime(IReadOnlyList<DateTime> sortedTimes, DateTime end)
        {
            double seconds = 0;
            double window = ActiveWindow.TotalSeconds;
            for (int i = 0; i < sortedTimes.Count; i++)
            {
                var next = i + 1 < sortedTimes.Count ? sortedTimes[i + 1] : end;
                seconds += Math.Clamp((next - sortedTimes[i]).TotalSeconds, 0, window);
            }
            return TimeSpan.FromSeconds(seconds);
        }

        public static List<DateTime> Merge(IEnumerable<List<DateTime>> perPlayer)
        {
            var all = perPlayer.SelectMany(t => t).ToList();
            all.Sort();
            return all;
        }
    }
}
