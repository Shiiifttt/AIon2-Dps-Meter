using AionDpsMeter.Services.Services.Session;

namespace AionDpsMeter.UI.ViewModels.History
{
    public sealed class HistoryEntryViewModel : ViewModelBase
    {
        public HistorySessionListItem Session { get; }

        private readonly PersonalBestComparison? _vsBest;

        public HistoryEntryViewModel(HistorySessionListItem session, PersonalBestComparison? vsBest = null)
        {
            Session = session;
            _vsBest = vsBest;
        }

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
