using AionDpsMeter.UI.Services.Windowing.Native;
using AionDpsMeter.UI.Utils;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace AionDpsMeter.UI.Services.Windowing;

/// <summary>
/// Read-only preview windows shown while the mouse rests on a row, placed beside the anchor window
/// (several side by side, shrunk to fit the screen if needed). Previews are click-through and never
/// take focus, so they can't get in the way of the list under them.
/// They fade and slide in; moving to another row glides the preview from where the last one was
/// (instead of closing it and popping a new one up), and leaving the list fades it out.
/// </summary>
internal sealed class HoverPreview
{
    private static readonly TimeSpan ShowDelay = TimeSpan.FromMilliseconds(350);
    // Already showing a preview: the next row's comes up sooner, and leaving a row waits a moment
    // so moving onto the next one doesn't close and reopen.
    private static readonly TimeSpan SwitchDelay = TimeSpan.FromMilliseconds(140);
    private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(160);

    private static readonly TimeSpan FadeInDuration = TimeSpan.FromMilliseconds(170);
    private static readonly TimeSpan MoveDuration = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan FadeOutDuration = TimeSpan.FromMilliseconds(120);
    private const double SlideDistance = 14;

    private const double Gap = 8;
    private const double MinSideScale = 0.6;

    private readonly Window anchor;
    private readonly DispatcherTimer showTimer;
    private readonly DispatcherTimer hideTimer;
    private Func<Task<IReadOnlyList<Window>?>>? pendingBuild;
    private object? currentKey;
    private object? shownKey;
    private int version;
    private List<Window> shown = [];

    private readonly Dictionary<Window, EventHandler> animations = new();

    public HoverPreview(Window anchor)
    {
        this.anchor = anchor;
        showTimer = new DispatcherTimer(DispatcherPriority.Normal, anchor.Dispatcher) { Interval = ShowDelay };
        showTimer.Tick += OnShowTick;
        hideTimer = new DispatcherTimer(DispatcherPriority.Normal, anchor.Dispatcher) { Interval = HideDelay };
        hideTimer.Tick += (_, _) => { hideTimer.Stop(); FadeOutAndClose(shown); shown = []; shownKey = null; };
        anchor.Closed += (_, _) => HideNow();
        anchor.Deactivated += (_, _) => { if (!anchor.IsMouseOver) Hide(); };
    }

    /// <param name="key">What is being previewed; asking again for the same key keeps the open preview.</param>
    /// <param name="build">Creates the windows (not shown yet), or null when there is nothing to show.</param>
    public void Request(object key, Func<Task<IReadOnlyList<Window>?>> build)
    {
        hideTimer.Stop();
        if (Equals(key, currentKey)) return;

        version++;
        currentKey = key;
        showTimer.Stop();

        // Back on the row whose preview is still up (left it only briefly): keep it.
        if (shown.Count > 0 && Equals(key, shownKey))
        {
            pendingBuild = null;
            return;
        }

        pendingBuild = build;
        showTimer.Interval = shown.Count > 0 ? SwitchDelay : ShowDelay;
        showTimer.Start();
    }

    /// <summary>The mouse left the row: fade the preview out unless another row asks for one first.</summary>
    public void Hide()
    {
        showTimer.Stop();
        version++;
        currentKey = null;
        pendingBuild = null;
        if (shown.Count > 0) hideTimer.Start();
    }

    /// <summary>Closes the preview straight away (a row was clicked, or the anchor is closing).</summary>
    public void HideNow()
    {
        showTimer.Stop();
        hideTimer.Stop();
        version++;
        currentKey = null;
        shownKey = null;
        pendingBuild = null;
        foreach (var w in shown) CloseWindow(w);
        shown = [];
    }

    private async void OnShowTick(object? sender, EventArgs e)
    {
        showTimer.Stop();
        var build = pendingBuild;
        if (build is null) return;
        pendingBuild = null;
        int myVersion = version;
        var key = currentKey;

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

        var targets = Place(windows);
        var previous = shown;
        double anchorCenter = anchor.Left + anchor.ActualWidth / 2;

        for (int i = 0; i < windows.Count; i++)
        {
            var w = windows[i];
            var (left, top) = targets[i];

            // Glide from where the previous preview was, or slide in from the anchor's side.
            double fromLeft, fromTop;
            if (previous.Count > 0)
            {
                var from = previous[Math.Min(i, previous.Count - 1)];
                (fromLeft, fromTop) = (from.Left, from.Top);
            }
            else
            {
                fromLeft = left + (left >= anchorCenter ? -SlideDistance : SlideDistance);
                fromTop = top;
            }

            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = fromLeft;
            w.Top = fromTop;
            w.ShowActivated = false;
            w.ShowInTaskbar = false;
            w.Topmost = true;
            if (w.Content is UIElement content) content.Opacity = 0;
            w.SourceInitialized += (_, _) => MakeClickThrough(w);
            w.Closed += (_, _) => StopAnimation(w);
            w.Show();

            Animate(w, fromLeft, left, fromTop, top, 0, 1,
                previous.Count > 0 ? MoveDuration : FadeInDuration);
        }

        FadeOutAndClose(previous);
        shown = windows.ToList();
        shownKey = key;
    }

    /// <summary>Where each window goes; shrinks them first if they don't fit beside the anchor.</summary>
    private List<(double Left, double Top)> Place(IReadOnlyList<Window> windows)
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

        var targets = new List<(double, double)>(windows.Count);
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

            targets.Add((left, Math.Max(wa.Top, Math.Min(anchor.Top, wa.Bottom - w.Height))));
            left += w.Width + Gap * scale;
        }
        return targets;
    }

    private void FadeOutAndClose(IEnumerable<Window> windows)
    {
        foreach (var w in windows.ToList())
        {
            double from = w.Content is UIElement content ? content.Opacity : 1;
            Animate(w, w.Left, w.Left, w.Top, w.Top, from, 0, FadeOutDuration, () => w.Close());
        }
    }

    private void CloseWindow(Window w)
    {
        StopAnimation(w);
        w.Close();
    }

    // Eased per-frame tween of position and content opacity. Window.Left/Top can't be animated with
    // storyboards smoothly, so this drives them from the render loop.
    private void Animate(Window w, double fromLeft, double toLeft, double fromTop, double toTop,
        double fromOpacity, double toOpacity, TimeSpan duration, Action? done = null)
    {
        StopAnimation(w);
        var content = w.Content as UIElement;
        var clock = Stopwatch.StartNew();

        EventHandler? tick = null;
        tick = (_, _) =>
        {
            double t = Math.Min(1, clock.Elapsed.TotalMilliseconds / duration.TotalMilliseconds);
            double eased = 1 - Math.Pow(1 - t, 3);

            if (fromLeft != toLeft) w.Left = fromLeft + (toLeft - fromLeft) * eased;
            if (fromTop != toTop) w.Top = fromTop + (toTop - fromTop) * eased;
            if (content is not null) content.Opacity = fromOpacity + (toOpacity - fromOpacity) * eased;

            if (t < 1) return;
            StopAnimation(w);
            done?.Invoke();
        };

        animations[w] = tick;
        CompositionTarget.Rendering += tick;
    }

    private void StopAnimation(Window w)
    {
        if (!animations.Remove(w, out var tick)) return;
        CompositionTarget.Rendering -= tick;
    }

    private static void MakeClickThrough(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        NativeWindowHelper.SetExtendedStyle(hwnd,
            NativeWindowHelper.GetExtendedStyle(hwnd) | NativeWindowHelper.ClickThroughExtendedStyle | NativeMethods.WS_EX_TOOLWINDOW);
    }
}
