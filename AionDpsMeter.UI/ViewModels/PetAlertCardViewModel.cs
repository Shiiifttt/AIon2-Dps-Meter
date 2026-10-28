using AionDpsMeter.Services.Services.Pets;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AionDpsMeter.UI.ViewModels
{
    /// <summary>One card in the pet popup; fades out on its own.</summary>
    public sealed partial class PetAlertCardViewModel : ViewModelBase
    {
        private static readonly TimeSpan Hold = TimeSpan.FromSeconds(6);
        private static readonly TimeSpan CompletedHold = TimeSpan.FromSeconds(12);
        private static readonly TimeSpan Fade = TimeSpan.FromSeconds(1.5);

        private DateTime _shownAt;

        [ObservableProperty] private double _opacity = 1;
        [ObservableProperty] private string _title = string.Empty;
        [ObservableProperty] private string _subtitle = string.Empty;
        [ObservableProperty] private IReadOnlyList<PetAlertLineViewModel> _lines = [];

        public PetAlertCardViewModel(PetAlert alert, DateTime now) => Update(alert, now);

        public static string KeyFor(PetAlertKind kind, string title) => $"{kind}|{title}";

        /// <summary>Cards with the same key replace each other instead of stacking.</summary>
        public string Key { get; private set; } = string.Empty;
        public PetAlertKind Kind { get; private set; }
        public bool IsCompleted => Kind == PetAlertKind.Completed;
        public string KindLabel => Kind switch
        {
            PetAlertKind.Nearby => "NEARBY",
            PetAlertKind.Completed => "PET COMPLETE",
            PetAlertKind.Synced => "COLLECTION SYNCED",
            _ => "PICKED UP",
        };
        public string KindColor => Kind switch
        {
            PetAlertKind.Nearby => "#79C0FF",
            PetAlertKind.Completed => "#FFD54F",
            PetAlertKind.Synced => "#B388FF",
            _ => "#7EE787",
        };
        public string BorderColor => IsCompleted ? "#FFD54F" : "#30363D";
        public bool CountsUnknown { get; private set; }
        private TimeSpan HoldTime => IsCompleted ? CompletedHold : Hold;

        public void Update(PetAlert alert, DateTime now)
        {
            Kind = alert.Kind;
            Key = KeyFor(alert.Kind, alert.Title);
            Title = alert.Title;
            CountsUnknown = !alert.CountsKnown && alert.Kind is PetAlertKind.Nearby or PetAlertKind.Gained;
            Lines = alert.Pets.Select(p => new PetAlertLineViewModel(p, alert.CountsKnown)).ToList();
            Subtitle = alert.Kind switch
            {
                PetAlertKind.Nearby => InRangeText(alert.InRange),
                PetAlertKind.Completed => "Max rank reached",
                PetAlertKind.Synced => alert.Note ?? string.Empty,
                _ => string.Empty,
            };
            _shownAt = now;
            Opacity = 1;
            OnPropertyChanged(nameof(KindLabel));
            OnPropertyChanged(nameof(KindColor));
            OnPropertyChanged(nameof(BorderColor));
            OnPropertyChanged(nameof(IsCompleted));
            OnPropertyChanged(nameof(CountsUnknown));
        }

        /// <summary>Nearby cards: how many are in range now; when none are left the card fades right away.</summary>
        public void SetInRange(int count, DateTime now)
        {
            Subtitle = InRangeText(count);
            if (count == 0 && now - _shownAt < HoldTime) _shownAt = now - HoldTime;
        }

        // The game reports monsters from about 150 m away, through walls and floors, and only while they stay in range.
        private static string InRangeText(int count) => count switch
        {
            0 => "left range or died",
            1 => "1 within ~150 m",
            _ => $"{count} within ~150 m",
        };

        /// <summary>Updates the fade; false once the card has faded out.</summary>
        public bool Tick(DateTime now)
        {
            var age = now - _shownAt;
            var hold = HoldTime;
            if (age <= hold) { Opacity = 1; return true; }
            var t = (age - hold) / Fade;
            Opacity = Math.Max(0, 1 - t);
            return t < 1;
        }
    }

    public sealed class PetAlertLineViewModel
    {
        public PetAlertLineViewModel(PetAlertLine line, bool countsKnown)
        {
            Name = line.Name;
            bool maxed = line.Rank >= PetCollectionTracker.MaxRank;
            IconPath = $"/AionDpsMeter.UI;component/Assets/Pets/{line.PetId}.png";
            RankText = maxed ? "" : line.Rank == 0 ? "locked" : $"rank {line.Rank}";
            ProgressText = maxed ? "MAX" : countsKnown ? $"{line.Souls}/{line.Goal}" : $"?/{line.Goal}";
            GainedText = line.Gained > 0 ? $"+{line.Gained}" : string.Empty;
            GuessText = line.Guess ? "  guess" : string.Empty;
            GuessTooltip = line.Guess ? "Matched by the monster's name; not yet seen dropping from it. Confirmed once you get its souls from this monster." : null;
            FillPercent = maxed ? 100 : countsKnown && line.Goal > 0 ? Math.Clamp(100.0 * line.Souls / line.Goal, 0, 100) : 0;
        }

        public string Name { get; }
        /// <summary>Built-in pet picture (Assets/Pets/<id>.png); nothing shows if there isn't one.</summary>
        public string IconPath { get; }
        public string RankText { get; }
        public string ProgressText { get; }
        public string GainedText { get; }
        public string GuessText { get; }
        public string? GuessTooltip { get; }
        /// <summary>0..100, for the progress bar.</summary>
        public double FillPercent { get; }
    }
}
