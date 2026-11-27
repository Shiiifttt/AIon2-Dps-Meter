using AionDpsMeter.Services.Services.Entity;

namespace AionDpsMeter.Services.Services.Arena
{
    /// <summary>One player in the arena, as the overlay shows them.</summary>
    public sealed record ArenaPlayer(
        int EntityId,
        string Name,
        int CombatPower,
        long HpCurrent,
        long HpMax,
        ArenaSide Side,
        bool IsSelf,
        string? ClassIcon);

    public enum ArenaSide { Unknown, Ally, Enemy }

    /// <summary>
    /// Follows who is in the arena with you: names and CP from the player info packets (3336 you, 4536 the others),
    /// live HP from 008D, your team from the group roster (0092). Arena = map 61, or any map that sends an
    /// "..._Arena_..." map event (2E92). Everything is dropped on the next map change.
    /// </summary>
    public sealed class ArenaTracker(EntityTracker entityTracker)
    {
        // Only one arena map seen so far (Impetusium arena, 5v5). Others are learned from their map event.
        private readonly HashSet<int> _arenaMaps = [61];

        // An arena holds 10 players; a map with more than this many other players in view isn't one.
        private const int MaxPlayersOutsideArena = 30;

        private readonly object _lock = new();
        private readonly Dictionary<int, Entry> _players = [];
        private readonly HashSet<string> _roster = new(StringComparer.Ordinal);
        private uint _rosterGroupId;
        private int _mapId;
        private bool _mapIsArena;
        private bool _collecting = true;
        private int _version;

        private sealed class Entry
        {
            public required int EntityId;
            public required string Name;
            public int CombatPower;
            public long HpCurrent;
            public long HpMax;
            public bool IsSelf;
        }

        /// <summary>True while you're in an arena and at least one player is known.</summary>
        public bool IsActive
        {
            get { lock (_lock) return _mapIsArena && _players.Count > 0; }
        }

        /// <summary>Bumped on every change, so a UI can skip redraws when nothing moved.</summary>
        public int Version
        {
            get { lock (_lock) return _version; }
        }

        public int MapId
        {
            get { lock (_lock) return _mapId; }
        }

        /// <summary>Allies first (you on top), then enemies; each side by CP, highest first.</summary>
        public IReadOnlyList<ArenaPlayer> Snapshot()
        {
            List<ArenaPlayer> list;
            lock (_lock)
            {
                list = new List<ArenaPlayer>(_players.Count);
                foreach (var e in _players.Values)
                {
                    var side = e.IsSelf || _roster.Contains(e.Name) ? ArenaSide.Ally
                        : _roster.Count > 0 ? ArenaSide.Enemy
                        : ArenaSide.Unknown;
                    list.Add(new ArenaPlayer(e.EntityId, e.Name, e.CombatPower, e.HpCurrent, e.HpMax, side, e.IsSelf, null));
                }
            }

            // Class is only known once a player has used a class skill (damage packets).
            for (int i = 0; i < list.Count; i++)
            {
                var icon = entityTracker.GetPlayerEntity(list[i].EntityId)?.CharacterClass?.Icon;
                if (icon != null) list[i] = list[i] with { ClassIcon = icon };
            }

            return list
                .OrderBy(p => p.Side == ArenaSide.Enemy ? 1 : 0)
                .ThenByDescending(p => p.IsSelf)
                .ThenByDescending(p => p.CombatPower)
                .ThenBy(p => p.Name, StringComparer.Ordinal)
                .ToList();
        }

        internal void OnMapChanged(int mapId)
        {
            lock (_lock)
            {
                _mapId = mapId;
                _mapIsArena = _arenaMaps.Contains(mapId);
                _players.Clear();
                _collecting = true;
                _version++;
            }
        }

        /// <summary>Map event names look like "Impetusium_Arena_MapEvent_02".</summary>
        internal void OnArenaMapEvent()
        {
            lock (_lock)
            {
                if (_mapIsArena) return;
                _mapIsArena = true;
                if (_mapId != 0) _arenaMaps.Add(_mapId);
                _version++;
            }
        }

        internal void OnPlayerInfo(int entityId, string name, int combatPower, long hpCurrent, long hpMax, bool isSelf)
        {
            if (entityId <= 0 || string.IsNullOrEmpty(name)) return;
            lock (_lock)
            {
                if (!_collecting) return;

                if (!_players.TryGetValue(entityId, out var entry))
                {
                    if (!_mapIsArena && _players.Count >= MaxPlayersOutsideArena)
                    {
                        // Open world or a town: stop collecting until the next map change.
                        _players.Clear();
                        _collecting = false;
                        _version++;
                        return;
                    }

                    // Same name under a new id (left and came back into view): drop the old one.
                    foreach (var old in _players.Values.Where(p => p.Name == name).ToList())
                        _players.Remove(old.EntityId);

                    entry = new Entry { EntityId = entityId, Name = name };
                    _players[entityId] = entry;
                }

                entry.Name = name;
                entry.IsSelf |= isSelf;
                if (combatPower > 0) entry.CombatPower = combatPower;
                if (hpMax > 0)
                {
                    entry.HpMax = hpMax;
                    entry.HpCurrent = Math.Clamp(hpCurrent, 0, hpMax);
                }
                _version++;
            }
        }

        /// <summary>From 008D: current HP, max HP, or both.</summary>
        internal void OnHp(int entityId, long? hpCurrent, long? hpMax)
        {
            lock (_lock)
            {
                if (!_players.TryGetValue(entityId, out var entry)) return;
                if (hpMax is > 0) entry.HpMax = hpMax.Value;
                if (hpCurrent is >= 0) entry.HpCurrent = hpCurrent.Value;
                if (entry.HpMax > 0 && entry.HpCurrent > entry.HpMax) entry.HpMax = entry.HpCurrent;
                _version++;
            }
        }

        /// <summary>
        /// From 0092. A roster packet can list only some of the members, so names are collected
        /// for as long as the group id stays the same.
        /// </summary>
        internal void OnRoster(uint groupId, IReadOnlyList<string> memberNames)
        {
            if (memberNames.Count == 0) return;
            lock (_lock)
            {
                if (groupId != _rosterGroupId)
                {
                    _roster.Clear();
                    _rosterGroupId = groupId;
                }
                foreach (var name in memberNames) _roster.Add(name);
                _version++;
            }
        }
    }
}
