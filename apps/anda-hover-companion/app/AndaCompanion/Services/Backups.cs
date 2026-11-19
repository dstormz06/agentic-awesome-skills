using System.Globalization;
using System.Text;

namespace AndaCompanion.Services;

/// <summary>One of the person's lists that copies are kept of: its file, and how to tell the app that the file was put back.</summary>
/// <param name="Count">How many items the text of a copy holds, or null when it cannot be read.</param>
public sealed record BackupList(string Id, string Name, string File, string One, string Many, Func<string, int?> Count, Action Reload);

/// <summary>One kept copy of a list: when it was made (UTC) and where it is.</summary>
public sealed record BackupCopy(DateTime Utc, string Path, long Bytes);

/// <summary>
/// Backup copies of the person's lists (the two to-do lists, the notes on ANDAs, the prompt library, the quick replies). After every change
/// to a list, a copy of its file is kept in the app's own folder on this PC; one press puts a copy back, and because the list as it was a
/// moment before is a copy too, that can be undone. <b>The person chooses how many copies are kept</b>: the newest N, and the last copy of
/// each of the newest N days. Older copies are removed by the app, which the window says. The lists themselves and the archive are never
/// removed. Copies hold what the lists hold, so they are as sensitive as the lists: they stay on this PC, are never sent, and are not part
/// of a settings backup. The rule is the simulation's (mockup/round30-golden.json).
/// </summary>
public sealed class Backups
{
    public static readonly IReadOnlyList<int> KeepChoices = new[] { 10, 30, 60, 100 };
    public const int KeepDefault = 30;
    public static readonly IReadOnlyList<int> ArchiveDaysChoices = new[] { 7, 30, 90, 365 };
    public const int ArchiveDaysDefault = 30;

    private static TimeZoneInfo Zone(string? id)
    {
        try { return string.IsNullOrEmpty(id) ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { return TimeZoneInfo.Utc; }      // a zone that is not known is read as UTC
    }

    /// <summary>The day a moment falls on for the person: in their time zone, whatever the clocks did that night.</summary>
    public static string LocalDay(long utcMs, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UnixEpoch.AddMilliseconds(utcMs), zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string LocalDay(long utcMs, string? zoneId) => LocalDay(utcMs, Zone(zoneId));

    /// <summary>
    /// Which copies of one list stay: the newest N, and the last copy of each of the newest N days that have one. The rest are removed.
    /// Both lists are newest first. A number that is not one of the choices is read as the usual one.
    /// </summary>
    public static (IReadOnlyList<long> Keep, IReadOnlyList<long> Remove) Plan(IEnumerable<long> stampsUtcMs, int keep, TimeZoneInfo zone)
    {
        int n = KeepChoices.Contains(keep) ? keep : KeepDefault;
        var sorted = stampsUtcMs.Distinct().OrderByDescending(s => s).ToList();
        var stay = new HashSet<long>(sorted.Take(n)); var days = new HashSet<string>();
        foreach (long s in sorted)
            if (days.Add(LocalDay(s, zone)) && days.Count <= n) stay.Add(s);
        return (sorted.Where(stay.Contains).ToList(), sorted.Where(s => !stay.Contains(s)).ToList());
    }
    public static (IReadOnlyList<long> Keep, IReadOnlyList<long> Remove) Plan(IEnumerable<long> stampsUtcMs, int keep, string? zoneId) => Plan(stampsUtcMs, keep, Zone(zoneId));

    /// <summary>How many items a copy holds: the length of the list under <paramref name="property"/>, or of the file when it is a list itself. Null when it cannot be read.</summary>
    public static int? ItemsIn(string text, string? property = null)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            var e = doc.RootElement;
            if (property is not null) { if (e.ValueKind != System.Text.Json.JsonValueKind.Object || !e.TryGetProperty(property, out e)) return null; }
            return e.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Array => e.GetArrayLength(),
                System.Text.Json.JsonValueKind.Object => e.EnumerateObject().Sum(p => p.Value.ValueKind == System.Text.Json.JsonValueKind.Array ? p.Value.GetArrayLength() : 0),      // the notes: a list for each ANDA
                _ => null,
            };
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    // ---------------------------------------------------------------- the files

    private const string StampFormat = "yyyyMMdd-HHmmss-fff";
    private readonly string _dir;
    private readonly Func<int> _keep;
    private readonly Func<TimeZoneInfo> _zone;
    private readonly Func<DateTime> _utcNow;
    private readonly object _gate = new();
    private readonly List<BackupList> _lists = new();

    /// <param name="dir">The folder the copies are kept in: one folder inside it for each list.</param>
    /// <param name="keep">How many copies the person chose to keep, asked each time.</param>
    public Backups(string dir, Func<int> keep, Func<TimeZoneInfo>? zone = null, Func<DateTime>? utcNow = null)
    {
        _dir = dir; _keep = keep; _zone = zone ?? (() => TimeZoneInfo.Local); _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public IReadOnlyList<BackupList> Lists { get { lock (_gate) return _lists.ToList(); } }

    /// <summary>Raised after a copy was made or removed, so that an open window can follow.</summary>
    public event Action? Changed;

    public void Register(BackupList list) { lock (_gate) { _lists.RemoveAll(l => l.Id == list.Id); _lists.Add(list); } }

    private BackupList? Find(string id) { lock (_gate) return _lists.FirstOrDefault(l => l.Id == id); }
    private string FolderFor(string id) => Path.Combine(_dir, id);
    private static long Ms(DateTime utc) => (long)(utc - DateTime.UnixEpoch).TotalMilliseconds;

    /// <summary>The copies of a list that exist, newest first. A file whose name is not one of ours is ignored.</summary>
    public IReadOnlyList<BackupCopy> Copies(string id)
    {
        lock (_gate)
        {
            var found = new List<BackupCopy>();
            string folder = FolderFor(id);
            try
            {
                if (!Directory.Exists(folder)) return found;
                foreach (string f in Directory.EnumerateFiles(folder, id + ".*.json"))
                {
                    string name = Path.GetFileNameWithoutExtension(f);
                    if (name.Length != id.Length + 1 + StampFormat.Length) continue;
                    if (DateTime.TryParseExact(name[(id.Length + 1)..], StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc))
                        found.Add(new BackupCopy(utc, f, new FileInfo(f).Length));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* what was found so far is shown */ }
            return found.OrderByDescending(c => c.Utc).ToList();
        }
    }

    /// <summary>Called when one of the app's files was written: if it is one of the lists, a copy is kept.</summary>
    public void Written(string path)
    {
        BackupList? list; lock (_gate) list = _lists.FirstOrDefault(l => string.Equals(Path.GetFullPath(l.File), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
        if (list is not null) Keep(list.Id);
    }

    /// <summary>Looks at every list once: at start, so that a list changed while the app was closed, or never copied, has a copy.</summary>
    public void Sweep() { foreach (var l in Lists) Keep(l.Id); }

    /// <summary>
    /// Keeps a copy of the list as it is now, unless the newest copy already holds the same text; then removes the copies beyond what the
    /// person chose to keep. True when a copy was made. Never throws: a copy that cannot be made must not stop the tool.
    /// </summary>
    public bool Keep(string id)
    {
        bool made = false;
        lock (_gate)
        {
            if (Find(id) is not { } list) return false;
            try
            {
                if (!File.Exists(list.File) || new FileInfo(list.File).Length > SafeFile.MaxBytes) return false;
                string text = File.ReadAllText(list.File);
                var copies = Copies(id);
                if (copies.Count > 0 && Same(copies[0].Path, text)) return false;
                Directory.CreateDirectory(FolderFor(id));
                var now = _utcNow();
                if (copies.Count > 0 && now <= copies[0].Utc) now = copies[0].Utc.AddMilliseconds(1);      // never the same name twice, even when the clock stands still or goes back
                string target = Path.Combine(FolderFor(id), id + "." + now.ToString(StampFormat, CultureInfo.InvariantCulture) + ".json");
                File.WriteAllText(target + ".tmp", text, new UTF8Encoding(false));
                File.Move(target + ".tmp", target, overwrite: false);
                made = true;
                PruneLocked(id);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { /* the list itself was written; only the copy is missing */ }
        }
        if (made) Raise();
        return made;
    }

    private static bool Same(string path, string text)
    {
        try { return new FileInfo(path).Length <= SafeFile.MaxBytes && File.ReadAllText(path) == text; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private int PruneLocked(string id)
    {
        var copies = Copies(id); int gone = 0;
        var plan = Plan(copies.Select(c => Ms(c.Utc)), _keep(), _zone());
        var remove = new HashSet<long>(plan.Remove);
        foreach (var c in copies.Where(c => remove.Contains(Ms(c.Utc))))
        {
            try { File.Delete(c.Path); gone++; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* tried again at the next change */ }
        }
        return gone;
    }

    /// <summary>Removes the copies beyond what the person chose to keep, for every list: when that number was made smaller. Returns how many went.</summary>
    public int Prune()
    {
        int gone = 0;
        lock (_gate) foreach (var l in _lists) gone += PruneLocked(l.Id);
        if (gone > 0) Raise();
        return gone;
    }

    /// <summary>The text of a copy, or null when it cannot be read.</summary>
    public string? Read(BackupCopy copy)
    {
        try { return new FileInfo(copy.Path).Length <= SafeFile.MaxBytes ? File.ReadAllText(copy.Path) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>"12 items" for a copy, or "cannot be read".</summary>
    public string Size(BackupList list, BackupCopy copy) =>
        Read(copy) is { } text && list.Count(text) is { } n ? $"{n.ToString("#,0", CultureInfo.InvariantCulture)} {(n == 1 ? list.One : list.Many)}" : "cannot be read";

    /// <summary>
    /// Puts a copy back in place of the list. The list as it was a moment before is a copy already, so this can be undone the same way.
    /// The message is what the window says.
    /// </summary>
    public (bool Ok, string Message) Restore(string id, DateTime utc, Func<DateTime, string>? when = null)
    {
        BackupList? list; string? text; string? now;
        lock (_gate)
        {
            list = Find(id);
            var copy = list is null ? null : Copies(id).FirstOrDefault(c => c.Utc == utc);
            if (list is null || copy is null) return (false, "That copy is no longer kept. Nothing was changed.");
            text = Read(copy);
            if (text is null || list.Count(text) is null) return (false, "That copy could not be read. Nothing was changed.");
            try { now = File.Exists(list.File) && new FileInfo(list.File).Length <= SafeFile.MaxBytes ? File.ReadAllText(list.File) : null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { now = null; }
            if (now == text) return (false, $"That copy is the {list.Name} as it is now. Nothing was changed.");
        }
        Keep(id);                                                   // the list as it is now is kept first, whatever happens next
        if (!SafeFile.Write(list.File, text)) return (false, "The list could not be written just now. Nothing was changed.");
        Keep(id);                                                   // and the list as it is after the restore is the newest copy
        try { list.Reload(); } catch (Exception) { /* the file is in place; the list shows it at the next start */ }
        EventLog.Record("file", "a list was restored from a backup");
        string shown = when is null ? utc.ToString("MMM d, h:mm:ss tt 'UTC'", CultureInfo.InvariantCulture) : when(utc);
        return (true, $"The {list.Name} is as it was on {shown}. The list as it was a moment ago is still among the copies, so this can be undone.");
    }

    private void Raise() { try { Changed?.Invoke(); } catch (Exception) { /* a window that cannot follow must not stop the tool */ } }
}
