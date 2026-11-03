using System.Globalization;

namespace AndaCompanion.Services;

/// <summary>
/// An optional, local usage log for the pilot scorecard. One line per event: date, event name and seconds. It never records an
/// ANDA number, a name, a draft or any other text, and it is off unless the person turns it on in Settings. The file is plain CSV
/// in the app's data folder; the person reads it, and shares it only if they choose to. Nothing is sent anywhere.
/// </summary>
public static class UsageLog
{
    /// <summary>The only event names that can be written. Anything else is dropped, so free text can never reach the file.</summary>
    public static readonly IReadOnlySet<string> Events = new HashSet<string>
    {
        "card_open", "peek_auto_open", "copy_status", "copy_reply", "draft_check", "ai_proofread", "ai_rewrite", "ai_fact_check", "watch_flag_shown",
    };

    private static readonly object Gate = new();
    private static bool _enabled;
    private static string? _path;

    public static void Configure(bool enabled, string? path = null)
    {
        lock (Gate) { _enabled = enabled; _path = path ?? AppPaths.UsageFile; }
    }

    public static bool Enabled { get { lock (Gate) return _enabled; } }

    public static string? FilePath { get { lock (Gate) return _path; } }

    public static void Record(string evt, double? seconds = null, DateOnly? day = null)
    {
        lock (Gate)
        {
            if (!_enabled || _path is null || !Events.Contains(evt)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                bool header = !File.Exists(_path);
                using var w = new StreamWriter(_path, append: true);
                if (header) w.WriteLine("date,event,seconds");
                w.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{(day ?? DateOnly.FromDateTime(DateTime.Now)):yyyy-MM-dd},{evt},{(seconds is { } s ? Math.Round(s, 1).ToString("0.#", CultureInfo.InvariantCulture) : "")}"));
            }
            catch { /* a log that cannot be written must never disturb the work */ }
        }
    }
}
