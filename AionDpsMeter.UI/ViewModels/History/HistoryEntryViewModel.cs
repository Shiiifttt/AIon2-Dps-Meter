using AionDpsMeter.Services.Services.Session;

namespace AionDpsMeter.UI.ViewModels.History
{
    public sealed class HistoryEntryViewModel : ViewModelBase
    {
        public HistorySessionListItem Session { get; }

        private readonly PersonalBestComparison? _vsBest;
        private readonly KillTimeComparison? _killTime;

        public HistoryEntryViewModel(HistorySessionListItem session, PersonalBestComparison? vsBest = null, KillTimeComparison? killTime = null)
        {
            Session = session;
            _vsBest = vsBest;
            _killTime = killTime;
        }

        /// <summary>The fight with your best DPS on this boss, for side-by-side comparison.</summary>
        public Guid? BestSessionId => _vsBest?.BestSessionId;

        public bool IsClear => Session.IsClear;

        // A finished fight where the boss didn't die.
        private bool IsNonClear => IsCompleted && !Session.IsClear;

        private string RemainingHpText => Session.RemainingHpPercent is { } hp
            ? $"{(hp is > 0 and < 0.1 ? 0.1 : hp).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}%"
            : "?%";

        // Kill time against the fastest saved kill of this boss. Wipes show the boss HP left (red),
        // a fight still in progress its current HP.
        public string KillTimeDisplay => _killTime switch
        {
            { IsFastest: true } => "PB",
            { } k => $"+{FormatDelta(k.Delta)}",
            null when !IsCompleted && Session.RemainingHpPercent is not null => RemainingHpText,
            null when IsNonClear => RemainingHpText,
            null => string.Empty,
        };
        public string KillTimeColor => _killTime switch
        {
            { IsFastest: true } => "#FFD54F",
            { Delta.TotalSeconds: < 15 } => "#DCDCAA",
            { } => "#CE9178",
            null when IsNonClear => NonClearColor,
            null => "#888888",
        };
        public string KillTimeTooltip => _killTime switch
        {
            { IsFastest: true } k => $"Fastest kill of this boss ({DamageFormatter.FormatDuration(k.Duration)})",
            { } k => $"{FormatDelta(k.Delta)} slower than the fastest kill ({DamageFormatter.FormatDuration(k.Fastest)} on {k.FastestAt:dd.MM HH:mm})",
            null when !IsCompleted => "Fight in progress: boss HP now",
            null when IsNonClear && Session.RemainingHpPercent is not null => $"Not cleared: boss was left at {RemainingHpText} HP",
            null when IsNonClear => "Not cleared (the boss HP is unknown)",
            null => "Cleared",
        };

        public const string NonClearColor = "#F06A6A";
        // Wipes get their HP left in a red pill.
        public bool ShowNonClearPill => IsNonClear && _killTime is null;

        private static string FormatDelta(TimeSpan delta)
            => delta.TotalHours >= 1 ? delta.ToString(@"h\:mm\:ss") : $"{(int)delta.TotalMinutes}:{delta.Seconds:D2}";

        // Your DPS in this fight against your best on this boss (same character).
        public string VsBestDisplay => _vsBest switch
        {
            null => string.Empty,
            { IsBest: true } => "PB",
            var c => $"{c.DeltaPercent:+0.0;-0.0}%",
        };
        public string VsBestColor => _vsBest switch
        {
            null => "#888888",
            { IsBest: true } => "#FFD54F",
            { DeltaPercent: > -5 } => "#DCDCAA",
            _ => "#CE9178",
        };
        public string VsBestTooltip => _vsBest switch
        {
            null => "No personal best yet (fights under 30 s don't count)",
            { BestIsClear: true } c => c.IsBest
                ? $"{c.CharacterName}: personal best, {DamageFormatter.Format(c.Dps)}/s on a clear"
                : $"{c.CharacterName}: {DamageFormatter.Format(c.Dps)}/s here, best clear on this boss {DamageFormatter.Format(c.BestDps)}/s",
            var c => (c.IsBest
                    ? $"{c.CharacterName}: no clear yet, this is your best attempt"
                    : $"{c.CharacterName}: {DamageFormatter.Format(c.Dps)}/s here; no clear yet, best attempt {DamageFormatter.Format(c.BestDps)}/s")
                + (c.BestRemainingHpPercent is { } hp ? $" (boss left at {hp.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}%)" : string.Empty),
        };

        public Guid SessionId => Session.SessionId;

        public string TargetName    => string.IsNullOrEmpty(Session.TargetName)
            ? $"Mob #{Session.TargetId}"
            : Session.TargetName;

        public string DateDisplay   => Session.SessionEnd.ToString("dd.MM  HH:mm:ss");
        public string Duration      => DamageFormatter.FormatDuration(Session.Duration);
        public bool   IsCompleted   => Session.State == SessionState.Completed;
        public string StateDisplay  => IsCompleted ? "?" : "?";
        public string StateColor    => IsCompleted ? "#888888" : "#4EC9B0";
        public int PlayerCount => Session.PlayerCount;

        public string TotalDamageDisplay => DamageFormatter.Format(Session.TotalDamage);
    }
}
