using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using AionDpsMeter.Services.Services.Settings;
using AionDpsMeter.UI.Pages;
using AionDpsMeter.UI.Services.Windowing;
using AionDpsMeter.UI.Services.Windowing.Native;
using AionDpsMeter.UI.Utils;
using AionDpsMeter.UI.ViewModels;
using Microsoft.AspNetCore.Components.WebView.Wpf;

namespace AionDpsMeter.UI.Views
{
    public partial class MainWindow : Window
    {
        private readonly IAppSettingsService settingsService;
        private DispatcherTimer? _saveBoundsTimer;
        private readonly DispatcherTimer _hoverWatchTimer;
        private bool _cursorWasOver;
        private GlobalHotkey? _globalHotkey;
        private readonly IWindowManagerService windowManager;
        private readonly WindowHelper windowHelper;

        public MainWindow(MainViewModel viewModel, IAppSettingsService settingsService, IWindowManagerService windowManager, WindowHelper windowHelper)
        {
            InitializeComponent();
            DataContext = viewModel;
            this.settingsService      = settingsService;
            this.windowManager = windowManager;
            this.windowHelper = windowHelper;

            _saveBoundsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5000) };
            _saveBoundsTimer.Tick += (_, _) => { _saveBoundsTimer.Stop(); SaveWindowBounds(); };

            ApplyLayoutMinSize();
            RestoreWindowBounds();

            MainBorder.Opacity = settingsService.WindowOpacity;

            settingsService.SettingsChanged += (_, _) =>
                Dispatcher.InvokeAsync(() =>
                {
                    ApplyLayoutMinSize();
                    MainBorder.Opacity = settingsService.WindowOpacity;
                    RegisterToggleHotkey();
                });

            _hoverWatchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _hoverWatchTimer.Tick += (_, _) => WatchMouseLeave();

            SourceInitialized += (_, _) => HideFromAltTab();
            Loaded += (_, _) => _hoverWatchTimer.Start();
            Loaded += (_, _) => RegisterToggleHotkey();
            Loaded += (_, _) => InitializeStyle2WebView();
            windowManager.CloseAppCommand += OnCloseCommand;
        }

        private void OnCloseCommand(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() => { CloseButton_Click(this, new RoutedEventArgs()); });
        }

      

        private void InitializeStyle2WebView()
        {
            Style2WebView.WebView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            Style2WebView.Services = App.AppHost.Services;
            Style2WebView.RootComponents.Clear();
            Style2WebView.RootComponents.Add(new RootComponent
            {
                Selector = "#app",
                ComponentType = typeof(MainDpsPage)
            });
        }


        private void RegisterToggleHotkey()
        {
            _globalHotkey ??= new GlobalHotkey(this);
            _globalHotkey.HotkeyPressed -= ToggleWindowVisibility;
            _globalHotkey.Unregister();

            var (mods, vk) = HotkeyParser.Parse(settingsService.ToggleVisibilityHotkey);
            if (vk != 0)
            {
                _globalHotkey.Register(mods, vk);
                _globalHotkey.HotkeyPressed += ToggleWindowVisibility;
            }
        }

        private void ToggleWindowVisibility() => windowHelper.ToggleMeterVisibility();

        // WebView2 doesn't always notice the mouse leaving (e.g. when click-through flips while hovering),
        // leaving hover effects and tooltips stuck. Watch the cursor ourselves and tell it.
        private void WatchMouseLeave()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero || !IsVisible) return;

            var isOver = NativeWindowHelper.IsCursorOver(hwnd);
            if (_cursorWasOver && !isOver)
                NativeWindowHelper.SendMouseLeave(hwnd);
            _cursorWasOver = isOver;
        }

        // The ultra compact (3) and horizontal (4) layouts fit in a much smaller window than the others.
        private void ApplyLayoutMinSize()
        {
            (MinWidth, MinHeight) = settingsService.UiStyle switch
            {
                3 => (150, 20),
                4 => (200, 40),
                _ => (370, 50)
            };
        }

        // ShowInTaskbar="False" keeps the meter off the taskbar; the tool-window style also keeps it
        // out of Alt+Tab. It lives in the tray instead (TrayIcon).
        private void HideFromAltTab()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            NativeWindowHelper.SetExtendedStyle(hwnd, NativeWindowHelper.GetExtendedStyle(hwnd) | NativeMethods.WS_EX_TOOLWINDOW);
        }


        private void RestoreWindowBounds()
        {
            var left   = settingsService.WindowLeft;
            var top    = settingsService.WindowTop;
            var width  = settingsService.WindowWidth;
            var height = settingsService.WindowHeight;

            if (!left.HasValue || !top.HasValue)
                return;

            double w = width.HasValue  ? Math.Max(MinWidth,  width.Value)  : Width;
            double h = height.HasValue ? Math.Max(MinHeight, height.Value) : Height;

            var wa = ScreenHelper.GetWorkingAreaForPoint(left.Value, top.Value);

            double l = Math.Max(wa.Left, Math.Min(left.Value, wa.Right  - w));
            double t = Math.Max(wa.Top,  Math.Min(top.Value,  wa.Bottom - h));

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left   = l;
            Top    = t;
            Width  = w;
            Height = h;
        }

      
        private void SaveWindowBounds()
        {
            if (WindowState != WindowState.Normal) return;
            settingsService.WindowLeft   = Left;
            settingsService.WindowTop    = Top;
            settingsService.WindowWidth  = Width;
            settingsService.WindowHeight = Height;
        }


        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _globalHotkey?.Dispose();
            if (DataContext is MainViewModel viewModel)
                viewModel.Dispose();
            Application.Current.Shutdown();
        }

        protected override void OnClosed(EventArgs e)
        {
            _saveBoundsTimer?.Stop();
            _hoverWatchTimer.Stop();
            _saveBoundsTimer = null;
            SaveWindowBounds();
            if (DataContext is MainViewModel viewModel)
                viewModel.Dispose();
            base.OnClosed(e);
        }
    }
}