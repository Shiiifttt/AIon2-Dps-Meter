using AionDpsMeter.Core.Models;
using AionDpsMeter.Services.Models;
using AionDpsMeter.Services.Services.Settings;
using AionDpsMeter.UI.Utils;

namespace AionDpsMeter.UI.ViewModels.History
{
    public sealed class HistoryPlayerViewModel : ViewModelBase
    {
        private readonly PlayerStats _stats;
        private readonly double? _bestUptimeForClass;
        private readonly double _partyBreak;
        private readonly IAppSettingsService _settingsService;

        public long    PlayerId              => _stats.PlayerId;
        public string  PlayerName            => _stats.PlayerName;
        public string  ServerName            => _stats.ServerName;
        public string? PlayerIcon            => _stats.PlayerIcon;
        public bool    HasPlayerIcon         => !string.IsNullOrEmpty(_stats.PlayerIcon);
        public string  ClassName             => _stats.ClassName;
        public string? ClassIcon             => _stats.ClassIcon;
        public bool    HasClassIcon          => !string.IsNullOrEmpty(_stats.ClassIcon);
        public bool    IsUser                => _stats.IsUser;
        public string     CombatPower           => DamageFormatter.Format(_stats.CombatPower);

        public string PlayerNameDisplay
        {
            get
            {
                string name = _settingsService.IsNicknameHidden
                    ? NicknameObfuscator.Mask(_stats.PlayerName)
                    : _stats.PlayerName;

                return string.IsNullOrEmpty(_stats.ServerName)
                    ? name
                    : $"{name}[{_stats.ServerName}]";
            }
        }

        public string  TotalDamageDisplay    => DamageFormatter.FormatFull(_stats.TotalDamage);
        public long    TotalDamage           => _stats.TotalDamage;
        public string  DpsDisplay            => DamageFormatter.Format(_stats.DamagePerSecond);
        public double  DamagePercentage      => _stats.DamagePercentage;
        public int     PlayerDeaths          => _stats.PlayerDeaths;
        public bool    ShowPlayerDeaths      => _settingsService.ShowPlayerDeaths;
        public bool    IsDeathDisplayVisible => ShowPlayerDeaths && PlayerDeaths > 0;
        public string  PlayerDeathsDisplay   => $"💀 {PlayerDeaths}";
        public string  DamagePercentDisplay  => DamageFormatter.FormatRate(_stats.DamagePercentage);
        public string  CritRateDisplay       => DamageFormatter.FormatRate(_stats.CriticalRate);
        public string  BackAttackRateDisplay => DamageFormatter.FormatRate(_stats.BackAttackRate);
        public string FrontAttackRateDisplay => DamageFormatter.FormatRate(_stats.FrontAttackRate);
        public string  PerfectRateDisplay    => DamageFormatter.FormatRate(_stats.PerfectRate);
        public string  DoubleDamageRateDisplay   => DamageFormatter.FormatRate(_stats.DoubleDamageRate);
        public string  ParryRateDisplay      => DamageFormatter.FormatRate(_stats.ParryRate);
        public string  TotalBreakDisplay     => _stats.TotalBreak.ToString("0.#");
        public string  BreakShareDisplay     => _partyBreak > 0 ? DamageFormatter.FormatRate(_stats.TotalBreak / _partyBreak * 100) : string.Empty;
        public string  UptimeDisplay         => UptimeFormatter.Display(_stats);
        public string  UptimeTooltip         => $"{UptimeFormatter.Tooltip(_stats, _bestUptimeForClass)}{Environment.NewLine}In combat {DurationDisplay}";
        public string  IdleDisplay           => _stats.UptimePercentage is null ? string.Empty : $"idle {UptimeFormatter.FormatIdle(_stats.IdleSeconds)}";
        public string  DurationDisplay       => DamageFormatter.FormatDuration(_stats.CombatDuration);
        public int     HitCount              => _stats.HitCount;

        public IReadOnlyList<SkillStatsViewModel>  Skills { get; }
        public IReadOnlyList<BuffStatsViewModel>   Buffs  { get; }

        /// <summary>Raw hits (newest first) for combat log and graph in snapshot details window.</summary>
        public IReadOnlyList<PlayerDamage> RawHits       { get; }

        /// <summary>Raw buff events for graph timeline in snapshot details window.</summary>
        public IReadOnlyList<BuffEvent>    RawBuffEvents { get; }

        public HistoryPlayerViewModel(
            PlayerStats stats,
            IReadOnlyCollection<SkillStats> skills,
            IReadOnlyCollection<BuffStats> buffs,
            IReadOnlyList<PlayerDamage> rawHits,
            IReadOnlyList<BuffEvent> rawBuffEvents,
            IAppSettingsService settingsService,
            double? bestUptimeForClass = null,
            double partyBreak = 0)
        {
            _stats           = stats;
            _bestUptimeForClass = bestUptimeForClass;
            _partyBreak      = partyBreak;
            _settingsService = settingsService;
            RawHits          = rawHits;
            RawBuffEvents    = rawBuffEvents;
            Skills = skills
                .OrderByDescending(s => s.TotalDamage)
                .Select(s => new SkillStatsViewModel(s))
                .ToList();
            Buffs = buffs
                .OrderByDescending(b => b.ApplicationCount)
                .Select(b => new BuffStatsViewModel(b))
                .ToList();
        }
    }
}
