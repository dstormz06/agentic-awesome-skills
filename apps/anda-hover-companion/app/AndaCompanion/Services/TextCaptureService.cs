using System.Runtime.InteropServices;

namespace AndaCompanion.Services;

/// <summary>
/// Captures the ANDA number the user is pointing at.
///
/// v0 ships "Select &amp; Peek": the user selects text containing the number and presses
/// the hotkey; we copy the selection (Ctrl+C via SendInput) and restore the clipboard.
/// UI Automation TextPattern under-cursor capture is the planned v1 upgrade — it slots
/// in behind the same method without touching callers.
/// </summary>
public static class TextCaptureService
{
    public static (int X, int Y) GetCursorPosition()
    {
        if (OperatingSystem.IsWindows() && GetCursorPos(out var p)) return (p.X, p.Y);
        return (200, 200);
    }

    public static string? TryCaptureSelectedText()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            string? before = ReadClipboardText();
            SendCtrlC();
            Thread.Sleep(140); // give the foreground app time to service the copy
            string? captured = ReadClipboardText();
            if (before is not null && captured != before) WriteClipboardText(before); // restore
            return captured == before ? null : captured;
        }
        catch { return null; }
    }

    private static void SendCtrlC()
    {
        var inputs = new[]
        {
            KeyInput(0x11, false), // Ctrl down
            KeyInput(0x43, false), // C down
            KeyInput(0x43, true),  // C up
            KeyInput(0x11, true),  // Ctrl up
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    private static Input KeyInput(ushort vk, bool up) => new()
    {
        type = 1, // INPUT_KEYBOARD
        u = new InputUnion { ki = new KeyboardInput { wVk = vk, dwFlags = up ? 2u : 0u } },
    };

    private const uint CfUnicodeText = 13;

    private static string? ReadClipboardText()
    {
        if (!OpenClipboard(IntPtr.Zero)) return null;
        try
        {
            IntPtr handle = GetClipboardData(CfUnicodeText);
            if (handle == IntPtr.Zero) return null;
            IntPtr ptr = GlobalLock(handle);
            if (ptr == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUni(ptr); }
            finally { GlobalUnlock(handle); }
        }
        finally { CloseClipboard(); }
    }

    private static void WriteClipboardText(string text)
    {
        if (!OpenClipboard(IntPtr.Zero)) return;
        try
        {
            EmptyClipboard();
            int bytes = (text.Length + 1) * 2;
            IntPtr h = GlobalAlloc(0x2002 /* GMEM_MOVEABLE|GMEM_ZEROINIT */, (UIntPtr)bytes);
            if (h == IntPtr.Zero) return;
            IntPtr ptr = GlobalLock(h);
            Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
            GlobalUnlock(h);
            SetClipboardData(CfUnicodeText, h); // clipboard owns the handle on success
        }
        finally { CloseClipboard(); }
    }

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
    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint uFormat);
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr hMem);
}
