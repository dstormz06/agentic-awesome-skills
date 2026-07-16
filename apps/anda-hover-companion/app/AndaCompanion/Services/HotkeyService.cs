using System.Runtime.InteropServices;

namespace AndaCompanion.Services;

/// <summary>
/// Global hotkeys via Win32 RegisterHotKey on a dedicated message-loop thread.
/// Windows-only at runtime; compiles everywhere. No hidden window is needed:
/// RegisterHotKey with a null HWND posts WM_HOTKEY to the registering thread's queue.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x1, ModControl = 0x2, ModNoRepeat = 0x4000;

    public const int IdPeek = 1;    // Ctrl+Alt+A → peek at the text selection / cursor
    public const int IdLookup = 2;  // Ctrl+Alt+Space → lookup window

    private readonly Action<int> _onHotkey;
    private Thread? _thread;
    private uint _threadId;

    public HotkeyService(Action<int> onHotkey) => _onHotkey = onHotkey;

    public void Start()
    {
        if (!OperatingSystem.IsWindows()) return;
        _thread = new Thread(MessageLoop) { IsBackground = true, Name = "AndaCompanion.Hotkeys" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void MessageLoop()
    {
        _threadId = GetCurrentThreadId();
        RegisterHotKey(IntPtr.Zero, IdPeek, ModControl | ModAlt | ModNoRepeat, (uint)'A');
        RegisterHotKey(IntPtr.Zero, IdLookup, ModControl | ModAlt | ModNoRepeat, 0x20 /* VK_SPACE */);
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WmHotkey) _onHotkey((int)msg.wParam);
        }
        UnregisterHotKey(IntPtr.Zero, IdPeek);
        UnregisterHotKey(IntPtr.Zero, IdLookup);
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam;
        public uint time; public int ptX; public int ptY;
    }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
