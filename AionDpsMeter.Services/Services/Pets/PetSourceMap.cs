using AionDpsMeter.Core.GameData.Services;
using System.Text.Json;

namespace AionDpsMeter.Services.Services.Pets
{
    /// <summary>
    /// Which pets' souls a monster can drop, by mob code. The game doesn't send this, so it comes from:
    /// - The table: GameData/Assets/pet_sources.json (pet id -> mob codes), built from metabot.gg's pet pages,
    ///   which list every source monster with the same mob codes the spawn packet carries.
    /// - Learned (confirmed): for monsters the table lacks, a soul gain right after exactly one kind of
    ///   monster died is saved here. Checked: the first 6 learned in play all matched the table.
    ///   Variants don't follow the names: "Yellow-Maned Zaif" gave Aberrant Zaif, "Baby Horn Ursus" gave
    ///   Young Ursus, and both Kailin monsters gave Kailin and Young Kailin.
    /// - Names (a guess, only without the table): a pet whose full name ends the monster's name ("Ellun River
    ///   Kailin" -> Kailin). Prefixes aren't stripped any more: that listed Aberrant / Mutated / Elite
    ///   variants for every plain monster of the family, which those monsters may not drop.
    /// Not thread-safe; <see cref="PetAlertService"/> serializes access.
    /// </summary>
    public sealed class PetSourceMap
    {
        private const string FilePath = "pet-sources.user.json";

        private readonly string path;
        private readonly Dictionary<int, HashSet<int>> learned;
        private readonly Dictionary<int, int[]> byName = new();
        private List<(int PetId, string Name)>? farmablePets;

        public PetSourceMap(string path = FilePath)
        {
            this.path = path;
            learned = Load(path);
        }

        /// <summary>
        /// Pets this monster drops souls for. Confirmed = from the monster -> pet table (pet_sources.json)
        /// or seen from your own kills; otherwise matched by name, only when the table is missing.
        /// </summary>
        public IReadOnlyList<(int PetId, bool Confirmed)> PetsFor(int mobCode)
        {
            var pets = GameDataProvider.Instance.Pets;
            // The table is authoritative for the monsters it lists (each drops souls for exactly one pet).
            var fromTable = pets.PetsDroppedBy(mobCode);
            if (fromTable.Count > 0) return fromTable.Select(p => (p, true)).ToList();

            var confirmed = (learned.TryGetValue(mobCode, out var s) ? s : []).ToList();
            var guessed = pets.HasSources ? [] : ByName(mobCode).Where(p => !confirmed.Contains(p));
            return confirmed.Select(p => (p, true)).Concat(guessed.Select(p => (p, false))).ToList();
        }

        /// <summary>
        /// Remembers a soul gain right after this kind of monster died. Only for monsters the table doesn't list
        /// (e.g. new ones after a patch): a mixed pull can credit the wrong monster, and the table is checked.
        /// </summary>
        public void Learn(int mobCode, int petId)
        {
            if (GameDataProvider.Instance.Pets.PetsDroppedBy(mobCode).Count > 0) return;
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
                farmablePets ??= data.Pets.All.Where(p => p.Farmable).Select(p => (p.Id, p.Name)).ToList();
                result = farmablePets
                    .Where(p => mob.Equals(p.Name, StringComparison.OrdinalIgnoreCase)
                             || mob.EndsWith(" " + p.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.PetId)
                    .ToArray();
            }
            byName[mobCode] = result;
            return result;
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
