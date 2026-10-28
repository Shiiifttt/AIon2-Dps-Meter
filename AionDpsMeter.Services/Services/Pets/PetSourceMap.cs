using AionDpsMeter.Core.GameData.Services;
using System.Text.Json;

namespace AionDpsMeter.Services.Services.Pets
{
    /// <summary>
    /// Which pets' souls a monster can drop, by mob code. The game data has no such table, so it starts
    /// from names and learns from play:
    /// - Names: monsters carry regional names ("Ellun River Kailin", "Wetland Slink") and one monster
    ///   drops souls for a whole family (Kailin and Young Kailin), so a monster matches every pet whose
    ///   name, without "Young" / "Elite" / "Aberrant" style prefixes, ends the monster's name.
    ///   Checked against a 3 hour log: right for 323 of 346 soul gains, and 185 of 199 farmable pets
    ///   have at least one matching monster.
    /// - Learned: each soul gain right after exactly one kind of monster died is saved here, which
    ///   covers the pets whose monsters are named differently.
    /// Not thread-safe; <see cref="PetAlertService"/> serializes access.
    /// </summary>
    public sealed class PetSourceMap
    {
        private const string FilePath = "pet-sources.user.json";

        private static readonly HashSet<string> NamePrefixes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Young", "Baby", "Elite", "Aberrant", "Mutated", "Modified", "Unstable", "Enraged",
            "Lesser", "Intermediate", "Superior", "Ultimate", "Male", "Female",
        };

        private readonly string path;
        private readonly Dictionary<int, HashSet<int>> learned;
        private readonly Dictionary<int, int[]> byName = new();
        private List<(int PetId, string Base)>? petBases;

        public PetSourceMap(string path = FilePath)
        {
            this.path = path;
            learned = Load(path);
        }

        public IReadOnlyCollection<int> PetsFor(int mobCode)
        {
            var named = ByName(mobCode);
            if (!learned.TryGetValue(mobCode, out var seen)) return named;
            return named.Concat(seen).Distinct().ToList();
        }

        public void Learn(int mobCode, int petId)
        {
            if (!learned.TryGetValue(mobCode, out var pets)) learned[mobCode] = pets = [];
            if (pets.Add(petId)) Save();
        }

        private int[] ByName(int mobCode)
        {
            if (byName.TryGetValue(mobCode, out var cached)) return cached;

            var data = GameDataProvider.Instance;
            int[] result = [];
            if (data.IsKnownMob(mobCode))
            {
                var mob = data.GetMobName(mobCode);
                petBases ??= data.Pets.All.Where(p => p.Farmable).Select(p => (p.Id, BaseName(p.Name))).ToList();
                result = petBases
                    .Where(p => mob.Equals(p.Base, StringComparison.OrdinalIgnoreCase)
                             || mob.EndsWith(" " + p.Base, StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.PetId)
                    .ToArray();
            }
            byName[mobCode] = result;
            return result;
        }

        private static string BaseName(string petName)
        {
            var words = petName.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            while (words.Count > 1 && NamePrefixes.Contains(words[0])) words.RemoveAt(0);
            return string.Join(' ', words);
        }

        private void Save()
        {
            try
            {
                var temp = path + ".tmp";
                var content = learned.ToDictionary(p => p.Key.ToString(), p => p.Value.Order().ToArray());
                File.WriteAllText(temp, JsonSerializer.Serialize(content, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temp, path, overwrite: true);
            }
            catch { }
        }

        private static Dictionary<int, HashSet<int>> Load(string path)
        {
            try
            {
                if (File.Exists(path) && JsonSerializer.Deserialize<Dictionary<string, int[]>>(File.ReadAllText(path)) is { } saved)
                {
                    return saved
                        .Where(p => int.TryParse(p.Key, out _))
                        .ToDictionary(p => int.Parse(p.Key), p => p.Value.ToHashSet());
                }
            }
            catch { }
            return [];
        }
    }
}
