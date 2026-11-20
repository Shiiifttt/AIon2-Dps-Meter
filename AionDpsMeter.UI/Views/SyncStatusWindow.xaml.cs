using AionDpsMeter.Core.Windowing;
using AionDpsMeter.Services.Services.Sync;
using AionDpsMeter.UI.Services.Windowing;
using AionDpsMeter.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace AionDpsMeter.UI.Views
{
    /// <summary>
    /// Sync popup: one card saying whether the meter follows the game and who you are, or what to do
    /// when it doesn't. Stays up while the meter is in the tray. Click-through except while Settings is open.
    /// </summary>
    public partial class SyncStatusWindow : Window
    {
        private readonly SyncCardViewModel _card = new();
        private readonly DispatcherTimer _fadeTimer;
        private readonly MeterSyncMonitor _monitor;
        private readonly IWindowManagerService _windowManager;

        public SyncStatusWindow()
        {
            InitializeComponent();
            Card.DataContext = _card;

            // Until it has been moved: top middle of the main screen, above where the pet popup starts.
            var wa = SystemParameters.WorkArea;
            Left = wa.Left + (wa.Width - Width) / 2;
            Top = wa.Top + wa.Height * 0.03;

            _fadeTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(50) };
            _fadeTimer.Tick += (_, _) => { if (!_card.Tick(DateTime.UtcNow)) _fadeTimer.Stop(); };

            _monitor = App.AppHost.Services.GetRequiredService<MeterSyncMonitor>();
            _windowManager = App.AppHost.Services.GetRequiredService<IWindowManagerService>();
            _monitor.Notice += OnNotice;
            Closed += (_, _) =>
            {
                _monitor.Notice -= OnNotice;
                _fadeTimer.Stop();
            };

            // A notice may have come before the window existed (fast lock at startup, or the setting
            // was switched on later): show where things stand.
            if (_monitor.Current is { } current) OnNotice(current);
        }

        public void SetEditMode(bool editing)
        {
            EditHint.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        }

        // Raised on capture / packet / timer threads.
        private void OnNotice(SyncNotice notice)
        {
            Dispatcher.BeginInvoke(() =>
            {
                _card.Show(notice, DateTime.UtcNow);
                _fadeTimer.Start();
            });
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && EditHint.Visibility == Visibility.Visible)
                _windowManager.Drag(WindowKey.SyncStatus);
        }
    }
}
