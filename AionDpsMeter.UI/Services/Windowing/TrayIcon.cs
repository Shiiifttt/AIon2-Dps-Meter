using System.Drawing;
using System.Windows.Forms;
using AionDpsMeter.Services.Services.Settings;
using AionDpsMeter.Services.Services.Sync;

namespace AionDpsMeter.UI.Services.Windowing
{
    /// <summary>
    /// Notification-area icon. The meter has no taskbar / Alt+Tab entry, so this is how it is
    /// shown again after being hidden, and how click-through is turned off (the meter itself
    /// can't be clicked while it's on).
    /// </summary>
    public sealed class TrayIcon : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly WindowHelper windowHelper;
        private readonly IAppSettingsService settingsService;

        private readonly ToolStripMenuItem _showHideItem;
        private readonly ToolStripMenuItem _clickThroughItem;
        private readonly ToolStripMenuItem _petCollectionItem;
        private readonly ToolStripMenuItem _startupItem;

        private const string AppName = "AION2 DPS Meter";
        private readonly MeterSyncMonitor _sync;

        public TrayIcon(WindowHelper windowHelper, IAppSettingsService settingsService, IWindowManagerService windowManager, MeterSyncMonitor sync)
        {
            this.windowHelper = windowHelper;
            this.settingsService = settingsService;
            _sync = sync;

            _showHideItem = new ToolStripMenuItem("Hide meter", null, (_, _) => windowHelper.ToggleMeterVisibility());
            _showHideItem.Font = new Font(_showHideItem.Font, System.Drawing.FontStyle.Bold);
            _clickThroughItem = new ToolStripMenuItem("Click-through", null, (_, _) => settingsService.MainClickThrough = !settingsService.MainClickThrough);
            _petCollectionItem = new ToolStripMenuItem("Pet collection", null, (_, _) => WithMeterShown(windowHelper.OpenPetCollection));
            _startupItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled));

            // Everything the meter's title bar buttons do, plus what only the tray can do.
            var menu = new ContextMenuStrip();
            menu.Items.Add(_showHideItem);
            menu.Items.Add(_clickThroughItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Reset meter", null, (_, _) => windowHelper.ResetMeter())
            {
                ToolTipText = "Saves the current fight to history and clears the meter",
            });
            menu.Items.Add(new ToolStripMenuItem("History", null, (_, _) => WithMeterShown(windowHelper.OpenHistory)));
            menu.Items.Add(new ToolStripMenuItem("Stat efficiency calculator", null, (_, _) => WithMeterShown(windowHelper.OpenStatEff)));
            menu.Items.Add(_petCollectionItem);
            menu.Items.Add(new ToolStripMenuItem("Settings", null, (_, _) => WithMeterShown(windowHelper.OpenSettings)));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Re-sync with game", null, (_, _) => sync.Resync())
            {
                ToolTipText = "Drops the current game connection and looks for it again (also finds a VPN connected after the meter started)",
            });
            menu.Items.Add(_startupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => windowManager.CloseApplication()));
            menu.Opening += (_, _) => RefreshMenu();

            _notifyIcon = new NotifyIcon
            {
                Icon = LoadAppIcon(),
                Text = AppName,
                ContextMenuStrip = menu,
                Visible = true
            };
            UpdateTooltip();
            // Raised on capture / packet threads.
            sync.SummaryChanged += OnSyncSummaryChanged;
            _notifyIcon.MouseClick += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    windowHelper.ToggleMeterVisibility();
            };
        }

        private void OnSyncSummaryChanged()
            => System.Windows.Application.Current?.Dispatcher.BeginInvoke(UpdateTooltip);

        // Hovering the tray icon shows whether the meter follows the game.
        private void UpdateTooltip()
        {
            var text = $"{AppName}\n{_sync.Summary}";
            // NotifyIcon.Text is limited to 127 characters.
            _notifyIcon.Text = text.Length <= 127 ? text : text[..126] + "…";
        }

        private void RefreshMenu()
        {
            UpdateTooltip();
            _showHideItem.Text = windowHelper.IsMeterVisible ? "Hide meter" : "Show meter";
            _clickThroughItem.Checked = settingsService.MainClickThrough;
            _petCollectionItem.Visible = settingsService.TrackPetCollection;
            _startupItem.Checked = StartupRegistration.IsEnabled;
        }

        // These windows belong to the meter, so bring it back from the tray first.
        private void WithMeterShown(Action open)
        {
            if (!windowHelper.IsMeterVisible)
                windowHelper.ToggleMeterVisibility();
            open();
        }

        private static Icon LoadAppIcon()
        {
            try
            {
                if (Environment.ProcessPath is { } exe && Icon.ExtractAssociatedIcon(exe) is { } icon)
                    return icon;
            }
            catch (Exception)
            {
                // Fall through to the stock icon.
            }
            return SystemIcons.Application;
        }

        public void Dispose()
        {
            _sync.SummaryChanged -= OnSyncSummaryChanged;
            // Without this the icon lingers in the tray until the user hovers over it.
            _notifyIcon.Visible = false;
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();
        }
    }
}
