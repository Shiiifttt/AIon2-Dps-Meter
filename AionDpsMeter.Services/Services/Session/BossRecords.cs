using AionDpsMeter.Services.Models;

namespace AionDpsMeter.Services.Services.Session
{
    /// <summary>Your DPS in one fight compared with your best on that boss (same character).</summary>
    public sealed record PersonalBestComparison(string CharacterName, double Dps, double BestDps, Guid? BestSessionId = null)
    {
        public bool IsBest => Dps >= BestDps;
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
    /// and your own DPS per character. Short fights don't count, so a quick wipe with one burst
    /// rotation can't become a record.
    /// </summary>
    public sealed class BossRecords
    {
        private static readonly TimeSpan MinUptimeFight = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan MinDpsFight = TimeSpan.FromSeconds(30);

        private readonly Dictionary<int, double> bestUptimeByClass = new();
        private readonly Dictionary<string, double> bestDpsByCharacter = new();
        private readonly Dictionary<string, Guid> bestDpsSessionByCharacter = new();
        private readonly Dictionary<Guid, (string Character, double Dps)> userDpsBySession = new();

        // A fight counts as a kill when the party dealt (nearly) the boss's whole HP; a few hits can be lost.
        private const double KillDamageShare = 0.97;
        private static readonly TimeSpan MinKillFight = TimeSpan.FromSeconds(10);
        private readonly Dictionary<Guid, TimeSpan> killTimeBySession = new();
        private (Guid SessionId, TimeSpan Duration, DateTime At)? fastestKill;

        public void AddFight(HistorySessionListItem fight)
        {
            if (fight.TargetHpTotal <= 0 || fight.TotalDamage < fight.TargetHpTotal * KillDamageShare) return;
            if (fight.Duration < MinKillFight) return;

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
                if (!bestDpsByCharacter.TryGetValue(character, out var bestDps) || s.DamagePerSecond > bestDps)
                {
                    bestDpsByCharacter[character] = s.DamagePerSecond;
                    bestDpsSessionByCharacter[character] = sessionId;
                }
            }
        }

        public double? BestUptime(int classId) => bestUptimeByClass.TryGetValue(classId, out var best) ? best : null;

        public PersonalBestComparison? CompareSaved(Guid sessionId)
            => userDpsBySession.TryGetValue(sessionId, out var mine) ? Compare(mine.Character, mine.Dps) : null;

        /// <summary>For a fight still in progress (not saved yet).</summary>
        public PersonalBestComparison? CompareLive(IEnumerable<PlayerStats> stats)
            => stats.Select(s => UserCharacter(s) is { } c && s.CombatDuration >= MinDpsFight ? Compare(c, s.DamagePerSecond) : null).FirstOrDefault(c => c is not null);

        private PersonalBestComparison? Compare(string character, double dps)
            => bestDpsByCharacter.TryGetValue(character, out var best)
                ? new PersonalBestComparison(CharacterName(character), dps, best,
                    bestDpsSessionByCharacter.TryGetValue(character, out var bestId) ? bestId : null)
                : null;

        // Alts and class changes each get their own best.
        private static string? UserCharacter(PlayerStats s)
            => s.IsUser && s.IsIdentified && !string.IsNullOrEmpty(s.PlayerName) ? $"{s.PlayerName}|{s.ClassId}" : null;

        private static string CharacterName(string character) => character[..character.LastIndexOf('|')];
    }
}
