using AionDpsMeter.Services.Services.Settings;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.UI.Services.Windowing;
using AionDpsMeter.UI.Utils;
using AionDpsMeter.UI.ViewModels;
using AionDpsMeter.UI.ViewModels.History;
using System.Windows;
using System.Windows.Input;

namespace AionDpsMeter.UI
{
    public partial class HistoryWindow : Window
    {
        private readonly CombatSessionManager _sessionManager;
        private readonly IAppSettingsService _settingsService;
        private readonly HoverPreview _preview;

        // Recently loaded fights, so hovering back and forth doesn't reload them from the database.
        private const int SessionCacheSize = 12;
        private readonly Dictionary<Guid, HistorySessionViewModel> _sessionCache = new();
        private readonly LinkedList<Guid> _sessionCacheOrder = new();

        public HistoryWindow(CombatSessionManager sessionManager, IAppSettingsService settingsService)
        {
            InitializeComponent();
            _sessionManager = sessionManager;
            _settingsService = settingsService;
            _preview = new HoverPreview(this);
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void PositionWindowToRight(Window child)
        {
            const double gap = 8;

            var wa = ScreenHelper.GetWorkingAreaForWindow(this);

            double parentRight = Left + Width;
            double candidateLeft = parentRight + gap;

            double childLeft = candidateLeft + child.Width <= wa.Right
                ? candidateLeft
                : Math.Max(wa.Left, wa.Right - child.Width);

            double childTop = Math.Max(wa.Top, Math.Min(Top, wa.Bottom - child.Height));

            child.WindowStartupLocation = WindowStartupLocation.Manual;
            child.Left = childLeft;
            child.Top = childTop;
        }

        private void SessionItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement el && el.Tag is HistoryEntryViewModel entry)
            {
                _preview.Hide();
                var session = GetSessionViewModel(entry.SessionId);
                if (session is null) return;

                var sessionWindow = new HistorySessionWindow(_settingsService)
                {
                    DataContext = session,
                    Owner = this
                };

                PositionWindowToRight(sessionWindow);
                sessionWindow.Show();
            }
        }

        // Hovering a fight previews its breakdown; clicking still opens it as its own window.
        private void SessionItem_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement { Tag: HistoryEntryViewModel entry })
                RequestSessionPreview(entry);
        }

        private void SessionItem_MouseLeave(object sender, MouseEventArgs e) => _preview.Hide();

        // Hovering "vs PB" puts your breakdown in this fight next to your breakdown in your best fight.
        private void VsBest_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: HistoryEntryViewModel entry }) return;
            if (!HasBestToCompare(entry)) return;

            var bestId = entry.BestSessionId!.Value;
            _preview.Request(("pb", entry.SessionId), async () =>
            {
                var (current, best) = await LoadPairAsync(entry.SessionId, bestId);
                return CreateComparisonWindows(current, best, entry.TargetName);
            });
        }

        private void VsBest_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: HistoryEntryViewModel entry } && IsMouseOverRow(sender))
                RequestSessionPreview(entry);
        }

        private void VsBest_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: HistoryEntryViewModel entry } || !HasBestToCompare(entry)) return;

            e.Handled = true;
            _preview.Hide();
            var windows = CreateComparisonWindows(GetSessionViewModel(entry.SessionId), GetSessionViewModel(entry.BestSessionId!.Value), entry.TargetName);
            if (windows is null) return;

            var wa = ScreenHelper.GetWorkingAreaForWindow(this);
            double left = Math.Max(wa.Left, wa.Left + (wa.Width - windows.Sum(w => w.Width)) / 2);
            foreach (var w in windows)
            {
                w.WindowStartupLocation = WindowStartupLocation.Manual;
                w.Left = Math.Min(left, Math.Max(wa.Left, wa.Right - w.Width));
                w.Top = Math.Max(wa.Top, Math.Min(Top, wa.Bottom - w.Height));
                w.Show();
                left += w.Width;
            }
        }

        private static bool HasBestToCompare(HistoryEntryViewModel entry)
            => entry.BestSessionId is { } bestId && bestId != entry.SessionId;

        private static bool IsMouseOverRow(object sender)
        {
            for (var el = sender as DependencyObject; el is not null; el = System.Windows.Media.VisualTreeHelper.GetParent(el))
            {
                if (el is FrameworkElement { Tag: HistoryEntryViewModel } row) return row.IsMouseOver;
            }
            return false;
        }

        private void RequestSessionPreview(HistoryEntryViewModel entry)
        {
            _preview.Request(entry.SessionId, async () =>
            {
                var session = await LoadAsync(entry.SessionId);
                if (session is null) return null;
                return [new HistorySessionWindow(_settingsService) { DataContext = session, Owner = this }];
            });
        }

        /// <summary>Your breakdown in this fight (left) and in your best fight (right).</summary>
        private IReadOnlyList<Window>? CreateComparisonWindows(HistorySessionViewModel? current, HistorySessionViewModel? best, string targetName)
        {
            if (current?.UserPlayer is not { } mine || best?.UserPlayer is not { } mineBest) return null;

            var now = PlayerDetailsViewModel.FromSnapshot(mine, _settingsService, targetName);
            now.FightLabel = $"This fight  {current.DateDisplay}";
            var pb = PlayerDetailsViewModel.FromSnapshot(mineBest, _settingsService, targetName);
            pb.FightLabel = $"Personal best  {best.DateDisplay}";
            return
            [
                new PlayerDetailsWindow { DataContext = now, Owner = this },
                new PlayerDetailsWindow { DataContext = pb, Owner = this },
            ];
        }

        private async Task<(HistorySessionViewModel? Current, HistorySessionViewModel? Best)> LoadPairAsync(Guid current, Guid best)
        {
            var a = LoadAsync(current);
            var b = LoadAsync(best);
            return (await a, await b);
        }

        private async Task<HistorySessionViewModel?> LoadAsync(Guid sessionId)
        {
            if (_sessionCache.TryGetValue(sessionId, out var cached)) return cached;
            var session = await Task.Run(() => BuildSessionViewModel(sessionId));
            if (session is not null) AddToCache(sessionId, session);
            return session;
        }

        private HistorySessionViewModel? GetSessionViewModel(Guid sessionId)
        {
            if (_sessionCache.TryGetValue(sessionId, out var cached)) return cached;
            var session = BuildSessionViewModel(sessionId);
            if (session is not null) AddToCache(sessionId, session);
            return session;
        }

        private HistorySessionViewModel? BuildSessionViewModel(Guid sessionId)
        {
            var snapshot = _sessionManager.GetHistorySession(sessionId);
            if (snapshot is null) return null;
            return new HistorySessionViewModel(snapshot, _settingsService,
                classId => _sessionManager.GetBestUptime(snapshot.TargetName, classId));
        }

        private void AddToCache(Guid sessionId, HistorySessionViewModel session)
        {
            // A fight still in progress keeps changing; only cache finished ones.
            if (!session.IsCompleted) return;
            if (_sessionCache.ContainsKey(sessionId)) return;
            _sessionCache[sessionId] = session;
            _sessionCacheOrder.AddLast(sessionId);
            while (_sessionCacheOrder.Count > SessionCacheSize)
            {
                _sessionCache.Remove(_sessionCacheOrder.First!.Value);
                _sessionCacheOrder.RemoveFirst();
            }
        }
    }
}
