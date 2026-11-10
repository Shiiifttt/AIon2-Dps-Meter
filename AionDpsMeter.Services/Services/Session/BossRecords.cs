using AionDpsMeter.Services.Models;

namespace AionDpsMeter.Services.Services.Session
{
    /// <summary>Your DPS in one fight compared with your best fight on that boss (same character).</summary>
    /// <param name="IsBest">This fight is the personal best.</param>
    /// <param name="BestIsClear">False when the boss was never cleared and the best is the attempt that got it lowest.</param>
    /// <param name="BestRemainingHpPercent">Boss HP left in the best fight (0 for a clear).</param>
    public sealed record PersonalBestComparison(
        string CharacterName,
        double Dps,
        double BestDps,
        Guid? BestSessionId,
        bool IsBest,
        bool BestIsClear,
        double? BestRemainingHpPercent)
    {
        public double DeltaPercent => BestDps > 0 ? (Dps - BestDps) / BestDps * 100 : 0;
    }

    /// <summary>How long this kill took compared with the fastest saved kill of that boss.</summary>
    public sealed record KillTimeComparison(TimeSpan Duration, TimeSpan Fastest, Guid FastestSessionId, DateTime FastestAt)
    {
        public bool IsFastest => Duration <= Fastest;
        public TimeSpan Delta => Duration - Fastest;
    }

    /// <summary>
    /// Bests against one boss from saved fights: uptime per class (anyone you've grouped with)
    /// and your own personal best per character. The personal best is your highest DPS among clears;
    /// until the boss has been cleared it is the attempt that got the boss's HP lowest. Short fights
    /// don't count, so a quick wipe with one burst rotation can't become a record.
    /// </summary>
    public sealed class BossRecords
    {
        private static readonly TimeSpan MinUptimeFight = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan MinDpsFight = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan MinKillFight = TimeSpan.FromSeconds(10);

        // Training dummies never die: their best is simply the highest DPS.
        private readonly bool isDummy;

        private readonly Dictionary<int, double> bestUptimeByClass = new();
        private readonly Dictionary<Guid, (string Character, double Dps)> userDpsBySession = new();
        private readonly Dictionary<Guid, (bool IsClear, double? RemainingHpPercent)> outcomeBySession = new();
        private readonly Dictionary<string, Best> bestByCharacter = new();

        private readonly Dictionary<Guid, TimeSpan> killTimeBySession = new();
        private (Guid SessionId, TimeSpan Duration, DateTime At)? fastestKill;

        private sealed record Best(Guid SessionId, double Dps, bool IsClear, double? RemainingHpPercent);

        public BossRecords(bool isDummy = false) => this.isDummy = isDummy;

        public void AddFight(HistorySessionListItem fight)
        {
            outcomeBySession[fight.SessionId] = (fight.IsClear, fight.RemainingHpPercent);
            bestByCharacter.Clear();

            if (!fight.IsClear || fight.Duration < MinKillFight) return;

            killTimeBySession[fight.SessionId] = fight.Duration;
            if (fastestKill is not { } best || fight.Duration < best.Duration)
                fastestKill = (fight.SessionId, fight.Duration, fight.SessionEnd);
        }

        /// <summary>Null when this fight wasn't a kill or no kill is saved.</summary>
        public KillTimeComparison? CompareKillTime(Guid sessionId)
            => killTimeBySession.TryGetValue(sessionId, out var duration) && fastestKill is { } best
                ? new KillTimeComparison(duration, best.Duration, best.SessionId, best.At)
                : null;

        public void Add(Guid sessionId, IEnumerable<PlayerStats> stats)
        {
            foreach (var s in stats)
            {
                if (s.UptimePercentage is { } uptime && s.ClassId > 0
                    && TimeSpan.FromSeconds(s.ActiveSeconds + s.IdleSeconds) >= MinUptimeFight
                    && (!bestUptimeByClass.TryGetValue(s.ClassId, out var bestUptime) || uptime > bestUptime))
                {
                    bestUptimeByClass[s.ClassId] = uptime;
                }

                if (UserCharacter(s) is not { } character || s.CombatDuration < MinDpsFight) continue;
                userDpsBySession[sessionId] = (character, s.DamagePerSecond);
                bestByCharacter.Clear();
            }
        }

        public double? BestUptime(int classId) => bestUptimeByClass.TryGetValue(classId, out var best) ? best : null;

        public PersonalBestComparison? CompareSaved(Guid sessionId)
            => userDpsBySession.TryGetValue(sessionId, out var mine) ? Compare(mine.Character, mine.Dps, sessionId) : null;

        /// <summary>For a fight still in progress (not saved yet).</summary>
        public PersonalBestComparison? CompareLive(IEnumerable<PlayerStats> stats)
            => stats.Select(s => UserCharacter(s) is { } c && s.CombatDuration >= MinDpsFight ? Compare(c, s.DamagePerSecond, null) : null)
                .FirstOrDefault(c => c is not null);

        private PersonalBestComparison? Compare(string character, double dps, Guid? sessionId)
            => BestFor(character) is { } best
                ? new PersonalBestComparison(CharacterName(character), dps, best.Dps, best.SessionId,
                    IsBest: sessionId == best.SessionId, best.IsClear, best.RemainingHpPercent)
                : null;

        private Best? BestFor(string character)
        {
            if (bestByCharacter.TryGetValue(character, out var cached)) return cached;

            var fights = userDpsBySession
                .Where(kv => kv.Value.Character == character)
                .Select(kv =>
                {
                    var outcome = outcomeBySession.TryGetValue(kv.Key, out var o) ? o : (IsClear: false, RemainingHpPercent: null);
                    return new Best(kv.Key, kv.Value.Dps, outcome.IsClear, outcome.RemainingHpPercent);
                })
                .ToList();
            if (fights.Count == 0) return null;

            var clears = fights.Where(f => f.IsClear).ToList();
            var best = isDummy ? fights.MaxBy(f => f.Dps)
                : clears.Count > 0 ? clears.MaxBy(f => f.Dps)
                // No clear yet: the attempt that got the boss lowest (unknown HP counts as untouched), then DPS.
                : fights.OrderBy(f => f.RemainingHpPercent ?? 100).ThenByDescending(f => f.Dps).First();

            bestByCharacter[character] = best!;
            return best;
        }

        // Alts and class changes each get their own best.
        private static string? UserCharacter(PlayerStats s)
            => s.IsUser && s.IsIdentified && !string.IsNullOrEmpty(s.PlayerName) ? $"{s.PlayerName}|{s.ClassId}" : null;

        private static string CharacterName(string character) => character[..character.LastIndexOf('|')];
    }
}
