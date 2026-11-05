namespace AndaCompanion.Services;

/// <summary>
/// Where user data lives. Default: %AppData%\AndaCompanion. Setting the
/// ANDA_COMPANION_HOME environment variable relocates everything, which gives
/// three things for free: a portable mode that never touches AppData (the demo
/// bundle's launcher uses it), isolated test runs, and a clean story for IT —
/// the app writes exactly two JSON files, and you choose the folder.
/// </summary>
public static class AppPaths
{
    public const string HomeEnvVar = "ANDA_COMPANION_HOME";

    public static string Root =>
        Environment.GetEnvironmentVariable(HomeEnvVar) is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AndaCompanion");

    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string UsageFile => Path.Combine(Root, "usage.csv");
    public static string ClosuresFile => Path.Combine(Root, "closures.txt");
    public static string PromptsFile => Path.Combine(Root, "prompts.json");
    public static string RepliesFile => Path.Combine(Root, "replies.json");
    public static string TodoFile => Path.Combine(Root, "todo.json");
    public static string ChecklistFile => Path.Combine(Root, "checklist.json");
    public static string ReferenceDownloadFile => Path.Combine(Root, "drugsfda-downloaded.zip");
}
