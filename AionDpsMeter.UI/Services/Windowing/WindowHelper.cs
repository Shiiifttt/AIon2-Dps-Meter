using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Settings;
using AionDpsMeter.Services.Services.Update;
using AionDpsMeter.Core.Windowing;
using AionDpsMeter.UI.Pages;
using AionDpsMeter.UI.ViewModels;
using AionDpsMeter.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using AionDpsMeter.UI.Services.Windowing.Native;
using System.Windows;
using System.Windows.Threading;

namespace AionDpsMeter.UI.Services.Windowing
{
    public class WindowHelper
    {
        //window states
        public EventHandler? WindowStateUpdated { get; set; }
        public bool IsBuffEdit { get; private set; }
        public bool IsSkillCdEdit { get; private set; }

        private bool IsBuffOverlayEnabled { get; set; }
        private bool IsSkillCdOverlayEnabled { get; set; }
        private bool IsPetAlertEnabled => settingsService.TrackPetCollection && settingsService.ShowPetAlerts;
        private bool _petAlertOpen;
        private bool IsMainClickThrough { get; set; }

        // Holding the configured key inverts click-through for as long as it's held. A key-up can't be
        // caught with RegisterHotKey, so the key state is polled.
        private readonly DispatcherTimer _holdKeyTimer;
        private bool _isHoldKeyDown;
        private bool? _appliedMainClickThrough;

        // Windows that were visible when the meter was last hidden, so showing it brings them back too.
        private List<Window> _windowsHiddenWithMeter = [];

        public bool IsMeterVisible => MainWindow.IsVisible;

        private MainWindow MainWindow => serviceProvider.GetRequiredService<MainWindow>();

        private readonly IWindowManagerService windowManager;
        private readonly IServiceProvider serviceProvider;
        private readonly CombatSessionManager sessionManager;
        private readonly IAppSettingsService settingsService;
        private readonly UpdateCheckerService updateService;

        public WindowHelper(IWindowManagerService windowManager, IServiceProvider serviceProvider, CombatSessionManager sessionManager, IAppSettingsService settingsService, UpdateCheckerService updateService)
        {

            this.windowManager = windowManager;
            this.serviceProvider = serviceProvider;
            this.sessionManager = sessionManager;
            this.settingsService = settingsService;
            this.updateService = updateService;

            IsBuffOverlayEnabled = settingsService.BufOverlaySettings.Enabled;
            IsSkillCdOverlayEnabled = settingsService.SkillCdOverlaySettings.Enabled;
            IsMainClickThrough = settingsService.MainClickThrough;
            settingsService.SettingsChanged += SettingsChanged;

            _holdKeyTimer = new DispatcherTimer(DispatcherPriority.Input, Application.Current.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(40)
            };
            _holdKeyTimer.Tick += (_, _) => PollClickThroughHoldKey();
        }

        private void SettingsChanged(object? sender, EventArgs e)
        {
            if (IsBuffOverlayEnabled != settingsService.BufOverlaySettings.Enabled)
            {
                IsBuffOverlayEnabled = settingsService.BufOverlaySettings.Enabled;
                ManageBuffOverlay();
            }

            if (IsSkillCdOverlayEnabled != settingsService.SkillCdOverlaySettings.Enabled)
            {
                IsSkillCdOverlayEnabled = settingsService.SkillCdOverlaySettings.Enabled;
                ManageSkillCdOverlay();
            }

            if (IsMainClickThrough != settingsService.MainClickThrough)
            {
                IsMainClickThrough = settingsService.MainClickThrough;
                ApplyMainClickThrough();
            }

            if (!settingsService.TrackPetCollection)
                windowManager.Close(WindowKey.PetCollection);

            ManagePetAlert();

            UpdateHoldKeyPolling();
        }

        public void OpenRequiredWindows()
        {
            ManageBuffOverlay();
            ManageSkillCdOverlay();
            windowManager.SetClickThrough(WindowKey.BuffOverlay);
            windowManager.SetClickThrough(WindowKey.SkillCdOverlay);
            ManagePetAlert();
            ApplyMainClickThrough();
            UpdateHoldKeyPolling();
        }

        private void ApplyMainClickThrough()
        {
            var clickThrough = IsMainClickThrough != _isHoldKeyDown;
            if (_appliedMainClickThrough == clickThrough) return;
            _appliedMainClickThrough = clickThrough;

            if (clickThrough) windowManager.SetClickThrough(WindowKey.Main);
            else windowManager.RestoreClickThrough(WindowKey.Main);
        }

        private static int HoldKeyToVirtualKey(string key) => key switch
        {
            "Ctrl" => 0x11,  // VK_CONTROL
            "Shift" => 0x10, // VK_SHIFT
            "Alt" => 0x12,   // VK_MENU
            _ => 0
        };

        private void UpdateHoldKeyPolling()
        {
            if (HoldKeyToVirtualKey(settingsService.ClickThroughHoldKey) != 0)
            {
                _holdKeyTimer.Start();
                return;
            }

            _holdKeyTimer.Stop();
            if (_isHoldKeyDown)
            {
                _isHoldKeyDown = false;
                ApplyMainClickThrough();
            }
        }

        private void PollClickThroughHoldKey()
        {
            var vk = HoldKeyToVirtualKey(settingsService.ClickThroughHoldKey);
            var isDown = vk != 0 && (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
            if (isDown == _isHoldKeyDown) return;

            _isHoldKeyDown = isDown;
            ApplyMainClickThrough();
        }

        /// <summary>Hides the meter (and every other open window) to the tray, or brings them back.</summary>
        public void ToggleMeterVisibility()
        {
            if (MainWindow.IsVisible) HideAllToTray();
            else ShowMeter();
        }

        /// <summary>Hides just the meter window to the tray (the meter's own hide button).</summary>
        public void HideMeter() => MainWindow.Hide();

        private void HideAllToTray()
        {
            _windowsHiddenWithMeter = Application.Current.Windows.OfType<Window>()
                .Where(window => window.IsVisible)
                // The pet popup is its own thing; it keeps working while the meter is in the tray.
                .Where(window => window is not PetAlertWindow)
                .ToList();

            foreach (var window in _windowsHiddenWithMeter)
                window.Hide();
        }

        private void ShowMeter()
        {
            foreach (var window in _windowsHiddenWithMeter)
            {
                // Skip windows closed while hidden, and overlays switched off in the meantime.
                if (!Application.Current.Windows.OfType<Window>().Contains(window)) continue;
                if (window is BuffOverlayWindow && !IsBuffOverlayEnabled) continue;
                if (window is SkillCdOverlayWindow && !IsSkillCdOverlayEnabled) continue;
                window.Show();
            }
            _windowsHiddenWithMeter = [];

            MainWindow.Show();
            if (!IsMainClickThrough)
                MainWindow.Activate();
        }


        public void OpenSettings()
        {
            IsBuffEdit = true;
            IsSkillCdEdit = true;
            windowManager.RestoreClickThrough(WindowKey.BuffOverlay);
            windowManager.RestoreClickThrough(WindowKey.SkillCdOverlay);
            SetPetAlertEditMode(true);
            WindowStateUpdated?.Invoke(this, EventArgs.Empty);
            var win = new BlazorWindow(App.AppHost.Services, typeof(SettingsPage))
            {
                Width = 500,
                Height = 900,
            };
            windowManager.Open(WindowKey.Settings, win, isSingleton: true, owner: MainWindow);
        }

        public void CloseSettings()
        {
            IsBuffEdit = false;
            IsSkillCdEdit = false;
            windowManager.SetClickThrough(WindowKey.BuffOverlay);
            SetPetAlertEditMode(false);
            windowManager.SetClickThrough(WindowKey.SkillCdOverlay);
            WindowStateUpdated?.Invoke(this, EventArgs.Empty);
            windowManager.Hide(WindowKey.Settings);
        }
        public void OpenPetCollection()
        {
            var win = new BlazorWindow(App.AppHost.Services, typeof(PetCollectionPage))
            {
                Width = 440,
                Height = 680,
            };
            windowManager.Open(WindowKey.PetCollection, win, isSingleton: true, owner: MainWindow, persistenceMode: WindowPersistenceMode.OnlyPosition);
        }

        public void OpenHistory()
        {
            var historyWindow = new HistoryWindow(sessionManager, settingsService)
            {
                DataContext = new ViewModels.History.HistoryViewModel(sessionManager, settingsService),
                Owner = MainWindow
            };
            windowManager.Open(WindowKey.History, historyWindow, true, null, MainWindow);
        }

        public void OpenStatEff()
        {
            var statEfficiencyCalculatorViewModel  =new StatEfficiencyCalculatorViewModel(settingsService);
            statEfficiencyCalculatorViewModel.LoadFromSnapshot(sessionManager.GetCurrentPlayerStatSnapshot());
            var statEfficiencyCalculatorWindow = new StatEfficiencyCalculatorWindow
            {
                DataContext = statEfficiencyCalculatorViewModel,
                Owner = MainWindow,
            };

            windowManager.Open(WindowKey.StatEfficiencyCalculator, statEfficiencyCalculatorWindow, true, null, MainWindow);
        }

        public void OpenWhatsNewWindow()
        {
            if (updateService.LatestReleaseInfo == null) return;
            var win = new WhatsNewWindow(updateService.LatestReleaseInfo, updateService)
            {
                Owner =  MainWindow
            };
            windowManager.Open(WindowKey.WhatsNew, win, true, null, MainWindow);
        }


        public void OpenPlayerDetails(PlayerRenderState player)
        {

            var detailsWindow = new PlayerDetailsWindow
            {
                DataContext = new PlayerDetailsViewModel(
                    sessionManager,
                    player.PlayerId,
                    player.PlayerNameDisplay,
                    player.ClassName,
                    null,
                    player.ClassIcon,
                    settingsService,
                    player.CombatPower,
                    player.ServerName),
                Owner = MainWindow
            };
            windowManager.Open(WindowKey.PlayerDetails, detailsWindow, false, null, MainWindow);
        }


        private void ManageBuffOverlay()
        {
            if (IsBuffOverlayEnabled) OpenBuffOverlay();
            else HideBuffOverlay();
        }
        private void ManageSkillCdOverlay()
        {
            if (IsSkillCdOverlayEnabled) OpenSkillCdOverlay();
            else HideSkillCdOverlay();
        }

        private void OpenBuffOverlay()
        {
            var buffOverlay = new BuffOverlayWindow();
            windowManager.Open(WindowKey.BuffOverlay, buffOverlay, true, persistenceMode: WindowPersistenceMode.OnlyPosition);
        }

        private void HideBuffOverlay()
        {
            windowManager.Hide(WindowKey.BuffOverlay);
        }

        private void OpenSkillCdOverlay()
        {
            var skillCdOverlay = new SkillCdOverlayWindow();
            windowManager.Open(WindowKey.SkillCdOverlay, skillCdOverlay, true, persistenceMode: WindowPersistenceMode.OnlyPosition);
        }

        private void HideSkillCdOverlay()
        {
            windowManager.Hide(WindowKey.SkillCdOverlay);
        }

        private void ManagePetAlert()
        {
            if (IsPetAlertEnabled == _petAlertOpen) return;
            _petAlertOpen = IsPetAlertEnabled;
            if (_petAlertOpen)
            {
                windowManager.Open(WindowKey.PetAlert, new PetAlertWindow(), true, persistenceMode: WindowPersistenceMode.OnlyPosition);
                SetPetAlertEditMode(windowManager.IsOpen(WindowKey.Settings));
            }
            else
            {
                windowManager.Close(WindowKey.PetAlert);
            }
        }

        private void SetPetAlertEditMode(bool editing)
        {
            if (!_petAlertOpen) return;
            if (editing) windowManager.RestoreClickThrough(WindowKey.PetAlert);
            else windowManager.SetClickThrough(WindowKey.PetAlert);
            foreach (var popup in Application.Current.Windows.OfType<PetAlertWindow>()) popup.SetEditMode(editing);
        }
    }
}
