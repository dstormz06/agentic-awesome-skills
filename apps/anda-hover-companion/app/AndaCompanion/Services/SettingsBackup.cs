using System.Reflection;
using System.Text.Json;

namespace AndaCompanion.Services;

/// <summary>
/// Back up and restore everything a person customizes, so a new PC, a reinstall or a colleague's pilot needs no re-typing.
/// The file holds settings, lenses and quick replies. It never holds a secret: the app stores none (a token is only ever the
/// NAME of an environment variable). File locations (portfolio, notes, Drugs@FDA file) are left out because they are specific
/// to one PC; a restore keeps the ones already set.
/// </summary>
public static class SettingsBackup
{
    public const int Version = 1;
    public const int MaxBytes = 2 * 1024 * 1024;

    public sealed class Backup
    {
        public int Version { get; set; }
        public string App { get; set; } = "ANDA Companion (Notula)";
        public DateTime SavedAt { get; set; }
        public Settings Settings { get; set; } = new();
        public List<LensPrompt> Lenses { get; set; } = new();
        public List<QuickReply> Replies { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Settings that are tied to one PC and so are not part of a backup.</summary>
    private static readonly string[] ThisPcOnly = { nameof(Settings.PortfolioPath), nameof(Settings.PortfolioColumns), nameof(Settings.NotesFolder), nameof(Settings.ReferenceDataPath), nameof(Settings.FirstRunDone) };

    public static string Export(Settings settings, PromptLibraryService lenses, QuickReplyService replies, DateTime? now = null)
    {
        var copy = new Settings();
        CopyFrom(copy, settings, skip: ThisPcOnly);
        return JsonSerializer.Serialize(new Backup
        {
            Version = Version, SavedAt = now ?? DateTime.Now, Settings = copy,
            Lenses = lenses.Prompts.ToList(), Replies = replies.Replies.ToList(),
        }, Json);
    }

    /// <summary>Applies a backup to the running settings. Returns a plain sentence either way. A refused file changes nothing.</summary>
    public static (bool Ok, string Message) Import(string json, Settings target, PromptLibraryService lenses, QuickReplyService replies)
    {
        if (json.Length > MaxBytes) return (false, "That file is too large to be a backup. Nothing was changed.");
        Backup? backup;
        try { backup = JsonSerializer.Deserialize<Backup>(json); }
        catch (JsonException) { return (false, "That file is not a backup from this app. Nothing was changed."); }
        if (backup is null || backup.Version != Version || backup.Settings is null)
            return (false, backup is { Version: > Version } ? "That backup is from a newer version of the app. Nothing was changed." : "That file is not a backup from this app. Nothing was changed.");

        var clean = SettingsService.Normalize(backup.Settings);
        CopyFrom(target, clean, skip: ThisPcOnly);
        lenses.ReplaceAll(backup.Lenses ?? new());
        replies.ReplaceAll(backup.Replies ?? new());
        return (true, $"Restored your settings, {lenses.Prompts.Count} lenses and {replies.Replies.Count} quick replies. File locations on this PC were kept.");
    }

    /// <summary>Back to what a new install starts with. File locations are kept, so the person's data is still found.</summary>
    public static void Reset(Settings target, PromptLibraryService lenses, QuickReplyService replies)
    {
        CopyFrom(target, SettingsService.Normalize(new Settings()), skip: ThisPcOnly);
        lenses.ReplaceAll(PromptLibraryService.Starters);
        replies.ReplaceAll(QuickReplyService.Starters);
    }

    private static void CopyFrom(Settings to, Settings from, string[] skip)
    {
        foreach (var p in typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (p.CanRead && p.CanWrite && !skip.Contains(p.Name)) p.SetValue(to, p.GetValue(from));
    }
}
