using AionDpsMeter.Services.Models;

namespace AionDpsMeter.Services.Services.Session.Persistence
{
    public interface ICombatHistoryStore
    {
        void Save(HistorySessionSnapshot snapshot);
        void FlushPendingSaves();
        IReadOnlyList<HistorySessionListItem> GetSessionList();
        int GetSessionCount(DateTime? dateFrom, DateTime? dateTo, string? bossNameContains, IReadOnlySet<Guid>? excludeSessionIds = null);
        IReadOnlyList<HistorySessionListItem> GetSessionPage(
            DateTime? dateFrom,
            DateTime? dateTo,
            string? bossNameContains,
            int skip,
            int take,
            IReadOnlySet<Guid>? excludeSessionIds = null);
        HistorySessionSnapshot? GetSession(Guid sessionId);
        /// <summary>Player stats per saved fight against this target (no hits or buffs loaded).</summary>
        IReadOnlyDictionary<Guid, IReadOnlyList<PlayerStats>> GetPlayerStatsForTarget(string targetName);
        /// <summary>Deletes saved fights against these targets that lasted less than <paramref name="maxDuration"/>; returns how many.</summary>
        int DeleteShortSessions(IReadOnlySet<string> targetNames, TimeSpan maxDuration);
    }
}
