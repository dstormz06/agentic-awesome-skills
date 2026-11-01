namespace AndaCompanion.Services;

/// <summary>
/// Select &amp; Peek works by sending Ctrl+C to the app in front. In a terminal,
/// Ctrl+C with nothing selected is an INTERRUPT: it would kill the user's running
/// command. So terminals are never sent a keystroke. The list covers the common
/// terminal window classes; the Settings switch for copy-based capture is the
/// backstop for anything it misses (e.g. an editor's embedded terminal panel).
/// </summary>
public static class TerminalGuard
{
    private static readonly HashSet<string> TerminalClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ConsoleWindowClass",              // classic console host (cmd, PowerShell, WSL)
        "CASCADIA_HOSTING_WINDOW_CLASS",   // Windows Terminal
        "PuTTY",
        "mintty",                          // Git Bash, Cygwin
        "VirtualConsoleClass",             // ConEmu / Cmder
        "Alacritty",
        "org.wezfurlong.wezterm",
    };

    public static bool IsTerminal(string? windowClass) =>
        windowClass is { Length: > 0 } && TerminalClasses.Contains(windowClass);
}
