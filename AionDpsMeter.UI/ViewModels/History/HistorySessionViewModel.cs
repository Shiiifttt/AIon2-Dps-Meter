using AionDpsMeter.Services.Models;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Settings;

namespace AionDpsMeter.UI.ViewModels.History
{
   
    public sealed class HistorySessionViewModel : ViewModelBase
    {
        private readonly HistorySessionSnapshot _snapshot;

        public string TargetName  => string.IsNullOrEmpty(_snapshot.TargetName)
            ? $"Mob #{_snapshot.TargetId}"
            : _snapshot.TargetName;

        public string DateDisplay => _snapshot.SessionEnd.ToString("dd.MM.yyyy  HH:mm:ss");
        public string Duration    => DamageFormatter.FormatDuration(_snapshot.Duration);
        public bool   IsCompleted => _snapshot.State == SessionState.Completed;
        public string StateLabel  => IsCompleted ? "Completed" : "Active";
        public string StateColor  => IsCompleted ? "#888888" : "#4EC9B0";

        public IReadOnlyList<HistoryPlayerViewModel> Players { get; }

        // Stagger gauge taken off the boss by the whole party (0 for bosses without a gauge or older fights).
        public double TotalBreak { get; }
        public bool   HasBreak => TotalBreak > 0;
        public string TotalBreakDisplay => TotalBreak.ToString("0");

        /// <param name="bestUptimeForClass">Best saved uptime on this boss for a class id, if any.</param>
        public HistorySessionViewModel(HistorySessionSnapshot snapshot, IAppSettingsService settingsService, Func<int, double?>? bestUptimeForClass = null)
        {
            _snapshot = snapshot;
            TotalBreak = snapshot.PlayerStats.Sum(p => p.TotalBreak);
            var buffStats = RecomputeOldFightUptime(snapshot);
            Players = snapshot.PlayerStats.Where(r => r.IsIdentified || r.DamagePercentage > 1)
                .OrderByDescending(p => p.TotalDamage)
                .Select(p => new HistoryPlayerViewModel(
                    p,
                    snapshot.SkillStatsByPlayer.TryGetValue(p.PlayerId, out var skills) ? skills : [],
                    buffStats.TryGetValue(p.PlayerId, out var buffs) ? buffs : [],
                    snapshot.HitsByPlayer.TryGetValue(p.PlayerId, out var hits) ? hits : [],
                    snapshot.BuffEventsByPlayer.TryGetValue(p.PlayerId, out var buffEvts) ? buffEvts : [],
                    settingsService,
                    bestUptimeForClass?.Invoke(p.ClassId),
                    TotalBreak,
                    snapshot.Duration))
                .ToList();
        }

        public Guid SessionId => _snapshot.SessionId;

        /// <summary>You in this fight, if you were in it.</summary>
        public HistoryPlayerViewModel? UserPlayer => Players.FirstOrDefault(p => p.IsUser);

        // Fights saved before buff uptime was measured against active time (and before downtime needed
        // more than one player hitting) get both recomputed from their saved hits.
        private static IReadOnlyDictionary<long, IReadOnlyCollection<BuffStats>> RecomputeOldFightUptime(HistorySessionSnapshot snapshot)
        {
            bool upToDate = snapshot.BuffStatsByPlayer.Values.All(list => list.All(b => b.ActiveUptimePercentage is not null));
            if (upToDate || snapshot.BuffEventsByPlayer.Count == 0) return snapshot.BuffStatsByPlayer;

            var uptime = UptimeCalculator.Compute(
                snapshot.HitsByPlayer.ToDictionary(p => p.Key, p => UptimeCalculator.ActiveHitTimes(p.Value)),
                snapshot.SessionEnd);

            foreach (var stats in snapshot.PlayerStats)
            {
                if (stats.UptimePercentage is null || uptime.FightSeconds <= 0 ||
                    !uptime.ActiveByPlayer.TryGetValue(stats.PlayerId, out var playerActive)) continue;
                var active = UptimeCalculator.Total(playerActive).TotalSeconds;
                stats.ActiveSeconds = active;
                stats.IdleSeconds = Math.Max(0, uptime.FightSeconds - active);
                stats.UptimePercentage = Math.Min(100, active / uptime.FightSeconds * 100);
            }

            var result = new Dictionary<long, IReadOnlyCollection<BuffStats>>(snapshot.BuffStatsByPlayer);
            foreach (var (playerId, events) in snapshot.BuffEventsByPlayer)
            {
                if (events.Count == 0 || !uptime.ActiveByPlayer.TryGetValue(playerId, out var active)) continue;
                result[playerId] = BuffStatisticsCalculator.ComputeBuffStats(events, snapshot.SessionStart, snapshot.SessionEnd, active);
            }
            return result;
        }
    }
}
