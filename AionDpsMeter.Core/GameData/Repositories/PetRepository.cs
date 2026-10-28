using System.Collections.Frozen;
using System.Text.Json;
using AionDpsMeter.Core.GameData.Contracts;

namespace AionDpsMeter.Core.GameData.Repositories
{
    /// <summary>
    /// Pets in the collection, by the id the game uses in its soul packets. The list comes from
    /// corpus.gg's pet collection page; pets missing from it still track, just without a name.
    /// </summary>
    public sealed class PetRepository
    {
        private FrozenDictionary<int, PetData> petsById = FrozenDictionary<int, PetData>.Empty;

        public IReadOnlyCollection<PetData> All => petsById.Values;

        public void Load(string path)
        {
            if (!File.Exists(path))
                return;

            var json = File.ReadAllText(path);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var file = JsonSerializer.Deserialize<PetsFile>(json, options);
            if (file is not null)
                petsById = file.Pets.DistinctBy(p => p.Id).ToFrozenDictionary(p => p.Id);
        }

        // Monster code -> pets it drops souls for, from pet_sources.json (empty when the file is missing).
        private FrozenDictionary<int, int[]> petsByMob = FrozenDictionary<int, int[]>.Empty;

        /// <summary>Whether the monster -> pet table was loaded.</summary>
        public bool HasSources => petsByMob.Count > 0;

        public void LoadSources(string path)
        {
            if (!File.Exists(path))
                return;

            var json = File.ReadAllText(path);
            var file = JsonSerializer.Deserialize<PetSourcesFile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (file is null) return;

            petsByMob = file.Pets
                .SelectMany(p => p.Value.Select(mob => (Mob: mob, Pet: int.Parse(p.Key))))
                .GroupBy(x => x.Mob)
                .ToFrozenDictionary(g => g.Key, g => g.Select(x => x.Pet).Distinct().ToArray());
        }

        /// <summary>Pets whose souls this monster drops, per the source table (empty if none or unknown).</summary>
        public IReadOnlyList<int> PetsDroppedBy(int mobCode) => petsByMob.TryGetValue(mobCode, out var pets) ? pets : [];

        public PetData? Get(int petId) => petsById.GetValueOrDefault(petId);

        public string GetName(int petId) =>
            petsById.TryGetValue(petId, out var pet) ? pet.Name : $"Pet #{petId}";
    }
}
