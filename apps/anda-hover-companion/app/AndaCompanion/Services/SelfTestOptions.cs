namespace AndaCompanion.Services;

/// <summary>
/// <c>AndaCompanion.exe --selftest [folder]</c> renders the real windows, exercises the Windows
/// integration (hotkeys, tray, clipboard, screen placement) and writes report.md, report.json and
/// PNGs to the folder. It is hermetic: settings and prompts go to a "home" subfolder, never to the
/// user's real AppData, and it makes no network calls.
/// </summary>
public sealed record SelfTestOptions(string OutDir)
{
    public const string Flag = "--selftest";

    public static SelfTestOptions? Parse(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a.Equals(Flag, StringComparison.OrdinalIgnoreCase))
            {
                string? dir = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : null;
                return new SelfTestOptions(Path.GetFullPath(dir ?? "selftest-out"));
            }
            if (a.StartsWith(Flag + "=", StringComparison.OrdinalIgnoreCase))
                return new SelfTestOptions(Path.GetFullPath(a[(Flag.Length + 1)..].Trim('"')));
        }
        return null;
    }
}
