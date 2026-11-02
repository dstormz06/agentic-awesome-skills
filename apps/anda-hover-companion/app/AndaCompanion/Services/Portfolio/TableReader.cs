using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace AndaCompanion.Services.Portfolio;

/// <summary>A message meant for the person, not a stack trace: what is wrong with their file and what to do.</summary>
public sealed class PortfolioException(string message) : Exception(message);

public sealed record RawTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows, string SheetName);

/// <summary>
/// Reads an .xlsx or .csv file as a plain table. Read-only and local: the file is opened with sharing allowed (so Excel can
/// keep it open) and is never written. No third-party library is used; an .xlsx is a zip of XML, read with size limits so
/// a hostile or huge file fails with a plain message instead of exhausting memory. Dates stored as Excel serial numbers are
/// converted to yyyy-MM-dd using the cell's number format.
/// </summary>
public static class TableReader
{
    public const long MaxFileBytes = 25L * 1024 * 1024;
    public const long MaxUnzippedBytes = 150L * 1024 * 1024;
    public const int MaxRows = 20_000, MaxColumns = 200;

    public static RawTable Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new PortfolioException("That file was not found. Choose your portfolio file again.");
        string ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            return ext switch
            {
                ".xlsx" or ".xlsm" => ReadXlsx(path),
                ".csv" or ".txt" or ".tsv" => ReadCsv(path),
                _ => throw new PortfolioException($"Files of type {ext} are not supported. Use .xlsx or .csv (in Excel: File, Save As, CSV)."),
            };
        }
        catch (PortfolioException) { throw; }
        catch (IOException ex) when (ex is not FileNotFoundException)
        {
            throw new PortfolioException("The file could not be opened. If it is open in another program, save it there and try again.");
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or UnauthorizedAccessException or FormatException)
        {
            throw new PortfolioException("The file could not be read. It may be damaged, password-protected or not a real workbook.");
        }
    }

    // ---------------- CSV ----------------

    public static RawTable ReadCsv(string path)
    {
        using var fs = Open(path);
        if (fs.Length > MaxFileBytes) throw new PortfolioException("The file is larger than 25 MB, which is more than this tool will read.");
        using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return ParseCsv(reader.ReadToEnd(), Path.GetFileName(path));
    }

    public static RawTable ParseCsv(string text, string name = "CSV")
    {
        char delimiter = DetectDelimiter(text);
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>(); var cell = new StringBuilder(); bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else quoted = false; }
                else cell.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == delimiter) { row.Add(cell.ToString()); cell.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString()); cell.Clear();
                if (row.Any(x => x.Length > 0)) rows.Add(Cap(row));
                row = new();
                if (rows.Count > MaxRows) throw new PortfolioException("The file has more than 20,000 rows, which is more than this tool will read.");
            }
            else cell.Append(c);
        }
        row.Add(cell.ToString());
        if (row.Any(x => x.Length > 0)) rows.Add(Cap(row));
        return Shape(rows, name);
    }

    private static char DetectDelimiter(string text)
    {
        string first = text.Split('\n', 2)[0];
        int comma = first.Count(c => c == ','), semi = first.Count(c => c == ';'), tab = first.Count(c => c == '\t');
        return tab > comma && tab > semi ? '\t' : semi > comma ? ';' : ',';
    }

    private static IReadOnlyList<string> Cap(List<string> row) => row.Count > MaxColumns ? row.Take(MaxColumns).ToList() : row;

    // ---------------- XLSX ----------------

    private static FileStream Open(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private static XmlReaderSettings Safe => new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = false };

    public static RawTable ReadXlsx(string path)
    {
        using var fs = Open(path);
        if (fs.Length > MaxFileBytes) throw new PortfolioException("The file is larger than 25 MB, which is more than this tool will read.");
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
        if (zip.Entries.Sum(e => e.Length) > MaxUnzippedBytes) throw new PortfolioException("The workbook expands to an unreasonable size, so it was not opened.");

        bool date1904 = false;
        var sheets = new List<(string Name, string Target)>();
        var rels = new Dictionary<string, string>();
        if (zip.GetEntry("xl/_rels/workbook.xml.rels") is { } relEntry)
            using (var r = XmlReader.Create(relEntry.Open(), Safe))
                while (r.Read())
                    if (r.NodeType == XmlNodeType.Element && r.LocalName == "Relationship")
                        rels[r.GetAttribute("Id") ?? ""] = r.GetAttribute("Target") ?? "";
        var wb = zip.GetEntry("xl/workbook.xml") ?? throw new PortfolioException("That does not look like an Excel workbook.");
        using (var r = XmlReader.Create(wb.Open(), Safe))
            while (r.Read())
            {
                if (r.NodeType != XmlNodeType.Element) continue;
                if (r.LocalName == "workbookPr") date1904 = r.GetAttribute("date1904") is "1" or "true";
                else if (r.LocalName == "sheet" && r.GetAttribute("state") is not ("hidden" or "veryHidden"))
                {
                    string rid = r.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships") ?? "";
                    if (rels.TryGetValue(rid, out var target))
                        sheets.Add((r.GetAttribute("name") ?? "Sheet", target.StartsWith('/') ? target[1..] : "xl/" + target));
                }
            }
        if (sheets.Count == 0) throw new PortfolioException("The workbook has no visible sheets.");

        var shared = ReadSharedStrings(zip);
        var dateStyles = ReadDateStyles(zip);

        RawTable? first = null;
        foreach (var (name, target) in sheets)
        {
            var entry = zip.GetEntry(target);
            if (entry is null) continue;
            var table = ReadSheet(entry, name, shared, dateStyles, date1904);
            first ??= table;
            if (table.Headers.Any(h => PortfolioColumns.Looks(h, "number"))) return table;      // the first sheet that has an ANDA column
        }
        return first ?? throw new PortfolioException("No readable sheet was found in the workbook.");
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var list = new List<string>();
        if (zip.GetEntry("xl/sharedStrings.xml") is not { } e) return list;
        using var r = XmlReader.Create(e.Open(), Safe);
        var doc = System.Xml.Linq.XDocument.Load(r);
        foreach (var si in doc.Descendants().Where(x => x.Name.LocalName == "si"))
            list.Add(string.Concat(si.Descendants()
                .Where(t => t.Name.LocalName == "t" && !t.Ancestors().Any(a => a.Name.LocalName == "rPh" && a != si))
                .Select(t => t.Value)));
        return list;
    }

    private static HashSet<int> ReadDateStyles(ZipArchive zip)
    {
        var dateXf = new HashSet<int>();
        if (zip.GetEntry("xl/styles.xml") is not { } e) return dateXf;
        var custom = new Dictionary<int, string>();
        var xfFormats = new List<int>();
        using (var r = XmlReader.Create(e.Open(), Safe))
        {
            bool inCellXfs = false;
            while (r.Read())
            {
                if (r.NodeType == XmlNodeType.Element)
                {
                    if (r.LocalName == "numFmt" && int.TryParse(r.GetAttribute("numFmtId"), out int id)) custom[id] = r.GetAttribute("formatCode") ?? "";
                    else if (r.LocalName == "cellXfs") inCellXfs = true;
                    else if (r.LocalName == "xf" && inCellXfs) xfFormats.Add(int.TryParse(r.GetAttribute("numFmtId"), out int f) ? f : 0);
                }
                else if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "cellXfs") inCellXfs = false;
            }
        }
        for (int i = 0; i < xfFormats.Count; i++)
        {
            int id = xfFormats[i];
            bool isDate = custom.TryGetValue(id, out var code) ? IsDateFormat(code) : IsBuiltInDate(id);
            if (isDate) dateXf.Add(i);
        }
        return dateXf;
    }

    private static bool IsBuiltInDate(int id) => id is >= 14 and <= 22 or >= 27 and <= 36 or >= 45 and <= 47 or >= 50 and <= 58;

    internal static bool IsDateFormat(string code)
    {
        var sb = new StringBuilder(); bool quote = false, bracket = false;
        foreach (char c in code)
        {
            if (c == '"') { quote = !quote; continue; }
            if (quote) continue;
            if (c == '[') { bracket = true; continue; }
            if (c == ']') { bracket = false; continue; }
            if (!bracket) sb.Append(char.ToLowerInvariant(c));
        }
        string s = sb.ToString();
        return s.IndexOfAny(new[] { 'd', 'm', 'y', 'h', 's' }) >= 0 && !s.Contains("general");
    }

    private static RawTable ReadSheet(ZipArchiveEntry entry, string name, List<string> shared, HashSet<int> dateStyles, bool date1904)
    {
        var rows = new List<IReadOnlyList<string>>();
        using var r = XmlReader.Create(entry.Open(), Safe);
        List<string>? row = null;
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "row") row = new();
            else if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "row")
            {
                if (row is not null && row.Any(x => x.Length > 0)) rows.Add(row);
                row = null;
                if (rows.Count > MaxRows) throw new PortfolioException("The sheet has more than 20,000 rows, which is more than this tool will read.");
            }
            else if (r.NodeType == XmlNodeType.Element && r.LocalName == "c" && row is not null)
            {
                string cellRef = r.GetAttribute("r") ?? "", type = r.GetAttribute("t") ?? "";
                int style = int.TryParse(r.GetAttribute("s"), out int s) ? s : 0;
                int col = ColumnIndex(cellRef);
                string value = "";
                if (!r.IsEmptyElement)
                {
                    using var cell = r.ReadSubtree();
                    cell.Read();
                    while (cell.Read())
                    {
                        if (cell.NodeType != XmlNodeType.Element) continue;
                        if (cell.LocalName == "v") value = cell.ReadElementContentAsString();
                        else if (cell.LocalName == "is") value = ReadInline(cell.ReadSubtree());
                    }
                }
                value = type switch
                {
                    "s" => int.TryParse(value, out int idx) && idx >= 0 && idx < shared.Count ? shared[idx] : "",
                    "b" => value == "1" ? "TRUE" : "FALSE",
                    "e" => "",
                    "str" or "inlineStr" => value,
                    _ => dateStyles.Contains(style) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double serial)
                            ? FromSerial(serial, date1904) : value,
                };
                if (col >= MaxColumns) continue;
                while (row.Count < col) row.Add("");
                if (row.Count == col) row.Add(value.Trim()); else row[col] = value.Trim();
            }
        }
        return Shape(rows, name);
    }

    private static string ReadInline(XmlReader sub)
    {
        using (sub)
        {
            var sb = new StringBuilder();
            sub.Read();
            while (sub.Read())
                if (sub.NodeType == XmlNodeType.Element && sub.LocalName == "t") sb.Append(sub.ReadElementContentAsString());
            return sb.ToString();
        }
    }

    internal static string FromSerial(double serial, bool date1904)
    {
        if (serial < 1 || serial > 2_958_465) return serial.ToString(CultureInfo.InvariantCulture);
        var baseDate = date1904 ? new DateTime(1904, 1, 1) : new DateTime(1899, 12, 30);
        return baseDate.AddDays(Math.Floor(serial)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    internal static int ColumnIndex(string cellRef)
    {
        int n = 0;
        foreach (char c in cellRef) { if (!char.IsLetter(c)) break; n = n * 26 + (char.ToUpperInvariant(c) - 'A' + 1); }
        return Math.Max(0, n - 1);
    }

    // ---------------- shared ----------------

    private static RawTable Shape(List<IReadOnlyList<string>> rows, string name)
    {
        if (rows.Count == 0) throw new PortfolioException("The file is empty.");
        // The header row is the first of the first ten that names an ANDA column; otherwise the first row.
        int header = 0;
        for (int i = 0; i < Math.Min(10, rows.Count); i++)
            if (rows[i].Any(h => PortfolioColumns.Looks(h, "number"))) { header = i; break; }
        var headers = rows[header].Select(h => h.Trim()).ToList();
        return new RawTable(headers, rows.Skip(header + 1).ToList(), name);
    }
}
