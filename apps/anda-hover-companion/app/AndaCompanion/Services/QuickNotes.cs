using System.Text.Json;
using System.Text.RegularExpressions;

namespace AndaCompanion.Services;

/// <summary>One quick note a person typed on a card. Text only.</summary>
public sealed record QuickNote(string Id, string Text, DateTime When);

/// <summary>
/// Quick notes typed on the card (round 24; in the simulation since round 20). They are the person's own, kept by the app in one file
/// in its own folder (quick-notes.json). The portfolio sheet and the notes folder stay read-only. A note is never sent anywhere: not to an
/// AI, not in a notice, and it is not part of the settings backup. The rules are the simulation's: text only, 500 characters, 50 notes
/// for one ANDA, a six-digit number as the key.
/// </summary>
public sealed class QuickNotesService
{
    public const int MaxLength = 500, PerAnda = 50;
    private const long MaxFileBytes = 4 * 1024 * 1024;
    private static readonly Regex SixDigits = new("^[0-9]{6}$", RegexOptions.CultureInvariant);
    private static readonly Regex Control = new("[\u0000-\u0008\u000b\u000c\u000e-\u001f]", RegexOptions.CultureInvariant);
    private static readonly Regex BlankRuns = new("\n{3,}", RegexOptions.CultureInvariant);

    private sealed class FileShape { public int Version { get; set; } = 1; public Dictionary<string, List<QuickNote>> Notes { get; set; } = new(); }

    private readonly string _path;
    private readonly object _gate = new();
    private readonly Func<DateTime> _now;
    private FileShape _file = new();
    private int _seq;

    /// <summary>Why the notes could not be read or saved, in plain words; null when all is well.</summary>
    public string? Problem { get; private set; }

    public QuickNotesService(string? path = null, Func<DateTime>? now = null)
    {
        _path = path ?? AppPaths.QuickNotesFile;
        _now = now ?? (() => DateTime.Now);
        Load();
    }

    /// <summary>The same cleaning as the simulation: Windows line ends become plain ones, control characters go, three or more line breaks
    /// become two, the ends are trimmed, and the text is cut at 500 characters (never in the middle of a character pair).</summary>
    public static string Clean(string? text)
    {
        string s = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        s = Control.Replace(s, "");
        s = BlankRuns.Replace(s, "\n\n").Trim();
        if (s.Length > MaxLength) { s = s[..MaxLength]; if (char.IsHighSurrogate(s[^1])) s = s[..^1]; }
        return s;
    }

    public static bool IsAndaNumber(string? number) => number is not null && SixDigits.IsMatch(number);

    /// <summary>The notes for one ANDA, newest first.</summary>
    public IReadOnlyList<QuickNote> For(string? number)
    {
        lock (_gate) return IsAndaNumber(number) && _file.Notes.TryGetValue(number!, out var list) ? list.AsEnumerable().Reverse().ToList() : Array.Empty<QuickNote>();
    }

    public int Count(string? number) { lock (_gate) return IsAndaNumber(number) && _file.Notes.TryGetValue(number!, out var list) ? list.Count : 0; }

    public (bool Ok, string Message) Add(string? number, string? text)
    {
        string clean = Clean(text);
        if (!IsAndaNumber(number)) return (false, "Open an ANDA first.");
        if (clean.Length == 0) return (false, "Type a note first.");
        lock (_gate)
        {
            if (!_file.Notes.TryGetValue(number!, out var list)) _file.Notes[number!] = list = new();
            if (list.Count >= PerAnda) return (false, $"This ANDA already has {PerAnda} quick notes. Delete one to add another.");
            list.Add(new QuickNote("q" + _now().ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + (++_seq), clean, _now()));
            return Save() ? (true, "Note saved.") : (true, "Note added, but it could not be saved to this PC: " + Problem);
        }
    }

    public (bool Ok, string Message) Edit(string? number, string id, string? text)
    {
        string clean = Clean(text);
        lock (_gate)
        {
            if (!IsAndaNumber(number) || !_file.Notes.TryGetValue(number!, out var list)) return (false, "That note is no longer here.");
            int i = list.FindIndex(n => n.Id == id);
            if (i < 0) return (false, "That note is no longer here.");
            if (clean.Length == 0) return (false, "A note cannot be empty. Use Delete to remove it.");
            list[i] = list[i] with { Text = clean };
            Save();
            return (true, "Note changed.");
        }
    }

    /// <summary>Removes a note and returns what is needed to put it back (Undo).</summary>
    public (int At, QuickNote Note)? Delete(string? number, string id)
    {
        lock (_gate)
        {
            if (!IsAndaNumber(number) || !_file.Notes.TryGetValue(number!, out var list)) return null;
            int i = list.FindIndex(n => n.Id == id);
            if (i < 0) return null;
            var gone = list[i]; list.RemoveAt(i);
            if (list.Count == 0) _file.Notes.Remove(number!);
            Save();
            return (i, gone);
        }
    }

    public bool Restore(string? number, int at, QuickNote note)
    {
        lock (_gate)
        {
            if (!IsAndaNumber(number)) return false;
            if (!_file.Notes.TryGetValue(number!, out var list)) _file.Notes[number!] = list = new();
            if (list.Count >= PerAnda || list.Any(n => n.Id == note.Id)) return false;
            list.Insert(Math.Clamp(at, 0, list.Count), note);
            Save();
            return true;
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            if (new FileInfo(_path).Length > MaxFileBytes) throw new InvalidDataException("the file is larger than expected");
            var read = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(_path)) ?? new FileShape();
            var kept = new FileShape();
            foreach (var (number, list) in read.Notes ?? new())
            {
                if (!IsAndaNumber(number) || list is null) continue;        // anything that is not a six-digit number is not ours
                var good = list.Where(n => n is not null && !string.IsNullOrEmpty(n.Id) && Clean(n.Text).Length > 0)
                    .Select(n => n with { Id = n.Id.Length > 60 ? n.Id[..60] : n.Id, Text = Clean(n.Text) })
                    .GroupBy(n => n.Id).Select(g => g.First()).Take(PerAnda).ToList();
                if (good.Count > 0) kept.Notes[number] = good;
            }
            _file = kept;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            // A file that cannot be read is set aside, never overwritten in place: the person's notes may still be in it.
            Problem = "The notes file could not be read (" + ex.Message + "). It was set aside as quick-notes.unreadable.json, and new notes start a new file.";
            try { File.Move(_path, Path.Combine(Path.GetDirectoryName(_path) ?? ".", "quick-notes.unreadable.json"), overwrite: true); } catch (Exception) { /* nothing more can be done; Save reports it */ }
            _file = new FileShape();
        }
    }

    private bool Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_file, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, _path, overwrite: true);       // a crash mid-write can never leave a half-written file
            Problem = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Problem = ex.Message;
            return false;
        }
    }
}

/// <summary>
/// The address the card's "Open ANDA page" button opens (round 24; in the simulation since round 21). The person sets it in Settings with
/// {anda} where the number goes. Only a plain https address is accepted, and only six digits are ever put into it.
/// The messages are the simulation's.
/// </summary>
public static class AndaPage
{
    public const int MaxLength = 300;
    private const string Where = "Please check it in Settings, My files and writing.";
    private static readonly Regex Shape = new("^https://[^\\s<>\"'`\\\\]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static (string? Url, string? Why) Build(string? number, string? template)
    {
        string t = (template ?? "").Trim();
        if (t.Length == 0) return (null, "No web address is set for ANDA pages. Add one in Settings, My files and writing.");
        if (t.Length > MaxLength) return (null, "That address is too long. " + Where);
        if (!Shape.IsMatch(t)) return (null, "The ANDA page address must start with https:// and have no spaces. " + Where);
        if (!t.Contains("{anda}", StringComparison.Ordinal)) return (null, "The ANDA page address needs {anda} where the six-digit number goes. " + Where);
        if (!QuickNotesService.IsAndaNumber(number)) return (null, "Open an ANDA first.");
        if (!Uri.TryCreate(t.Replace("{anda}", number, StringComparison.Ordinal), UriKind.Absolute, out var url)) return (null, "That address could not be read. " + Where);
        if (url.Scheme != Uri.UriSchemeHttps || url.UserInfo.Length > 0 || url.Host.Length == 0) return (null, "The ANDA page address must be a plain https address. " + Where);
        return (url.AbsoluteUri, null);
    }

    /// <summary>What Settings shows under the box: null when the address would work.</summary>
    public static string? Check(string? template) => Build("123456", template).Why;
}
