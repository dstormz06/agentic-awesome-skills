using System.Globalization;
using System.Text;
using AndaCompanion.Data;

namespace AndaCompanion.Services;

public enum SelectionKind { Empty, Anda, Table, List, Text }

/// <summary>What a selection looks like, in words, and which tool suits it best. A fixed rule, never a guess about meaning.</summary>
public sealed record SelectionShape(SelectionKind Kind, int Rows, int Columns, string Label, string Suggestion);

/// <summary>
/// Quick checks on selected text or spreadsheet cells, done on this PC with fixed rules: no AI, nothing sent, the same answer every time.
/// Cells copied from Excel arrive as tab-separated text (rows on lines, quotes around a cell that holds a tab or a line break); that is what is read here.
/// Each check says what it found and how much it looked at. It reports; it never changes anything and never decides what a repeat or a difference means.
/// </summary>
public static class SelectionHelpers
{
    public const int MaxChars = 20000, MaxRows = 1000, MaxColumns = 30, MaxLines = 12;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ---------- reading the selection ----------

    /// <summary>Rows of cells, trimmed. A trailing empty line is dropped. At most <see cref="MaxRows"/> rows and <see cref="MaxColumns"/> columns are kept.</summary>
    public static string[][] Rows(string? text)
    {
        string t = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        if (t.Length > MaxChars) t = t[..MaxChars];
        var rows = new List<string[]>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        int i = 0;
        void EndCell() { cells.Add(cell.ToString().Trim()); cell.Clear(); }
        void EndRow() { EndCell(); rows.Add(cells.Take(MaxColumns).ToArray()); cells = new List<string>(); }
        while (i < t.Length && rows.Count < MaxRows)
        {
            char c = t[i];
            if (c == '"' && cell.Length == 0)
            {
                // a quoted cell: runs to the closing quote; two quotes in a row are one quote
                int j = i + 1; var q = new StringBuilder(); bool closed = false;
                while (j < t.Length)
                {
                    if (t[j] == '"') { if (j + 1 < t.Length && t[j + 1] == '"') { q.Append('"'); j += 2; continue; } closed = true; j++; break; }
                    q.Append(t[j]); j++;
                }
                if (closed && (j >= t.Length || t[j] == '\t' || t[j] == '\n')) { cell.Append(q); i = j; continue; }
                cell.Append('"'); i++; continue;      // not a well-formed quoted cell: the quote is just a character
            }
            if (c == '\t') { EndCell(); i++; continue; }
            if (c == '\n') { EndRow(); i++; continue; }
            cell.Append(c); i++;
        }
        if (rows.Count < MaxRows && (cell.Length > 0 || cells.Count > 0)) EndRow();
        while (rows.Count > 0 && rows[^1].All(x => x.Length == 0)) rows.RemoveAt(rows.Count - 1);
        return rows.ToArray();
    }

    public static SelectionShape Classify(string? text)
    {
        string trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0)
            return new(SelectionKind.Empty, 0, 0, "Nothing is selected.", "Select text or cells and press the shortcut again, or paste them above.");
        var rows = Rows(text);
        int columns = rows.Length == 0 ? 0 : rows.Max(r => r.Length);
        int wide = rows.Count(r => r.Length >= 2);
        if (rows.Length >= 2 && columns >= 2 && wide >= 2)
            return new(SelectionKind.Table, rows.Length, columns, $"A table of {rows.Length} rows and {columns} columns.",
                "Try “Find duplicates”, “Compare two columns”, “Find blanks” or “Totals”.");
        if (rows.Length == 1 && trimmed.Length <= 60 && AndaValidator.Extract(trimmed) is { } number)
            return new(SelectionKind.Anda, 1, 1, $"An ANDA number: {number}.", "Use Lookup for its card, or ask a stored prompt about it.");
        if (rows.Length >= 2)
            return new(SelectionKind.List, rows.Length, 1, $"A list of {rows.Length} lines.", "Try “Find duplicates”, or ask a stored prompt about it.");
        bool draftLike = AndaValidator.Extract(trimmed) is not null || System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"\b(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\.?\s+[0-9]{1,2}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return new(SelectionKind.Text, 1, 1, $"Text of {trimmed.Length:N0} characters.",
            draftLike ? "Use “Review a draft” to check it, or ask a stored prompt about it." : "Ask a stored prompt about it, or use “Review a draft” to check it.");
    }

    // ---------- the checks ----------

    private static bool IsNumber(string s, out double value)
    {
        string t = s.Trim().Replace("$", "").Replace(",", "");
        if (t.EndsWith('%')) t = t[..^1];
        value = 0;
        if (t.Length == 0) return false;
        int k = 0; if (t[0] is '-' or '+') k = 1;
        bool digits = false, dot = false;
        for (; k < t.Length; k++)
        {
            char c = t[k];
            if (c is >= '0' and <= '9') digits = true;
            else if (c == '.' && !dot) dot = true;
            else return false;
        }
        return digits && double.TryParse(t, NumberStyles.Float, Inv, out value);
    }

    /// <summary>The first row is treated as headings when there is more than one row and it is all words (no numbers), none blank, none repeated.</summary>
    private static bool HasHeader(string[][] rows) =>
        rows.Length >= 2 && rows[0].Length >= 2 && rows[0].All(c => c.Length > 0 && !IsNumber(c, out _))
        && rows[0].Select(c => c.ToLowerInvariant()).Distinct().Count() == rows[0].Length;

    private static string Heading(string[][] rows, bool header, int col) => header && col < rows[0].Length ? $" ({rows[0][col]})" : "";

    private static string Examples(IEnumerable<string> items, int max = 6)
    {
        var list = items.ToList();
        string shown = string.Join(", ", list.Take(max).Select(x => "“" + Short(x) + "”"));
        return list.Count > max ? shown + $" and {list.Count - max} more" : shown;
    }

    private static string Short(string s) => s.Length <= 40 ? s : s[..37] + "...";

    private static string Plural(int n, string one, string many) => n == 1 ? one : many;

    public static IReadOnlyList<string> Describe(string? text)
    {
        var shape = Classify(text);
        var lines = new List<string> { shape.Label };
        if (shape.Kind is SelectionKind.Empty or SelectionKind.Anda or SelectionKind.Text) return lines;
        var rows = Rows(text);
        bool header = HasHeader(rows);
        if (header) lines.Add("The first row looks like headings: " + string.Join(", ", rows[0].Select(c => "“" + Short(c) + "”")) + ".");
        int blank = rows.Skip(header ? 1 : 0).Sum(r => r.Count(c => c.Length == 0));
        lines.Add(blank == 0 ? "No blank cells." : $"{blank} blank {Plural(blank, "cell", "cells")}.");
        return lines;
    }

    public static IReadOnlyList<string> FindDuplicates(string? text)
    {
        var shape = Classify(text);
        if (shape.Kind is SelectionKind.Empty or SelectionKind.Anda or SelectionKind.Text)
            return new[] { "There is nothing to compare: select cells or a list with more than one line." };
        var rows = Rows(text);
        bool header = HasHeader(rows);
        int start = header ? 1 : 0;
        var lines = new List<string>();
        int repeatedRows = 0;
        var firstSeen = new Dictionary<string, int>();
        for (int r = start; r < rows.Length; r++)
        {
            if (rows[r].All(c => c.Length == 0)) continue;
            string key = string.Join("\t", rows[r]).ToLowerInvariant();
            if (firstSeen.TryGetValue(key, out int first))
            {
                repeatedRows++;
                if (lines.Count < MaxLines) lines.Add($"Row {r + 1} repeats row {first + 1}: {string.Join(" | ", rows[r].Select(Short))}");
            }
            else firstSeen[key] = r;
        }
        int repeatedValues = 0, columnsWithRepeats = 0;
        int columns = rows.Max(r => r.Length);
        for (int c = 0; c < columns; c++)
        {
            var seen = new Dictionary<string, List<int>>();
            var order = new List<string>();
            for (int r = start; r < rows.Length; r++)
            {
                string v = c < rows[r].Length ? rows[r][c] : "";
                if (v.Length == 0) continue;
                string key = v.ToLowerInvariant();
                if (!seen.TryGetValue(key, out var where)) { where = new List<int>(); seen[key] = where; order.Add(key); }
                where.Add(r + 1);
            }
            var dups = order.Where(k => seen[k].Count >= 2).ToList();
            if (dups.Count == 0) continue;
            columnsWithRepeats++; repeatedValues += dups.Count;
            if (columns >= 2 && rows.Length >= 2)
                foreach (var k in dups.Take(3))
                    if (lines.Count < MaxLines * 2)
                        lines.Add($"Column {c + 1}{Heading(rows, header, c)}: “{Short(rows[seen[k][0] - 1][c])}” appears {seen[k].Count} times (rows {string.Join(", ", seen[k].Take(6))}{(seen[k].Count > 6 ? ", …" : "")}).");
        }
        string summary = repeatedRows == 0 && repeatedValues == 0
            ? "No repeated rows and no repeated values."
            : $"Found {repeatedRows} repeated {Plural(repeatedRows, "row", "rows")} and {repeatedValues} repeated {Plural(repeatedValues, "value", "values")} in {columnsWithRepeats} {Plural(columnsWithRepeats, "column", "columns")}.";
        lines.Insert(0, summary);
        lines.Add($"Looked at {rows.Length - start} {Plural(rows.Length - start, "row", "rows")}{(header ? " (not counting the headings)" : "")}. Upper and lower case count as the same, and blank cells are ignored.");
        return lines;
    }

    /// <summary>Compares two columns (1 = first). What is in both, and what is only in one.</summary>
    public static IReadOnlyList<string> CompareColumns(string? text, int a = 1, int b = 2)
    {
        var shape = Classify(text);
        if (shape.Kind != SelectionKind.Table) return new[] { "Comparing needs a table: select at least two columns of cells." };
        var rows = Rows(text);
        int columns = rows.Max(r => r.Length);
        if (a < 1 || b < 1 || a > columns || b > columns || a == b)
            return new[] { $"Please choose two different columns between 1 and {columns}." };
        bool header = HasHeader(rows);
        int start = header ? 1 : 0;
        List<string> Column(int c) => rows.Skip(start).Select(r => c - 1 < r.Length ? r[c - 1] : "").Where(v => v.Length > 0).ToList();
        var colA = Column(a); var colB = Column(b);
        var setA = new HashSet<string>(colA.Select(v => v.ToLowerInvariant())); var setB = new HashSet<string>(colB.Select(v => v.ToLowerInvariant()));
        List<string> Distinct(List<string> col) { var seen = new HashSet<string>(); return col.Where(v => seen.Add(v.ToLowerInvariant())).ToList(); }
        var both = Distinct(colA).Where(v => setB.Contains(v.ToLowerInvariant())).ToList();
        var onlyA = Distinct(colA).Where(v => !setB.Contains(v.ToLowerInvariant())).ToList();
        var onlyB = Distinct(colB).Where(v => !setA.Contains(v.ToLowerInvariant())).ToList();
        string nameA = $"column {a}{Heading(rows, header, a - 1)}", nameB = $"column {b}{Heading(rows, header, b - 1)}";
        var lines = new List<string>
        {
            $"In both {nameA} and {nameB}: {both.Count} {Plural(both.Count, "value", "values")}.",
            $"Only in {nameA}: {onlyA.Count}" + (onlyA.Count > 0 ? ": " + Examples(onlyA) + "." : "."),
            $"Only in {nameB}: {onlyB.Count}" + (onlyB.Count > 0 ? ": " + Examples(onlyB) + "." : "."),
            "Blank cells are ignored, upper and lower case count as the same, and each value is counted once.",
        };
        return lines;
    }

    public static IReadOnlyList<string> FindBlanks(string? text)
    {
        var shape = Classify(text);
        if (shape.Kind != SelectionKind.Table && shape.Kind != SelectionKind.List)
            return new[] { "There are no cells to look at: select cells or a list with more than one line." };
        var rows = Rows(text);
        bool header = HasHeader(rows);
        int start = header ? 1 : 0, columns = rows.Max(r => r.Length);
        var lines = new List<string>();
        int total = 0, rowsWith = 0;
        for (int r = start; r < rows.Length; r++)
        {
            var missing = new List<int>();
            for (int c = 0; c < columns; c++) if ((c < rows[r].Length ? rows[r][c] : "").Length == 0) missing.Add(c + 1);
            if (missing.Count == 0) continue;
            total += missing.Count; rowsWith++;
            if (lines.Count < MaxLines) lines.Add($"Row {r + 1} has {missing.Count} blank {Plural(missing.Count, "cell", "cells")} (column {string.Join(", ", missing.Take(8))}{(missing.Count > 8 ? ", …" : "")}).");
        }
        lines.Insert(0, total == 0 ? "No blank cells." : $"{total} blank {Plural(total, "cell", "cells")} in {rowsWith} {Plural(rowsWith, "row", "rows")}.");
        return lines;
    }

    public static IReadOnlyList<string> Totals(string? text)
    {
        var shape = Classify(text);
        if (shape.Kind != SelectionKind.Table && shape.Kind != SelectionKind.List)
            return new[] { "There are no numbers to total: select cells or a list with more than one line." };
        var rows = Rows(text);
        bool header = HasHeader(rows);
        int start = header ? 1 : 0, columns = rows.Max(r => r.Length);
        var lines = new List<string>();
        for (int c = 0; c < columns; c++)
        {
            var values = new List<double>(); int nonBlank = 0;
            for (int r = start; r < rows.Length; r++)
            {
                string v = c < rows[r].Length ? rows[r][c] : "";
                if (v.Length == 0) continue;
                nonBlank++;
                if (IsNumber(v, out double d)) values.Add(d);
            }
            if (values.Count == 0 || values.Count * 5 < nonBlank * 4) continue;       // a numeric column: at least 80% of its filled cells are numbers
            // numbers of five or more digits with no decimals, commas or signs look like identifiers (ANDA numbers, ids): a total of them means nothing
            var raw = new List<string>();
            for (int r = start; r < rows.Length; r++) { string v = c < rows[r].Length ? rows[r][c] : ""; if (IsNumber(v, out _)) raw.Add(v); }
            if (raw.All(v => v.Length >= 5 && v.All(ch => ch is >= '0' and <= '9')))
            {
                lines.Add($"Column {c + 1}{Heading(rows, header, c)}: looks like identifiers ({values.Count} {Plural(values.Count, "number", "numbers")} of 5 or more digits), so it is not totalled.");
                continue;
            }
            double sum = values.Sum();
            lines.Add($"Column {c + 1}{Heading(rows, header, c)}: {values.Count} {Plural(values.Count, "number", "numbers")}, total {Num(sum)}, smallest {Num(values.Min())}, largest {Num(values.Max())}, average {Num(sum / values.Count)}" +
                      (nonBlank > values.Count ? $" ({nonBlank - values.Count} other {Plural(nonBlank - values.Count, "entry was not a number", "entries were not numbers")} and left out)." : "."));
            if (lines.Count >= MaxLines) break;
        }
        if (lines.Count == 0) return new[] { "No column holds mostly numbers, so there is nothing to total." };
        lines.Add("Numbers may have a $ sign, commas or a % sign. Percent signs are removed, not converted.");
        return lines;
    }

    private static string Num(double d)
    {
        string s = Math.Round(d, 4, MidpointRounding.AwayFromZero).ToString("0.####", Inv);
        return s == "-0" ? "0" : s;
    }
}
