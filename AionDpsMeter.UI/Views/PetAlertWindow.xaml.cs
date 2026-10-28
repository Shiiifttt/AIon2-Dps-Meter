using AionDpsMeter.Core.Windowing;
using AionDpsMeter.Services.Services.Pets;
using AionDpsMeter.UI.Services.Windowing;
using AionDpsMeter.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace AionDpsMeter.UI.Views
{
    /// <summary>
    /// Pet popup: a stack of cards (newest on top) that fade out on their own. Separate from the meter
    /// and stays up when the meter is hidden. Click-through except while Settings is open.
    /// </summary>
    public partial class PetAlertWindow : Window
    {
        private const int MaxCards = 4;

        private readonly ObservableCollection<PetAlertCardViewModel> _cards = [];
        private readonly DispatcherTimer _fadeTimer;
        private readonly PetAlertService _alerts;
        private readonly IWindowManagerService _windowManager;

        public PetAlertWindow()
        {
            InitializeComponent();
            Cards.ItemsSource = _cards;

            // Until it has been moved: top middle of the main screen, clear of the meter.
            var wa = SystemParameters.WorkArea;
            Left = wa.Left + (wa.Width - Width) / 2;
            Top = wa.Top + wa.Height * 0.12;

            _fadeTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(50) };
            _fadeTimer.Tick += (_, _) => TickFades();

            _alerts = App.AppHost.Services.GetRequiredService<PetAlertService>();
            _windowManager = App.AppHost.Services.GetRequiredService<IWindowManagerService>();
            _alerts.Alert += OnAlert;
            Closed += (_, _) => { _alerts.Alert -= OnAlert; _fadeTimer.Stop(); };
        }

        /// <summary>While Settings is open the popup shows a placeholder and can be dragged.</summary>
        public void SetEditMode(bool editing)
        {
            EditHint.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnAlert(PetAlert alert)
        {
            // Raised on the packet thread.
            Dispatcher.BeginInvoke(() =>
            {
                var now = DateTime.UtcNow;
                var key = $"{alert.Kind}|{alert.Title}";
                var existing = _cards.FirstOrDefault(c => c.Key == key);
                if (existing is not null)
                {
                    existing.Update(alert, now);
                    _cards.Move(_cards.IndexOf(existing), 0);
                }
                else
                {
                    _cards.Insert(0, new PetAlertCardViewModel(alert, now));
                    while (_cards.Count > MaxCards) _cards.RemoveAt(_cards.Count - 1);
                }
                _fadeTimer.Start();
            });
        }

        private void TickFades()
        {
            var now = DateTime.UtcNow;
            for (int i = _cards.Count - 1; i >= 0; i--)
            {
                if (!_cards[i].Tick(now)) _cards.RemoveAt(i);
            }
            if (_cards.Count == 0) _fadeTimer.Stop();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && EditHint.Visibility == Visibility.Visible)
                _windowManager.Drag(WindowKey.PetAlert);
        }
    }
}
