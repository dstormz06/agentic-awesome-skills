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
    public const int MaxParagraphs = 2000;
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
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
        foreach (var p in paragraphs.Take(MaxParagraphs))
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
            list.Add((date, new NewsItem($"n|{Timeline.Iso(date)}|{Timeline.Hash(text)}", Timeline.Iso(date), text)));
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
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length > MaxFileBytes) return (null, "The news document is larger than 10 MB, which is more than this tool will read.");
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            var entry = zip.GetEntry("word/document.xml");
            if (entry is null) return (null, "The news document could not be read as a Word document.");
            if (entry.Length > MaxXmlChars * 2) return (null, "The news document holds more text than this tool will read.");
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxXmlChars };
            using var stream = entry.Open();
            using var xml = XmlReader.Create(stream, settings);
            var paragraphs = new List<string>(); StringBuilder? p = null;
            while (xml.Read() && paragraphs.Count < MaxParagraphs * 4)
            {
                if (xml.NamespaceURI != W) continue;
                if (xml.NodeType == XmlNodeType.Element)
                {
                    if (xml.LocalName == "p") { if (xml.IsEmptyElement) paragraphs.Add(""); else p = new StringBuilder(); }
                    else if (p is not null && xml.LocalName == "t" && !xml.IsEmptyElement) p.Append(xml.ReadElementContentAsString());
                    else if (p is not null && xml.LocalName is "tab" or "br" or "cr") p.Append(' ');
                }
                else if (xml.NodeType == XmlNodeType.EndElement && xml.LocalName == "p" && p is not null) { paragraphs.Add(p.ToString()); p = null; }
            }
            return (paragraphs, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or XmlException or NotSupportedException or ArgumentException)
        {
            return (null, "The news document could not be read just now. It may be open elsewhere, not yet synced, or not a Word document.");
        }
    }
}
