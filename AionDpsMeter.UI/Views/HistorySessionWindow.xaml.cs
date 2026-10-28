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
                _preview?.Hide();
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
