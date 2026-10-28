using AionDpsMeter.UI.Services.Windowing.Native;
using AionDpsMeter.UI.Utils;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace AionDpsMeter.UI.Services.Windowing;

/// <summary>
/// Read-only preview windows shown while the mouse rests on a row, placed beside the anchor window
/// (several side by side, shrunk to fit the screen if needed). Previews are click-through and never
/// take focus, so they can't get in the way of the list under them.
/// </summary>
internal sealed class HoverPreview
{
    private static readonly TimeSpan ShowDelay = TimeSpan.FromMilliseconds(350);
    private const double Gap = 8;
    private const double MinSideScale = 0.6;

    private readonly Window anchor;
    private readonly DispatcherTimer timer;
    private Func<Task<IReadOnlyList<Window>?>>? pendingBuild;
    private object? currentKey;
    private int version;
    private List<Window> shown = [];

    public HoverPreview(Window anchor)
    {
        this.anchor = anchor;
        timer = new DispatcherTimer(DispatcherPriority.Normal, anchor.Dispatcher) { Interval = ShowDelay };
        timer.Tick += OnTick;
        anchor.Closed += (_, _) => Hide();
        anchor.Deactivated += (_, _) => { if (!anchor.IsMouseOver) Hide(); };
    }

    /// <param name="key">What is being previewed; asking again for the same key keeps the open preview.</param>
    /// <param name="build">Creates the windows (not shown yet), or null when there is nothing to show.</param>
    public void Request(object key, Func<Task<IReadOnlyList<Window>?>> build)
    {
        if (Equals(key, currentKey)) return;
        Hide();
        currentKey = key;
        pendingBuild = build;
        timer.Start();
    }

    public void Hide()
    {
        timer.Stop();
        version++;
        currentKey = null;
        pendingBuild = null;
        foreach (var w in shown) w.Close();
        shown = [];
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        timer.Stop();
        var build = pendingBuild;
        if (build is null) return;
        int myVersion = ++version;

        IReadOnlyList<Window>? windows;
        try
        {
            windows = await build();
        }
        catch (Exception)
        {
            return;
        }
        if (windows is null || windows.Count == 0) return;
        if (myVersion != version || !anchor.IsVisible)
        {
            foreach (var w in windows) w.Close();
            return;
        }

        Place(windows);
        foreach (var w in windows)
        {
            w.ShowActivated = false;
            w.ShowInTaskbar = false;
            w.Topmost = true;
            w.SourceInitialized += (_, _) => MakeClickThrough(w);
            w.Show();
        }
        shown = windows.ToList();
    }

    private void Place(IReadOnlyList<Window> windows)
    {
        var wa = ScreenHelper.GetWorkingAreaForWindow(anchor);
        double total = windows.Sum(w => w.Width) + Gap * (windows.Count - 1);
        double anchorRight = anchor.Left + anchor.ActualWidth;
        double rightSpace = wa.Right - anchorRight - Gap;
        double leftSpace = anchor.Left - wa.Left - Gap;

        double scale;
        double left;
        if (total <= rightSpace) { scale = 1; left = anchorRight + Gap; }
        else if (total <= leftSpace) { scale = 1; left = anchor.Left - Gap - total; }
        else
        {
            var side = Math.Max(rightSpace, leftSpace);
            scale = side / total;
            if (scale >= MinSideScale)
            {
                left = rightSpace >= leftSpace ? anchorRight + Gap : anchor.Left - Gap - total * scale;
            }
            else
            {
                // Not enough room beside the anchor: cover it (previews are click-through).
                scale = Math.Min(1, (wa.Width - 2 * Gap) / total);
                left = wa.Right - Gap - total * scale;
            }
        }

        foreach (var w in windows)
        {
            if (scale < 1 && w.Content is FrameworkElement content)
            {
                content.LayoutTransform = new ScaleTransform(scale, scale);
                w.MinWidth = 0;
                w.MinHeight = 0;
                w.Width *= scale;
                w.Height *= scale;
            }

            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = left;
            w.Top = Math.Max(wa.Top, Math.Min(anchor.Top, wa.Bottom - w.Height));
            left += w.Width + Gap * scale;
        }
    }

    private static void MakeClickThrough(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        NativeWindowHelper.SetExtendedStyle(hwnd,
            NativeWindowHelper.GetExtendedStyle(hwnd) | NativeWindowHelper.ClickThroughExtendedStyle | NativeMethods.WS_EX_TOOLWINDOW);
    }
}
