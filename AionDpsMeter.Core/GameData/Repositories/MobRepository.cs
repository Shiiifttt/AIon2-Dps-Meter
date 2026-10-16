using System.Collections.Frozen;
using System.Text.Json;
using AionDpsMeter.Core.GameData.Contracts;

namespace AionDpsMeter.Core.GameData.Repositories
{
   
    public sealed class MobRepository
    {
        private FrozenDictionary<int, MobData> mobsById = FrozenDictionary<int, MobData>.Empty;

        /// <summary>Names of training dummies, for matching saved fights (history keeps only the name).</summary>
        public FrozenSet<string> DummyNames { get; private set; } = FrozenSet<string>.Empty;

        public void Load(string path)
        {
            if (!File.Exists(path))
                return; 

            var json = File.ReadAllText(path);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var raw = JsonSerializer.Deserialize<Dictionary<int, MobData>>(json, options);
            if (raw is not null)
            {
                mobsById = raw.ToFrozenDictionary();
                DummyNames = raw.Values.Where(m => m.isDummy).Select(m => m.Name).ToFrozenSet();
            }
        }

        public bool IsKnownMob(int mobId) => mobsById.ContainsKey(mobId);

        public string GetName(int mobId) =>
            mobsById.TryGetValue(mobId, out var mob) ? mob.Name : $"Unknown ({mobId})";

        public bool IsBoss(int mobId) =>
            mobsById.TryGetValue(mobId, out var mob) && mob.IsBoss;

        public bool IsDummy(int mobId) =>
          mobsById.TryGetValue(mobId, out var mob) && mob.isDummy;
    }
}
