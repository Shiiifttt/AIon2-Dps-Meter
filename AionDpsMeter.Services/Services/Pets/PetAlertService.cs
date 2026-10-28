using AionDpsMeter.Core.GameData.Services;
using AionDpsMeter.Services.Services.Entity;
using AionDpsMeter.Services.Services.Settings;

namespace AionDpsMeter.Services.Services.Pets
{
    public enum PetAlertKind
    {
        /// <summary>A monster that drops souls for an unfinished pet came into range.</summary>
        Nearby,
        /// <summary>Souls were picked up.</summary>
        Gained,
        /// <summary>A pet reached max rank.</summary>
        Completed,
        /// <summary>The game sent the whole collection (login).</summary>
        Synced,
    }

    /// <param name="Guess">Matched by monster name only; not yet seen dropping from this monster.</param>
    public sealed record PetAlertLine(int PetId, string Name, int Rank, int Souls, int Goal, int Gained = 0, bool Guess = false);

    /// <param name="CountsKnown">False until the collection is known (log in with tracking on), so counts read 0.</param>
    /// <param name="InRange">Nearby only: how many of this monster are in range right now.</param>
    /// <param name="Note">Extra line under the title (the sync summary).</param>
    public sealed record PetAlert(PetAlertKind Kind, string Title, IReadOnlyList<PetAlertLine> Pets, bool CountsKnown, int InRange = 0, string? Note = null);

    /// <summary>
    /// Raises short-lived alerts for the pet popup: monsters nearby that drop souls for pets you haven't
    /// maxed, and soul pickups. Also teaches <see cref="PetSourceMap"/> which monster gave which souls.
    /// Only runs while pet tracking is on.
    /// </summary>
    public sealed class PetAlertService
    {
        // The same kind of monster keeps respawning around you; mention it again only after a while.
        private static readonly TimeSpan NearbyRepeat = TimeSpan.FromMinutes(5);
        // Souls arrive right after the kill; a death this long before the gain can be its source.
        private static readonly TimeSpan DeathToGain = TimeSpan.FromSeconds(3);
        private const int MaxRecentDeaths = 64;

        private readonly Lock gate = new();
        private readonly EntityTracker entityTracker;
        private readonly PetCollectionTracker collection;
        private readonly IAppSettingsService settings;
        private readonly PetSourceMap sources = new();
        private readonly Dictionary<int, DateTime> lastNearbyByMobCode = new();
        private readonly List<(DateTime At, int MobCode)> recentDeaths = [];
        // Monsters that drop souls for unfinished pets and are in range now: entity id -> monster name.
        private readonly Dictionary<int, (string Name, DateTime Since)> inRange = new();
        // A missed "left range" packet (zone change, capture hiccup) must not keep a monster counted forever.
        private static readonly TimeSpan InRangeExpiry = TimeSpan.FromMinutes(15);

        public event Action<PetAlert>? Alert;

        /// <summary>How many of a monster (by name) are in range changed; 0 = all left range or died.</summary>
        public event Action<string, int>? InRangeChanged;

        public PetAlertService(EntityTracker entityTracker, PetCollectionTracker collection, IAppSettingsService settings)
        {
            this.entityTracker = entityTracker;
            this.collection = collection;
            this.settings = settings;
            entityTracker.MobSpawned += OnMobSpawned;
            entityTracker.EntityDied += OnEntityDied;
            entityTracker.EntityRemoved += OnEntityGone;
            collection.SoulsGained += OnSoulsGained;
            collection.RankRaised += OnRankRaised;
            collection.CollectionSynced += OnCollectionSynced;
        }

        private bool AlertsOn => settings.TrackPetCollection && settings.ShowPetAlerts;

        private void OnMobSpawned(int entityId, int mobCode)
        {
            if (!AlertsOn) return;
            // Players' summons (a Spiritmaster's Water / Fire / Earth / Wind Spirit) share names with pets.
            if (entityTracker.IsSummon(entityId)) return;

            PetAlert? alert = null;
            (string Name, int Count)? update = null;
            var now = DateTime.UtcNow;
            lock (gate)
            {
                var lines = sources.PetsFor(mobCode)
                    .Select(pet => Line(pet.PetId, !pet.Confirmed))
                    .Where(l => l is not null)
                    .Select(l => l!)
                    .ToList();
                if (lines.Count == 0) return;

                var name = GameDataProvider.Instance.GetMobName(mobCode);
                foreach (var stale in inRange.Where(e => now - e.Value.Since > InRangeExpiry).Select(e => e.Key).ToList()) inRange.Remove(stale);
                inRange[entityId] = (name, now);
                int count = CountInRange(name);

                if (lastNearbyByMobCode.TryGetValue(mobCode, out var last) && now - last < NearbyRepeat)
                {
                    update = (name, count); // the card may still be up: refresh its count
                }
                else
                {
                    lastNearbyByMobCode[mobCode] = now;
                    alert = new PetAlert(PetAlertKind.Nearby, name, lines, collection.HasCollection, count);
                }
            }
            if (alert is not null) Alert?.Invoke(alert);
            if (update is { } u) InRangeChanged?.Invoke(u.Name, u.Count);
        }

        private void OnEntityDied(int entityId)
        {
            OnEntityGone(entityId);
            if (!settings.TrackPetCollection) return;
            if (entityTracker.GetTargetMob(entityId) is not { MobCode: > 0 } mob) return;

            lock (gate)
            {
                recentDeaths.Add((DateTime.UtcNow, mob.MobCode));
                if (recentDeaths.Count > MaxRecentDeaths) recentDeaths.RemoveRange(0, recentDeaths.Count - MaxRecentDeaths);
            }
        }

        // Died, despawned or out of range.
        private void OnEntityGone(int entityId)
        {
            (string Name, int Count) update;
            lock (gate)
            {
                if (!inRange.Remove(entityId, out var entry)) return;
                update = (entry.Name, CountInRange(entry.Name));
            }
            InRangeChanged?.Invoke(update.Name, update.Count);
        }

        private int CountInRange(string name) => inRange.Values.Count(e => e.Name == name);

        private void OnCollectionSynced()
        {
            if (!AlertsOn) return;
            var view = collection.GetView();
            var farmable = view.Pets.Where(p => p.Farmable).ToList();
            int maxed = view.Pets.Count(p => p.Rank >= PetCollectionTracker.MaxRank);
            int rank2 = view.Pets.Count(p => p.Rank == 2), rank1 = view.Pets.Count(p => p.Rank == 1);
            int locked = farmable.Count(p => p.Rank == 0);
            var title = string.IsNullOrEmpty(view.Character) ? "Pet collection" : view.Character;
            Alert?.Invoke(new PetAlert(PetAlertKind.Synced, title, [], true,
                Note: $"{maxed} maxed · {rank2} at rank 2 · {rank1} at rank 1 · {locked} farmable still locked"));
        }

        private void OnRankRaised(int petId, int rank)
        {
            if (rank < PetCollectionTracker.MaxRank || !AlertsOn) return;
            var name = GameDataProvider.Instance.Pets.GetName(petId);
            Alert?.Invoke(new PetAlert(PetAlertKind.Completed, name,
                [new PetAlertLine(petId, name, rank, 0, 0)], collection.HasCollection));
        }

        private void OnSoulsGained(IReadOnlyList<PetSoulGain> gains)
        {
            if (!settings.TrackPetCollection) return;

            var now = DateTime.UtcNow;
            List<PetAlertLine> lines;
            lock (gate)
            {
                LearnSources(gains, now);

                lines = gains
                    .Where(g => g.Goal is not null)
                    .Select(g => new PetAlertLine(g.PetId, g.Name, g.Rank, g.Souls, g.Goal!.Value, g.Amount))
                    .ToList();
            }

            if (lines.Count == 0 || !AlertsOn) return;
            var title = lines.Count == 1 ? $"+{lines[0].Gained} {lines[0].Name} soul" : "Pet souls";
            Alert?.Invoke(new PetAlert(PetAlertKind.Gained, title, lines, collection.HasCollection));
        }

        // Learn only when the kill is unambiguous: one kind of monster died just before the souls came in.
        private void LearnSources(IReadOnlyList<PetSoulGain> gains, DateTime now)
        {
            var codes = recentDeaths
                .Where(d => now - d.At <= DeathToGain)
                .Select(d => d.MobCode)
                .Distinct()
                .ToList();
            if (codes.Count != 1) return;

            foreach (var gain in gains) sources.Learn(codes[0], gain.PetId);
        }

        /// <summary>The pet's progress, or null when it's maxed.</summary>
        private PetAlertLine? Line(int petId, bool guess)
        {
            var (rank, souls) = collection.GetProgress(petId);
            if (PetCollectionTracker.SoulsForNextRank(rank) is not { } goal) return null;
            return new PetAlertLine(petId, GameDataProvider.Instance.Pets.GetName(petId), rank, souls, goal, Guess: guess);
        }
    }
}
