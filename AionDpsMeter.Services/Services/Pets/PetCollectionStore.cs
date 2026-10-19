using System.Text.Json;
using System.Text.Json.Serialization;

namespace AionDpsMeter.Services.Services.Pets
{
    public sealed class PetProgress
    {
        /// <summary>0 = not unlocked yet, 3 = max.</summary>
        [JsonPropertyName("rank")]
        public int Rank { get; set; }

        /// <summary>Souls collected toward the next rank.</summary>
        [JsonPropertyName("souls")]
        public int Souls { get; set; }
    }

    public sealed class PetCollectionData
    {
        [JsonPropertyName("character")]
        public string Character { get; set; } = "";

        /// <summary>Last time the game sent the whole collection (at character login).</summary>
        [JsonPropertyName("syncedUtc")]
        public DateTime? SyncedUtc { get; set; }

        [JsonPropertyName("updatedUtc")]
        public DateTime UpdatedUtc { get; set; }

        [JsonPropertyName("pets")]
        public Dictionary<int, PetProgress> Pets { get; set; } = [];

        public PetCollectionData Clone() => new()
        {
            Character = Character,
            SyncedUtc = SyncedUtc,
            UpdatedUtc = UpdatedUtc,
            Pets = Pets.ToDictionary(p => p.Key, p => new PetProgress { Rank = p.Value.Rank, Souls = p.Value.Souls }),
        };
    }

    /// <summary>
    /// Pet collections by character, saved next to the exe. Not thread-safe;
    /// <see cref="PetCollectionTracker"/> serializes access.
    /// </summary>
    public sealed class PetCollectionStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private sealed class StoreFile
        {
            [JsonPropertyName("lastCharacter")]
            public string? LastCharacter { get; set; }

            [JsonPropertyName("characters")]
            public Dictionary<string, PetCollectionData> Characters { get; set; } = [];
        }

        private readonly string path;
        private readonly StoreFile file;

        public PetCollectionStore(string path)
        {
            this.path = path;
            file = Load(path);
        }

        public string? LastCharacter => file.LastCharacter;

        public PetCollectionData? Get(string key) => file.Characters.GetValueOrDefault(key)?.Clone();

        public void Put(string key, PetCollectionData data)
        {
            file.Characters[key] = data.Clone();
            file.LastCharacter = key;
            Save();
        }

        private void Save()
        {
            // Write a temp file and swap it in, so a crash mid-write can't eat the collection.
            try
            {
                var temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(file, JsonOptions));
                File.Move(temp, path, overwrite: true);
            }
            catch { }
        }

        private static StoreFile Load(string path)
        {
            try
            {
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<StoreFile>(File.ReadAllText(path)) ?? new StoreFile();
            }
            catch { }
            return new StoreFile();
        }
    }
}
