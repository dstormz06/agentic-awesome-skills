namespace AndaCompanion.Services;

/// <summary>
/// Where user data lives. Default: %AppData%\AndaCompanion. Setting the
/// ANDA_COMPANION_HOME environment variable relocates everything, which gives
/// three things for free: a portable mode that never touches AppData (the demo
/// bundle's launcher uses it), isolated test runs, and a clean story for IT —
/// the app writes only inside this one folder, and you choose the folder.
/// </summary>
public static class AppPaths
{
    public const string HomeEnvVar = "ANDA_COMPANION_HOME";

    public static string Root => RootFor(Environment.GetEnvironmentVariable(HomeEnvVar), AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    /// <summary>
    /// Where the app keeps what it stores. In order: the folder named by the environment variable; a folder named "data" beside the program
    /// (the demo ships one, so the demo never touches the person's profile and needs no launcher script); otherwise the person's own
    /// application-data folder.
    /// </summary>
    internal static string RootFor(string? fromEnvironment, string programFolder, string applicationData)
    {
        if (fromEnvironment is { Length: > 0 }) return fromEnvironment;
        try { string beside = Path.Combine(programFolder, "data"); if (Directory.Exists(beside)) return beside; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { /* an odd program path: the normal place is used */ }
        return Path.Combine(applicationData, "AndaCompanion");
    }

    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string UsageFile => Path.Combine(Root, "usage.csv");
    public static string ClosuresFile => Path.Combine(Root, "closures.txt");
    public static string PromptsFile => Path.Combine(Root, "prompts.json");
    public static string RepliesFile => Path.Combine(Root, "replies.json");
    public static string TodoFile => Path.Combine(Root, "todo.json");
    public static string MyTodoFile => Path.Combine(Root, "mytodo.json");
    public static string QuickNotesFile => Path.Combine(Root, "quick-notes.json");
    public static string ReferenceDownloadFile => Path.Combine(Root, "drugsfda-downloaded.zip");
}
