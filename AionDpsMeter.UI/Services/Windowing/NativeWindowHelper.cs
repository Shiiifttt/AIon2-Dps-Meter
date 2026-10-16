namespace AionDpsMeter.UI.Services.Windowing.Native;

internal static class NativeWindowHelper
{
    public const long ClickThroughExtendedStyle =
        NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_NOACTIVATE;

    public static bool IsValid(IntPtr hwnd) =>
        NativeMethods.IsWindow(hwnd);

    public static long GetExtendedStyle(IntPtr hwnd) =>
        NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();

    public static void SetExtendedStyle(IntPtr hwnd, long style) =>
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style));

    public static uint GetStyle(IntPtr hwnd) =>
        NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);

    public static bool IsEnabled(IntPtr hwnd) =>
        (GetStyle(hwnd) & NativeMethods.WS_DISABLED) == 0;

    public static void SetEnabled(IntPtr hwnd, bool enabled) =>
        NativeMethods.EnableWindow(hwnd, enabled);

    public static bool IsCursorOver(IntPtr hwnd) =>
        NativeMethods.GetCursorPos(out var cursor) &&
        NativeMethods.GetWindowRect(hwnd, out var rect) &&
        cursor.X >= rect.Left && cursor.X < rect.Right &&
        cursor.Y >= rect.Top && cursor.Y < rect.Bottom;

    /// <summary>
    /// Tells every child window (the WebView2 ones included) that the mouse has left. WebView2 sometimes
    /// misses the real one, e.g. when click-through flips while hovering, and keeps :hover and tooltips stuck.
    /// </summary>
    public static void SendMouseLeave(IntPtr hwnd)
    {
        foreach (var child in GetChildWindows(hwnd))
            NativeMethods.PostMessage(child, NativeMethods.WM_MOUSELEAVE, IntPtr.Zero, IntPtr.Zero);
    }

    public static IReadOnlyList<IntPtr> GetChildWindows(IntPtr parent)
    {
        var children = new List<IntPtr>();

        NativeMethods.EnumChildWindows(
            parent,
            (child, _) =>
            {
                if (IsValid(child))
                {
                    children.Add(child);
                }

                // Keep enumerating.
                return true;
            },
            IntPtr.Zero);

        return children;
    }
}
