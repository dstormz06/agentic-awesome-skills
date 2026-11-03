using System.Text.Json;
using System.Text.RegularExpressions;

namespace AndaCompanion.Services;

public sealed record RegistrationStatus(bool Registered, string Message);

/// <summary>
/// Connects the Edge extension to this app: writes the native-messaging host manifest and the current-user registry entry that
/// tells Edge where it is. Current user only (no admin rights), and removable. The manifest lists exactly one extension ID, so no
/// other extension can start the host.
/// </summary>
public static class NativeHostRegistration
{
    public const string HostName = "com.anda_hover_companion.host";
    public const string DefaultExtensionId = "kkohidikfneelkeiojlmdinpncfmgjcl";
    public const string EdgeKey = @"Software\Microsoft\Edge\NativeMessagingHosts";
    public const string DirEnvVar = "ANDA_NATIVE_HOST_DIR";

    public static string HostDir => Environment.GetEnvironmentVariable(DirEnvVar) is { Length: > 0 } d ? d
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AndaHoverCompanion", "native-host");
    public static string ManifestPath => Path.Combine(HostDir, HostName + ".json");
    private static string HomeFile => Path.Combine(HostDir, "home.txt");

    public static bool IsValidExtensionId(string? id) => id is not null && Regex.IsMatch(id, "^[a-p]{32}$");

    public static string ManifestJson(string exePath, string extensionId) => JsonSerializer.Serialize(new
    {
        name = HostName,
        description = "ANDA Companion (Notula): lets the browser extension read your own data through the desktop app",
        path = exePath,
        type = "stdio",
        allowed_origins = new[] { $"chrome-extension://{extensionId}/" },
    }, new JsonSerializerOptions { WriteIndented = true });

    /// <summary>
    /// The browser starts the host without the app's environment, so the data folder the app was using (for example the demo's
    /// own folder) is remembered in a small file next to the manifest and adopted by the host.
    /// </summary>
    public static void AdoptInstalledHome()
    {
        try
        {
            if (Environment.GetEnvironmentVariable(AppPaths.HomeEnvVar) is { Length: > 0 }) return;
            if (File.Exists(HomeFile) && File.ReadAllText(HomeFile).Trim() is { Length: > 0 } home && Directory.Exists(home))
                Environment.SetEnvironmentVariable(AppPaths.HomeEnvVar, home);
        }
        catch { /* fall back to the default data folder */ }
    }

    /// <summary>Writes the manifest and the home pointer. The registry step is separate so it can be tested and skipped off Windows.</summary>
    public static string WriteFiles(string exePath, string extensionId)
    {
        if (!IsValidExtensionId(extensionId)) throw new ArgumentException("An extension ID is 32 letters from a to p.");
        Directory.CreateDirectory(HostDir);
        File.WriteAllText(ManifestPath, ManifestJson(exePath, extensionId));
        File.WriteAllText(HomeFile, AppPaths.Root);
        return ManifestPath;
    }

    public static (bool Ok, string Message) Install(string exePath, string extensionId, string registryBase = EdgeKey)
    {
        try
        {
            string manifest = WriteFiles(exePath, extensionId);
            if (!OperatingSystem.IsWindows()) return (false, "The connection can only be registered on Windows.");
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey($@"{registryBase}\{HostName}");
            key.SetValue("", manifest);
            return (true, "Connected. Reload the extension in Edge (edge://extensions) once, then use its Data page to test.");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return (false, ex is ArgumentException ? ex.Message : "The connection could not be written: " + ex.Message);
        }
    }

    public static (bool Ok, string Message) Uninstall(string registryBase = EdgeKey)
    {
        try
        {
            if (OperatingSystem.IsWindows()) Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree($@"{registryBase}\{HostName}", throwOnMissingSubKey: false);
            foreach (var f in new[] { ManifestPath, HomeFile }) if (File.Exists(f)) File.Delete(f);
            return (true, "Disconnected. The extension keeps working on its own.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, "The connection could not be removed: " + ex.Message);
        }
    }

    public static RegistrationStatus Status(string registryBase = EdgeKey)
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return new(false, "Edge connection: Windows only.");
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"{registryBase}\{HostName}");
            if (key?.GetValue("") is not string path || path.Length == 0) return new(false, "Not connected to the Edge extension.");
            if (!File.Exists(path)) return new(false, "Connection is registered but its file is missing. Connect again.");
            string id = "";
            try { id = JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("allowed_origins")[0].GetString() ?? ""; } catch { }
            return new(true, $"Connected for the extension {id.Replace("chrome-extension://", "").TrimEnd('/')}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new(false, "Could not read the connection status."); }
    }
}
