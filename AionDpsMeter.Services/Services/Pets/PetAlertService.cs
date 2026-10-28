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
    }

    public sealed record PetAlertLine(int PetId, string Name, int Rank, int Souls, int Goal, int Gained = 0);

    /// <param name="CountsKnown">False until the collection is known (log in with tracking on), so counts read 0.</param>
    public sealed record PetAlert(PetAlertKind Kind, string Title, IReadOnlyList<PetAlertLine> Pets, bool CountsKnown);

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

        public event Action<PetAlert>? Alert;

        public PetAlertService(EntityTracker entityTracker, PetCollectionTracker collection, IAppSettingsService settings)
        {
            this.entityTracker = entityTracker;
            this.collection = collection;
            this.settings = settings;
            entityTracker.MobSpawned += OnMobSpawned;
            entityTracker.EntityDied += OnEntityDied;
            collection.SoulsGained += OnSoulsGained;
        }

        private bool AlertsOn => settings.TrackPetCollection && settings.ShowPetAlerts;

        private void OnMobSpawned(int entityId, int mobCode)
        {
            if (!AlertsOn) return;

            PetAlert? alert;
            var now = DateTime.UtcNow;
            lock (gate)
            {
                if (lastNearbyByMobCode.TryGetValue(mobCode, out var last) && now - last < NearbyRepeat) return;

                var lines = sources.PetsFor(mobCode)
                    .Select(pet => Line(pet))
                    .Where(l => l is not null)
                    .Select(l => l!)
                    .ToList();
                if (lines.Count == 0) return;

                lastNearbyByMobCode[mobCode] = now;
                alert = new PetAlert(PetAlertKind.Nearby, GameDataProvider.Instance.GetMobName(mobCode), lines, collection.HasCollection);
            }
            Alert?.Invoke(alert);
        }

        private void OnEntityDied(int entityId)
        {
            if (!settings.TrackPetCollection) return;
            if (entityTracker.GetTargetMob(entityId) is not { MobCode: > 0 } mob) return;

            lock (gate)
            {
                recentDeaths.Add((DateTime.UtcNow, mob.MobCode));
                if (recentDeaths.Count > MaxRecentDeaths) recentDeaths.RemoveRange(0, recentDeaths.Count - MaxRecentDeaths);
            }
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
        private PetAlertLine? Line(int petId)
        {
            var (rank, souls) = collection.GetProgress(petId);
            if (PetCollectionTracker.SoulsForNextRank(rank) is not { } goal) return null;
            return new PetAlertLine(petId, GameDataProvider.Instance.Pets.GetName(petId), rank, souls, goal);
        }
    }
}
