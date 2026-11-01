using System.Runtime.InteropServices;
using System.Text;

namespace AndaCompanion.Services;

/// <summary>Win32 clipboard primitives. Internal: tests reach them via InternalsVisibleTo.</summary>
internal static class ClipboardNative
{
    public const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;

    /// <summary>Another process can hold the clipboard open for a few milliseconds; retry briefly.</summary>
    public static bool OpenWithRetry(IntPtr owner, int attempts = 12, int delayMs = 10)
    {
        for (int i = 0; i < attempts; i++)
        {
            if (OpenClipboard(owner)) return true;
            Thread.Sleep(delayMs);
        }
        return false;
    }

    /// <summary>
    /// Formats whose data is a GDI/OLE handle rather than plain global memory, or whose
    /// contents are process-specific. They cannot be saved and replayed byte-for-byte.
    /// </summary>
    public static bool IsHandleOnly(uint format)
    {
        if (format is 2 /*CF_BITMAP*/ or 3 /*CF_METAFILEPICT*/ or 9 /*CF_PALETTE*/ or 14 /*CF_ENHMETAFILE*/
                   or 0x80 /*CF_OWNERDISPLAY*/ or 0x82 or 0x83 or 0x8E /*CF_DSP* handle variants*/)
            return true;
        if (format is >= 0x200 and <= 0x3FF) return true;   // CF_PRIVATE*/CF_GDIOBJ* ranges
        if (format >= 0xC000)
        {
            // OLE bookkeeping formats embed process-specific data; replaying them can confuse paste targets.
            string name = FormatName(format);
            if (name is "DataObject" or "Ole Private Data") return true;
        }
        return false;
    }

    public static string FormatName(uint format)
    {
        var sb = new StringBuilder(128);
        return GetClipboardFormatName(format, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
    }

    /// <summary>Allocates movable global memory holding <paramref name="data"/>; the clipboard takes ownership on success.</summary>
    public static IntPtr AllocGlobal(byte[] data)
    {
        IntPtr h = GlobalAlloc(GmemMoveable, (UIntPtr)data.Length);
        if (h == IntPtr.Zero) return IntPtr.Zero;
        IntPtr p = GlobalLock(h);
        if (p == IntPtr.Zero) { GlobalFree(h); return IntPtr.Zero; }
        Marshal.Copy(data, 0, p, data.Length);
        GlobalUnlock(h);
        return h;
    }

    [DllImport("user32.dll", SetLastError = true)] public static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")] public static extern bool CloseClipboard();
    [DllImport("user32.dll")] public static extern bool EmptyClipboard();
    [DllImport("user32.dll")] public static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll")] public static extern IntPtr GetClipboardData(uint uFormat);
    [DllImport("user32.dll")] public static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
    public static uint RegisterFormat(string name) => RegisterClipboardFormat(name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string lpszFormat);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClipboardFormatName(uint format, StringBuilder lpszFormatName, int cchMaxCount);
    [DllImport("kernel32.dll")] public static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
    [DllImport("kernel32.dll")] public static extern IntPtr GlobalFree(IntPtr hMem);
    [DllImport("kernel32.dll")] public static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")] public static extern bool GlobalUnlock(IntPtr hMem);
    [DllImport("kernel32.dll")] public static extern UIntPtr GlobalSize(IntPtr hMem);
}

/// <summary>
/// A real, message-pumping, never-visible window to own the clipboard while we write to it.
///
/// Why this exists: opening the clipboard with a NULL owner makes SetClipboardData fail
/// (EmptyClipboard succeeds, then nothing can be written), which would turn "restore the
/// user's clipboard" into "empty the user's clipboard". And the owner must pump messages:
/// Windows sends it WM_DESTROYCLIPBOARD when another app takes the clipboard, and a window
/// that never pumps would stall that other app. The window lives on its own thread for the
/// life of the process.
/// </summary>
internal static class ClipboardOwner
{
    private static readonly object Gate = new();
    private static IntPtr _hwnd;

    public static IntPtr Handle
    {
        get { lock (Gate) { if (_hwnd == IntPtr.Zero) Start(); return _hwnd; } }
    }

    private static void Start()
    {
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            _hwnd = CreateWindowEx(0, "STATIC", "AndaCompanion.ClipboardOwner", 0x80000000 /* WS_POPUP, never shown */,
                0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            ready.Set();
            if (_hwnd == IntPtr.Zero) return;
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        })
        { IsBackground = true, Name = "AndaCompanion.ClipboardOwner" };
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);   // the owner window needs an STA pump
        thread.Start();
        ready.Wait(TimeSpan.FromSeconds(2));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam;
        public uint time; public int ptX; public int ptY;
    }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern int GetMessage(out Msg msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Msg msg);
}

/// <summary>
/// A copy of every plain-memory format currently on the clipboard, so Select &amp; Peek can put the
/// user's clipboard back exactly as it found it. Formats that are GDI handles (bitmaps, metafiles)
/// or process-specific OLE data cannot be replayed; their presence is recorded in
/// <see cref="Lossy"/> (Windows re-derives a bitmap from the DIB format, which IS saved).
/// Anything over <see cref="MaxBytes"/> is refused outright rather than half-saved.
/// </summary>
internal sealed class ClipboardSnapshot
{
    public const long MaxBytes = 32L * 1024 * 1024;

    private readonly List<(uint Format, byte[] Data)> _items = new();

    public bool Lossy { get; private set; }
    public bool TooLarge { get; private set; }
    public int FormatCount => _items.Count;

    /// <summary>Null when the clipboard could not be opened.</summary>
    public static ClipboardSnapshot? Take()
    {
        if (!ClipboardNative.OpenWithRetry(ClipboardOwner.Handle)) return null;
        try
        {
            var snap = new ClipboardSnapshot();
            long total = 0;
            uint format = 0;
            while ((format = ClipboardNative.EnumClipboardFormats(format)) != 0)
            {
                if (ClipboardNative.IsHandleOnly(format)) { snap.Lossy = true; continue; }

                IntPtr handle = ClipboardNative.GetClipboardData(format);
                if (handle == IntPtr.Zero) continue;
                long size = (long)ClipboardNative.GlobalSize(handle);
                if (size <= 0) continue;

                total += size;
                if (total > MaxBytes) { snap.TooLarge = true; break; }

                IntPtr ptr = ClipboardNative.GlobalLock(handle);
                if (ptr == IntPtr.Zero) continue;
                try
                {
                    var bytes = new byte[size];
                    Marshal.Copy(ptr, bytes, 0, (int)size);
                    snap._items.Add((format, bytes));
                }
                finally { ClipboardNative.GlobalUnlock(handle); }
            }
            return snap;
        }
        finally { ClipboardNative.CloseClipboard(); }
    }

    /// <summary>Replaces the clipboard with the saved formats. An originally-empty clipboard is left empty.</summary>
    public bool Restore()
    {
        if (!ClipboardNative.OpenWithRetry(ClipboardOwner.Handle)) return false;
        try
        {
            ClipboardNative.EmptyClipboard();
            foreach (var (format, data) in _items)
            {
                IntPtr handle = ClipboardNative.AllocGlobal(data);
                if (handle == IntPtr.Zero) continue;
                if (ClipboardNative.SetClipboardData(format, handle) == IntPtr.Zero)
                    ClipboardNative.GlobalFree(handle);   // the clipboard only takes ownership on success
            }
            return true;
        }
        finally { ClipboardNative.CloseClipboard(); }
    }

    /// <summary>Test/diagnostic helper: the saved bytes for a format, or null.</summary>
    public byte[]? GetBytes(uint format) => _items.FirstOrDefault(i => i.Format == format).Data;
}
