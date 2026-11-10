using AionDpsMeter.Services.Services.Settings;
using AionDpsMeter.UI.Services.Windowing;
using AionDpsMeter.UI.ViewModels;
using AionDpsMeter.UI.ViewModels.History;
using System.Windows;
using System.Windows.Input;

namespace AionDpsMeter.UI
{
    public partial class HistorySessionWindow : Window
    {
        private readonly IAppSettingsService _settingsService;
        private HoverPreview? _preview;

        public HistorySessionWindow(IAppSettingsService settingsService)
        {
            InitializeComponent();
            _settingsService = settingsService;
            DataContextChanged += (_, _) => FitHeightToPlayers();
        }

        // Rough per-row height, only used if measuring fails.
        private const double FallbackChromeHeight = 120;
        private const double FallbackRowHeight = 44;

        /// <summary>
        /// Tall enough for every player (the list doesn't scroll), so a full raid fits and a duo isn't
        /// mostly empty. Done before the window is shown, since the hover preview places it by its size.
        /// </summary>
        private void FitHeightToPlayers()
        {
            if (DataContext is not HistorySessionViewModel session || Content is not FrameworkElement root) return;

            // Set here rather than bound, so the rows exist for the measure below.
            PlayerList.ItemsSource = session.Players;

            double height;
            try
            {
                root.Measure(new Size(Width, double.PositiveInfinity));
                height = root.DesiredSize.Height;
            }
            catch (Exception)
            {
                height = 0;
            }
            // Measuring a window that isn't shown yet should work; if it comes back empty, estimate.
            if (height < FallbackChromeHeight)
                height = FallbackChromeHeight + session.Players.Count * FallbackRowHeight;

            var wa = System.Windows.SystemParameters.WorkArea;
            MinHeight = 0;
            Height = Math.Min(Math.Ceiling(height), wa.Height);
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void PlayerItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement el &&
                el.Tag is HistoryPlayerViewModel player &&
                DataContext is HistorySessionViewModel session)
            {
                _preview?.HideNow();
                var detailsWindow = new PlayerDetailsWindow
                {
                    DataContext = PlayerDetailsViewModel.FromSnapshot(player, _settingsService, session.TargetName),
                    Owner = this
                };
                detailsWindow.Show();
            }
        }

        // Hovering a player previews their breakdown; clicking still opens it as its own window.
        private void PlayerItem_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: HistoryPlayerViewModel player } ||
                DataContext is not HistorySessionViewModel session) return;

            _preview ??= new HoverPreview(this);
            _preview.Request(player, () => Task.FromResult<IReadOnlyList<Window>?>(
            [
                new PlayerDetailsWindow
                {
                    DataContext = PlayerDetailsViewModel.FromSnapshot(player, _settingsService, session.TargetName),
                    Owner = this
                },
            ]));
        }

        private void PlayerItem_MouseLeave(object sender, MouseEventArgs e) => _preview?.Hide();
    }
}
