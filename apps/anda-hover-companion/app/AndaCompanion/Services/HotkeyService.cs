using System.Runtime.InteropServices;

namespace AndaCompanion.Services;

public sealed record HotkeyBinding(int Id, string Name, string Combo, uint Modifiers, uint VirtualKey, string Fallback);

public sealed record HotkeyStatus(HotkeyBinding Binding, bool Registered, int Win32Error);

/// <summary>
/// The slice of RegisterHotKey the service needs, so registration outcomes
/// (success, "already in use", other failures) are unit-testable on any OS.
/// </summary>
public interface IHotkeyApi
{
    bool Register(int id, uint modifiers, uint virtualKey, out int win32Error);
    void Unregister(int id);
}

/// <summary>
/// Global hotkeys via Win32 RegisterHotKey on a dedicated message-loop thread.
/// Windows-only at runtime; compiles everywhere. With a null HWND, WM_HOTKEY is
/// posted to the registering thread's queue, so no hidden window is needed.
///
/// Registration is CHECKED: if another program already owns a combination, the
/// user is told immediately (and what to use instead) rather than the demo
/// silently doing nothing.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    public const int IdPeek = 1;    // Ctrl+Alt+A     → peek at the current selection
    public const int IdLookup = 2;  // Ctrl+Alt+Space → lookup box
    public const int IdDraft = 3;   // Ctrl+Alt+D     → check the selected draft

    private const int WmHotkey = 0x0312;
    private const int WmQuit = 0x0012;
    private const int ErrorHotkeyAlreadyRegistered = 1409;
    private const uint ModAlt = 0x1, ModControl = 0x2, ModNoRepeat = 0x4000;

    public static readonly IReadOnlyList<HotkeyBinding> Bindings = new[]
    {
        new HotkeyBinding(IdPeek, "Select & Peek", "Ctrl+Alt+A",
            ModControl | ModAlt | ModNoRepeat, 0x41, "Choose another shortcut in Settings, or use the tray icon."),
        new HotkeyBinding(IdLookup, "Lookup", "Ctrl+Alt+Space",
            ModControl | ModAlt | ModNoRepeat, 0x20, "Choose another shortcut in Settings, or use the tray icon."),
        new HotkeyBinding(IdDraft, "Check a draft", "Ctrl+Alt+D",
            ModControl | ModAlt | ModNoRepeat, 0x44, "Choose another shortcut in Settings, or use the tray icon."),
    };

    /// <summary>The three bindings with the shortcuts a person chose. An invalid or duplicate choice falls back to its default.</summary>
    public static IReadOnlyList<HotkeyBinding> FromCombos(string? peek, string? lookup, string? draft)
    {
        var chosen = new[] { (IdPeek, peek), (IdLookup, lookup), (IdDraft, draft) };
        var result = new List<HotkeyBinding>();
        var taken = new HashSet<string>();
        foreach (var (id, combo) in chosen)
        {
            var def = Bindings.Single(b => b.Id == id);
            if (HotkeyParser.TryParse(combo, out uint mods, out uint vk, out string norm, out _) && taken.Add(norm))
                result.Add(def with { Combo = norm, Modifiers = mods | ModNoRepeat, VirtualKey = vk });
            else { taken.Add(def.Combo); result.Add(def); }
        }
        return result;
    }

    /// <summary>Registers every binding and reports each outcome. Pure apart from the api calls.</summary>
    public static IReadOnlyList<HotkeyStatus> RegisterAll(IHotkeyApi api, IReadOnlyList<HotkeyBinding>? bindings = null)
    {
        bindings ??= Bindings;
        var result = new List<HotkeyStatus>(bindings.Count);
        foreach (var b in bindings)
        {
            bool ok = api.Register(b.Id, b.Modifiers, b.VirtualKey, out int error);
            result.Add(new HotkeyStatus(b, ok, ok ? 0 : error));
        }
        return result;
    }

    /// <summary>
    /// One line per shortcut for the Settings panel: a tick, or what is wrong and what to do. Names the likely cause (another program
    /// already owns the combination) so a shortcut that does nothing is never a mystery.
    /// </summary>
    public static string Report(IReadOnlyList<HotkeyStatus> status)
    {
        if (status.Count == 0) return "Shortcuts: starting…";
        return string.Join("\n", status.Select(s => s.Registered
            ? $"✓ {s.Binding.Name}: {s.Binding.Combo} is active"
            : s.Win32Error == ErrorHotkeyAlreadyRegistered
                ? $"✗ {s.Binding.Name}: {s.Binding.Combo} is already used by another program (many apps claim a global shortcut). Choose a different one above; the tray icon always works."
                : $"✗ {s.Binding.Name}: {s.Binding.Combo} could not be registered (Windows error {s.Win32Error}). Choose a different one above; the tray icon always works."));
    }

    /// <summary>A plain-language problem report, or null when the hotkey registered fine.</summary>
    public static string? Describe(HotkeyStatus s) =>
        s.Registered ? null
        : s.Win32Error == ErrorHotkeyAlreadyRegistered
            ? $"{s.Binding.Combo} is already used by another program. {s.Binding.Fallback}"
            : $"{s.Binding.Combo} could not be registered (Windows error {s.Win32Error}). {s.Binding.Fallback}";

    private readonly Action<int> _onHotkey;
    private readonly Action<string>? _onProblem;
    private readonly IHotkeyApi _api;
    private readonly ManualResetEventSlim _ready = new();
    private Thread? _thread;
    private volatile uint _threadId;
    private volatile IReadOnlyList<HotkeyBinding> _bindings;
    private volatile IReadOnlyList<HotkeyBinding>? _pending;
    private const int WmApp = 0x8000;

    /// <summary>Per-binding outcome; empty until <see cref="WaitReady"/> returns true.</summary>
    public IReadOnlyList<HotkeyStatus> Status { get; private set; } = Array.Empty<HotkeyStatus>();

    public HotkeyService(Action<int> onHotkey, Action<string>? onProblem = null, IHotkeyApi? api = null,
        IReadOnlyList<HotkeyBinding>? bindings = null)
    {
        _onHotkey = onHotkey;
        _onProblem = onProblem;
        _api = api ?? new Win32HotkeyApi();
        _bindings = bindings ?? Bindings;
    }

    /// <summary>
    /// Swaps the registered shortcuts while the app runs (a change in Settings). The message loop unregisters the old ones and
    /// registers the new ones on its own thread, because RegisterHotKey belongs to the thread that called it.
    /// </summary>
    public void Reconfigure(IReadOnlyList<HotkeyBinding> bindings)
    {
        _pending = bindings;
        if (_threadId != 0) PostThreadMessage(_threadId, WmApp, IntPtr.Zero, IntPtr.Zero);
        else _bindings = bindings;   // not started (or not Windows): the next Start uses them
    }

    public void Start()
    {
        if (!OperatingSystem.IsWindows()) { _ready.Set(); return; }
        _thread = new Thread(MessageLoop) { IsBackground = true, Name = "AndaCompanion.Hotkeys" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public bool WaitReady(TimeSpan timeout) => _ready.Wait(timeout);

    private void MessageLoop()
    {
        _threadId = GetCurrentThreadId();
        // Force this thread's message queue into existence before anything can post to it.
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0 /* PM_NOREMOVE */);

        var status = RegisterAll(_api, _bindings);
        Status = status;
        _ready.Set();
        foreach (var s in status)
            if (Describe(s) is { } problem) SafeProblem(problem);

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WmApp && _pending is { } next)
            {
                _pending = null;
                foreach (var old in status) if (old.Registered) _api.Unregister(old.Binding.Id);
                _bindings = next;
                status = RegisterAll(_api, next);
                Status = status;
                foreach (var s in status)
                    if (Describe(s) is { } problem) SafeProblem(problem);
                continue;
            }
            if (msg.message != WmHotkey) continue;
            try { _onHotkey((int)msg.wParam); }
            catch { /* a handler failure must never kill the message loop */ }
        }

        foreach (var s in status)
            if (s.Registered) _api.Unregister(s.Binding.Id);
    }

    private void SafeProblem(string problem)
    {
        try { _onProblem?.Invoke(problem); } catch { /* reporting must not throw */ }
    }

    public void Dispose()
    {
        if (_thread is null) return;
        _ready.Wait(TimeSpan.FromSeconds(2));
        if (_threadId != 0) PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(1));
    }

    private sealed class Win32HotkeyApi : IHotkeyApi
    {
        public bool Register(int id, uint modifiers, uint virtualKey, out int win32Error)
        {
            bool ok = RegisterHotKey(IntPtr.Zero, id, modifiers, virtualKey);
            win32Error = ok ? 0 : Marshal.GetLastWin32Error();
            return ok;
        }

        public void Unregister(int id) => UnregisterHotKey(IntPtr.Zero, id);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam;
        public uint time; public int ptX; public int ptY;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Msg lpMsg, IntPtr hWnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
