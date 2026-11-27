using AionDpsMeter.Services.Services.Arena;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;

namespace AionDpsMeter.UI.ViewModels
{
    /// <summary>The arena overlay: your team on top, the other team below, each with CP and live HP.</summary>
    public sealed partial class ArenaOverlayViewModel : ViewModelBase
    {
        [ObservableProperty] private bool _isActive;
        [ObservableProperty] private bool _hasTeams;
        [ObservableProperty] private string _allyHeader = "ALLIES";
        [ObservableProperty] private string _enemyHeader = "ENEMIES";
        [ObservableProperty] private string _allyAverageCp = string.Empty;
        [ObservableProperty] private string _enemyAverageCp = string.Empty;

        public ObservableCollection<ArenaRowViewModel> Allies { get; } = [];
        public ObservableCollection<ArenaRowViewModel> Enemies { get; } = [];

        public void Update(bool active, IReadOnlyList<ArenaPlayer> players)
        {
            IsActive = active && players.Count > 0;
            if (!IsActive)
            {
                Allies.Clear();
                Enemies.Clear();
                return;
            }

            // Without a team roster every player lands in the top list.
            HasTeams = players.Any(p => p.Side == ArenaSide.Enemy);
            AllyHeader = HasTeams ? "ALLIES" : "PLAYERS";

            var allies = players.Where(p => p.Side != ArenaSide.Enemy).ToList();
            var enemies = players.Where(p => p.Side == ArenaSide.Enemy).ToList();
            Sync(Allies, allies);
            Sync(Enemies, enemies);
            AllyAverageCp = AverageCp(allies);
            EnemyAverageCp = AverageCp(enemies);
        }

        private static string AverageCp(List<ArenaPlayer> players)
        {
            var known = players.Where(p => p.CombatPower > 0).ToList();
            return known.Count == 0 ? string.Empty : "avg " + ArenaRowViewModel.FormatCp((int)known.Average(p => p.CombatPower));
        }

        // Rows are updated in place so the list doesn't flicker; rebuilt only when who's in it changes.
        private static void Sync(ObservableCollection<ArenaRowViewModel> rows, List<ArenaPlayer> players)
        {
            bool sameIds = rows.Count == players.Count;
            for (int i = 0; sameIds && i < players.Count; i++)
                sameIds = rows[i].EntityId == players[i].EntityId;

            if (!sameIds)
            {
                rows.Clear();
                foreach (var p in players) rows.Add(new ArenaRowViewModel(p.EntityId));
            }

            for (int i = 0; i < players.Count; i++) rows[i].Update(players[i]);
        }
    }

    public sealed partial class ArenaRowViewModel(int entityId) : ViewModelBase
    {
        public int EntityId { get; } = entityId;

        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private string _cpText = string.Empty;
        [ObservableProperty] private string _hpText = string.Empty;
        [ObservableProperty] private double _hpFraction;
        [ObservableProperty] private bool _isSelf;
        [ObservableProperty] private bool _isDead;
        [ObservableProperty] private bool _isEnemy;
        [ObservableProperty] private string? _classIcon;

        public void Update(ArenaPlayer p)
        {
            Name = p.Name;
            IsSelf = p.IsSelf;
            IsEnemy = p.Side == ArenaSide.Enemy;
            ClassIcon = p.ClassIcon;
            CpText = p.CombatPower > 0 ? FormatCp(p.CombatPower) : "?";

            if (p.HpMax <= 0)
            {
                HpFraction = 0;
                IsDead = false;
                HpText = "?";
                return;
            }

            IsDead = p.HpCurrent <= 0;
            HpFraction = Math.Clamp((double)p.HpCurrent / p.HpMax, 0, 1);
            HpText = IsDead ? "DEAD" : FormatHp(p.HpCurrent);
        }

        internal static string FormatCp(int cp) => cp.ToString("N0", CultureInfo.InvariantCulture);

        private static string FormatHp(long hp) => hp >= 10_000
            ? (hp / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "k"
            : hp.ToString(CultureInfo.InvariantCulture);
    }
}
