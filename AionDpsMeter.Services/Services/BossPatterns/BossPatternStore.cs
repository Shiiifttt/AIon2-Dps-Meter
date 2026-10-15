using System.Text.Json;
using System.Text.Json.Serialization;

namespace AionDpsMeter.Services.Services.BossPatterns
{
    /// <summary>Timings seen for one boss skill, in ms. Only skills with a cast bar are recorded.</summary>
    public sealed class BossSkillSamples
    {
        [JsonPropertyName("castMs")]
        public int CastMs { get; set; }

        // From the pull to the first cast, first engagement of each boss only.
        [JsonPropertyName("firstCastMs")]
        public List<int> FirstCastMs { get; set; } = [];

        // Between consecutive casts in one engagement.
        [JsonPropertyName("gapMs")]
        public List<int> GapMs { get; set; } = [];
    }

    /// <summary>
    /// Boss skill timings by mob code and skill code. Defaults ship with the app (built from packet
    /// logs) and are never written; what this PC learns goes to its own file, and both are used together.
    /// Not thread-safe; <see cref="BossPatternTracker"/> serializes access.
    /// </summary>
    public sealed class BossPatternStore
    {
        private const int MaxSamples = 40;
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly string learnedPath;
        private readonly Dictionary<int, Dictionary<int, BossSkillSamples>> defaults;
        private readonly Dictionary<int, Dictionary<int, BossSkillSamples>> learned;
        private bool dirty;

        public static string DefaultsPath => Path.Combine(AppContext.BaseDirectory, "GameData", "Assets", "boss_patterns.json");

        public BossPatternStore(string learnedPath, string? defaultsPath)
        {
            this.learnedPath = learnedPath;
            defaults = defaultsPath is null ? [] : Load(defaultsPath);
            learned = Load(learnedPath);
        }

        public IEnumerable<int> SkillsFor(int mobCode)
            => (defaults.GetValueOrDefault(mobCode)?.Keys ?? Enumerable.Empty<int>())
                .Union(learned.GetValueOrDefault(mobCode)?.Keys ?? Enumerable.Empty<int>());

        public void AddCast(int mobCode, int skillCode, int castMs)
        {
            var s = Learned(mobCode, skillCode);
            if (s.CastMs != castMs) { s.CastMs = castMs; dirty = true; }
        }

        public void AddFirstCast(int mobCode, int skillCode, TimeSpan sincePull) => Add(Learned(mobCode, skillCode).FirstCastMs, sincePull);

        public void AddGap(int mobCode, int skillCode, TimeSpan gap) => Add(Learned(mobCode, skillCode).GapMs, gap);

        /// <summary>
        /// Earliest the boss has opened with the skill, and its cooldown: the shortest gap between casts,
        /// ignoring gaps under half the median (a skill that's usually 40 s apart but once came 10 s
        /// later would otherwise look ready almost all the time).
        /// </summary>
        public (TimeSpan? FirstCast, TimeSpan? Cooldown) Estimate(int mobCode, int skillCode)
        {
            var first = Samples(mobCode, skillCode, s => s.FirstCastMs).DefaultIfEmpty(-1).Min();

            var gaps = Samples(mobCode, skillCode, s => s.GapMs).Order().ToList();
            int? cooldown = null;
            if (gaps.Count > 0)
            {
                var median = gaps[(gaps.Count - 1) / 2];
                cooldown = gaps.First(g => g >= median / 2);
            }

            return (first >= 0 ? TimeSpan.FromMilliseconds(first) : null,
                    cooldown is { } c ? TimeSpan.FromMilliseconds(c) : null);
        }

        public void Save()
        {
            if (!dirty) return;
            try
            {
                File.WriteAllText(learnedPath, JsonSerializer.Serialize(learned, JsonOptions));
                dirty = false;
            }
            catch { }
        }

        private IEnumerable<int> Samples(int mobCode, int skillCode, Func<BossSkillSamples, List<int>> pick)
        {
            IEnumerable<int> result = [];
            if (defaults.GetValueOrDefault(mobCode)?.GetValueOrDefault(skillCode) is { } d) result = result.Concat(pick(d));
            if (learned.GetValueOrDefault(mobCode)?.GetValueOrDefault(skillCode) is { } l) result = result.Concat(pick(l));
            return result;
        }

        private BossSkillSamples Learned(int mobCode, int skillCode)
        {
            if (!learned.TryGetValue(mobCode, out var skills)) learned[mobCode] = skills = [];
            if (!skills.TryGetValue(skillCode, out var samples)) skills[skillCode] = samples = new BossSkillSamples();
            return samples;
        }

        private void Add(List<int> list, TimeSpan value)
        {
            list.Add((int)value.TotalMilliseconds);
            if (list.Count > MaxSamples) list.RemoveAt(0);
            dirty = true;
        }

        private static Dictionary<int, Dictionary<int, BossSkillSamples>> Load(string path)
        {
            try
            {
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<Dictionary<int, Dictionary<int, BossSkillSamples>>>(File.ReadAllText(path)) ?? [];
            }
            catch { }
            return [];
        }
    }
}
