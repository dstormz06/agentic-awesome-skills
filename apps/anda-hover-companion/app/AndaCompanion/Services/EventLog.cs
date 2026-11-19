using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AndaCompanion.Services;

public sealed record EventEntry(string T, string Type, string Detail);

/// <summary>
/// The event log: when a connection was switched on or off, when data was sent and how much, when a file of the person's own was set aside
/// or restored, what was deleted at their request, and errors. Each line is a time (UTC), a kind from a fixed list, and words and numbers the
/// program itself wrote. <b>It never holds anything the person typed, an ANDA number, a name, an address or any data</b>: a detail is kept
/// only when it is one of the log's own sentences (<see cref="Phrases"/>), when it is written and again when it is read. The file is only ever
/// added to; the person can open it, export it, or delete it by hand.
/// The rules are the simulation's (mockup/round29-golden.json).
/// </summary>
public static class EventLog
{
    /// <summary>The kinds, in the order they are listed. An unknown kind is written as a warning.</summary>
    public static readonly IReadOnlyList<(string Type, string Meaning)> Types = new[]
    {
        ("connection", "Connection switched on, off or changed"), ("sent", "Data sent"), ("send_failed", "No answer, or the request failed"),
        ("file", "A file of yours was set aside, kept or restored"), ("deleted", "Deleted at your request"), ("error", "Error"), ("warning", "Warning"),
    };
    public const int MaxDetail = 120;
    public const string Withheld = "(detail withheld: not one of the log's own sentences)";
    public const string Header = "time_utc,type,detail";

    /// <summary>
    /// Every sentence the log can hold; # stands for a count. Nothing else is ever written or shown, whoever writes the line: so the log cannot
    /// hold a name, an address, a number of an ANDA or any text of the person's. The same list as the simulation's (mockup/round29-golden.json).
    /// </summary>
    public static readonly IReadOnlyList<string> Phrases = new[]
    {
        "at start: AI on, agents on: #, Outlook on", "at start: AI on, agents on: #, Outlook off", "at start: AI off, agents on: #, Outlook on", "at start: AI off, agents on: #, Outlook off",
        "AI switched on", "AI switched off", "AI connection changed", "agents switched on: # to #", "agents changed", "Outlook switched on", "Outlook switched off", "Outlook connection changed",
        "AI request, # characters", "AI request (simulated), # characters", "agent request, # characters", "agent request (simulated)", "connection test", "Outlook sign-in request", "Outlook search for one number", "request for the Drugs at FDA file",
        "AI request: no answer", "AI request: timed out or canceled", "AI request (simulated): no answer", "agent request: no answer", "agent request (simulated): no answer",
        "settings restored from a backup", "a list was restored from a backup", "a to-do item was put back from the archive", "weekly copy of the sheet switched on", "weekly copy of the sheet switched off",
        "settings file could not be used as it was at start", "ANDA to-do list could not be used as it was at start", "to-do list could not be used as it was at start", "prompt library could not be used as it was at start", "quick replies could not be used as it was at start",
        "kept copies of the sheet",
    };
    private static readonly HashSet<string> Known = new(Phrases, StringComparer.Ordinal);
    private static readonly Regex Number = new("[0-9]{1,3}(?:,[0-9]{3})*", RegexOptions.CultureInvariant);      // a count, written with commas: 7, 1,234

    public static string Meaning(string type) => Types.FirstOrDefault(t => t.Type == type).Meaning ?? "Warning";

    /// <summary>A count the way the log writes it: 1,234.</summary>
    public static string Count(long n) => n.ToString("#,0", CultureInfo.InvariantCulture);

    /// <summary>One line of the log. A detail that is not one of the log's own sentences is never kept.</summary>
    public static EventEntry Entry(string? type, string? detail, DateTime utc)
    {
        string d = detail ?? "";
        bool ok = d.Length == 0 || d.Length <= MaxDetail && !d.Contains('#') && Known.Contains(Number.Replace(d, "#"));
        return new EventEntry(utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            Types.Any(t => t.Type == type) ? type! : "warning", ok ? d : Withheld);
    }

    private static string Line(EventEntry e) => e.T + "," + e.Type + ",\"" + e.Detail.Replace("\"", "\"\"") + "\"";

    /// <summary>The whole log as CSV, oldest first: the form the file itself has.</summary>
    public static string Csv(IEnumerable<EventEntry> entries) => string.Join("\r\n", new[] { Header }.Concat(entries.Select(Line))) + "\r\n";

    // ---------------------------------------------------------------- the file

    private static readonly object Gate = new();
    private static string? _path;

    /// <summary>Raised after a line was added, on the thread that added it. A window that shows the log follows it.</summary>
    public static event Action? Recorded;

    /// <summary>The file the log is written to, or null while the log is not in use (in tests, and until the app has started).</summary>
    public static string? FilePath { get { lock (Gate) return _path; } }

    /// <summary>Starts writing to this file. Null stops writing.</summary>
    public static void Configure(string? path) { lock (Gate) _path = path; }

    /// <summary>Adds one line. Never throws: a log that cannot be written must not stop the tool.</summary>
    public static void Record(string type, string? detail, DateTime? utcNow = null)
    {
        lock (Gate)
        {
            if (_path is null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
                bool fresh = !File.Exists(_path) || new FileInfo(_path).Length == 0;
                File.AppendAllText(_path, (fresh ? Header + "\r\n" : "") + Line(Entry(type, detail, utcNow ?? DateTime.UtcNow)) + "\r\n", new UTF8Encoding(false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { return; /* nothing more can be done */ }
        }
        try { Recorded?.Invoke(); } catch (Exception) { /* a window that cannot follow must not stop the tool */ }
    }

    /// <summary>
    /// One line of the file as an entry, or null when it is not a line of the log. The detail may stand without quotes, as a spreadsheet
    /// writes it when the file is saved again. Every line goes through the same rule as when it is written, so a line edited by hand
    /// cannot show what the log may not hold.
    /// </summary>
    private static EventEntry? Parse(string line)
    {
        int a = line.IndexOf(','), b = a < 0 ? -1 : line.IndexOf(',', a + 1);
        if (b < 0 || line.Length > 4 * MaxDetail) return null;
        string t = line[..a], type = line[(a + 1)..b], rest = line[(b + 1)..];
        string detail = rest.Length >= 2 && rest[0] == '"' && rest[^1] == '"' ? rest[1..^1].Replace("\"\"", "\"") : rest;
        if (!DateTime.TryParseExact(t, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var when)) return null;
        return Entry(type, detail, when);
    }

    /// <summary>
    /// The newest lines of the file, oldest first; how many lines of the log the file holds; and how many other lines could not be read as
    /// lines of the log. Only the newest are held, so a log of any length costs the same memory.
    /// </summary>
    public static (IReadOnlyList<EventEntry> Entries, int Total, int Unreadable) Read(int newest = 200)
    {
        string? path = FilePath;
        var all = new Queue<EventEntry>(); int total = 0, unreadable = 0;
        try
        {
            if (path is null || !File.Exists(path)) return (all.ToList(), 0, 0);
            // Read without the lock that writing takes, and shared with it: a long log never holds up a line that is being added.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (reader.ReadLine() is { } line)
            {
                if (line.Length == 0 || line == Header) continue;
                if (Parse(line) is not { } entry) { unreadable++; continue; }
                all.Enqueue(entry); total++;
                if (all.Count > newest) all.Dequeue();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* what was read so far is shown */ }
        return (all.ToList(), total, unreadable);
    }

    /// <summary>Writes the whole log as CSV to <paramref name="to"/>, line by line through the same rule. Returns how many lines were written.</summary>
    public static int Export(TextWriter to)
    {
        string? path = FilePath; int n = 0;
        to.Write(Header + "\r\n");
        if (path is null || !File.Exists(path)) return 0;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
            if (line.Length > 0 && line != Header && Parse(line) is { } entry) { to.Write(Line(entry) + "\r\n"); n++; }
        return n;
    }

    /// <summary>A time of the log in the person's own time zone, with the zone's abbreviation: "Nov 18, 7:03:59 PM EST".</summary>
    public static string Local(string utcText, TimeZoneInfo? zone = null)
    {
        if (!DateTime.TryParseExact(utcText, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc)) return utcText;
        zone ??= TimeZoneInfo.Local;
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        return local.ToString("MMM d, h:mm:ss tt", CultureInfo.InvariantCulture) + " " + Abbreviation(zone, utc);
    }

    /// <summary>
    /// "EST" or "EDT" from the name Windows gives a zone ("Eastern Standard Time"), the name itself when it is already short, and otherwise the
    /// difference from UTC ("UTC-5"), which is always right.
    /// </summary>
    /// <param name="utc">The moment in UTC. The hour that occurs twice when the clocks go back can only be told apart this way.</param>
    public static string Abbreviation(TimeZoneInfo zone, DateTime utc)
    {
        utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        if (zone.Id is "UTC" or "Etc/UTC" || zone.BaseUtcOffset == TimeSpan.Zero && !zone.SupportsDaylightSavingTime) return "UTC";
        string name = zone.IsDaylightSavingTime(utc) ? zone.DaylightName : zone.StandardName;
        string[] words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 1 && name.Length is >= 2 and <= 5 && name.All(char.IsLetter)) return name.ToUpperInvariant();
        if (words.Length is >= 3 and <= 4 && words[^1] == "Time" && words.All(w => char.IsLetter(w[0])))
            return string.Concat(words.Select(w => char.ToUpperInvariant(w[0])));
        var off = zone.GetUtcOffset(utc);
        return "UTC" + (off < TimeSpan.Zero ? "-" : "+") + Math.Abs(off.Hours).ToString(CultureInfo.InvariantCulture) + (off.Minutes != 0 ? ":" + Math.Abs(off.Minutes).ToString("00", CultureInfo.InvariantCulture) : "");
    }
}
