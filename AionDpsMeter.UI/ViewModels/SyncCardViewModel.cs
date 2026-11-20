using AionDpsMeter.Services.Services.Sync;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AionDpsMeter.UI.ViewModels
{
    /// <summary>The sync popup's one card. A new notice replaces it; it fades once its hold time is up.</summary>
    public sealed partial class SyncCardViewModel : ViewModelBase
    {
        private static readonly TimeSpan Fade = TimeSpan.FromSeconds(1.5);

        private DateTime _shownAt;
        private TimeSpan? _hold;

        [ObservableProperty] private bool _isShown;
        [ObservableProperty] private double _opacity = 1;
        [ObservableProperty] private string _kindLabel = string.Empty;
        [ObservableProperty] private string _kindColor = "#7EE787";
        [ObservableProperty] private string _borderColor = "#30363D";
        [ObservableProperty] private string _title = string.Empty;
        [ObservableProperty] private string _detail = string.Empty;
        [ObservableProperty] private IReadOnlyList<string> _hints = [];

        public void Show(SyncNotice notice, DateTime now)
        {
            (KindLabel, KindColor) = notice.Kind switch
            {
                SyncNoticeKind.Synced => ("SYNCED", "#7EE787"),
                SyncNoticeKind.SyncedUnknownUser => ("SYNCED", "#E3B341"),
                SyncNoticeKind.NoGameTraffic => ("NOT SYNCED", "#FF7B72"),
                SyncNoticeKind.Lost => ("SYNC LOST", "#FF7B72"),
                _ => ("METER", "#79C0FF"),
            };
            BorderColor = notice.Kind is SyncNoticeKind.NoGameTraffic or SyncNoticeKind.Lost ? "#FF7B72" : "#30363D";
            Title = notice.Title;
            Detail = notice.Detail;
            Hints = notice.Hints;
            _hold = notice.Hold;
            _shownAt = now;
            Opacity = 1;
            IsShown = true;
        }

        /// <summary>Updates the fade; false once the card is gone (or stays up for good).</summary>
        public bool Tick(DateTime now)
        {
            if (!IsShown || _hold is not { } hold) return false;
            var age = now - _shownAt;
            if (age <= hold) { Opacity = 1; return true; }
            var t = (age - hold) / Fade;
            Opacity = Math.Max(0, 1 - t);
            if (t < 1) return true;
            IsShown = false;
            return false;
        }
    }
}
