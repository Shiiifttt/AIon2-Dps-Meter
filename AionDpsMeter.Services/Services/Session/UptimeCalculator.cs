using AionDpsMeter.Core.Models;

namespace AionDpsMeter.Services.Services.Session
{
    /// <summary>A stretch of time, start inclusive, end exclusive.</summary>
    public readonly record struct TimeInterval(DateTime Start, DateTime End)
    {
        public TimeSpan Length => End > Start ? End - Start : TimeSpan.Zero;
    }

    /// <summary>
    /// Damage uptime: each direct hit counts its player as active for a short window after it.
    /// While attacking, a Gladiator lands a hit at least every second (701 of 717 gaps under 0.5 s),
    /// and every other class hits at least as often, so longer gaps are moving, dodging, being dead or idling.
    ///
    /// Fight time only counts while enough of the party is hitting (<see cref="Quorum"/>). In a group, one
    /// player (or a summon) still landing hits while everyone else is forced off the boss by a mechanic
    /// doesn't turn that stretch into idle time for the rest. Uptime = player active time inside fight time /
    /// fight time. DoT ticks and summon hits never count as activity.
    /// </summary>
    public static class UptimeCalculator
    {
        public static readonly TimeSpan ActiveWindow = TimeSpan.FromSeconds(1.5);

        // Players with fewer direct hits than this (stray summons the meter never linked to an owner,
        // someone passing by) don't count towards the party size.
        private const int MinMemberHits = 20;

        public sealed class Result
        {
            public static readonly Result Empty = new([], new Dictionary<long, List<TimeInterval>>());

            public Result(List<TimeInterval> fightTime, Dictionary<long, List<TimeInterval>> activeByPlayer)
            {
                FightTime = fightTime;
                ActiveByPlayer = activeByPlayer;
                FightSeconds = Total(fightTime).TotalSeconds;
            }

            /// <summary>When enough of the party was hitting. Everything else is downtime nobody is blamed for.</summary>
            public List<TimeInterval> FightTime { get; }
            public double FightSeconds { get; }

            /// <summary>Each player's active time, already limited to <see cref="FightTime"/>.</summary>
            public Dictionary<long, List<TimeInterval>> ActiveByPlayer { get; }
        }

        /// <summary>Players needed to call a moment fight time, for this many players present.</summary>
        public static int Quorum(int present) => present <= 2 ? 1 : Math.Max(2, (present + 3) / 4);

        public static List<DateTime> ActiveHitTimes(IEnumerable<PlayerDamage> hits)
        {
            var times = hits.Where(h => !h.IsDot && h.SourceSummon is null).Select(h => h.DateTime).ToList();
            times.Sort();
            return times;
        }

        /// <param name="hitTimesByPlayer">Sorted direct-hit times per player (see <see cref="ActiveHitTimes"/>).</param>
        /// <param name="end">Fight end; no activity is counted past it.</param>
        public static Result Compute(IReadOnlyDictionary<long, List<DateTime>> hitTimesByPlayer, DateTime end)
        {
            var activeByPlayer = hitTimesByPlayer.ToDictionary(p => p.Key, p => ActiveIntervals(p.Value, end));

            // A member is "present" from their first hit until their last activity ends.
            var members = hitTimesByPlayer.Where(p => p.Value.Count >= MinMemberHits).Select(p => p.Key).ToList();
            if (members.Count == 0)
                members = hitTimesByPlayer.Where(p => p.Value.Count > 0).Select(p => p.Key).ToList();

            var events = new List<(DateTime At, int Active, int Present)>();
            foreach (var id in members)
            {
                var active = activeByPlayer[id];
                if (active.Count == 0) continue;
                events.Add((active[0].Start, 0, +1));
                events.Add((active[^1].End, 0, -1));
                foreach (var i in active)
                {
                    events.Add((i.Start, +1, 0));
                    events.Add((i.End, -1, 0));
                }
            }
            events.Sort((a, b) => a.At.CompareTo(b.At));

            var fightTime = new List<TimeInterval>();
            int activeCount = 0, presentCount = 0;
            for (int e = 0; e < events.Count; e++)
            {
                activeCount += events[e].Active;
                presentCount += events[e].Present;
                if (e + 1 < events.Count && events[e + 1].At == events[e].At) continue;
                if (e + 1 >= events.Count) break;

                var from = events[e].At;
                var to = events[e + 1].At;
                if (activeCount > 0 && activeCount >= Quorum(presentCount))
                    Append(fightTime, from, to);
            }

            var limited = activeByPlayer.ToDictionary(p => p.Key, p => Intersect(p.Value, fightTime));
            return new Result(fightTime, limited);
        }

        /// <summary>[t, t + window] for each hit, merged, never past <paramref name="end"/>.</summary>
        public static List<TimeInterval> ActiveIntervals(IReadOnlyList<DateTime> sortedTimes, DateTime end)
        {
            var result = new List<TimeInterval>();
            foreach (var t in sortedTimes)
            {
                var stop = t + ActiveWindow;
                if (stop > end) stop = end;
                if (stop <= t) continue;
                Append(result, t, stop);
            }
            return result;
        }

        /// <summary>Overlap of two sorted, non-overlapping interval lists.</summary>
        public static List<TimeInterval> Intersect(IReadOnlyList<TimeInterval> a, IReadOnlyList<TimeInterval> b)
        {
            var result = new List<TimeInterval>();
            int i = 0, j = 0;
            while (i < a.Count && j < b.Count)
            {
                var start = a[i].Start > b[j].Start ? a[i].Start : b[j].Start;
                var end = a[i].End < b[j].End ? a[i].End : b[j].End;
                if (start < end) result.Add(new TimeInterval(start, end));
                if (a[i].End < b[j].End) i++; else j++;
            }
            return result;
        }

        public static TimeSpan Total(IEnumerable<TimeInterval> intervals)
            => TimeSpan.FromTicks(intervals.Sum(i => i.Length.Ticks));

        // Adds [from, to) to a list sorted by start, merging with the last interval when they touch.
        private static void Append(List<TimeInterval> list, DateTime from, DateTime to)
        {
            if (list.Count > 0 && from <= list[^1].End)
            {
                if (to > list[^1].End) list[^1] = list[^1] with { End = to };
                return;
            }
            list.Add(new TimeInterval(from, to));
        }
    }
}
