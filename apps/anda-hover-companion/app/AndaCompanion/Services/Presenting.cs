using System.Runtime.InteropServices;
using System.Text;

namespace AndaCompanion.Services;

/// <summary>
/// Round 42 review: whether the window in front covers its whole screen (a slide show, a full-screen program or video call), so a notice
/// waits and ANDA numbers do not appear on a projected screen. Only user32, already used by the app, is called (the app keeps to three
/// Windows libraries). It cannot see Focus assist, nor a screen share of a window that is not full screen. Elsewhere than Windows, never.
/// </summary>
public static class Presenting
{
    internal static Func<bool?>? StateForTests;

    public static bool Now() => (StateForTests is { } f ? f() : Query()) == true;

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    private const int Style = -16, Caption = 0x00C00000;      // GWL_STYLE, WS_CAPTION
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr hwnd, StringBuilder name, int max);

    private static bool? Query()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            // a maximized window with a title bar is ordinary work, even where it reaches the screen's edges (an auto-hidden taskbar); a browser in
            // full screen may also say it is maximized, but has no title bar, so it still holds the notice (round 42 re-review)
            if (hwnd == IntPtr.Zero || (IsZoomed(hwnd) && (GetWindowLong(hwnd, Style) & Caption) == Caption) || !GetWindowRect(hwnd, out var r)) return false;
            var name = new StringBuilder(64); GetClassNameW(hwnd, name, name.Capacity);
            if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;       // the desktop and the taskbar
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfoW(MonitorFromWindow(hwnd, 2), ref info)) return false;
            var m = info.Monitor;
            return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return null; }
    }
}
