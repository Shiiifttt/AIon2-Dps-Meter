using AionDpsMeter.Core.Windowing;
using AionDpsMeter.Services.Services.Arena;
using AionDpsMeter.UI.Services.Windowing;
using AionDpsMeter.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace AionDpsMeter.UI.Views
{
    /// <summary>
    /// Arena overlay: all players in the arena with CP and HP. Empty (and click-through) outside an arena,
    /// draggable while Settings is open. Stays up while the meter is in the tray.
    /// </summary>
    public partial class ArenaOverlayWindow : Window
    {
        private readonly ArenaOverlayViewModel _viewModel = new();
        private readonly ArenaTracker _tracker;
        private readonly IWindowManagerService _windowManager;
        private readonly DispatcherTimer _refreshTimer;
        private int _shownVersion = -1;
        private int _ticksSinceRefresh;

        public ArenaOverlayWindow()
        {
            InitializeComponent();
            DataContext = _viewModel;

            // Until it has been moved: left edge of the main screen, a bit above the middle.
            var wa = SystemParameters.WorkArea;
            Left = wa.Left + 12;
            Top = wa.Top + wa.Height * 0.25;

            _tracker = App.AppHost.Services.GetRequiredService<ArenaTracker>();
            _windowManager = App.AppHost.Services.GetRequiredService<IWindowManagerService>();

            _refreshTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(200) };
            _refreshTimer.Tick += (_, _) => Refresh();
            _refreshTimer.Start();
            Closed += (_, _) => _refreshTimer.Stop();
        }

        public void SetEditMode(bool editing)
        {
            EditHint.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Refresh()
        {
            // Class icons come from the entity tracker, which has no version, so redraw once a second anyway.
            int version = _tracker.Version;
            if (version == _shownVersion && ++_ticksSinceRefresh < 5) return;
            _shownVersion = version;
            _ticksSinceRefresh = 0;

            bool active = _tracker.IsActive;
            _viewModel.Update(active, active ? _tracker.Snapshot() : []);
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && EditHint.Visibility == Visibility.Visible)
                _windowManager.Drag(WindowKey.ArenaOverlay);
        }
    }
}
