using System.Diagnostics;
using AndaCompanion.Services;
using Xunit;
using Xunit.Abstractions;

namespace AndaCompanion.Tests;

/// <summary>
/// Round 26, the security review: what a file or a selection contains is only ever text. These tests hold the fixes the review asked for:
/// a pasted report cannot become a spreadsheet formula, a selection cannot add a line of its own to a calendar file, a very long value is
/// shown cut, the saved report can load and run nothing, and the largest file the reader accepts is still compared in good time.
/// </summary>
public class Round26SecurityTests
{
    private readonly ITestOutputHelper _out;
    public Round26SecurityTests(ITestOutputHelper output) => _out = output;

    private static CompareTable T(string name, string[] headers, params string[][] rows) => new(name, headers, rows);

    [Theory]
    [InlineData("=HYPERLINK(\"http://example.invalid\",\"open\")")]
    [InlineData("+1+1")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1:A2)")]
    [InlineData("＝1+1")]
    public void A_copied_report_never_starts_a_line_with_a_formula(string key)
    {
        var a = T("Earlier", new[] { "Id", "Value" }, new[] { key, "x" }, new[] { "gone" + key, "1" });
        var b = T("Later", new[] { "Id", "Value" }, new[] { key, "y" }, new[] { key + "new", "2" }, new[] { key + " also new", "3" });
        var r = FileCompare.Compare(a, b);
        var raw = FileCompare.Report(r, key + ".csv", "Later");
        Assert.Contains(raw, l => l.StartsWith(key, StringComparison.Ordinal));                    // the report itself is not changed for reading
        var paste = FileCompare.ForPaste(raw);
        Assert.Equal(raw.Count, paste.Count);
        Assert.DoesNotContain(paste, l => l.Length > 0 && "=+-@\t\r＝＋－＠".Contains(l[0]));
        Assert.All(paste.Zip(raw), p => Assert.True(p.First == p.Second || p.First == "'" + p.Second));
        Assert.Contains(paste, l => l.StartsWith("'" + key, StringComparison.Ordinal));
    }

    [Fact]
    public void An_ordinary_report_is_copied_unchanged()
    {
        var r = FileCompare.Compare(FileCompare.SampleA, FileCompare.SampleB);
        var raw = FileCompare.Report(r, FileCompare.SampleA.Name, FileCompare.SampleB.Name);
        Assert.Equal(raw, FileCompare.ForPaste(raw));
    }

    [Fact]
    public void A_selection_cannot_add_a_line_of_its_own_to_a_calendar_file()
    {
        string nl = "\r\n", ls = ((char)0x2028).ToString(), ps = ((char)0x2029).ToString(), nel = ((char)0x85).ToString();
        string text = "Review on Dec 1, 2026" + nl + "ATTENDEE:mailto:someone@example.invalid" + ls + "BEGIN:VALARM" + ps + "ACTION:PROCEDURE" + nel + "END:VEVENT\rORGANIZER:x\nX-BAD;PARAM=1:2"
            + (char)0 + (char)7 + (char)0x1B + "[31m" + (char)127 + "\u000B" + "\u000C";
        var made = Invitations.For(text, new DateOnly(2026, 11, 18), "20261118T120000Z", "t@notula");
        Assert.NotNull(made);
        var lines = made!.Ics.Split("\r\n");
        // Unfold (a line that starts with a space continues the one before), then every line must be one the app wrote itself.
        var logical = new List<string>();
        foreach (string l in lines) { if (l.StartsWith(' ') && logical.Count > 0) logical[^1] += l[1..]; else logical.Add(l); }
        string[] allowed = { "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:", "CALSCALE:GREGORIAN", "METHOD:PUBLISH", "BEGIN:VEVENT", "UID:t@notula", "DTSTAMP:20261118T120000Z", "DTSTART;VALUE=DATE:20261201", "DTEND;VALUE=DATE:20261202", "SUMMARY:", "DESCRIPTION:", "TRANSP:TRANSPARENT", "END:VEVENT", "END:VCALENDAR", "" };
        Assert.All(logical, l => Assert.Contains(allowed, a => a.Length == 0 ? l.Length == 0 : l.StartsWith(a, StringComparison.Ordinal)));
        Assert.Single(logical, l => l == "BEGIN:VEVENT"); Assert.Single(logical, l => l == "END:VEVENT");
        Assert.DoesNotContain(logical, l => l.StartsWith("ATTENDEE", StringComparison.Ordinal) || l.StartsWith("ORGANIZER", StringComparison.Ordinal) || l.StartsWith("BEGIN:VALARM", StringComparison.Ordinal) || l.StartsWith("X-BAD", StringComparison.Ordinal));
        // No raw line break other than the file's own CRLF, and no control character at all.
        Assert.DoesNotContain(made.Ics.Replace("\r\n", ""), c => c < 32 && c != 9 || c == 127 || c == 0x85 || c == 0x2028 || c == 0x2029);
        Assert.All(lines, l => Assert.True(l.Length <= 61));
        string description = logical.Single(l => l.StartsWith("DESCRIPTION:", StringComparison.Ordinal));
        Assert.Contains("\\nATTENDEE:mailto:someone@example.invalid\\nBEGIN:VALARM\\nACTION:PROCEDURE\\nEND:VEVENT\\nORGANIZER:x\\nX-BAD\\;PARAM=1:2[31m", description);
    }

    [Fact]
    public void A_very_long_value_is_shown_cut_but_still_compared_whole()
    {
        string longA = new string('a', 5000) + "1", longB = new string('a', 5000) + "2", longKey = "K" + new string('k', 900), head = "H" + new string('h', 300);
        var a = T("A", new[] { "Id", head }, new[] { "1", longA }, new[] { longKey, "same" }, new[] { longKey + "-gone", "x" });
        var b = T("B", new[] { "Id", head }, new[] { "1", longB }, new[] { longKey, "changed" }, new[] { longKey + "-new", "x" });
        var r = FileCompare.Compare(a, b);
        Assert.Equal(2, r.ChangedRows);                                         // the difference is past the cut, and is still found
        Assert.Equal(1, r.AddedCount); Assert.Equal(1, r.RemovedCount);         // long keys that differ only past the cut are still different rows
        Assert.All(r.Changes.SelectMany(c => c.Cells), c => { Assert.True(c.From.Length <= FileCompare.MaxValue); Assert.True(c.To.Length <= FileCompare.MaxValue); Assert.True(c.Column.Length <= FileCompare.MaxHead + 4); });
        Assert.All(r.Changes, c => Assert.True(c.Row.Length <= FileCompare.MaxValue));
        Assert.All(r.Added.Concat(r.Removed), k => Assert.True(k.Length <= FileCompare.MaxValue && k.EndsWith('…')));
        Assert.All(FileCompare.Report(r, "A", "B"), l => Assert.True(l.Length < 1200, "a report line is too long: " + l.Length));
        Assert.Equal("ab" + "…", FileCompare.Show("ab😀cd", 4));                 // never cut through the middle of one character
        Assert.Equal("ab😀" + "…", FileCompare.Show("ab😀cd", 5));
    }

    [Fact]
    public void The_saved_report_can_load_and_run_nothing()
    {
        var a = T("<img src=x onerror=alert(1)>", new[] { "Id", "V" }, new[] { "<script>alert(1)</script>", "\"><svg onload=alert(1)>" });
        var b = T("B&B", new[] { "Id", "V" }, new[] { "<script>alert(1)</script>", "javascript:alert(1)" });
        string html = FileCompare.HtmlReport(FileCompare.Compare(a, b), a.Name, b.Name);
        Assert.Contains("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'\">", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<svg", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" href=", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
    }

    [Fact]
    public void The_largest_table_the_reader_accepts_is_compared_in_good_time()
    {
        const int rows = 20_000, cols = 30;
        var headers = Enumerable.Range(0, cols).Select(i => i == 0 ? "Id" : "C" + i).ToArray();
        IReadOnlyList<IReadOnlyList<string>> Make(int shift) => Enumerable.Range(0, rows).Select(r => (IReadOnlyList<string>)Enumerable.Range(0, cols).Select(c => c == 0 ? "K" + r : c % 3 == 0 ? "10/" + (1 + (r + shift) % 28) + "/2026" : c % 3 == 1 ? ((r + shift) * 7).ToString() : "text " + (r + shift) + "   with  spaces").ToArray()).ToList();
        var a = new CompareTable("A", headers, Make(0)); var b = new CompareTable("B", headers, Make(1));
        var watch = Stopwatch.StartNew();
        var r = FileCompare.Compare(a, b);
        var report = FileCompare.Report(r, "A", "B");
        watch.Stop();
        _out.WriteLine($"{rows} x {cols}: {watch.ElapsedMilliseconds} ms, {r.ChangedCells} changed cells, report {report.Count} lines");
        Assert.Equal(rows, r.ChangedRows);
        Assert.True(r.Changes.Count <= FileCompare.MaxChanges);
        Assert.True(report.Count < 1000);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"took {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void The_usage_log_reader_takes_only_dates_and_known_event_names()
    {
        string path = Path.Combine(Path.GetTempPath(), "usage-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            File.WriteAllLines(path, new[]
            {
                "2026-11-18,card_open,3", "2026-11-18,ANDA 216482,1", "2026-11-18,=cmd|' /C calc'!A0,1", "not a date,card_open,1", "2026-11-18", "", "2026-11-18,compare_files,0",
                "2026-11-18,card_open,2,extra,fields", "2026-11-18,<script>,1",
            });
            var days = UsageSummary.Read(path);
            Assert.Single(days);
            Assert.Equal(new[] { "card_open", "compare_files" }, days["2026-11-18"].Keys.OrderBy(k => k));
            var lines = UsageSummary.Lines(days, new DateOnly(2026, 11, 18));
            Assert.DoesNotContain(lines, l => l.Contains("216482") || l.Contains("cmd") || l.Contains("script"));
            Assert.Contains("Cards opened: 2 (2)", lines);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Native_calls_go_only_to_three_Windows_libraries_loaded_from_the_system_folder()
    {
        var app = typeof(FileCompare).Assembly;
        var paths = app.GetCustomAttributes(typeof(System.Runtime.InteropServices.DefaultDllImportSearchPathsAttribute), false).Cast<System.Runtime.InteropServices.DefaultDllImportSearchPathsAttribute>().Single();
        Assert.Equal(System.Runtime.InteropServices.DllImportSearchPath.System32, paths.Paths);
        const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
        var libraries = app.GetTypes().SelectMany(t => t.GetMethods(all))
            .Select(m => m.GetCustomAttributes(typeof(System.Runtime.InteropServices.DllImportAttribute), false).Cast<System.Runtime.InteropServices.DllImportAttribute>().FirstOrDefault()?.Value)
            .Where(v => v is not null).Select(v => v!.ToLowerInvariant()).Distinct().OrderBy(v => v).ToArray();
        Assert.Equal(new[] { "gdi32.dll", "kernel32.dll", "user32.dll" }, libraries);
    }
}
