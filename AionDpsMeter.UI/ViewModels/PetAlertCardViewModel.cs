using AionDpsMeter.Services.Services.Pets;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AionDpsMeter.UI.ViewModels
{
    /// <summary>One card in the pet popup; fades out on its own.</summary>
    public sealed partial class PetAlertCardViewModel : ViewModelBase
    {
        private static readonly TimeSpan Hold = TimeSpan.FromSeconds(6);
        private static readonly TimeSpan Fade = TimeSpan.FromSeconds(1.5);

        private DateTime _shownAt;

        [ObservableProperty] private double _opacity = 1;
        [ObservableProperty] private string _title = string.Empty;
        [ObservableProperty] private IReadOnlyList<PetAlertLineViewModel> _lines = [];

        public PetAlertCardViewModel(PetAlert alert, DateTime now) => Update(alert, now);

        /// <summary>Cards with the same key replace each other instead of stacking.</summary>
        public string Key { get; private set; } = string.Empty;
        public PetAlertKind Kind { get; private set; }
        public string KindLabel => Kind == PetAlertKind.Nearby ? "NEARBY" : "PICKED UP";
        public string KindColor => Kind == PetAlertKind.Nearby ? "#79C0FF" : "#7EE787";
        public bool CountsUnknown { get; private set; }

        public void Update(PetAlert alert, DateTime now)
        {
            Kind = alert.Kind;
            Key = $"{alert.Kind}|{alert.Title}";
            Title = alert.Title;
            CountsUnknown = !alert.CountsKnown;
            Lines = alert.Pets.Select(p => new PetAlertLineViewModel(p, alert.CountsKnown)).ToList();
            _shownAt = now;
            Opacity = 1;
            OnPropertyChanged(nameof(KindLabel));
            OnPropertyChanged(nameof(KindColor));
            OnPropertyChanged(nameof(CountsUnknown));
        }

        /// <summary>Updates the fade; false once the card has faded out.</summary>
        public bool Tick(DateTime now)
        {
            var age = now - _shownAt;
            if (age <= Hold) { Opacity = 1; return true; }
            var t = (age - Hold) / Fade;
            Opacity = Math.Max(0, 1 - t);
            return t < 1;
        }
    }

    public sealed class PetAlertLineViewModel
    {
        public PetAlertLineViewModel(PetAlertLine line, bool countsKnown)
        {
            Name = line.Name;
            RankText = line.Rank == 0 ? "locked" : $"rank {line.Rank}";
            ProgressText = countsKnown ? $"{line.Souls}/{line.Goal}" : $"?/{line.Goal}";
            GainedText = line.Gained > 0 ? $"+{line.Gained}" : string.Empty;
            FillPercent = countsKnown && line.Goal > 0 ? Math.Clamp(100.0 * line.Souls / line.Goal, 0, 100) : 0;
        }

        public string Name { get; }
        public string RankText { get; }
        public string ProgressText { get; }
        public string GainedText { get; }
        /// <summary>0..100, for the progress bar.</summary>
        public double FillPercent { get; }
    }
}
