using AionDpsMeter.Core.Models;

namespace AionDpsMeter.Services.Services.Session
{
    /// <summary>
    /// The server only reports the stagger gauge itself, not how much each hit took off it.
    /// A gauge update arrives right after the hits that caused it, so each drop is split
    /// between the hits from the same batch. Break per skill is fixed (e.g. Smashing Blow 2,
    /// Overhead Slam 5, Ruinous Blow 25), so values are learned as the fight goes: when several
    /// skills land in one batch, known skills get their usual amount and unknown ones the rest.
    /// </summary>
    public sealed class BreakAttributor
    {
        // Hits this close before a gauge update count as having caused it.
        public static readonly TimeSpan BatchWindow = TimeSpan.FromMilliseconds(150);

        // Skill id -> break per cast of that skill in one batch.
        private readonly Dictionary<int, double> learnedBreak = new();

        public void Attribute(IReadOnlyList<PlayerDamage> batch, double drop)
        {
            if (batch.Count == 0 || drop <= 0) return;

            var casts = batch
                .GroupBy(h => (h.SourceEntity.Id, Skill: h.Skill.Id))
                .Select(g => (g.Key.Skill, Hits: g.ToList()))
                .ToList();

            if (casts.Count == 1)
            {
                learnedBreak[casts[0].Skill] = drop;
                Spread(casts[0].Hits, drop);
                return;
            }

            var known = casts.Where(c => learnedBreak.ContainsKey(c.Skill)).ToList();
            var unknown = casts.Where(c => !learnedBreak.ContainsKey(c.Skill)).ToList();
            double knownTotal = known.Sum(c => learnedBreak[c.Skill]);
            double remainder = drop - knownTotal;

            if (unknown.Count > 0 && remainder > RoundingTolerance)
            {
                // Known skills take their usual amount, the unknown ones split what's left.
                foreach (var c in known) Spread(c.Hits, learnedBreak[c.Skill]);
                foreach (var c in unknown) Spread(c.Hits, remainder / unknown.Count);
                if (unknown.Count == 1) learnedBreak[unknown[0].Skill] = remainder;
                return;
            }

            // Known skills account for the whole drop: the unknown ones did nothing.
            foreach (var c in unknown) learnedBreak[c.Skill] = 0;

            if (knownTotal > 0)
                foreach (var c in known) Spread(c.Hits, drop * learnedBreak[c.Skill] / knownTotal);
            else
                foreach (var c in casts) Spread(c.Hits, drop / casts.Count);
        }

        private const double RoundingTolerance = 0.5;

        // Called for hits that landed while the gauge was going down but caused no drop.
        public void LearnNoBreak(PlayerDamage hit)
        {
            learnedBreak.TryAdd(hit.Skill.Id, 0);
        }

        private static void Spread(List<PlayerDamage> hits, double amount)
        {
            foreach (var hit in hits) hit.Break += amount / hits.Count;
        }
    }
}
