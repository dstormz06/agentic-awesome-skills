using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace AndaCompanion.Services;

/// <summary>One line of division news: its date, its text (plain, cut to 240 characters) and a key that never holds the text.</summary>
public sealed record NewsItem(string Key, string Date, string Text);

/// <summary>
/// Round 42 (round 19 in the simulation): division news from one Word document the division keeps in SharePoint, read from the copy that
/// OneDrive syncs to this PC, as a local file. Nothing is fetched or sent. Each paragraph that starts with its date (2026-11-20, 11/20/2026
/// or Nov 20, 2026) is a line; lines from the last 14 days are shown, newest first. Same rule as the simulation's newsFrom
/// (mockup/round42-golden.json).
/// </summary>
public static class DivisionNews
{
    public const long MaxFileBytes = 10 * 1024 * 1024, MaxXmlChars = 20_000_000;
    public const int MaxParagraphs = 200_000;      // round 42 review: the whole document is read (news is often added at the end); this only bounds memory
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main", Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const int OnlineOnly = 0x400000 | 0x40000;       // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS and _ON_OPEN: OneDrive has the file online only
    public const string SaidOnlineOnly = "The news document is kept online only, so it was not opened (that would download it). In OneDrive, choose \"Always keep on this device\" for it.";
    public const string SaidNotLocal = "The news document must be a file on this PC (the copy OneDrive syncs), not a network path or a web address.";
    public const int MaxPathLength = 500;

    /// <summary>Round 42 review: a path the app will read: on this PC, not a network share (Windows would sign in to it) nor a web address, at most 500 characters.</summary>
    public static string? PathProblem(string path) =>
        path.Length > MaxPathLength ? "The path of the news document is longer than 500 characters. Please move the document to a shorter path."
        : path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal) || path.Contains("://", StringComparison.Ordinal) || OnNetworkDrive(path) ? SaidNotLocal : null;

    /// <summary>A drive letter mapped to a network share (round 42 re-review): Windows would reach and sign in to that server on each read.</summary>
    internal static Func<string, bool>? NetworkDriveForTests;
    private static bool OnNetworkDrive(string path)
    {
        if (NetworkDriveForTests is { } f) return f(path);
        if (!OperatingSystem.IsWindows() || path.Length < 3 || path[1] != ':') return false;
        try { return new DriveInfo(path[..3]).DriveType == DriveType.Network; }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { return false; }
    }
    private static readonly string[] Months = { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };
    private static readonly string Sp = Timeline.JsSpace;
    private static readonly Regex Dated = new(
        "^" + Sp + @"*(?:([0-9]{4})-([0-9]{2})-([0-9]{2})|([0-9]{1,2})/([0-9]{1,2})/([0-9]{4})|(Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|June?|July?|Aug(?:ust)?|Sept?(?:ember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?"
        + Sp + "+([0-9]{1,2}),?" + Sp + @"+([0-9]{4}))(?![0-9/-])" + Sp + @"*[:–—-]?" + Sp + "*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public const string SaidOff = "Division news is off. Switch it on, and choose the document, in Settings, How it starts.";
    public static string SaidFrom(string file) => $"From {file}. Lines from the last {Timeline.NewsDays} days, newest first.";
    public static string SaidNone => $"No lines in the last {Timeline.NewsDays} days.";

    /// <summary>The dated lines of the last 14 days, newest first.</summary>
    public static IReadOnlyList<NewsItem> Parse(IEnumerable<string?> paragraphs, string today)
    {
        var t = Timeline.Iso(today) ?? throw new ArgumentException("today must be YYYY-MM-DD", nameof(today));
        var list = new List<(DateOnly D, NewsItem Item)>();
        var keys = new HashSet<string>(StringComparer.Ordinal);      // a line given twice is kept once (round 42 review)
        foreach (var p in paragraphs)
        {
            string s = Timeline.WithoutFormat(p ?? "");
            var m = Dated.Match(s);
            if (!m.Success) continue;
            int y, mo, d;
            if (m.Groups[1].Success) (y, mo, d) = (Int(m.Groups[1]), Int(m.Groups[2]), Int(m.Groups[3]));
            else if (m.Groups[6].Success) (y, mo, d) = (Int(m.Groups[6]), Int(m.Groups[4]), Int(m.Groups[5]));
            else (y, mo, d) = (Int(m.Groups[9]), Array.IndexOf(Months, m.Groups[7].Value[..3].ToLowerInvariant()) + 1, Int(m.Groups[8]));
            if (mo is < 1 or > 12 || d < 1 || y < 1 || d > DateTime.DaysInMonth(y, mo)) continue;      // a date that does not exist
            var date = new DateOnly(y, mo, d);
            string text = Timeline.Cut(s[m.Length..], Timeline.NewsMax);
            if (text.Length == 0 || date > t || t.DayNumber - date.DayNumber > Timeline.NewsDays) continue;
            string key = $"n|{Timeline.Iso(date)}|{Timeline.Hash(text)}";
            if (!keys.Add(key)) continue;
            list.Add((date, new NewsItem(key, Timeline.Iso(date), text)));
        }
        return list.OrderByDescending(x => x.D).ThenBy(x => x.Item.Key, StringComparer.Ordinal).Select(x => x.Item).ToList();
    }

    private static int Int(Group g) => int.Parse(g.Value, CultureInfo.InvariantCulture);

    /// <summary>
    /// The paragraphs of a .docx, as plain text, or a sentence saying why not. Read-only: the file is opened so that OneDrive and Word may keep
    /// it open; no macro, field, link or picture is read; DTD processing is prohibited; at most 10 MB of file and 20 million characters of text.
    /// </summary>
    public static (IReadOnlyList<string>? Paragraphs, string? Problem) ReadDocx(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return (null, "Please choose the division's news document first.");
        if (!path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)) return (null, "The news document must be a Word document (.docx).");
        if (PathProblem(path) is { } bad) return (null, bad);
        try
        {
            // round 42 review: a copy OneDrive keeps online only would be downloaded on opening, on the app's own thread; it is left alone
            if (File.Exists(path) && ((int)File.GetAttributes(path) & (OnlineOnly | (int)FileAttributes.Offline)) != 0) return (null, SaidOnlineOnly);
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length > MaxFileBytes) return (null, "The news document is larger than 10 MB, which is more than this tool will read.");
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            var entry = zip.GetEntry("word/document.xml");
            if (entry is null) return (null, "The news document could not be read as a Word document.");
            if (entry.Length > MaxXmlChars * 2) return (null, "The news document holds more text than this tool will read.");
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxXmlChars };
            using var stream = entry.Open();
            using var xml = XmlReader.Create(stream, settings);
            // One step at a time: ReadElementContentAsString already moves past the text, so the loop does not read again after it (round 42
            // review: a tab or a second piece of text right after a piece of text was being skipped). A paragraph inside another (a text box)
            // is its own line, and the outer one goes on after it.
            var paragraphs = new List<string>(); var open = new Stack<StringBuilder>();
            xml.Read();
            while (!xml.EOF && paragraphs.Count < MaxParagraphs)
            {
                // Word writes a text box twice, the second time for older readers (mc:Fallback): read once (round 42 review)
                if (xml.NodeType == XmlNodeType.Element && xml.NamespaceURI == Mc && xml.LocalName == "Fallback") { xml.Skip(); continue; }
                if (xml.NamespaceURI == W && xml.NodeType == XmlNodeType.Element)
                {
                    if (xml.LocalName == "p") { if (xml.IsEmptyElement) paragraphs.Add(""); else open.Push(new StringBuilder()); }
                    else if (open.Count > 0 && xml.LocalName == "t" && !xml.IsEmptyElement) { open.Peek().Append(xml.ReadElementContentAsString()); continue; }
                    else if (open.Count > 0 && xml.LocalName is "tab" or "br" or "cr") open.Peek().Append(' ');
                }
                else if (xml.NamespaceURI == W && xml.NodeType == XmlNodeType.EndElement && xml.LocalName == "p" && open.Count > 0) paragraphs.Add(open.Pop().ToString());
                xml.Read();
            }
            return (paragraphs, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or XmlException or NotSupportedException or ArgumentException)
        {
            return (null, "The news document could not be read just now. It may be open elsewhere, not yet synced, or not a Word document.");
        }
    }
}
