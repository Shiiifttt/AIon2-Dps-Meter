using System.Drawing;
using System.Windows.Forms;
using AionDpsMeter.Services.Services.Settings;

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

        public TrayIcon(WindowHelper windowHelper, IAppSettingsService settingsService, IWindowManagerService windowManager)
        {
            this.windowHelper = windowHelper;
            this.settingsService = settingsService;

            _showHideItem = new ToolStripMenuItem("Hide meter", null, (_, _) => windowHelper.ToggleMeterVisibility());
            _showHideItem.Font = new Font(_showHideItem.Font, System.Drawing.FontStyle.Bold);
            _clickThroughItem = new ToolStripMenuItem("Click-through", null, (_, _) => settingsService.MainClickThrough = !settingsService.MainClickThrough);

            var menu = new ContextMenuStrip();
            menu.Items.Add(_showHideItem);
            menu.Items.Add(_clickThroughItem);
            menu.Items.Add(new ToolStripMenuItem("Settings", null, (_, _) => OpenSettings()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => windowManager.CloseApplication()));
            menu.Opening += (_, _) => RefreshMenu();

            _notifyIcon = new NotifyIcon
            {
                Icon = LoadAppIcon(),
                Text = "AION2 DPS Meter",
                ContextMenuStrip = menu,
                Visible = true
            };
            _notifyIcon.MouseClick += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    windowHelper.ToggleMeterVisibility();
            };
        }

        private void RefreshMenu()
        {
            _showHideItem.Text = windowHelper.IsMeterVisible ? "Hide meter" : "Show meter";
            _clickThroughItem.Checked = settingsService.MainClickThrough;
        }

        private void OpenSettings()
        {
            if (!windowHelper.IsMeterVisible)
                windowHelper.ToggleMeterVisibility();
            windowHelper.OpenSettings();
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
            // Without this the icon lingers in the tray until the user hovers over it.
            _notifyIcon.Visible = false;
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();
        }
    }
}
