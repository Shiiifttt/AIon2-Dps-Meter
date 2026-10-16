namespace AionDpsMeter.Services.Services.Session
{
    /// <summary>
    /// Works out which player is you when the meter missed the login packet (started mid-session).
    /// Skill cooldown packets only ever come for your own skills, so a player who keeps using a skill
    /// right as its cooldown arrives is you. Takes at least two matches and three times as many as
    /// anyone else, so another player of your class using the same skill at the same moment isn't picked.
    /// </summary>
    internal sealed class SelfIdentifier
    {
        private const int MatchesNeeded = 2;
        private static readonly TimeSpan Window = TimeSpan.FromSeconds(2.5);

        private readonly List<(int SkillId, int PlayerId, DateTime At)> recentUses = [];
        private readonly List<(int SkillId, DateTime At)> recentCooldowns = [];
        private readonly Dictionary<int, int> matchesByPlayer = [];

        /// <summary>Returns your player id once it's certain.</summary>
        public int? OnSkillUsed(int skillId, int playerId, DateTime at)
        {
            Prune(at);
            // A skill hits several times per use; one use per player is enough.
            if (recentUses.Any(u => u.SkillId == skillId && u.PlayerId == playerId)) return null;
            recentUses.Add((skillId, playerId, at));
            return recentCooldowns.Any(c => c.SkillId == skillId) ? Match(skillId) : null;
        }

        public int? OnCooldownStarted(int skillId, DateTime at)
        {
            Prune(at);
            recentCooldowns.Add((skillId, at));
            return Match(skillId);
        }

        private int? Match(int skillId)
        {
            // Two players using the skill at once tells us nothing.
            var players = recentUses.Where(u => u.SkillId == skillId).Select(u => u.PlayerId).Distinct().ToList();
            if (players.Count != 1) return null;
            matchesByPlayer[players[0]] = matchesByPlayer.GetValueOrDefault(players[0]) + 1;

            // Consume both sides so one cooldown can't count twice.
            recentUses.RemoveAll(u => u.SkillId == skillId);
            recentCooldowns.RemoveAll(c => c.SkillId == skillId);

            var ranked = matchesByPlayer.OrderByDescending(m => m.Value).ToList();
            var runnerUp = ranked.Count > 1 ? ranked[1].Value : 0;
            return ranked[0].Value >= MatchesNeeded && ranked[0].Value >= 3 * runnerUp ? ranked[0].Key : null;
        }

        private void Prune(DateTime now)
        {
            recentUses.RemoveAll(u => now - u.At > Window);
            recentCooldowns.RemoveAll(c => now - c.At > Window);
        }
    }
}
