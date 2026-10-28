using AionDpsMeter.Core.GameData.Services;
using AionDpsMeter.Services.Services.Entity;
using AionDpsMeter.Services.Services.Settings;

namespace AionDpsMeter.Services.Services.Pets
{
    public enum PetCollectionStatus
    {
        /// <summary>Nothing known yet: log into a character with the meter running.</summary>
        Waiting,
        /// <summary>The game hasn't sent the collection yet: only counts changes seen since the meter started.</summary>
        Partial,
        /// <summary>Saved collection plus whatever was gained since; may be off until the next login.</summary>
        LastSession,
        /// <summary>The game sent the whole collection this session.</summary>
        Synced,
    }

    public sealed record PetEntry(int Id, string Name, string Genus, bool Farmable, int Rank, int Souls)
    {
        /// <summary>Souls for the next rank, or null at max rank.</summary>
        public int? Goal => PetCollectionTracker.SoulsForNextRank(Rank);
    }

    public sealed record PetSoulGain(DateTime At, int PetId, string Name, int Amount, int Rank, int Souls)
    {
        public int? Goal => PetCollectionTracker.SoulsForNextRank(Rank);
    }

    public sealed record PetCollectionView(PetCollectionStatus Status, string Character, DateTime? SyncedUtc, IReadOnlyList<PetEntry> Pets, IReadOnlyList<PetSoulGain> RecentGains);

    /// <summary>
    /// Pet collection progress for the local character. The game sends the whole collection at
    /// character login, then soul gains, souls spent on rank-ups and rank changes as they happen.
    /// Collections are saved per character so they show (and keep counting) when the meter was
    /// started mid-session, until the next login brings them back in line with the game.
    /// </summary>
    public sealed class PetCollectionTracker
    {
        private const string FilePath = "pet-collection.user.json";
        private const int MaxRecentGains = 30;

        // How far apart the login packet naming the character and the collection packet can be and
        // still be taken as the same login. Either one can come first.
        private static readonly TimeSpan LoginWindow = TimeSpan.FromMinutes(2);

        private readonly Lock gate = new();
        private readonly PetCollectionStore store;
        private readonly Func<bool> enabled;
        private readonly List<PetSoulGain> recentGains = [];

        // Profile the collection is saved under; null while the character isn't known yet.
        private string? key;
        private PetCollectionData? current;
        private bool synced;
        private DateTime syncedAt;
        private string? identifiedKey;
        private string identifiedName = "";
        private DateTime identifiedAt;

        /// <summary>Goes up on every change, so a UI can poll cheaply.</summary>
        public int Version { get; private set; }

        public static int? SoulsForNextRank(int rank) => rank switch
        {
            0 => 5,
            1 => 25,
            2 => 75,
            _ => null,
        };

        public PetCollectionTracker(EntityTracker entityTracker, IAppSettingsService settings)
            : this(entityTracker, new PetCollectionStore(FilePath), () => settings.TrackPetCollection) { }

        /// <param name="enabled">Pet tracking is opt-in; while this returns false nothing is tracked or saved.</param>
        public PetCollectionTracker(EntityTracker entityTracker, PetCollectionStore store, Func<bool>? enabled = null)
        {
            this.store = store;
            this.enabled = enabled ?? (() => true);
            if (store.LastCharacter is { } last && store.Get(last) is { } saved)
            {
                key = last;
                current = saved;
            }
            entityTracker.UserIdentified += OnUserIdentified;
        }

        public void OnCollection(IReadOnlyDictionary<int, int> ranks, IReadOnlyDictionary<int, int> souls, DateTime at)
        {
            lock (gate)
            {
                var data = new PetCollectionData { SyncedUtc = at.ToUniversalTime() };
                foreach (var (pet, rank) in ranks) data.Pets[pet] = new PetProgress { Rank = rank };
                foreach (var (pet, count) in souls)
                {
                    if (!data.Pets.TryGetValue(pet, out var progress)) data.Pets[pet] = progress = new PetProgress();
                    progress.Souls = count;
                }

                // A new login: if the character was named just before, it's theirs; otherwise wait for the name.
                key = identifiedKey is not null && at - identifiedAt <= LoginWindow ? identifiedKey : null;
                data.Character = key is null ? "" : identifiedName;
                if (current?.Character != data.Character) recentGains.Clear();
                current = data;
                synced = true;
                syncedAt = at;
                Changed(at);
            }
        }

        public void OnSoulsGained(IReadOnlyList<(int PetId, int Amount)> gains, DateTime at)
        {
            var added = new List<PetSoulGain>(gains.Count);
            lock (gate)
            {
                current ??= new PetCollectionData();
                foreach (var (pet, amount) in gains)
                {
                    var progress = Progress(pet);
                    progress.Souls += amount;
                    var gain = new PetSoulGain(at, pet, GameDataProvider.Instance.Pets.GetName(pet), amount, progress.Rank, progress.Souls);
                    recentGains.Insert(0, gain);
                    added.Add(gain);
                }
                if (recentGains.Count > MaxRecentGains) recentGains.RemoveRange(MaxRecentGains, recentGains.Count - MaxRecentGains);
                Changed(at);
            }
            SoulsGained?.Invoke(added);
        }

        /// <summary>Raised after souls were counted, with the pets' progress after the gain.</summary>
        public event Action<IReadOnlyList<PetSoulGain>>? SoulsGained;

        /// <summary>Whether anything is known about the collection (otherwise every count reads 0).</summary>
        public bool HasCollection
        {
            get { lock (gate) return current is not null; }
        }

        /// <summary>Rank and souls toward the next rank; (0, 0) for pets not seen yet.</summary>
        public (int Rank, int Souls) GetProgress(int petId)
        {
            lock (gate)
            {
                return current?.Pets.TryGetValue(petId, out var progress) == true ? (progress.Rank, progress.Souls) : (0, 0);
            }
        }

        public void OnSoulsSpent(IReadOnlyList<(int PetId, int Amount)> spent, DateTime at)
        {
            lock (gate)
            {
                current ??= new PetCollectionData();
                foreach (var (pet, amount) in spent)
                {
                    var progress = Progress(pet);
                    progress.Souls = Math.Max(0, progress.Souls - amount);
                }
                Changed(at);
            }
        }

        public void OnRankChanged(int petId, int rank, DateTime at)
        {
            lock (gate)
            {
                current ??= new PetCollectionData();
                Progress(petId).Rank = rank;
                Changed(at);
            }
        }

        public PetCollectionView GetView()
        {
            lock (gate)
            {
                var status = current is null ? PetCollectionStatus.Waiting
                    : synced ? PetCollectionStatus.Synced
                    : current.SyncedUtc is null ? PetCollectionStatus.Partial
                    : PetCollectionStatus.LastSession;
                var tracked = current?.Pets ?? [];
                var catalog = GameDataProvider.Instance.Pets;

                var pets = catalog.All
                    .Select(p => tracked.TryGetValue(p.Id, out var progress)
                        ? new PetEntry(p.Id, p.Name, p.Genus, p.Farmable, progress.Rank, progress.Souls)
                        : new PetEntry(p.Id, p.Name, p.Genus, p.Farmable, 0, 0))
                    .Concat(tracked
                        .Where(t => catalog.Get(t.Key) is null)
                        .Select(t => new PetEntry(t.Key, catalog.GetName(t.Key), "", false, t.Value.Rank, t.Value.Souls)))
                    .ToList();

                return new PetCollectionView(status, current?.Character ?? "", current?.SyncedUtc, pets, recentGains.ToList());
            }
        }

        private void OnUserIdentified(string name, string server)
        {
            if (!enabled()) return;
            var newKey = string.IsNullOrEmpty(server) ? name : $"{name}@{server}";
            var display = string.IsNullOrEmpty(server) ? name : $"{name} ({server})";
            var at = DateTime.UtcNow; // packet times are UTC too
            lock (gate)
            {
                identifiedKey = newKey;
                identifiedName = display;
                identifiedAt = at;
                if (newKey == key) return;

                if (synced && key is null && at - syncedAt <= LoginWindow)
                {
                    // The collection from this login arrived before the character's name.
                    key = newKey;
                    current!.Character = display;
                    store.Put(key, current);
                }
                else
                {
                    // A different character than the one being shown: switch to their saved collection.
                    // Gains seen before the name arrived stay only if nothing was saved for them yet.
                    var saved = store.Get(newKey);
                    if (saved is not null || current?.Character is { Length: > 0 })
                    {
                        current = saved;
                        recentGains.Clear();
                        synced = false;
                    }
                    key = newKey;
                    if (current is not null)
                    {
                        current.Character = display;
                        store.Put(key, current);
                    }
                }
                Version++;
            }
        }

        private PetProgress Progress(int petId)
        {
            if (!current!.Pets.TryGetValue(petId, out var progress)) current.Pets[petId] = progress = new PetProgress();
            return progress;
        }

        private void Changed(DateTime at)
        {
            current!.UpdatedUtc = at.ToUniversalTime();
            if (key is not null) store.Put(key, current);
            Version++;
        }
    }
}
