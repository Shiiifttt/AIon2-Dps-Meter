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
        // The layout the window is sized for (see ApplyLayoutSwitch).
        private int _layoutStyle;
        private GlobalHotkey? _globalHotkey;
        private readonly IWindowManagerService windowManager;
        private readonly WindowHelper windowHelper;
        private readonly MeterLayoutMetrics layoutMetrics;

        public MainWindow(MainViewModel viewModel, IAppSettingsService settingsService, IWindowManagerService windowManager, WindowHelper windowHelper, MeterLayoutMetrics layoutMetrics)
        {
            InitializeComponent();
            DataContext = viewModel;
            this.settingsService      = settingsService;
            this.windowManager = windowManager;
            this.windowHelper = windowHelper;
            this.layoutMetrics = layoutMetrics;
            layoutMetrics.Measured += (style, metrics) => Dispatcher.InvokeAsync(() => OnLayoutMeasured(style, metrics));

            _saveBoundsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5000) };
            _saveBoundsTimer.Tick += (_, _) => { _saveBoundsTimer.Stop(); SaveWindowBounds(); };

            _layoutStyle = settingsService.UiStyle;
            ApplyLayoutMinSize();
            RestoreWindowBounds();

            MainBorder.Opacity = settingsService.WindowOpacity;

            settingsService.SettingsChanged += (_, _) =>
                Dispatcher.InvokeAsync(() =>
                {
                    ApplyLayoutSwitch();
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

        // Switching layouts keeps about the same number of bars on screen: if Ultra Compact showed ~5.5 rows,
        // Standard is made tall enough for ~5.5 of its (taller) rows. The width follows each layout's shape
        // (Ultra Compact a bit narrower, Horizontal a wide strip). The top-left corner stays put.
        private void ApplyLayoutSwitch()
        {
            int style = settingsService.UiStyle;
            int previous = _layoutStyle;
            if (style == previous) return;
            _layoutStyle = style;
            ApplyLayoutMinSize();
            if (WindowState != WindowState.Normal) return;

            // Horizontal shows one row of slots; going back, bring back the bar count from before it.
            double bars = previous == MeterLayoutMetrics.Horizontal
                ? _barsBeforeHorizontal
                : layoutMetrics.BarsFitting(previous, Height);
            if (style == MeterLayoutMetrics.Horizontal && previous != MeterLayoutMetrics.Horizontal)
                _barsBeforeHorizontal = bars;

            double width = Width * MeterLayoutMetrics.WidthFactor(style) / MeterLayoutMetrics.WidthFactor(previous);
            SetMeterSize(width, layoutMetrics.HeightFor(style, bars));
            // The new layout may not have been measured yet (estimates); correct once it has.
            _pendingFit = (style, bars, Width, Height, DateTime.UtcNow + PendingFitWindow);
        }

        private static readonly TimeSpan PendingFitWindow = TimeSpan.FromSeconds(20);
        private (int Style, double Bars, double Width, double Height, DateTime Until)? _pendingFit;
        private double _barsBeforeHorizontal = 5;

        private void OnLayoutMeasured(int style, MeterLayoutMetrics.Metrics _)
        {
            if (_pendingFit is not { } fit || fit.Style != style || style != _layoutStyle) return;
            // Give up once the user has resized it themselves, or it's been a while.
            if (DateTime.UtcNow > fit.Until || WindowState != WindowState.Normal
                || Math.Abs(Width - fit.Width) > 0.5 || Math.Abs(Height - fit.Height) > 0.5)
            {
                _pendingFit = null;
                return;
            }
            SetMeterSize(Width, layoutMetrics.HeightFor(style, fit.Bars));
            _pendingFit = fit with { Height = Height };
        }

        private void SetMeterSize(double width, double height)
        {
            var wa = ScreenHelper.GetWorkingAreaForPoint(Left, Top);
            Width = Math.Round(Math.Clamp(width, MinWidth, Math.Max(MinWidth, wa.Width)));
            Height = Math.Round(Math.Clamp(height, MinHeight, Math.Max(MinHeight, wa.Height)));
            Left = Math.Max(wa.Left, Math.Min(Left, wa.Right - Width));
            Top = Math.Max(wa.Top, Math.Min(Top, wa.Bottom - Height));
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