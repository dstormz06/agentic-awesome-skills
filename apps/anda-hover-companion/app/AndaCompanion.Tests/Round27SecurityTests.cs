using System.Diagnostics;
using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AndaCompanion.Models;
using AndaCompanion.Services;
using AndaCompanion.Theming;
using AndaCompanion.UI;
using Xunit;
using Xunit.Abstractions;

namespace AndaCompanion.Tests;

/// <summary>
/// Round 27, the security review of the new work. These tests hold what the review asked for: a backup from another PC cannot switch the
/// weekly copy of the sheet on; a document is read up to a fixed size and no further; giving a prompt the files themselves is a choice made
/// anew for every pair of files; and the check of an AI answer takes time in proportion to the text, however large the files are.
/// </summary>
[Collection("Global state")]
public class Round27SecurityTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx; private readonly ITestOutputHelper _out;
    public Round27SecurityTests(HeadlessFixture fx, ITestOutputHelper output) { _fx = fx; _out = output; }
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static string[] Texts(Control c) => c.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();

    [Fact]
    public void A_backup_from_another_PC_cannot_switch_the_weekly_copy_of_the_sheet_on_or_off()
    {
        string dir = Temp.NewDir();
        var lenses = new PromptLibraryService(Path.Combine(dir, "p.json")); var replies = new QuickReplyService(Path.Combine(dir, "r.json"));
        string json = SettingsBackup.Export(new Settings { KeepWeeklySheetCopy = true }, lenses, replies);
        Assert.DoesNotContain("\"KeepWeeklySheetCopy\": true", json, StringComparison.OrdinalIgnoreCase);      // the choice does not travel
        var off = new Settings();
        var (ok, message) = SettingsBackup.Import(json, off, lenses, replies);
        Assert.True(ok, message); Assert.False(off.KeepWeeklySheetCopy);

        // A backup file edited by hand to say "true" changes nothing either, and neither a restore nor a reset switches a copy that is on, off.
        string edited = System.Text.RegularExpressions.Regex.Replace(json, "\"KeepWeeklySheetCopy\"\\s*:\\s*false", "\"KeepWeeklySheetCopy\": true", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        Assert.NotEqual(json, edited);
        var still = new Settings();
        Assert.True(SettingsBackup.Import(edited, still, lenses, replies).Ok); Assert.False(still.KeepWeeklySheetCopy);
        var on = new Settings { KeepWeeklySheetCopy = true };
        Assert.True(SettingsBackup.Import(json, on, lenses, replies).Ok); Assert.True(on.KeepWeeklySheetCopy);
        SettingsBackup.Reset(on, lenses, replies); Assert.True(on.KeepWeeklySheetCopy);
    }

    [Fact]
    public void A_document_is_read_up_to_a_fixed_size_and_a_larger_one_is_refused_in_plain_words()
    {
        string dir = Temp.NewDir(), txt = Path.Combine(dir, "long.txt"), ok = Path.Combine(dir, "fits.txt"), docx = Path.Combine(dir, "long.docx"), bomb = Path.Combine(dir, "bomb.docx");
        File.WriteAllText(txt, new string('a', DocumentReader.MaxChars + 1));                      // under the 5 MB limit for the file, over the limit for its text
        File.WriteAllText(ok, new string('a', 1000) + "\n\n" + new string('b', 1000));
        const string head = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>", tail = "</w:body></w:document>";
        void Docx(string path, int paragraphs, int each)
        {
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            using var w = new StreamWriter(zip.CreateEntry("word/document.xml", CompressionLevel.Fastest).Open());
            w.Write(head); string run = new string('x', each);
            for (int i = 0; i < paragraphs; i++) { w.Write("<w:p><w:r><w:t>"); w.Write(run); w.Write("</w:t></w:r></w:p>"); }
            w.Write(tail);
        }
        Docx(docx, 60, 100_000);                 // 6,000,000 characters of text in a file of a few kilobytes
        Docx(bomb, 55, 1_000_000);               // 55 MB of XML: under the limit for the part, over the limit for its text

        var a = DocumentReader.Read(txt); Assert.Null(a.Document);
        Assert.Equal("long.txt holds more than 5,000,000 characters of text, which is more than this tool compares.", a.Problem);
        var b = DocumentReader.Read(ok); Assert.Null(b.Problem); Assert.Equal(2002, b.Document!.Text.Length);
        var c = DocumentReader.Read(docx); Assert.Null(c.Document);
        Assert.Equal("long.docx holds more than 5,000,000 characters of text, which is more than this tool compares.", c.Problem);
        var sw = Stopwatch.StartNew(); long before = GC.GetTotalMemory(true);
        var d = DocumentReader.Read(bomb); sw.Stop();
        Assert.Null(d.Document); Assert.NotNull(d.Problem);
        _out.WriteLine($"a 55 MB document part was refused in {sw.ElapsedMilliseconds} ms: {d.Problem}");
        Assert.True(sw.ElapsedMilliseconds < 20_000, "refusing a very large document took too long");
        Assert.True(GC.GetTotalMemory(true) - before < 200L * 1024 * 1024, "memory was kept after a refused document");
    }

    [Fact]
    public async Task Giving_a_prompt_the_files_themselves_is_chosen_anew_for_every_pair_of_files()
    {
        string dir = Temp.NewDir(), a = Path.Combine(dir, "a.txt"), b = Path.Combine(dir, "b.txt");
        File.WriteAllText(a, "One.\n\nTwo."); File.WriteAllText(b, "One.\n\nThree.");
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var w = new CompareWindow(); w.Show(); Settle();
            var box = w.FindControl<CheckBox>("WholeBox")!; var got = new List<bool>();
            void Tick() { box.IsChecked = true; Settle(); }
            Click(w.FindControl<Button>("SampleDocsBtn")!); Settle(); got.Add(box.IsChecked == true);       // 0: off to begin with
            Tick(); Click(w.FindControl<Button>("SampleBtn")!); Settle(); got.Add(box.IsChecked == true);    // 1: other sample files
            Tick(); w.LoadFile(a, true); Settle(); got.Add(box.IsChecked == true);                            // 2: a file chosen from disk
            Tick(); w.LoadFile(b, false); Settle(); got.Add(box.IsChecked == true);                           // 3: the second file
            Tick(); w.Load(FileCompare.SampleA, FileCompare.SampleB); Settle(); got.Add(box.IsChecked == true);                          // 4: tables handed over
            Tick(); w.LoadDocuments(new ReadDocument("x", "p", Array.Empty<string>()), new ReadDocument("y", "q", Array.Empty<string>())); Settle(); got.Add(box.IsChecked == true);      // 5: documents handed over
            Tick(); w.SetMode("data"); Settle(); got.Add(box.IsChecked == true);                              // 6: another kind of comparison
            Tick(); w.SetMode("two"); Settle(); got.Add(box.IsChecked == true);                               // 7
            Tick(); bool staysWhileWorking = box.IsChecked == true;                                           // ticking it does stick until the files change
            w.Close();
            return (got, staysWhileWorking);
        }, CancellationToken.None);
        Assert.Equal(new[] { false, false, false, false, false, false, false, false }, r.got);
        Assert.True(r.staysWhileWorking);
    }

    [Fact]
    public void The_check_of_an_answer_takes_time_in_proportion_to_the_text_and_reads_an_answer_up_to_a_fixed_length()
    {
        // The worst text for the check: nothing but dates and numbers. One million characters of facts, and an answer at the limit.
        var facts = new System.Text.StringBuilder(); int n = 0;
        while (facts.Length < 2_000_000) { facts.Append("Oct ").Append(1 + n % 28).Append(", ").Append(1990 + n % 60).Append(" value ").Append(n).Append(" K").Append(n).Append('\n'); n++; }
        var answer = new System.Text.StringBuilder(); int k = 0;
        while (answer.Length < AnswerCheck.MaxAnswer - 40) { answer.Append("Nov ").Append(1 + k % 28).Append(", ").Append(2100 + k).Append(" and ").Append(9_000_000 + k).Append(" Z").Append(k).Append(' '); k++; }
        var sw = Stopwatch.StartNew();
        var r = AnswerCheck.Check(answer.ToString(), facts.ToString()); sw.Stop();
        _out.WriteLine($"{facts.Length:#,0} characters of facts and an answer of {answer.Length:#,0}: {r.Checked:#,0} values checked, {r.Missing.Count:#,0} listed, in {sw.ElapsedMilliseconds} ms");
        Assert.Equal(k * 3, r.Checked); Assert.Equal(k * 3, r.Missing.Count);        // every date, number and name in this answer stands nowhere
        Assert.True(sw.ElapsedMilliseconds < 8_000, $"the check took {sw.ElapsedMilliseconds} ms");
        Assert.Contains("; and ", r.Lines[0]);                                         // twelve are shown, the rest counted

        var tooLong = AnswerCheck.Check(new string('7', AnswerCheck.MaxAnswer + 1), "7");
        Assert.Equal(new[] { "The answer is longer than 200,000 characters, which is more than this check reads." }, tooLong.Lines); Assert.Equal(0, tooLong.Checked);
        Assert.Equal(1, AnswerCheck.Check(new string('7', 30) + " ", "x").Missing.Count);

        // The faster rules give the same answers as the plain ones: a date agrees when the years agree or one side names none.
        string f = "Due Oct 3, 2026. Met on Nov 7. Count 1,250 and ID AB-12.";
        Assert.Empty(AnswerCheck.Check("Oct 3 and Oct 3, 2026 and 10/3/2026; Nov 7, 2031; 1250; ab-12", f).Missing);
        Assert.Equal(new[] { "Oct 3, 2027", "Oct 4", "1251", "AB-13" }, AnswerCheck.Check("Oct 3, 2027, then Oct 4, then 1251 and AB-13, and Oct 3, 2027 again", f).Missing.OrderBy(x => x.StartsWith("Oct 3") ? 0 : x == "Oct 4" ? 1 : x == "1251" ? 2 : 3).ToArray());
    }

    [Fact]
    public async Task An_answer_is_checked_against_the_first_million_characters_of_a_very_large_file_and_the_window_says_so()
    {
        string big = string.Join("\n\n", Enumerable.Range(0, 1500).Select(i => $"Paragraph {i} " + new string('w', 780) + "."));      // about 1,200,000 characters
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var w = new CompareWindow(); w.Show();
            w.LoadDocuments(new ReadDocument("Big, first", big, Array.Empty<string>()), new ReadDocument("Big, second", big + "\n\nThe goal date is Oct 17, 2026.", Array.Empty<string>())); Settle();
            var box = w.FindControl<TextBox>("AnswerBox")!; var check = w.FindControl<Button>("CheckBtn")!; var outPanel = w.FindControl<StackPanel>("CheckOut")!;
            box.Text = "Paragraph 3 stayed. The goal date is Oct 17, 2026."; var sw = Stopwatch.StartNew(); Click(check); Settle(); sw.Stop();
            var large = Texts(outPanel); int factsLength = w.Facts().Length; long ms = sw.ElapsedMilliseconds;
            w.LoadDocuments(new ReadDocument("Small, first", "One 1.", Array.Empty<string>()), new ReadDocument("Small, second", "Two 2.", Array.Empty<string>())); Settle();
            box.Text = "Two 2."; Click(check); Settle(); var small = Texts(outPanel);
            box.Text = new string('7', AnswerCheck.MaxAnswer + 1); Click(check); Settle(); var tooLong = Texts(outPanel); int max = box.MaxLength;
            w.Close();
            return (large, small, factsLength, ms, tooLong, max);
        }, CancellationToken.None);
        _out.WriteLine($"facts of {r.factsLength:#,0} characters checked in {r.ms} ms");
        Assert.Equal("A file is very large: only its first 1,000,000 characters were searched, so a value from further on can be listed although it is there.", r.large[0]);
        Assert.True(r.factsLength < 2 * CompareWindow.FactsMax + 200_000, "the facts were not cut");
        Assert.True(r.ms < 8_000, $"the check took {r.ms} ms");
        Assert.DoesNotContain(r.small, t => t.StartsWith("A file is very large"));
        Assert.Equal(AnswerCheck.MaxAnswer + 1, r.max);                 // the box takes no more than the check reads, plus one character so that the check can say so
        Assert.Equal(new[] { "The answer is longer than 200,000 characters, which is more than this check reads." }, r.tooLong);
    }
}
