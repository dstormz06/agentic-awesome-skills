using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AndaCompanion.Services;

public enum CaptureOutcome
{
    Captured,
    NothingSelected,
    TerminalInFront,
    ModifiersHeld,
    ClipboardBusy,
    ClipboardTooLarge,
    Unsupported,
}

public sealed record CaptureResult(CaptureOutcome Outcome, string? Text = null)
{
    /// <summary>A short plain-language notice for outcomes the user should hear about; null when silence is right.</summary>
    public string? Notice => Outcome switch
    {
        CaptureOutcome.TerminalInFront => "Paused in terminals. Please type the number.",
        CaptureOutcome.ModifiersHeld => "Please release Ctrl and Alt, then try again",
        CaptureOutcome.ClipboardBusy => "The clipboard is busy. Please try again",
        CaptureOutcome.ClipboardTooLarge => "Clipboard too large. Please use the lookup box",
        _ => null,
    };
}

/// <summary>
/// Captures the ANDA number the user has selected.
///
/// v0 "Select &amp; Peek" copies the selection with a synthesized Ctrl+C and puts the user's
/// clipboard back afterwards. Every step below exists because the naive version fails or
/// hurts someone:
///   1. Terminals are skipped: Ctrl+C there is an interrupt that would kill a running command.
///   2. The user is still holding Ctrl+Alt from the hotkey. A masking keystroke stops the Alt
///      release from activating the app's menu bar, and we WAIT for release so the target app
///      sees Ctrl+C rather than Ctrl+Alt+C.
///   3. Success is detected by the clipboard sequence number, not by comparing text, so a
///      selection identical to the current clipboard still works; a fixed sleep is replaced by polling.
///   4. The clipboard is snapshotted in every plain-memory format first and restored after, with
///      a real owner window (see ClipboardOwner); oversized clipboards are refused, not half-saved.
///   5. Runs off the UI thread (the caller wraps it in Task.Run).
/// UI Automation TextPattern capture is the planned v1: no keystroke, no clipboard involved.
/// </summary>
public static class TextCaptureService
{
    private const ushort MaskKey = 0xE8;   // unassigned virtual key: marks Alt as "used" without doing anything
    private static readonly ushort[] Modifiers = { 0x10 /*Shift*/, 0x11 /*Ctrl*/, 0x12 /*Alt*/, 0x5B /*LWin*/, 0x5C /*RWin*/ };

    public static (int X, int Y) GetCursorPosition()
    {
        if (OperatingSystem.IsWindows() && GetCursorPos(out var p)) return (p.X, p.Y);
        return (200, 200);
    }

    public static CaptureResult CaptureSelection()
    {
        if (!OperatingSystem.IsWindows()) return new(CaptureOutcome.Unsupported);
        try
        {
            if (TerminalGuard.IsTerminal(ForegroundWindowClass()))
                return new(CaptureOutcome.TerminalInFront);

            SendKeyTap(MaskKey);
            if (!WaitForModifierRelease(TimeSpan.FromMilliseconds(1200)))
                return new(CaptureOutcome.ModifiersHeld);

            var snapshot = ClipboardSnapshot.Take();
            if (snapshot is null) return new(CaptureOutcome.ClipboardBusy);
            if (snapshot.TooLarge) return new(CaptureOutcome.ClipboardTooLarge);

            uint sequenceBefore = ClipboardNative.GetClipboardSequenceNumber();
            SendCtrlC();
            if (!WaitForClipboardChange(sequenceBefore, TimeSpan.FromMilliseconds(600)))
                return new(CaptureOutcome.NothingSelected);

            string? text = ReadUnicodeText();
            snapshot.Restore();
            return text is { Length: > 0 } ? new(CaptureOutcome.Captured, text) : new(CaptureOutcome.NothingSelected);
        }
        catch
        {
            return new(CaptureOutcome.NothingSelected);
        }
    }

    // ---------- helpers (internal so the Windows CI tests can exercise them directly) ----------

    internal static bool WaitForModifierRelease(TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (AnyModifierDown())
        {
            if (sw.Elapsed > timeout) return false;
            Thread.Sleep(10);
        }
        return true;
    }

    internal static bool AnyModifierDown() =>
        Modifiers.Any(vk => (GetAsyncKeyState(vk) & 0x8000) != 0);

    internal static bool WaitForClipboardChange(uint sequenceBefore, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (ClipboardNative.GetClipboardSequenceNumber() == sequenceBefore)
        {
            if (sw.Elapsed > timeout) return false;
            Thread.Sleep(15);
        }
        return true;
    }

    internal static string? ReadUnicodeText()
    {
        if (!ClipboardNative.OpenWithRetry(ClipboardOwner.Handle)) return null;
        try
        {
            IntPtr handle = ClipboardNative.GetClipboardData(ClipboardNative.CfUnicodeText);
            if (handle == IntPtr.Zero) return null;
            IntPtr ptr = ClipboardNative.GlobalLock(handle);
            if (ptr == IntPtr.Zero) return null;
            try
            {
                // A selection can be enormous; the identifier we need is short. Cap at 64K characters.
                int chars = (int)Math.Min((long)ClipboardNative.GlobalSize(handle) / 2, 65536);
                string text = Marshal.PtrToStringUni(ptr, chars);
                int nul = text.IndexOf('\0');
                return nul >= 0 ? text[..nul] : text;
            }
            finally { ClipboardNative.GlobalUnlock(handle); }
        }
        finally { ClipboardNative.CloseClipboard(); }
    }

    internal static string ForegroundWindowClass()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return string.Empty;
        var sb = new StringBuilder(256);
        return GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
    }

    /// <summary>Injects raw key events. Used by the self-test to press the hotkeys for real.</summary>
    internal static void SendKeys(params (ushort Vk, bool Up)[] keys) =>
        SendInput(keys.Select(k => KeyInput(k.Vk, k.Up)).ToArray());

    /// <summary>Injects a middle-button click (optionally with Ctrl held). Used by the self-test to exercise the middle-click trigger for real.</summary>
    internal static void SendMiddleClick(bool ctrl)
    {
        var list = new List<Input>();
        if (ctrl) list.Add(KeyInput(0x11, up: false));
        list.Add(new Input { type = 0, u = new InputUnion { mi = new MouseInput { dwFlags = 0x0020 } } });   // MOUSEEVENTF_MIDDLEDOWN
        list.Add(new Input { type = 0, u = new InputUnion { mi = new MouseInput { dwFlags = 0x0040 } } });   // MOUSEEVENTF_MIDDLEUP
        if (ctrl) list.Add(KeyInput(0x11, up: true));
        SendInput(list.ToArray());
    }

    private static void SendCtrlC() => SendInput(new[]
    {
        KeyInput(0x11, up: false), // Ctrl down
        KeyInput(0x43, up: false), // C down
        KeyInput(0x43, up: true),  // C up
        KeyInput(0x11, up: true),  // Ctrl up
    });

    private static void SendKeyTap(ushort vk) => SendInput(new[] { KeyInput(vk, up: false), KeyInput(vk, up: true) });

    private static void SendInput(Input[] inputs) => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());

    private static Input KeyInput(ushort vk, bool up) => new()
    {
        type = 1, // INPUT_KEYBOARD
        u = new InputUnion { ki = new KeyboardInput { wVk = vk, dwFlags = up ? 2u : 0u } },
    };

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion { [FieldOffset(0)] public KeyboardInput ki; [FieldOffset(0)] public MouseInput mi; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint type; public InputUnion u; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point lpPoint);
    [DllImport("user32.dll")] private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
}
