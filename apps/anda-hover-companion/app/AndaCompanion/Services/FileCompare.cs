using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AndaCompanion.Services;

/// <summary>A table as the comparison sees it: a name to show, the headings, and the rows as text.</summary>
public sealed record CompareTable(string Name, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);

public sealed record CellChange(string Column, string From, string To, string Note);
public sealed record RowChange(string Row, IReadOnlyList<CellChange> Cells);
public sealed record DateShift(string Row, string Column, string From, string To, int Days);

public sealed record CompareResult(
    string? Key, string KeyWhy, IReadOnlyList<string> Shared, int RowsA, int RowsB, int Same, int ChangedRows, int ChangedCells,
    IReadOnlyList<string> Added, int AddedCount, IReadOnlyList<string> Removed, int RemovedCount, int BlankA, int BlankB,
    IReadOnlyList<string> DupA, IReadOnlyList<string> DupB, IReadOnlyList<string> ColsOnlyA, IReadOnlyList<string> ColsOnlyB,
    IReadOnlyList<(string Column, int Count)> ByColumn, IReadOnlyList<RowChange> Changes, DateShift? BiggestShift);

/// <summary>
/// "Compare two files": two versions of the same report, compared by fixed rules on this PC. No AI is used and nothing is sent. Rows are matched by
/// the column that identifies a row in both files (suggested; the person can choose another). Values are compared as text, as numbers ("1,000"
/// equals "1000.0") and as dates ("10/3/2026" equals "Oct 3, 2026"). The result is counts, a short summary in plain sentences, and the list of
/// changes. This is a port of the simulation's cmpTables(), kept the same rule for rule: the shared cases are in mockup/compare-golden.json.
/// </summary>
public static class FileCompare
{
    public const int MaxChanges = 50, MaxCells = 12, MaxKeys = 50;
    /// <summary>The longest value and the longest heading that are shown. Rows are still matched and compared on the whole value.</summary>
    public const int MaxValue = 200, MaxHead = 80;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The prompt for a written narrative. It is filled with the summary and the listed changes only, never with the files.</summary>
    public const string AiTemplate = "Write an executive summary of the differences between two versions of a report, for a busy manager: three to five sentences, then up to five bullets with what changed and what may need attention. Use only the facts below. Do not guess at reasons.\n\n{selection}";

    private static readonly Regex
        WsRun = JsRegex.Make(@"\s+"),
        Keyish = JsRegex.Make(@"(^|[^a-z])(anda|application|appl|app|number|num|no|id)([^a-z]|$)|#", true),
        Num = JsRegex.Make(@"^[-+]?\$?(?:\d{1,3}(?:,\d{3})+|\d{1,15})(?:\.\d{1,6})?%?$"),
        Iso = JsRegex.Make(@"^(\d{4})-(\d{2})-(\d{2})$"),
        Slash = JsRegex.Make(@"^(\d{1,2})\/(\d{1,2})\/(\d{4})$"),
        Named = JsRegex.Make(@"^([A-Za-z]{3})[a-z]*\.? (\d{1,2}),? (\d{4})$");

    private const string MonthStarts = "janfebmaraprmayjunjulaugsepoctnovdec";

    internal static string Show(string v, int max = MaxValue)
    {
        if (v.Length <= max) return v;
        int cut = max - 1; if (char.IsHighSurrogate(v[cut - 1])) cut--;
        return v[..cut] + "…";
    }

    /// <summary>Lines for the clipboard. A line that a spreadsheet would read as a formula (it starts with =, +, - or @) gets an apostrophe in front, so a pasted report is always text.</summary>
    public static IReadOnlyList<string> ForPaste(IEnumerable<string> lines) =>
        lines.Select(l => l.Length > 0 && "=+-@\t\r＝＋－＠".Contains(l[0]) ? "'" + l : l).ToList();

    internal static string Cell(string? v) => JsRegex.Trim(WsRun.Replace(v ?? "", " "));

    internal static double? Number(string v) =>
        Num.IsMatch(v) && double.TryParse(v.Replace("$", "").Replace(",", "").Replace("%", ""), NumberStyles.Float, Inv, out double n) ? n : null;

    /// <summary>The day a cell names, as a count of days, or null: 2026-10-03, 10/3/2026, Oct 3, 2026, October 3 2026.</summary>
    internal static int? Day(string v)
    {
        int y, mo, d; Match m;
        if ((m = Iso.Match(v)).Success) { y = I(m, 1); mo = I(m, 2); d = I(m, 3); }
        else if ((m = Slash.Match(v)).Success) { mo = I(m, 1); d = I(m, 2); y = I(m, 3); }
        else if ((m = Named.Match(v)).Success)
        {
            int at = MonthStarts.IndexOf(m.Groups[1].Value.ToLowerInvariant(), StringComparison.Ordinal);
            mo = at >= 0 && at % 3 == 0 ? at / 3 + 1 : 0; d = I(m, 2); y = I(m, 3);
        }
        else return null;
        if (y < 1900 || y > 2200 || mo < 1 || mo > 12 || d < 1 || d > DateTime.DaysInMonth(y, mo)) return null;
        return new DateOnly(y, mo, d).DayNumber;
    }

    private static int I(Match m, int g) => int.Parse(m.Groups[g].Value, NumberStyles.None, Inv);

    /// <summary>Headings as shown, made unique: an empty heading is "Column 3", a repeated one gets "(2)".</summary>
    internal static List<string> Headers(IReadOnlyList<string> headers)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal); var result = new List<string>();
        for (int i = 0; i < headers.Count; i++)
        {
            string name = Show(Cell(headers[i]), MaxHead); if (name.Length == 0) name = "Column " + (i + 1).ToString(Inv);
            string k = name.ToLowerInvariant(); int n = seen.GetValueOrDefault(k) + 1; seen[k] = n;
            if (n > 1) name += " (" + n.ToString(Inv) + ")";
            result.Add(name);
        }
        return result;
    }

    private sealed record Col(string Name, int A, int B);

    public static CompareResult Compare(CompareTable a, CompareTable b, string? keyName = null)
    {
        var ha = Headers(a.Headers); var hb = Headers(b.Headers);
        var la = ha.Select(h => h.ToLowerInvariant()).ToList(); var lb = hb.Select(h => h.ToLowerInvariant()).ToList();
        var shared = new List<Col>();
        for (int i = 0; i < la.Count; i++) { int j = lb.IndexOf(la[i]); if (j >= 0) shared.Add(new Col(ha[i], i, j)); }
        var colsOnlyA = ha.Where((_, i) => !lb.Contains(la[i])).ToList(); var colsOnlyB = hb.Where((_, j) => !la.Contains(lb[j])).ToList();
        var ra = a.Rows.Select(r => r.Select(Cell).ToList()).ToList(); var rb = b.Rows.Select(r => r.Select(Cell).ToList()).ToList();
        static string At(List<string> row, int i) => i < row.Count ? row[i] : "";
        static bool Identifies(List<List<string>> rows, int i)
        {
            if (rows.Count == 0) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in rows) { string v = At(r, i); if (v.Length == 0 || !seen.Add(v)) return false; }
            return true;
        }

        // The key: the person's choice if it is a shared column; else the first shared column that names every row once in both files,
        // a column whose heading sounds like a number first.
        Col? key; string keyWhy;
        string wanted = Cell(keyName).ToLowerInvariant();
        var chosen = string.IsNullOrEmpty(keyName) ? null : shared.FirstOrDefault(c => c.Name.ToLowerInvariant() == wanted);
        if (chosen is not null) { key = chosen; keyWhy = "chosen by you"; }
        else
        {
            var able = shared.Where(c => Identifies(ra, c.A) && Identifies(rb, c.B)).ToList();
            key = able.FirstOrDefault(c => Keyish.IsMatch(c.Name)) ?? able.FirstOrDefault();
            keyWhy = key is not null ? "it names every row once in both files" : shared.Count == 0 ? "the files share no column" : "no shared column names every row once in both files";
        }

        int same = 0, changedRows = 0, changedCells = 0, blankA = 0, blankB = 0; DateShift? biggest = null;
        var counts = shared.ToDictionary(c => c.Name, _ => 0, StringComparer.Ordinal);
        var changes = new List<RowChange>(); var added = new List<string>(); var removed = new List<string>(); var dupA = new List<string>(); var dupB = new List<string>();

        void CompareRows(string name, List<string> x, List<string> y)
        {
            var cells = new List<CellChange>(); string label = Show(name);
            foreach (var c in shared)
            {
                if (key is not null && ReferenceEquals(c, key)) continue;
                string va = At(x, c.A), vb = At(y, c.B); if (va == vb) continue;
                double? na = Number(va), nb = Number(vb); int? da = Day(va), db = Day(vb); string note = "";
                if (na is { } p && nb is { } q)
                {
                    if (p == q) continue;
                    if (p == Math.Floor(p) && q == Math.Floor(q) && Math.Abs(p) < 1e12 && Math.Abs(q) < 1e12) { double d = q - p; note = (d > 0 ? "+" : "−") + Math.Abs(d).ToString("#,0", Inv); }
                }
                else if (da is { } dayA && db is { } dayB)
                {
                    if (dayA == dayB) continue;
                    int d = dayB - dayA; note = Math.Abs(d).ToString(Inv) + " " + (Math.Abs(d) == 1 ? "day" : "days") + (d > 0 ? " later" : " earlier");
                    if (biggest is null || Math.Abs(d) > Math.Abs(biggest.Days)) biggest = new DateShift(label, c.Name, Show(va), Show(vb), d);
                }
                counts[c.Name]++; changedCells++;
                if (cells.Count < MaxCells) cells.Add(new CellChange(c.Name, Show(va), Show(vb), note));
            }
            if (cells.Count == 0) { same++; return; }
            changedRows++; if (changes.Count < MaxChanges) changes.Add(new RowChange(label, cells));
        }

        if (key is not null)
        {
            static (List<string> Order, Dictionary<string, List<string>> Map, int Blank) Index(List<List<string>> rows, int i, List<string> dups)
            {
                var order = new List<string>(); var map = new Dictionary<string, List<string>>(StringComparer.Ordinal); int blank = 0;
                foreach (var r in rows)
                {
                    string v = At(r, i);
                    if (v.Length == 0) { blank++; continue; }
                    if (map.ContainsKey(v)) { if (!dups.Contains(v) && dups.Count < 5) dups.Add(v); continue; }
                    map[v] = r; order.Add(v);
                }
                return (order, map, blank);
            }
            var ia = Index(ra, key.A, dupA); var ib = Index(rb, key.B, dupB); blankA = ia.Blank; blankB = ib.Blank;
            foreach (string k in ia.Order) { if (ib.Map.TryGetValue(k, out var other)) CompareRows(k, ia.Map[k], other); else removed.Add(k); }
            foreach (string k in ib.Order) if (!ia.Map.ContainsKey(k)) added.Add(k);
        }
        else
        {
            int n = Math.Min(ra.Count, rb.Count);
            for (int i = 0; i < n; i++) CompareRows("row " + (i + 1).ToString(Inv), ra[i], rb[i]);
            for (int i = n; i < ra.Count; i++) removed.Add("row " + (i + 1).ToString(Inv));
            for (int i = n; i < rb.Count; i++) added.Add("row " + (i + 1).ToString(Inv));
        }
        var byColumn = shared.Select((c, i) => (c.Name, Count: counts[c.Name], i)).Where(x => x.Count > 0).OrderByDescending(x => x.Count).ThenBy(x => x.i).Select(x => (x.Name, x.Count)).ToList();
        return new CompareResult(key?.Name, keyWhy, shared.Select(c => c.Name).ToList(), ra.Count, rb.Count, same, changedRows, changedCells,
            added.Take(MaxKeys).Select(k => Show(k)).ToList(), added.Count, removed.Take(MaxKeys).Select(k => Show(k)).ToList(), removed.Count, blankA, blankB,
            dupA.Select(k => Show(k)).ToList(), dupB.Select(k => Show(k)).ToList(), colsOnlyA, colsOnlyB, byColumn, changes, biggest);
    }

    private static string N(int x) => x.ToString("#,0", Inv);
    private static string Pl(int n, string one, string many) => n == 1 ? one : many;
    private static string Rows(int x) => $"{N(x)} {Pl(x, "row", "rows")}";
    private static string List(IReadOnlyList<string> items) => items.Count == 1 ? items[0] : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];

    /// <summary>The executive summary: a few plain sentences made from the counts.</summary>
    public static IReadOnlyList<string> Summary(CompareResult r, string nameA, string nameB)
    {
        var lines = new List<string>
        {
            $"{nameA} has {Rows(r.RowsA)}; {nameB} has {Rows(r.RowsB)}." + (r.Key is not null ? $" Rows were matched by “{r.Key}” ({r.KeyWhy})." : r.Shared.Count == 0 ? "" : $" Rows were compared by position, because {r.KeyWhy}."),
        };
        if (r.RowsA == 0 && r.RowsB == 0) lines.Add("Neither file has any rows.");
        else if (r.Shared.Count == 0) lines.Add("The two files have no column heading in common, so their rows could not be compared.");
        else if (r.ChangedRows == 0 && r.AddedCount == 0 && r.RemovedCount == 0 && r.ColsOnlyA.Count == 0 && r.ColsOnlyB.Count == 0) lines.Add("The two files hold the same rows and the same values.");
        else
        {
            lines.Add($"{Rows(r.Same)} {Pl(r.Same, "is", "are")} the same, {N(r.ChangedRows)} changed ({N(r.ChangedCells)} {Pl(r.ChangedCells, "cell", "cells")}), {N(r.AddedCount)} {Pl(r.AddedCount, "is", "are")} new in {nameB}, and {N(r.RemovedCount)} {Pl(r.RemovedCount, "is", "are")} no longer there.");
            if (r.ByColumn.Count > 0) lines.Add($"Most changes are in {List(r.ByColumn.Take(3).Select(c => $"{c.Column} ({N(c.Count)})").ToList())}.");
            if (r.BiggestShift is { } s) lines.Add($"The largest date change: {s.Column} for {s.Row} moved {Math.Abs(s.Days).ToString(Inv)} {Pl(Math.Abs(s.Days), "day", "days")} {(s.Days > 0 ? "later" : "earlier")} ({s.From} to {s.To}).");
            if (r.ColsOnlyB.Count > 0) lines.Add($"{nameB} has {Pl(r.ColsOnlyB.Count, "a column", "columns")} that {nameA} does not: {List(r.ColsOnlyB.Take(6).ToList())}.");
            if (r.ColsOnlyA.Count > 0) lines.Add($"{nameA} has {Pl(r.ColsOnlyA.Count, "a column", "columns")} that {nameB} does not: {List(r.ColsOnlyA.Take(6).ToList())}.");
        }
        if (r.BlankA + r.BlankB > 0) lines.Add($"{Rows(r.BlankA + r.BlankB)} had no value in “{r.Key}” and {Pl(r.BlankA + r.BlankB, "was", "were")} left out.");
        if (r.DupA.Count + r.DupB.Count > 0) lines.Add($"Some values of “{r.Key}” appear more than once (for example {r.DupA.Concat(r.DupB).First()}); the first row of each was used.");
        lines.Add("Compared as text, numbers and dates, in the columns both files have. Formulas, formatting, hidden rows and other sheets were not compared.");
        return lines;
    }

    /// <summary>The summary and then the detail, as plain text to copy.</summary>
    public static IReadOnlyList<string> Report(CompareResult r, string nameA, string nameB)
    {
        var lines = Summary(r, nameA, nameB).ToList();
        static string Shown(string v) => v.Length == 0 ? "(empty)" : v;
        if (r.Changes.Count > 0)
        {
            lines.Add(""); lines.Add("CHANGED");
            foreach (var c in r.Changes) foreach (var x in c.Cells) lines.Add($"{c.Row}: {x.Column}: {Shown(x.From)} → {Shown(x.To)}{(x.Note.Length > 0 ? " (" + x.Note + ")" : "")}");
            int more = r.ChangedRows - r.Changes.Count;
            if (more > 0) lines.Add($"…and {N(more)} more changed {Pl(more, "row", "rows")}.");
        }
        if (r.AddedCount > 0) { lines.Add(""); lines.Add("NEW IN " + nameB.ToUpperInvariant()); lines.Add(string.Join(", ", r.Added) + (r.AddedCount > r.Added.Count ? $", and {N(r.AddedCount - r.Added.Count)} more" : "")); }
        if (r.RemovedCount > 0) { lines.Add(""); lines.Add("NO LONGER IN " + nameB.ToUpperInvariant()); lines.Add(string.Join(", ", r.Removed) + (r.RemovedCount > r.Removed.Count ? $", and {N(r.RemovedCount - r.Removed.Count)} more" : "")); }
        return lines;
    }

    /// <summary>A short form for an AI prompt: the summary, then changes until the limit is reached.</summary>
    public static string Digest(CompareResult r, string nameA, string nameB, int max = 1800)
    {
        string text = string.Join("\n", Report(r, nameA, nameB));
        if (text.Length <= max) return text;
        text = text[..max]; int cut = text.LastIndexOf('\n');
        return (cut > 200 ? text[..cut] : text) + "\n(the list is cut here)";
    }

    /// <summary>A one-page report to save: text only, with every value escaped.</summary>
    public static string HtmlReport(CompareResult r, string nameA, string nameB)
    {
        static string E(string t) => t.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        var lines = Report(r, nameA, nameB).ToList(); int cut = lines.IndexOf("");
        string summary = string.Concat((cut < 0 ? lines : lines.Take(cut)).Select(l => $"<p>{E(l)}</p>"));
        string detail = cut < 0 ? "" : $"<pre>{E(string.Join("\n", lines.Skip(cut + 1)))}</pre>";
        string title = $"Comparison: {E(nameA)} and {E(nameB)}";
        return "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'\"><title>" + title + "</title><style>body{font:15px/1.5 Segoe UI,Arial,sans-serif;max-width:820px;margin:32px auto;padding:0 16px;color:#1b2430}h1{font-size:22px}pre{white-space:pre-wrap;font:13px/1.5 Consolas,monospace;background:#f4f5f7;padding:12px;border-radius:8px}</style></head><body><h1>"
            + title + "</h1>" + summary + detail + "<p><small>Made on this PC by fixed rules. No AI was used and nothing was sent.</small></p></body></html>";
    }

    // Two fictional versions of one report, for trying the tool with no file at hand. The same as the simulation's.
    public static readonly CompareTable SampleA = new("Portfolio, Nov 10", new[] { "ANDA", "Product", "Applicant", "Goal date", "Forecast date", "Status", "Open items" }, new[]
    {
        new[] { "216482", "Metformin HCl ER Tablets, 500 mg", "Meridian Pharmaceuticals (fictional)", "10/3/2026", "10/17/2026", "IR response pending", "2" },
        new[] { "214990", "Metformin HCl ER Tablets, 750 mg", "Meridian Pharmaceuticals (fictional)", "8/15/2026", "8/6/2026", "On track", "0" },
        new[] { "217301", "Atorvastatin Calcium Tablets, 20 mg", "Northfield Generics (fictional)", "12/1/2026", "12/1/2026", "Under review", "1" },
        new[] { "218044", "Lisinopril Tablets, 10 mg", "Harbor Labs (fictional)", "1/15/2027", "1/29/2027", "Labeling comments sent", "3" },
        new[] { "219120", "Sertraline HCl Tablets, 50 mg", "Northfield Generics (fictional)", "2/20/2027", "2/20/2027", "Under review", "0" },
        new[] { "219555", "Amlodipine Besylate Tablets, 5 mg", "Cedar Point Pharma (fictional)", "3/5/2027", "3/5/2027", "Filed", "1,000" },
    });
    public static readonly CompareTable SampleB = new("Portfolio, Nov 17", new[] { "ANDA", "Product", "Applicant", "Goal date", "Forecast date", "Status", "Open items", "Reviewer note" }, new[]
    {
        new[] { "216482", "Metformin HCl ER Tablets, 500 mg", "Meridian Pharmaceuticals (fictional)", "Oct 3, 2026", "10/31/2026", "IR response received", "1", "Response in on Nov 14" },
        new[] { "214990", "Metformin HCl ER Tablets, 750 mg", "Meridian Pharmaceuticals (fictional)", "8/15/2026", "8/6/2026", "On track", "0", "" },
        new[] { "217301", "Atorvastatin Calcium Tablets, 20 mg", "Northfield Generics (fictional)", "12/1/2026", "11/24/2026", "Under review", "1", "" },
        new[] { "218044", "Lisinopril Tablets, 10 mg", "Harbor Labs (fictional)", "1/15/2027", "1/29/2027", "Labeling agreed", "0", "Final format pending" },
        new[] { "219555", "Amlodipine Besylate Tablets, 5 mg", "Cedar Point Pharma (fictional)", "3/5/2027", "3/5/2027", "Filed", "1000.0", "" },
        new[] { "220017", "Losartan Potassium Tablets, 25 mg", "Harbor Labs (fictional)", "4/10/2027", "4/10/2027", "Filed", "0", "New this week" },
    });
}

/// <summary>A calendar invitation made from a piece of text: its title, its day, and the file content in the standard calendar format.</summary>
public sealed record Invitation(string Title, string Day, string Shown, string Ics);

/// <summary>
/// A calendar invitation from a selection, by fixed rules: the first real date the text names becomes an all-day event; with no year it is the
/// next such day; a bare "3/4" is not read as a date. The result is a small .ics file. The person saves it and opens it in their calendar; the
/// app sends nothing and adds nothing itself. The same rules as the simulation's icsFor(); shared cases in mockup/compare-golden.json.
/// </summary>
public static class Invitations
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly Regex WsRun = JsRegex.Make(@"\s+"), LineEnd = new("\r\n|\r|\n", RegexOptions.CultureInvariant, JsRegex.Limit), LineCut = new("\r?\n", RegexOptions.CultureInvariant, JsRegex.Limit);

    /// <summary>Control characters are taken out and the rarer line breaks become plain ones, so no part of a selection can start a line of its own in the file.</summary>
    internal static string Clean(string t)
    {
        var sb = new StringBuilder(t.Length);
        foreach (char c in t)
        {
            if (c == (char)0x85 || c == (char)0x2028 || c == (char)0x2029) sb.Append('\n');
            else if ((c < 32 && c != 9 && c != 10 && c != 13) || c == 127) continue;
            else sb.Append(c);
        }
        return sb.ToString();
    }

    internal static string Escape(string t) => LineEnd.Replace(t.Replace("\\", "\\\\"), "\\n").Replace(",", "\\,").Replace(";", "\\;");

    internal static IEnumerable<string> Fold(string line)
    {
        string rest = line;
        while (rest.Length > 60)
        {
            int cut = 60; if (char.IsHighSurrogate(rest[cut - 1])) cut--;
            yield return rest[..cut]; rest = " " + rest[cut..];
        }
        yield return rest;
    }

    private static string TrimEndJs(string s) { int b = s.Length; while (b > 0 && JsRegex.IsWs(s[b - 1])) b--; return s[..b]; }

    public static Invitation? For(string? text, DateOnly today, string stamp, string uid)
    {
        string t = Clean(text ?? "");
        if (JsRegex.Trim(t).Length == 0) return null;
        DateOnly? day = null;
        try
        {
            foreach (Match m in DraftText.Date.Matches(t))
            {
                if (day is not null) break;
                if (m.Index > 0 && (t[m.Index - 1] is (>= '0' and <= '9') or '/')) continue;
                if (m.Groups[4].Success && !m.Groups[6].Success) continue;              // "3/4" with no year is as often a fraction or a version as a date
                if (DraftText.ToMention(m) is not { } mt || mt.Mon < 1 || mt.Mon > 12 || mt.Day < 1) continue;
                if (mt.Year is { } y) { if (y >= 1900 && y <= 2200 && mt.Day <= DateTime.DaysInMonth(y, mt.Mon)) day = new DateOnly(y, mt.Mon, mt.Day); }
                else
                    for (int year = today.Year; year <= today.Year + 8 && day is null; year++)
                        if (mt.Day <= DateTime.DaysInMonth(year, mt.Mon) && new DateOnly(year, mt.Mon, mt.Day) >= today) day = new DateOnly(year, mt.Mon, mt.Day);
            }
        }
        catch (RegexMatchTimeoutException) { return null; }
        if (day is not { } found) return null;

        string firstLine = JsRegex.Trim(WsRun.Replace(LineCut.Split(t).FirstOrDefault(l => JsRegex.Trim(l).Length > 0) ?? "", " "));
        var numbers = DraftText.AndaNumbers(t);
        string title = firstLine.Length > 80 ? TrimEndJs(firstLine[..79]) + "…" : firstLine;
        if (numbers.Count == 1 && !title.Contains(numbers[0], StringComparison.Ordinal)) title = $"ANDA {numbers[0]}: {title}";
        string body = JsRegex.Trim(t.Replace("\r\n", "\n").Replace('\r', '\n'));
        string description = body.Length > 800 ? TrimEndJs(body[..799]) + "…" : body;
        var lines = new[]
        {
            "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//ANDA Companion (Notula)//EN", "CALSCALE:GREGORIAN", "METHOD:PUBLISH", "BEGIN:VEVENT", "UID:" + uid, "DTSTAMP:" + stamp,
            "DTSTART;VALUE=DATE:" + found.ToString("yyyyMMdd", Inv), "DTEND;VALUE=DATE:" + found.AddDays(1).ToString("yyyyMMdd", Inv), "SUMMARY:" + Escape(title), "DESCRIPTION:" + Escape(description),
            "TRANSP:TRANSPARENT", "END:VEVENT", "END:VCALENDAR",
        };
        return new Invitation(title, found.ToString("yyyy-MM-dd", Inv), found.ToString("dddd, MMM d, yyyy", Inv), string.Join("\r\n", lines.SelectMany(Fold)) + "\r\n");
    }
}

/// <summary>Four ways to have a text read for you. Each is a prompt that is filled in and shown first; nothing is sent until the person presses Send.</summary>
public static class ReadingPrompts
{
    public static readonly IReadOnlyList<(string Name, string Template)> All = new[]
    {
        ("Give me the TL;DR", "Give me the TL;DR of the text below in at most five short bullets. Use only what the text says. If something important is unclear or missing, say so in one line.\n\n{selection}"),
        ("What else should I know?", "Read the text below. List what a careful reader should also know before acting on it: what it assumes, what it leaves out, dates or numbers that need checking, and anything that could be misread. At most six short bullets. Do not invent facts.\n\n{selection}"),
        ("Onboard me on this topic", "Explain the text below to someone new to the topic: what it is about, the terms it uses, who is involved and what happens next. Plain words and short paragraphs. Mark anything you are unsure of as unsure.\n\n{selection}"),
        ("Why does it matter?", "In three or four sentences, say why the text below matters to the reader, what decision or action it calls for, and by when, if it says. Use only what the text says.\n\n{selection}"),
    };
}

/// <summary>
/// "My usage": how often each tool was used in the last 7 and 30 days, read from the optional usage log. Counts only: the log never holds an
/// ANDA number, a name or any text, and it exists only if the person switched it on. The person copies the summary if they choose to share it.
/// </summary>
public static class UsageSummary
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static readonly IReadOnlyList<(string Event, string Label)> Events = new[]
    {
        ("card_open", "Cards opened"), ("draft_check", "Drafts reviewed"), ("ask_open", "Selections asked about"), ("compare_files", "File comparisons"), ("reading_prompt", "Reading prompts used"),
        ("note_added", "Notes added from a selection"), ("invitation_saved", "Calendar invitations saved"), ("copy_status", "Status lines copied"),
    };

    public static IReadOnlyList<string> Lines(IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> days, DateOnly today)
    {
        IEnumerable<IReadOnlyDictionary<string, double>> Within(int n) => days.Where(d =>
            DateOnly.TryParseExact(d.Key, "yyyy-MM-dd", Inv, DateTimeStyles.None, out var x) && x <= today && x.DayNumber > today.DayNumber - n).Select(d => d.Value);
        static long Sum(IEnumerable<IReadOnlyDictionary<string, double>> set, string evt) =>
            set.Sum(c => c.TryGetValue(evt, out double v) && v > 0 && v == Math.Floor(v) ? (long)v : 0L);
        var week = Within(7).ToList(); var month = Within(30).ToList();
        var lines = new List<string> { "My usage: last 7 days (last 30 days)" }; bool any = false;
        foreach (var (evt, label) in Events)
        {
            long a = Sum(week, evt), b = Sum(month, evt);
            if (b > 0) { any = true; lines.Add($"{label}: {a.ToString("#,0", Inv)} ({b.ToString("#,0", Inv)})"); }
        }
        if (!any) lines.Add("Nothing has been counted yet.");
        lines.Add("Counts only. No ANDA number, name or text is ever recorded. Kept on this PC, and shared only if you copy it.");
        return lines;
    }

    /// <summary>Reads the usage log (date,event,seconds per line) into counts per day. A line that is not in that form is skipped.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Read(string? path)
    {
        var days = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);
        try
        {
            if (path is null || !File.Exists(path) || new FileInfo(path).Length > 20L * 1024 * 1024) return Freeze(days);
            foreach (string line in File.ReadLines(path))
            {
                var parts = line.Split(',');
                if (parts.Length < 2 || !DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", Inv, DateTimeStyles.None, out _) || !UsageLog.Events.Contains(parts[1])) continue;
                if (!days.TryGetValue(parts[0], out var day)) days[parts[0]] = day = new Dictionary<string, double>(StringComparer.Ordinal);
                day[parts[1]] = day.GetValueOrDefault(parts[1]) + 1;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* a log that cannot be read is an empty summary */ }
        return Freeze(days);
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Freeze(Dictionary<string, Dictionary<string, double>> days) =>
        days.ToDictionary(d => d.Key, d => (IReadOnlyDictionary<string, double>)d.Value, StringComparer.Ordinal);
}
