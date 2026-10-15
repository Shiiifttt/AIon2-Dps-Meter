using AionDpsMeter.Core.GameData.Services;

namespace AionDpsMeter.Services.Services.BossPatterns
{
    public enum BossPatternState { Filling, Ready, Casting }

    /// <summary>
    /// One predicted boss skill. Filling: <see cref="Progress"/> runs 0..1 until the skill is expected to
    /// be off cooldown. Ready: it is, and the boss hasn't used it yet (<see cref="Overdue"/> counts up).
    /// Casting: the boss is casting it now and <see cref="Remaining"/> is the cast bar.
    /// </summary>
    public sealed record BossPatternBar(int SkillCode, string Name, BossPatternState State, double Progress, TimeSpan Remaining, TimeSpan Overdue);

    /// <summary>
    /// Learns when bosses use their cast-bar skills and predicts the next ones. Each skill gets a
    /// cooldown (shortest normal gap between casts) and an opener time (pull to first cast); the bar
    /// fills from the last cast (or the pull) and then holds full until the boss casts it again.
    /// Bosses use skills by priority once off cooldown, so "ready" means "can come any time now".
    /// </summary>
    public sealed class BossPatternTracker
    {
        private const string LearnedFilePath = "boss-patterns.user.json";

        // The pull packet can arrive just after the first cast in the same batch.
        private static readonly TimeSpan PullGrace = TimeSpan.FromSeconds(1.5);
        // A cast bar that never completed (boss staggered, cast cancelled) stops showing after this.
        private static readonly TimeSpan CastGrace = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan StaleFight = TimeSpan.FromMinutes(15);

        private readonly Lock gate = new();
        private readonly BossPatternStore store;
        private readonly Dictionary<int, Fight> fights = [];
        private readonly HashSet<int> engagedEntities = [];

        public BossPatternTracker() : this(new BossPatternStore(LearnedFilePath, BossPatternStore.DefaultsPath)) { }

        public BossPatternTracker(BossPatternStore store) => this.store = store;

        public void OnPull(int entityId, int mobCode, DateTime at)
        {
            lock (gate)
            {
                if (fights.TryGetValue(entityId, out var fight))
                {
                    if (fight.PullAt is not null || at - fight.StartedAt > PullGrace) return;

                    // Casts from the same batch got here first: they belong to this pull.
                    fight.PullAt = fight.StartedAt;
                    fight.LearnOpeners = engagedEntities.Add(entityId);
                    if (fight.LearnOpeners)
                        foreach (var (skill, castAt) in fight.FirstCastAt) store.AddFirstCast(mobCode, skill, castAt - fight.StartedAt);
                    return;
                }

                // Only a boss's first engagement teaches openers: a later one may be a phase change
                // rather than a reset, and the boss would carry on mid-rotation.
                StartFight(entityId, mobCode, at, pullAt: at, learnOpeners: engagedEntities.Add(entityId));
            }
        }

        public void OnLeaveCombat(int entityId)
        {
            lock (gate)
            {
                if (fights.Remove(entityId)) store.Save();
            }
        }

        public void OnCastStart(int entityId, int mobCode, int skillCode, int castMs, DateTime at)
        {
            lock (gate)
            {
                // No pull seen (meter started mid-fight): track casts, but there's nothing to time openers from.
                if (!fights.TryGetValue(entityId, out var fight)) fight = StartFight(entityId, mobCode, at, pullAt: null, learnOpeners: false);
                fight.LastEventAt = at;

                store.AddCast(mobCode, skillCode, castMs);
                if (fight.LastCastAt.TryGetValue(skillCode, out var last))
                {
                    var gap = at - last;
                    if (gap <= TimeSpan.FromMilliseconds(castMs)) return; // same cast reported twice
                    store.AddGap(mobCode, skillCode, gap);
                }
                else
                {
                    fight.FirstCastAt[skillCode] = at;
                    if (fight.PullAt is { } pull && fight.LearnOpeners) store.AddFirstCast(mobCode, skillCode, at - pull);
                }

                fight.LastCastAt[skillCode] = at;
                fight.Casting[skillCode] = (at, TimeSpan.FromMilliseconds(castMs));
            }
        }

        public void OnSkillUsed(int entityId, int skillCode, DateTime at)
        {
            lock (gate)
            {
                if (!fights.TryGetValue(entityId, out var fight)) return;
                fight.LastEventAt = at;
                fight.Casting.Remove(skillCode);
            }
        }

        /// <summary>Bars for the boss's skills: casting first, then ready (newest first), then soonest.</summary>
        public IReadOnlyList<BossPatternBar> GetBars(int entityId, DateTime now, int max)
        {
            lock (gate)
            {
                if (!fights.TryGetValue(entityId, out var fight)) return [];

                var bars = new List<BossPatternBar>();
                foreach (var skill in store.SkillsFor(fight.MobCode).Union(fight.LastCastAt.Keys))
                {
                    if (Bar(fight, skill, now) is { } bar) bars.Add(bar);
                }

                return bars
                    .OrderBy(b => b.State switch { BossPatternState.Casting => 0, BossPatternState.Ready => 1, _ => 2 })
                    .ThenBy(b => b.State == BossPatternState.Ready ? b.Overdue : b.Remaining)
                    .Take(max)
                    .ToList();
            }
        }

        private BossPatternBar? Bar(Fight fight, int skill, DateTime now)
        {
            var name = SkillName(skill);

            if (fight.Casting.TryGetValue(skill, out var cast))
            {
                var elapsed = now - cast.At;
                if (elapsed < cast.Duration + CastGrace)
                {
                    var remaining = cast.Duration > elapsed ? cast.Duration - elapsed : TimeSpan.Zero;
                    return new BossPatternBar(skill, name, BossPatternState.Casting, Fraction(elapsed, cast.Duration), remaining, TimeSpan.Zero);
                }
                fight.Casting.Remove(skill);
            }

            var (firstCast, cooldown) = store.Estimate(fight.MobCode, skill);
            DateTime from;
            TimeSpan wait;
            if (fight.LastCastAt.TryGetValue(skill, out var last))
            {
                if (cooldown is null) return null;
                (from, wait) = (last, cooldown.Value);
            }
            else
            {
                if (fight.PullAt is not { } pull || firstCast is null) return null;
                (from, wait) = (pull, firstCast.Value);
            }

            var since = now - from;
            return since >= wait
                ? new BossPatternBar(skill, name, BossPatternState.Ready, 1, TimeSpan.Zero, since - wait)
                : new BossPatternBar(skill, name, BossPatternState.Filling, Fraction(since, wait), wait - since, TimeSpan.Zero);
        }

        private Fight StartFight(int entityId, int mobCode, DateTime at, DateTime? pullAt, bool learnOpeners)
        {
            foreach (var stale in fights.Where(f => at - f.Value.LastEventAt > StaleFight).Select(f => f.Key).ToList())
                fights.Remove(stale);

            var fight = new Fight { MobCode = mobCode, StartedAt = at, LastEventAt = at, PullAt = pullAt, LearnOpeners = learnOpeners };
            fights[entityId] = fight;
            return fight;
        }

        private static double Fraction(TimeSpan part, TimeSpan whole)
            => whole > TimeSpan.Zero ? Math.Clamp(part / whole, 0, 1) : 1;

        private static string SkillName(int skillCode)
            => GameDataProvider.Instance.Skills.GetName(skillCode) is { Length: > 0 } name && name != "Attack"
                ? name
                : $"Skill {skillCode}";

        private sealed class Fight
        {
            public int MobCode { get; init; }
            public DateTime StartedAt { get; init; }
            public DateTime LastEventAt { get; set; }
            public DateTime? PullAt { get; set; }
            public bool LearnOpeners { get; set; }
            public Dictionary<int, DateTime> FirstCastAt { get; } = [];
            public Dictionary<int, DateTime> LastCastAt { get; } = [];
            public Dictionary<int, (DateTime At, TimeSpan Duration)> Casting { get; } = [];
        }
    }
}
