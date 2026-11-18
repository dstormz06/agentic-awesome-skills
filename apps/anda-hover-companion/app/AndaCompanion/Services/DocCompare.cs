using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AndaCompanion.Services;

public sealed record DocChange(string Kind, int At, string From, string To);

public sealed record DocResult(int ParasA, int ParasB, int Same, int Changed, int Added, int Removed, IReadOnlyList<DocChange> Changes, bool Skipped);

/// <summary>
/// Two documents compared paragraph by paragraph, by fixed rules on this PC: no AI is used and nothing is sent. Paragraphs that are equal are
/// matched in order (the longest such list); between two matches, a paragraph that shares at least half of its words with its opposite number
/// is shown as changed, and the rest as new or no longer there. A port of the simulation's docCompare(), kept the same rule for rule: the
/// shared cases are in mockup/round27-golden.json.
/// </summary>
public static class DocCompare
{
    public const int MaxParas = 2000, MaxChanges = 60, ShowChars = 240;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly Regex Blank = new(@"\n[ \t]*\n", RegexOptions.CultureInvariant, JsRegex.Limit), WsRun = JsRegex.Make(@"\s+"), LineEnd = new("\r\n?", RegexOptions.CultureInvariant, JsRegex.Limit);

    /// <summary>A document as paragraphs: blocks between blank lines, or lines when there are no blank lines; spacing made single.</summary>
    public static List<string> Paras(string? text)
    {
        string t = LineEnd.Replace(text ?? "", "\n");
        IEnumerable<string> blocks = Blank.Split(t);
        var lines = t.Split('\n').Where(l => JsRegex.Trim(l).Length > 0).ToList();
        if (blocks.Count(b => JsRegex.Trim(b).Length > 0) <= 2 && lines.Count > 3) blocks = lines;
        return blocks.Select(b => JsRegex.Trim(WsRun.Replace(b, " "))).Where(b => b.Length > 0).ToList();
    }

    private static (List<(int A, int B)> Pairs, bool Skipped) Lcs(List<string> a, List<string> b)
    {
        int s = 0; while (s < a.Count && s < b.Count && a[s] == b[s]) s++;
        int ea = a.Count, eb = b.Count; while (ea > s && eb > s && a[ea - 1] == b[eb - 1]) { ea--; eb--; }
        int n = ea - s, m = eb - s; var pairs = new List<(int, int)>(); bool skipped = false;
        for (int i = 0; i < s; i++) pairs.Add((i, i));
        if (n > 0 && m > 0)
        {
            if (n > MaxParas || m > MaxParas) skipped = true;
            else
            {
                int w = m + 1; var L = new int[(n + 1) * w];
                for (int i = n - 1; i >= 0; i--)
                    for (int j = m - 1; j >= 0; j--)
                        L[i * w + j] = a[s + i] == b[s + j] ? L[(i + 1) * w + j + 1] + 1 : Math.Max(L[(i + 1) * w + j], L[i * w + j + 1]);
                int x = 0, y = 0;
                while (x < n && y < m)
                {
                    if (a[s + x] == b[s + y]) { pairs.Add((s + x, s + y)); x++; y++; }
                    else if (L[(x + 1) * w + y] >= L[x * w + y + 1]) x++;
                    else y++;
                }
            }
        }
        for (int k = 0; k < a.Count - ea; k++) pairs.Add((ea + k, eb + k));
        return (pairs, skipped);
    }

    /// <summary>Two paragraphs that share at least half of their words: one is shown as a changed form of the other.</summary>
    internal static bool Alike(string x, string y)
    {
        string[] wa = x.Split(' '), wb = y.Split(' '); var count = new Dictionary<string, int>(StringComparer.Ordinal); int common = 0;
        foreach (string w in wa) count[w] = count.GetValueOrDefault(w) + 1;
        foreach (string w in wb) { int c = count.GetValueOrDefault(w); if (c > 0) { common++; count[w] = c - 1; } }
        return common * 4 >= wa.Length + wb.Length;
    }

    /// <summary>Both forms of a changed paragraph, starting a little before the first difference.</summary>
    internal static (string From, string To) Around(string x, string y)
    {
        int d = 0, lim = Math.Min(x.Length, y.Length); while (d < lim && x[d] == y[d]) d++;
        int start = d > 60 ? d - 60 : 0;
        if (start > 0)
        {
            int sp = x.IndexOf(' ', start);
            if (sp >= 0 && sp < d) start = sp + 1;
            else if (char.IsLowSurrogate(x[start])) start++;
        }
        string Part(string t) => (start > 0 ? "…" : "") + FileCompare.Show(t[start..], ShowChars);
        return (Part(x), Part(y));
    }

    public static DocResult Compare(string? textA, string? textB)
    {
        var a = Paras(textA); var b = Paras(textB); var (pairs, skipped) = Lcs(a, b);
        int changed = 0, added = 0, removed = 0; var changes = new List<DocChange>();
        void Push(DocChange c) { if (changes.Count < MaxChanges) changes.Add(c); }
        void Gap(int ia, int ja, int ib, int jb)      // paragraphs a[ia..ja) and b[ib..jb) lie between two matches
        {
            int k = Math.Min(ja - ia, jb - ib);
            for (int x = 0; x < k; x++)
            {
                string p = a[ia + x], q = b[ib + x];
                if (Alike(p, q)) { changed++; var (from, to) = Around(p, q); Push(new DocChange("changed", ib + x + 1, from, to)); }
                else
                {
                    removed++; Push(new DocChange("removed", ia + x + 1, FileCompare.Show(p, ShowChars), ""));
                    added++; Push(new DocChange("added", ib + x + 1, "", FileCompare.Show(q, ShowChars)));
                }
            }
            for (int x = ia + k; x < ja; x++) { removed++; Push(new DocChange("removed", x + 1, FileCompare.Show(a[x], ShowChars), "")); }
            for (int x = ib + k; x < jb; x++) { added++; Push(new DocChange("added", x + 1, "", FileCompare.Show(b[x], ShowChars))); }
        }
        int pi = 0, pj = 0;
        foreach (var (i, j) in pairs) { Gap(pi, i, pj, j); pi = i + 1; pj = j + 1; }
        Gap(pi, a.Count, pj, b.Count);
        return new DocResult(a.Count, b.Count, pairs.Count, changed, added, removed, changes, skipped);
    }

    private static string N(int x) => x.ToString("#,0", Inv);
    internal static string Pl(int n, string one, string many) => n == 1 ? one : many;
    private static string ParasText(int x) => $"{N(x)} {Pl(x, "paragraph", "paragraphs")}";

    public static IReadOnlyList<string> Summary(DocResult r, string nameA, string nameB)
    {
        var lines = new List<string> { $"{nameA} has {ParasText(r.ParasA)}; {nameB} has {ParasText(r.ParasB)}." };
        if (r.ParasA == 0 && r.ParasB == 0) lines.Add("Neither file has any text.");
        else if (r.Changed == 0 && r.Added == 0 && r.Removed == 0) lines.Add("The two files have the same text, paragraph for paragraph.");
        else lines.Add($"{ParasText(r.Same)} {Pl(r.Same, "is", "are")} the same, {N(r.Changed)} changed, {N(r.Added)} {Pl(r.Added, "is", "are")} new in {nameB}, and {N(r.Removed)} {Pl(r.Removed, "is", "are")} no longer there.");
        if (r.Skipped) lines.Add("The files are very long. Paragraphs in the middle were compared in order, not matched, so a moved paragraph there reads as changed.");
        lines.Add("Compared as plain text, paragraph by paragraph. Formatting, tracked changes, comments, headers, footnotes and pictures were not compared.");
        return lines;
    }

    public static IReadOnlyList<string> Report(DocResult r, string nameA, string nameB)
    {
        var lines = Summary(r, nameA, nameB).ToList();
        List<DocChange> Of(string kind) => r.Changes.Where(c => c.Kind == kind).ToList();
        if (Of("changed").Count > 0) { lines.Add(""); lines.Add("CHANGED"); foreach (var c in Of("changed")) lines.Add($"Paragraph {c.At.ToString(Inv)}: {c.From} → {c.To}"); }
        if (Of("added").Count > 0) { lines.Add(""); lines.Add("NEW IN " + nameB.ToUpperInvariant()); foreach (var c in Of("added")) lines.Add($"Paragraph {c.At.ToString(Inv)}: {c.To}"); }
        if (Of("removed").Count > 0) { lines.Add(""); lines.Add("NO LONGER IN " + nameB.ToUpperInvariant()); foreach (var c in Of("removed")) lines.Add($"Paragraph {c.At.ToString(Inv)} of {nameA}: {c.From}"); }
        int total = r.Changed + r.Added + r.Removed;
        if (total > r.Changes.Count) { lines.Add(""); lines.Add($"…and {N(total - r.Changes.Count)} more {Pl(total - r.Changes.Count, "change", "changes")}."); }
        return lines;
    }

    // Two fictional versions of one letter, for trying the document comparison with no file at hand. The same as the simulation's.
    public const string SampleNameA = "Letter, first draft", SampleNameB = "Letter, second draft";
    public const string SampleA = "Dear Dr. Lee,\n\nWe received your amendment to ANDA 216482 on Nov 3, 2026.\n\nThe goal date is Oct 3, 2026 and the review is on track.\n\nPlease respond by Dec 3, 2026.\n\nSincerely,\nThe project team (fictional)";
    public const string SampleB = "Dear Dr. Lee,\n\nWe received your amendment to ANDA 216482 on Nov 3, 2026.\n\nThe goal date is Oct 17, 2026 and the review is on track.\n\nOne labeling comment is still open.\n\nSincerely,\nThe project team (fictional)";
}

/// <summary>
/// What one of the person's stored prompts is given when it is run on a comparison: the differences the fixed rules found and, only if the
/// person asks for it, the beginning of each file. Everything is cleaned of control characters, and the whole block goes inside the same fence
/// as any selection, so text in a file cannot pose as an instruction. The preview shows every character and the size before anything is sent.
/// </summary>
public static class CompareMaterial
{
    public const int PartMax = 6000;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly Regex LineEnd = new("\r\n?", RegexOptions.CultureInvariant, JsRegex.Limit);

    /// <summary>Never through the middle of one character.</summary>
    internal static string CutAt(string t, int n)
    {
        if (t.Length <= n) return t;
        int cut = n; if (char.IsHighSurrogate(t[cut - 1])) cut--;
        return t[..cut];
    }

    /// <summary>What may go into a prompt: line breaks made plain, control characters removed.</summary>
    public static string Clean(string? text)
    {
        string t = LineEnd.Replace(text ?? "", "\n"); var sb = new StringBuilder(t.Length);
        foreach (char c in t) { if ((c < 32 && c != 10 && c != 9) || (c >= 127 && c <= 159)) continue; sb.Append(c); }
        return JsRegex.Trim(sb.ToString());
    }

    public static string TableText(CompareTable t) =>
        string.Join("\n", new[] { string.Join(" | ", t.Headers.Select(FileCompare.Cell)) }.Concat(t.Rows.Select(r => string.Join(" | ", r.Select(FileCompare.Cell)))));

    /// <param name="nameB">Null when one file was checked against the person's data: then only that file can be added.</param>
    public static string For(IEnumerable<string> reportLines, string nameA, string textA, string? nameB, string textB, bool whole)
    {
        static string Part(string title, string text)
        {
            string t = Clean(text);
            return t.Length > PartMax ? $"{title} (the first {PartMax.ToString("#,0", Inv)} of {t.Length.ToString("#,0", Inv)} characters)\n{CutAt(t, PartMax)}" : $"{title}\n{t}";
        }
        var parts = new List<string> { Part("DIFFERENCES, FOUND BY FIXED RULES", string.Join("\n", reportLines)) };
        if (whole && nameB is null) parts.Add(Part($"THE FILE: {Clean(nameA)}", textA));
        else if (whole) { parts.Add(Part($"EARLIER FILE: {Clean(nameA)}", textA)); parts.Add(Part($"LATER FILE: {Clean(nameB)}", textB)); }
        return string.Join("\n\n", parts);
    }

    /// <summary>The stored prompt with the material in the place of {selection}, or after it when the prompt has no such place. Always fenced.</summary>
    public static string Prompt(string template, string material, DateOnly today)
    {
        string fenced = ActionFill.Fence(material);
        return template.Contains("{selection}", StringComparison.Ordinal)
            ? QuickReplyFill.FillWithoutRecord(template, today, new Dictionary<string, string> { ["selection"] = fenced })
            : QuickReplyFill.FillWithoutRecord(template, today) + "\n\n" + fenced;
    }

    /// <summary>A long report cut at a line end, for the built-in narrative.</summary>
    public static string CutText(string text, int limit)
    {
        if (text.Length <= limit) return text;
        string t = text[..limit]; int cut = t.LastIndexOf('\n');
        return (cut > 200 ? t[..cut] : t) + "\n(the list is cut here)";
    }
}

public sealed record AnswerResult(int Checked, IReadOnlyList<string> Missing, IReadOnlyList<string> Lines);

/// <summary>
/// Checks an AI answer against a comparison, by fixed rules: every number, date and name with a digit in the answer must also stand in the
/// comparison or in the two files; what does not is listed for the person to check. It cannot judge wording or reasoning, and says so.
/// </summary>
public static class AnswerCheck
{
    public const int MaxAnswer = 200_000;      // the longest answer the check reads
    public const string Limit = "This checks only that each such value in the answer also stands in the comparison or in the two files. It cannot tell whether the wording or the reasoning is right.";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly Regex Token = new(@"[A-Za-z0-9$][A-Za-z0-9.,%$-]*", RegexOptions.CultureInvariant, JsRegex.Limit),
        Number = new(@"^\$?[0-9][0-9,]*(?:\.[0-9]+)?%?\z", RegexOptions.CultureInvariant, JsRegex.Limit),
        OneOrTwoDigits = new(@"^[0-9]{1,2}\z", RegexOptions.CultureInvariant, JsRegex.Limit);

    private sealed record DateFact(string Text, int Mon, int Day, int? Year);
    private sealed record Facts(List<DateFact> Dates, List<(string Text, double Value)> Numbers, List<(string Text, string Key)> Names);

    private static Facts Read(string? text)
    {
        string t = text ?? ""; var dates = new List<DateFact>(); var spans = new List<(int, int)>(); var numbers = new List<(string, double)>(); var names = new List<(string, string)>();
        foreach (Match m in DraftText.Date.Matches(t))
        {
            if (m.Index > 0 && (t[m.Index - 1] is (>= '0' and <= '9') or '/')) continue;
            if (DraftText.ToMention(m) is not { } mt || mt.Mon < 1 || mt.Mon > 12 || mt.Day < 1 || mt.Day > 31) continue;
            dates.Add(new DateFact(m.Value, mt.Mon, mt.Day, mt.Year)); spans.Add((m.Index, m.Index + m.Length));
        }
        int si = 0;      // dates and words both come in reading order, so one pointer finds a word inside a date: the time taken grows with the length of the text and no faster
        foreach (Match m in Token.Matches(t))
        {
            int start = m.Index; string w = m.Value.TrimEnd('.', ',', '-'); if (!w.Any(c => c is >= '0' and <= '9')) continue;
            int end = start + w.Length; while (si < spans.Count && spans[si].Item2 <= start) si++;
            if (si < spans.Count && end > spans[si].Item1) continue;
            if (Number.IsMatch(w))
            {
                if (!double.TryParse(w.Replace("$", "").Replace(",", "").Replace("%", ""), NumberStyles.Float, Inv, out double value) || !double.IsFinite(value)) continue;
                if (OneOrTwoDigits.IsMatch(w) && end < t.Length && t[end] is '.' or ')')
                {
                    int b = start; while (b > 0 && (t[b - 1] == ' ' || t[b - 1] == '\t')) b--;
                    if (b == 0 || t[b - 1] == '\n') continue;      // "1." at the start of a line numbers a list
                }
                numbers.Add((w, value));
            }
            else names.Add((w, w.ToLowerInvariant()));
        }
        return new Facts(dates, numbers, names);
    }

    public static AnswerResult Check(string? answer, string? factsText)
    {
        if (JsRegex.Trim(answer ?? "").Length == 0) return new AnswerResult(0, Array.Empty<string>(), new[] { "Please paste the answer first." });
        if (answer!.Length > MaxAnswer) return new AnswerResult(0, Array.Empty<string>(), new[] { "The answer is longer than 200,000 characters, which is more than this check reads." });
        Facts a, f;
        try { a = Read(answer); f = Read(factsText); }
        catch (RegexMatchTimeoutException) { return new AnswerResult(0, Array.Empty<string>(), new[] { "The answer could not be checked in the time allowed. Nothing was sent." }); }
        var missing = new List<string>(); var listed = new HashSet<string>(StringComparer.Ordinal);
        void Add(string x) { if (listed.Add(x)) missing.Add(x); }
        var nums = f.Numbers.Select(x => x.Value).ToHashSet(); var names = f.Names.Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        // A date stands in the facts when the month and day agree and the years agree or one side names no year. Three sets answer that without reading the facts once per date.
        var anyYear = new HashSet<long>(); var noYear = new HashSet<long>(); var withYear = new HashSet<long>();
        foreach (var d in f.Dates) { long k = d.Mon * 100 + d.Day; anyYear.Add(k); if (d.Year is { } y) withYear.Add(y * 10000L + k); else noYear.Add(k); }
        foreach (var x in a.Numbers) if (!nums.Contains(x.Value)) Add(x.Text);
        foreach (var x in a.Dates) { long k = x.Mon * 100 + x.Day; if (!(x.Year is { } y ? noYear.Contains(k) || withYear.Contains(y * 10000L + k) : anyYear.Contains(k))) Add(x.Text); }
        foreach (var x in a.Names) if (!names.Contains(x.Key)) Add(x.Text);
        int total = a.Numbers.Count + a.Dates.Count + a.Names.Count;
        if (total == 0) return new AnswerResult(0, missing, new[] { "The answer holds no number, date or name with a digit to check.", Limit });
        if (missing.Count == 0) return new AnswerResult(total, missing, new[] { $"All {total.ToString("#,0", Inv)} numbers, dates and names with digits in the answer also stand in the comparison or in the two files.", Limit });
        var shown = missing.Take(12).Select(x => FileCompare.Show(x, 40));
        string more = missing.Count > 12 ? $"; and {(missing.Count - 12).ToString(Inv)} more" : "";
        return new AnswerResult(total, missing, new[]
        {
            $"{missing.Count.ToString("#,0", Inv)} of the values in the answer {DocCompare.Pl(missing.Count, "does", "do")} not stand in the comparison or in the two files. Please check {DocCompare.Pl(missing.Count, "it", "them")}: {string.Join("; ", shown)}{more}.",
            Limit,
        });
    }
}

public sealed record ReportCheckResult(int Named, int Known, int Agree, int Differ, IReadOnlyList<string> Unknown, int Unread, int Repeated, IReadOnlyList<string> Diffs, IReadOnlyList<string> Lines);

/// <summary>
/// A report checked against the person's data, by fixed rules: for the ANDAs the report names, only the goal date and the forecast date are
/// compared. The person's data is not the system of record, and every result says so.
/// </summary>
public static class ReportCheck
{
    public const string Close = "Only the goal date and the forecast date were checked. Your data is not the system of record.";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly Regex
        Anda = JsRegex.Make(@"^(?:anda|anda ?(?:number|no\.?|#)|application ?(?:number|no\.?|#)?|appl\.? ?(?:number|no\.?|#)?)$", true),
        Goal = JsRegex.Make(@"^(?:(?:gdufa )?goal(?: date)?|goal date \(gdufa\))$", true),
        Forecast = JsRegex.Make(@"^(?:forecast(?:ed)?(?: date)?|projected(?: action)? date)$", true),
        SixDigits = new(@"(?:^|[^0-9])([0-9]{6})(?![0-9])", RegexOptions.CultureInvariant, JsRegex.Limit);

    /// <param name="lookup">For an ANDA number: the goal and forecast dates of the person's data as text ("" when the data has none), or null when the ANDA is not in the data.</param>
    public static ReportCheckResult Check(CompareTable report, Func<string, (string Goal, string Forecast)?> lookup)
    {
        var heads = FileCompare.Headers(report.Headers); int Col(Regex re) => heads.FindIndex(re.IsMatch);
        int ia = Col(Anda), ig = Col(Goal), ifc = Col(Forecast); string name = report.Name;
        static string N(int x) => x.ToString("#,0", Inv);
        if (ia < 0) return new ReportCheckResult(0, 0, 0, 0, Array.Empty<string>(), 0, 0, Array.Empty<string>(), new[] { $"{name} has no ANDA column, so it cannot be checked against your data.", Close });
        if (ig < 0 && ifc < 0) return new ReportCheckResult(0, 0, 0, 0, Array.Empty<string>(), 0, 0, Array.Empty<string>(), new[] { $"{name} has no goal date or forecast date column, so there is nothing to check against your data.", Close });
        int named = 0, known = 0, agree = 0, differ = 0, unread = 0, repeated = 0; var unknown = new List<string>(); var diffs = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        static string At(IReadOnlyList<string> row, int i) => i >= 0 && i < row.Count ? FileCompare.Cell(row[i]) : "";
        foreach (var row in report.Rows)
        {
            var m = SixDigits.Match(At(row, ia));
            if (!m.Success) { if (row.Any(x => FileCompare.Cell(x).Length > 0)) unread++; continue; }
            string id = m.Groups[1].Value; if (!seen.Add(id)) { repeated++; continue; }
            named++;
            if (lookup(id) is not { } mine) { unknown.Add(id); continue; }
            known++;
            int bad = 0;
            foreach (var (label, i, own) in new[] { ("Goal date", ig, mine.Goal), ("Forecast date", ifc, mine.Forecast) })
            {
                string v = At(row, i); if (i < 0 || v.Length == 0 || string.IsNullOrEmpty(own)) continue;
                int? a = FileCompare.Day(v), b = FileCompare.Day(own);
                if (a is null) { bad++; diffs.Add($"ANDA {id}: {label}: the report says “{FileCompare.Show(v, 40)}”, which this tool does not read as a date; your data says {own}."); }
                else if (a != b) { bad++; int k = Math.Abs((b ?? 0) - a.Value); diffs.Add($"ANDA {id}: {label}: the report says {v}; your data says {own} ({k.ToString(Inv)} {DocCompare.Pl(k, "day", "days")} {(b > a ? "later" : "earlier")})."); }
            }
            if (bad > 0) differ++; else agree++;
        }
        string cols = ig >= 0 && ifc >= 0 ? "the goal date and the forecast date" : ig >= 0 ? "the goal date" : "the forecast date";
        var lines = new List<string> { $"{name} names {N(named)} {DocCompare.Pl(named, "ANDA", "ANDAs")}; {N(known)} {DocCompare.Pl(known, "is", "are")} in your data." };
        if (known > 0) lines.Add($"{N(agree)} {DocCompare.Pl(agree, "agrees", "agree")} with your data on {cols}, and {N(differ)} {DocCompare.Pl(differ, "differs", "differ")}.");
        lines.AddRange(diffs.Take(40));
        if (diffs.Count > 40) lines.Add($"…and {N(diffs.Count - 40)} more {DocCompare.Pl(diffs.Count - 40, "difference", "differences")}.");
        if (unknown.Count > 0) lines.Add($"Not in your data: {string.Join(", ", unknown.Take(20))}{(unknown.Count > 20 ? $", and {N(unknown.Count - 20)} more" : "")}.");
        if (unread > 0) lines.Add($"{N(unread)} {DocCompare.Pl(unread, "row", "rows")} had no six-digit number in the ANDA column and {DocCompare.Pl(unread, "was", "were")} left out.");
        if (repeated > 0) lines.Add($"{N(repeated)} {DocCompare.Pl(repeated, "row repeats", "rows repeat")} an ANDA already named; the first row of each was used.");
        lines.Add(Close);
        return new ReportCheckResult(named, known, agree, differ, unknown, unread, repeated, diffs, lines);
    }
}
