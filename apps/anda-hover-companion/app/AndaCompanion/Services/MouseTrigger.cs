using System.Runtime.InteropServices;

namespace AndaCompanion.Services;

/// <summary>When a press of the middle (wheel) button may start Select &amp; Peek. Pure, so it is tested on any OS.</summary>
public static class MouseTriggerRules
{
    /// <summary>
    /// True when the trigger is on and exactly the chosen modifier is held. "None" means no Ctrl, Shift or Alt at all, so a
    /// Ctrl+middle-click meant for another program is never taken. A click injected by other software is ignored, so nothing else
    /// can start a capture by simulating the mouse.
    /// </summary>
    public static bool ShouldTrigger(bool enabled, string modifier, bool ctrl, bool shift, bool alt, bool injected, bool allowInjected = false)
    {
        if (!enabled || (injected && !allowInjected)) return false;
        return modifier switch
        {
            "Ctrl" => ctrl && !shift && !alt,
            "Shift" => shift && !ctrl && !alt,
            "Alt" => alt && !ctrl && !shift,
            _ => !ctrl && !shift && !alt,
        };
    }
}

/// <summary>
/// Starts Select &amp; Peek from a middle-click. Off by default; the hook exists only while the setting is on. It uses a Windows
/// low-level mouse hook, which sees the button press and nothing else: it never reads the pointer position, never swallows the
/// click (the other program still gets it), and does its work off the hook thread so it cannot slow the mouse.
/// </summary>
public sealed class MouseTriggerService : IDisposable
{
    private const int WhMouseLl = 14, WmMButtonDown = 0x0207, WmQuit = 0x0012, LlmhfInjected = 0x1;
    private const int VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12;
    private const long DebounceMs = 400;

    private readonly Func<string> _modifier;
    private readonly Action _onTrigger;
    private readonly bool _allowInjected;
    private readonly ManualResetEventSlim _ready = new();
    private Thread? _thread;
    private volatile uint _threadId;
    private IntPtr _hook;
    private LowLevelMouseProc? _proc;      // kept in a field so the delegate is never collected while the hook is installed
    private long _lastTick;

    /// <summary>Null until started; then null on success or a plain sentence on failure.</summary>
    public string? Problem { get; private set; }

    public MouseTriggerService(Func<string> modifier, Action onTrigger, bool allowInjected = false)
    {
        _modifier = modifier; _onTrigger = onTrigger; _allowInjected = allowInjected;
    }

    public bool Start(TimeSpan? wait = null)
    {
        if (!OperatingSystem.IsWindows()) { Problem = "The middle-click trigger needs Windows."; return false; }
        _thread = new Thread(Loop) { IsBackground = true, Name = "AndaCompanion.MouseTrigger" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(wait ?? TimeSpan.FromSeconds(2));
        return Problem is null && _hook != IntPtr.Zero;
    }

    private void Loop()
    {
        _threadId = GetCurrentThreadId();
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        _proc = HookProc;
        _hook = SetWindowsHookEx(WhMouseLl, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) Problem = $"Windows would not start the middle-click trigger (error {Marshal.GetLastWin32Error()}). Your security software may block mouse hooks.";
        _ready.Set();
        if (_hook == IntPtr.Zero) return;
        while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && (int)wParam == WmMButtonDown)
            {
                bool injected = (Marshal.ReadInt32(lParam, 12) & LlmhfInjected) != 0;   // MSLLHOOKSTRUCT.flags
                if (MouseTriggerRules.ShouldTrigger(true, _modifier(), Down(VkControl), Down(VkShift), Down(VkMenu), injected, _allowInjected))
                {
                    long now = Environment.TickCount64;
                    if (now - Interlocked.Exchange(ref _lastTick, now) > DebounceMs)
                        Task.Run(() => { try { _onTrigger(); } catch { /* a handler failure must never reach the hook */ } });
                }
            }
        }
        catch { /* the hook must never throw into Windows */ }
        return CallNextHookEx(_hook, nCode, wParam, lParam);   // always pass the click on, untouched
    }

    private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        if (_thread is null) return;
        if (_threadId != 0) PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(1));
        _thread = null;
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int ptX; public int ptY; }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Msg lpMsg, IntPtr hWnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
