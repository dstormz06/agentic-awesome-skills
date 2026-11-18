using System.Globalization;
using System.Text;

namespace AndaCompanion.Services;

public sealed record EventEntry(string T, string Type, string Detail);

/// <summary>
/// The event log: when a connection was switched on or off, when data was sent and how much, when a file of the person's own was set aside
/// or restored, what was deleted at their request, and errors. Each line is a time (UTC), a kind from a fixed list, and words and numbers the
/// program itself wrote. <b>It never holds anything the person typed, an ANDA number, a name, an address or any data</b>: a detail that is
/// not in the allowed form is withheld. The file is only ever added to; the person can open it, export it, or delete it by hand.
/// The rules are the simulation's (mockup/round29-golden.json).
/// </summary>
public static class EventLog
{
    /// <summary>The kinds, in the order they are listed. An unknown kind is written as a warning.</summary>
    public static readonly IReadOnlyList<(string Type, string Meaning)> Types = new[]
    {
        ("connection", "Connection switched on or off"), ("sent", "Data sent"), ("send_failed", "Sending did not complete"),
        ("file", "A file of yours was set aside, kept or restored"), ("deleted", "Deleted at your request"), ("error", "Error"), ("warning", "Warning"),
    };
    public const int MaxDetail = 120;
    public const string Withheld = "(detail withheld: not in the allowed form)";
    public const string Header = "time_utc,type,detail";
    private const string Allowed = " .,:;()+=/-";

    public static string Meaning(string type) => Types.FirstOrDefault(t => t.Type == type).Meaning ?? "Warning";

    /// <summary>One line of the log. Six digits in a row could be an ANDA number, so such a detail is never kept.</summary>
    public static EventEntry Entry(string? type, string? detail, DateTime utc)
    {
        string d = detail ?? ""; bool ok = d.Length <= MaxDetail && !(d.Length > 0 && d[0] is '=' or '+' or '-'); int digits = 0;      // a spreadsheet reads a leading = + - as a formula
        foreach (char c in d)
        {
            if (!ok) break;
            bool digit = c is >= '0' and <= '9';
            digits = digit ? digits + 1 : 0;
            ok = digits < 6 && (digit || c is >= 'A' and <= 'Z' || c is >= 'a' and <= 'z' || Allowed.Contains(c));
        }
        return new EventEntry(utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            Types.Any(t => t.Type == type) ? type! : "warning", ok ? d : Withheld);
    }

    private static string Line(EventEntry e) => e.T + "," + e.Type + ",\"" + e.Detail.Replace("\"", "\"\"") + "\"";

    /// <summary>The whole log as CSV, oldest first: the form the file itself has.</summary>
    public static string Csv(IEnumerable<EventEntry> entries) => string.Join("\r\n", new[] { Header }.Concat(entries.Select(Line))) + "\r\n";

    // ---------------------------------------------------------------- the file

    private static readonly object Gate = new();
    private static string? _path;

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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { /* nothing more can be done */ }
        }
    }

    /// <summary>The newest lines of the file, oldest first, and how many lines the file holds. A line that is not one of ours is passed over.</summary>
    public static (IReadOnlyList<EventEntry> Entries, int Total) Read(int newest = 200)
    {
        lock (Gate)
        {
            var all = new Queue<EventEntry>(); int total = 0;      // only the newest are held, so a log of any length costs the same memory
            try
            {
                if (_path is null || !File.Exists(_path)) return (all.ToList(), 0);
                foreach (string line in File.ReadLines(_path))
                {
                    if (line.Length == 0 || line == Header) continue;
                    int a = line.IndexOf(','), b = a < 0 ? -1 : line.IndexOf(',', a + 1);
                    if (b < 0 || line.Length < b + 3 || line[b + 1] != '"' || line[^1] != '"') continue;
                    string t = line[..a], type = line[(a + 1)..b], detail = line[(b + 2)..^1].Replace("\"\"", "\"");
                    if (!DateTime.TryParseExact(t, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var when)) continue;
                    all.Enqueue(Entry(type, detail, when));      // read through the same rule, so a line edited by hand cannot show what the log may not hold
                    total++;
                    if (all.Count > newest) all.Dequeue();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* what was read so far is shown */ }
            return (all.ToList(), total);
        }
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
