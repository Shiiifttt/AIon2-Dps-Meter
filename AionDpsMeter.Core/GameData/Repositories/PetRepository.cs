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

        public PetData? Get(int petId) => petsById.GetValueOrDefault(petId);

        public string GetName(int petId) =>
            petsById.TryGetValue(petId, out var pet) ? pet.Name : $"Pet #{petId}";
    }
}
