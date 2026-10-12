using AionDpsMeter.Core.Models;
using AionDpsMeter.Services.Models;
using AionDpsMeter.Services.Services.Entity;
using AionDpsMeter.Services.Services.Settings;
using System.Data;
using static System.Net.Mime.MediaTypeNames;

namespace AionDpsMeter.Services.Services.Session
{
    public enum SessionState
    {
        Active,
        Completed
    }

    public sealed class TargetCombatSession
    {
        private readonly Dictionary<long, PlayerSession> playerSessions = new();
        private readonly EntityTracker entityTracker;

        public Guid SessionId { get; } = Guid.NewGuid();
        public int TargetId { get; }
        public Mob TargetInfo { get; }
        public DateTime SessionStart { get; }
        public DateTime LastHitTime { get; private set; }
        public DateTime? CompletedAt { get; private set; }
        public SessionState State { get; private set; } = SessionState.Active;
        public bool IsCompleted => State == SessionState.Completed;

        private readonly BuffEventManager buffEventManager = new();

        private long LastKnownTargetHp { get; set; } = -1;
        private readonly IAppSettingsService settingsService;

        // Hits not yet matched to a stagger gauge update.
        private readonly List<PlayerDamage> pendingBreakHits = new();
        private static readonly TimeSpan PendingBreakHitLifetime = TimeSpan.FromSeconds(2);

        public int StaggerCount { get; private set; }
        public DateTime? LastStaggerAt { get; private set; }


        public TargetCombatSession(Mob targetInfo, DateTime sessionStart, EntityTracker entityTracker, IAppSettingsService settingsService)
        {
            TargetId = targetInfo.Id;
            TargetInfo = targetInfo;
            this.settingsService = settingsService;
            SessionStart = sessionStart;
            LastHitTime = sessionStart;
            this.entityTracker = entityTracker;

        }

        public void AddDamage(PlayerDamage damage)
        {
            if (settingsService.OneMinuteDummyMode && TargetInfo.IsDummy && IsOneMinuteModeComplete())
                return;

            if (!playerSessions.TryGetValue(damage.SourceEntity.Id, out var session))
            {
                session = new PlayerSession(damage.SourceEntity.Id, entityTracker);
                playerSessions[damage.SourceEntity.Id] = session;
            }

            session.AddDamage(damage);

            if (!damage.IsDot)
            {
                pendingBreakHits.RemoveAll(h => damage.DateTime - h.DateTime > PendingBreakHitLifetime);
                pendingBreakHits.Add(damage);
            }

            if (damage.DateTime > LastHitTime)
                LastHitTime = damage.DateTime;
            LastKnownTargetHp = TargetInfo.HpCurrent;
        }

        /// <summary>
        /// Credits a stagger gauge drop to the hits that arrived just before the update.
        /// <paramref name="gaugeWasDropping"/> is false while the boss is staggered, when hits don't move the gauge.
        /// </summary>
        public void ApplyStaggerDrop(int drop, DateTime at, bool gaugeWasDropping, BreakAttributor attributor)
        {
            if (IsCompleted) return;

            var batch = pendingBreakHits.Where(h => at - h.DateTime <= BreakAttributor.BatchWindow).ToList();
            if (gaugeWasDropping)
            {
                foreach (var hit in pendingBreakHits.Except(batch)) attributor.LearnNoBreak(hit);
            }
            pendingBreakHits.Clear();

            if (drop > 0) attributor.Attribute(batch, drop);
        }

        public void RegisterStagger(DateTime at)
        {
            if (IsCompleted) return;
            StaggerCount++;
            LastStaggerAt = at;
        }

        public void RegisterPlayerDeath(int playerId)
        {
            if (TargetInfo.HpCurrent == 0) return;
            if (!playerSessions.TryGetValue(playerId, out var session)) return;
            session.RegisterPlayerDeath();
        }

        private bool IsOneMinuteModeComplete()
        {
            if (LastHitTime - SessionStart > TimeSpan.FromMinutes(1)) return true;
            return false;
        }



        public void ProcessBuffEvent(BuffEvent buffEvent)
        {
            if (IsCompleted) return;
            var buffEnd = buffEvent.AppliedAt.AddMilliseconds(buffEvent.DurationMs);
            if (buffEnd <= SessionStart) return;
            buffEventManager.Add(buffEvent);
        }


        public bool IsNewTry()
        {
            
            if (TargetInfo.IsDummy) return false;
            if (LastKnownTargetHp == -1) return false;
            if (TargetInfo.HpCurrent > LastKnownTargetHp && (DateTime.Now - LastHitTime) > TimeSpan.FromSeconds(5)) return true;
            return false;
        }


        public IReadOnlyCollection<int> GetPlayerEntityIds()
        {
            return playerSessions.Keys.Select(k => (int)k).ToList();
        }

        public void Complete()
        {
            if (IsCompleted) return;
            State = SessionState.Completed;
            CompletedAt = LastHitTime;
        }

        public TimeSpan GetCombatDuration()
        {
            var duration = LastHitTime - SessionStart;
            return duration > TimeSpan.Zero ? duration : TimeSpan.Zero;
        }

        public IReadOnlyCollection<PlayerStats> GetPlayerStats()
        {
            var activeSessions = playerSessions.Values
                .Where(s => s.HitCount > 0)
                .ToList();

            if (activeSessions.Count == 0)
                return [];

            long totalDamage = activeSessions.Sum(s => s.TotalDamage);
            var uptime = GetUptime();

            return activeSessions
                .Select(s =>
                {
                    var stats = DamageStatisticsCalculator.ComputePlayerStats(s, totalDamage, LastStaggerAt);
                    if (uptime.ByPlayer.TryGetValue(s.PlayerId, out var active) && uptime.PartyActive > TimeSpan.Zero)
                    {
                        stats.ActiveSeconds = active.TotalSeconds;
                        stats.IdleSeconds = (uptime.PartyActive - active).TotalSeconds;
                        stats.UptimePercentage = Math.Min(100, active / uptime.PartyActive * 100);
                    }
                    return stats;
                })
                .OrderByDescending(s => s.TotalDamage)
                .ToList();
        }

        // Uptime needs every player's hits sorted, so it's refreshed at most once a second while
        // the fight runs (the main meter reads stats ~30 times a second) and exactly once it ends.
        private static readonly TimeSpan UptimeRefreshInterval = TimeSpan.FromSeconds(1);
        private (TimeSpan PartyActive, Dictionary<long, TimeSpan> ByPlayer) uptimeCache = (TimeSpan.Zero, new());
        private int uptimeCacheHitCount = -1;
        private DateTime uptimeCacheAt;
        private bool uptimeCacheFinal;

        private (TimeSpan PartyActive, Dictionary<long, TimeSpan> ByPlayer) GetUptime()
        {
            int hitCount = playerSessions.Values.Sum(s => s.Hits.Count);
            bool stale = hitCount != uptimeCacheHitCount
                         && (IsCompleted || DateTime.Now - uptimeCacheAt >= UptimeRefreshInterval);
            if (!stale && !(IsCompleted && !uptimeCacheFinal)) return uptimeCache;

            var times = playerSessions.ToDictionary(p => p.Key, p => UptimeCalculator.ActiveHitTimes(p.Value.Hits));
            var partyActive = UptimeCalculator.ActiveTime(UptimeCalculator.Merge(times.Values), LastHitTime);
            var byPlayer = times.ToDictionary(t => t.Key, t => UptimeCalculator.ActiveTime(t.Value, LastHitTime));

            uptimeCache = (partyActive, byPlayer);
            uptimeCacheHitCount = hitCount;
            uptimeCacheAt = DateTime.Now;
            uptimeCacheFinal = IsCompleted;
            return uptimeCache;
        }

        public IReadOnlyList<PlayerDamage> GetCombatLog(long playerId)
        {
            if (!playerSessions.TryGetValue(playerId, out var session))
                return [];

            return [.. session.Hits.AsEnumerable().Reverse()];
        }

        public IReadOnlyCollection<SkillStats> GetSkillStats(long playerId)
        {
            if (!playerSessions.TryGetValue(playerId, out var session))
                return [];

            return DamageStatisticsCalculator.ComputeSkillStats(session, settingsService.GroupSummonDamage);
        }

        public IReadOnlyList<BuffEvent> GetBuffEvents(long playerId, DateTime from, DateTime to)
            => buffEventManager.GetBuffEvents((int)playerId, from, to);

        public int CountRecentHits(DateTime cutoff)
            => playerSessions.Values.Sum(s => s.CountHitsAfter(cutoff));

        public DateTime? GetUserLastHitTime()
            => playerSessions.Values
                .Where(s => s.IsUser && s.LastHit.HasValue)
                .Select(s => s.LastHit!.Value)
                .Cast<DateTime?>()
                .Max();

        public void Reset()
        {
            foreach (var session in playerSessions.Values)
                session.Reset();
            playerSessions.Clear();
            buffEventManager.Reset();
            pendingBreakHits.Clear();
        }

        /// <summary>
        /// Transfers all damage recorded under <paramref name="summonId"/> to the owner's player session.
        /// Returns:
        ///   -1  – no summon session found in this combat session (nothing to do)
        ///    0  – summon session found but owner player entity is unknown; session kept as-is,
        ///         a placeholder entity named "Summon_{summonId}" is registered in the tracker
        ///   >0  – number of hits successfully transferred to the owner's session
        /// </summary>
        public int TransferSummonDamage(int summonId, int ownerId)
        {
            if (!playerSessions.TryGetValue(summonId, out var summonSession))
                return -1;

            var ownerEntity = entityTracker.GetPlayerEntity(ownerId);
            if (ownerEntity is null)
                return 0;

            if (!playerSessions.TryGetValue(ownerId, out var ownerSession))
            {
                ownerSession = new PlayerSession(ownerId, entityTracker);
                playerSessions[ownerId] = ownerSession;
            }

            foreach (var hit in summonSession.Hits)
            {
                var reStamped = new PlayerDamage
                {
                    DateTime       = hit.DateTime,
                    SourceEntity   = ownerEntity,
                    SourceSummon   = hit.SourceEntity,
                    TargetEntity   = hit.TargetEntity,
                    Skill          = hit.Skill,
                    CharacterClass = hit.CharacterClass,
                    Damage         = hit.Damage,
                    IsCritical     = hit.IsCritical,
                    IsBackAttack   = hit.IsBackAttack,
                    IsFrontAttack  = hit.IsFrontAttack,
                    IsPerfect      = hit.IsPerfect,
                    IsDoubleDamage = hit.IsDoubleDamage,
                    IsParry        = hit.IsParry,
                    IsDot          = hit.IsDot,
                    Break          = hit.Break,
                };
                ownerSession.AddDamage(reStamped);
            }

            var transferred = summonSession.Hits.Count;
            playerSessions.Remove(summonId);
            return transferred;
        }
    }
}