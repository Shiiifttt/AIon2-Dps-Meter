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

        // Kill time against the fastest saved kill of this boss; empty for wipes and unfinished fights.
        public string KillTimeDisplay => _killTime switch
        {
            null => string.Empty,
            { IsFastest: true } => "PB",
            var k => $"+{FormatDelta(k.Delta)}",
        };
        public string KillTimeColor => _killTime switch
        {
            null => "#888888",
            { IsFastest: true } => "#FFD54F",
            { Delta.TotalSeconds: < 15 } => "#DCDCAA",
            _ => "#CE9178",
        };
        public string KillTimeTooltip => _killTime is { } k
            ? (k.IsFastest
                ? $"Fastest kill of this boss ({DamageFormatter.FormatDuration(k.Duration)})"
                : $"{FormatDelta(k.Delta)} slower than the fastest kill ({DamageFormatter.FormatDuration(k.Fastest)} on {k.FastestAt:dd.MM HH:mm})")
            : "Not a kill (wipe, reset or unfinished), or the boss HP is unknown";

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
        public string VsBestTooltip => _vsBest is { } c
            ? $"{c.CharacterName}: {DamageFormatter.Format(c.Dps)}/s here, best on this boss {DamageFormatter.Format(c.BestDps)}/s"
            : "No personal best yet (fights under 30 s don't count)";

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
