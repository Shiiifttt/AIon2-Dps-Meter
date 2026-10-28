using AionDpsMeter.Core.Models;
using AionDpsMeter.Services.Models;

namespace AionDpsMeter.Services.Services.Session
{

    public static class BuffStatisticsCalculator
    {
        /// <param name="activeIntervals">When the player was attacking; buff uptime % is measured against this time.</param>
        public static IReadOnlyCollection<BuffStats> ComputeBuffStats(
            IReadOnlyList<BuffEvent> buffEvents,
            DateTime windowStart,
            DateTime windowEnd,
            IReadOnlyList<TimeInterval>? activeIntervals = null)
        {
            if (buffEvents.Count == 0)
                return [];

            var activeSeconds = activeIntervals is null ? 0 : UptimeCalculator.Total(activeIntervals).TotalSeconds;

            return buffEvents
                .GroupBy(e => e.BuffId)
                .Select(g =>
                {
                    var intervals = g
                        .Select(e =>
                        {
                            var start = e.AppliedAt < windowStart ? windowStart : e.AppliedAt;
                            var end = e.AppliedAt.AddMilliseconds(e.DurationMs);
                            if (end > windowEnd) end = windowEnd;
                            return (Start: start, End: end);
                        })
                        .Where(i => i.Start < i.End)
                        .OrderBy(i => i.Start)
                        .ToList();

                    var merged = MergeIntervals(intervals);
                    double mergedSeconds = UptimeCalculator.Total(merged).TotalSeconds;

                    double? activeUptime = activeSeconds > 0
                        ? Math.Min(100, UptimeCalculator.Total(UptimeCalculator.Intersect(merged, activeIntervals!)).TotalSeconds / activeSeconds * 100)
                        : null;

                    var first = g.First();
                    return new BuffStats
                    {
                        BuffId = g.Key,
                        BuffName = first.BuffName,
                        BuffIcon = first.BuffIcon,
                        Description = first.Description,
                        ApplicationCount = g.Count(),
                        EffectiveDurationSeconds = mergedSeconds,
                        ActiveUptimePercentage = activeUptime,
                    };
                })
                .OrderByDescending(b => b.ApplicationCount)
                .ToList();
        }


        private static List<TimeInterval> MergeIntervals(List<(DateTime Start, DateTime End)> sorted)
        {
            var result = new List<TimeInterval>();
            if (sorted.Count == 0) return result;

            var currentStart = sorted[0].Start;
            var currentEnd = sorted[0].End;

            for (int i = 1; i < sorted.Count; i++)
            {
                var (start, end) = sorted[i];
                if (start <= currentEnd)
                {

                    if (end > currentEnd)
                        currentEnd = end;
                }
                else
                {
                    result.Add(new TimeInterval(currentStart, currentEnd));
                    currentStart = start;
                    currentEnd = end;
                }
            }

            result.Add(new TimeInterval(currentStart, currentEnd));
            return result;
        }
    }
}
