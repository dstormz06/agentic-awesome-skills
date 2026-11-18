using System.IO.Compression;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AndaCompanion.Data;
using AndaCompanion.Models;
using AndaCompanion.Services;
using AndaCompanion.Theming;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// Round 27 in the Windows app: documents read and compared, one of the person's stored prompts on what was compared, the answer check, a report
/// against my data, the weekly copy of the sheet, the hand-off and meeting pages, and in Review the signs worth a second look and the follow-up
/// for a reply. The rules themselves are pinned to the simulation's answers in <see cref="Round27Tests"/>.
/// </summary>
[Collection("Global state")]
public class Round27UiTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public Round27UiTests(HeadlessFixture fx) => _fx = fx;
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static string[] Texts(Control c) => c.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
    private static readonly MockAndaRepository Repo = new();
    private static readonly DateOnly Today = new(2026, 7, 16);

    /// <summary>A small .docx made by hand: a zip of the XML parts Word itself writes.</summary>
    private static void WriteDocx(string path, string bodyXml, string? coreXml = null, string? appXml = null, string? prolog = null)
    {
        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add(string name, string xml) { using var w = new StreamWriter(zip.CreateEntry(name).Open()); w.Write(xml); }
        Add("word/document.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + (prolog ?? "") + "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" + bodyXml + "</w:body></w:document>");
        if (coreXml is not null) Add("docProps/core.xml", coreXml);
        if (appXml is not null) Add("docProps/app.xml", appXml);
    }
    private static string P(string text) => $"<w:p><w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r></w:p>";

    // ---------------------------------------------------------------- reading documents

    [Fact]
    public void A_Word_document_is_read_as_its_body_text_and_what_the_file_says_about_itself_is_only_quoted()
    {
        string dir = Temp.NewDir(), path = Path.Combine(dir, "letter.docx");
        WriteDocx(path,
            P("Dear Dr. Lee,") + "<w:p><w:r><w:t>Two </w:t></w:r><w:r><w:t>runs</w:t><w:t> and a third.</w:t></w:r></w:p>" + "<w:p></w:p>"
            + "<w:p><w:r><w:t>Kept </w:t></w:r><w:ins w:id=\"1\"><w:r><w:t>inserted</w:t></w:r></w:ins><w:del w:id=\"2\"><w:r><w:delText>DELETED</w:delText></w:r></w:del><w:r><w:tab/><w:t>end &lt;b&gt;</w:t></w:r></w:p>",
            "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\"><dc:creator>A. Writer</dc:creator><cp:lastModifiedBy>B.  Editor\n</cp:lastModifiedBy><dcterms:modified>2026-07-01T10:00:00Z</dcterms:modified></cp:coreProperties>",
            "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\"><Application>Microsoft Office Word</Application></Properties>");
        var (doc, problem) = DocumentReader.Read(path);
        Assert.Null(problem); Assert.NotNull(doc);
        Assert.Equal("Dear Dr. Lee,\n\nTwo runs and a third.\n\nKept inserted end <b>", doc!.Text);            // runs joined, an empty paragraph dropped, deleted text not read, markup is text
        Assert.Equal(new[]
        {
            "The file says: created by A. Writer; last saved by B. Editor; last saved on 2026-07-01T10:00:00Z; written with Microsoft Office Word. Anyone can change these fields, so they prove nothing.",
            "The document has tracked changes. Of each, the inserted text was read and the deleted text was not.",
        }, doc.Says);
        Assert.Equal(3, DocCompare.Paras(doc.Text).Count);
    }

    [Fact]
    public void A_file_that_is_not_a_readable_document_is_said_plainly_and_nothing_in_it_is_followed()
    {
        string dir = Temp.NewDir();
        string Problem(string name, Action<string> make) { string p = Path.Combine(dir, name); make(p); var (d, why) = DocumentReader.Read(p); Assert.Null(d); return why!; }
        Assert.Equal("junk.docx could not be read as a document.", Problem("junk.docx", p => File.WriteAllBytes(p, new byte[] { 1, 2, 3, 4 })));
        Assert.Equal("empty.docx has no text that could be read.", Problem("empty.docx", p => WriteDocx(p, "<w:p></w:p>")));
        Assert.Equal("no-body.docx does not look like a Word document this tool can read.", Problem("no-body.docx", p => { using var z = ZipFile.Open(p, ZipArchiveMode.Create); z.CreateEntry("hello.txt"); }));
        // A document type definition (the door to outside entities) is refused outright.
        Assert.Equal("dtd.docx could not be read as a document.", Problem("dtd.docx", p => WriteDocx(p, P("&x;"), prolog: "<!DOCTYPE d [<!ENTITY x SYSTEM \"file:///etc/passwd\">]>")));
        Assert.Equal("binary.txt does not look like plain text, so it was not read.", Problem("binary.txt", p => File.WriteAllBytes(p, new byte[] { 65, 0, 66 })));
        Assert.Equal("blank.md has no text that could be read.", Problem("blank.md", p => File.WriteAllText(p, " \n \n")));
        Assert.Equal("missing.txt could not be read as a document.", DocumentReader.Read(Path.Combine(dir, "missing.txt")).Problem);
        string big = Path.Combine(dir, "big.txt"); using (var f = File.Create(big)) f.SetLength(DocumentReader.MaxTextBytes + 1);
        Assert.Equal("big.txt is larger than 5 MB, which is more than this tool reads as plain text.", DocumentReader.Read(big).Problem);
        Assert.True(DocumentReader.IsDocument("a.DOCX")); Assert.True(DocumentReader.IsDocument("a.md")); Assert.False(DocumentReader.IsDocument("a.csv")); Assert.False(DocumentReader.IsDocument("a.pdf"));
    }

    // ---------------------------------------------------------------- the Compare window

    private static List<CustomAction> Stored() => new()
    {
        new CustomAction { Name = "Compare and contrast", Kind = ActionKind.FillPrompt, Template = "Compare and contrast the two versions. List every difference that matters.\n\n{selection}", Enabled = true },
        new CustomAction { Name = "Explain to my director", Kind = ActionKind.AskAi, Template = "Explain what changed, for my director, in five lines.", Enabled = true, SendWithoutPreview = true },
        new CustomAction { Name = "Switched off", Kind = ActionKind.AskAi, Template = "x {selection}", Enabled = false },
        new CustomAction { Name = "A to-do", Kind = ActionKind.AddToTodo, Template = "y", Enabled = true },
    };

    [Fact]
    public async Task Two_documents_are_read_from_disk_compared_by_paragraphs_and_a_table_with_a_document_is_refused_in_words()
    {
        string dir = Temp.NewDir(), a = Path.Combine(dir, "plan v1.txt"), b = Path.Combine(dir, "plan v2.docx"), c = Path.Combine(dir, "data.csv");
        File.WriteAllText(a, "Intro stays.\n\n<img src=x onerror=alert(1)>\n\n=HYPERLINK(\"http://x\")\n\nThe goal date is Oct 3, 2026 and all is well.");
        WriteDocx(b, P("Intro stays.") + P("&lt;script&gt;alert(2)&lt;/script&gt;") + P("=HYPERLINK(\"http://x\")") + P("The goal date is Oct 17, 2026 and all is well."));
        File.WriteAllText(c, "ANDA,Status\n216482,x\n");
        var copied = new List<string>(); var saved = new List<string>();
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var w = new CompareWindow { WriteClipboard = t => { copied.Add(t); return Task.CompletedTask; }, SaveReport = html => { saved.Add(html); return Task.FromResult(true); } };
            w.Show();
            string? p1 = w.LoadFile(a, true), p2 = w.LoadFile(b, false); Settle();
            var first = (p1, p2, w.Kind, w.Doc!.Same, w.Doc.Changed, w.Doc.Added, w.Doc.Removed, NameA: w.FindControl<TextBlock>("NameA")!.Text, NameB: w.FindControl<TextBlock>("NameB")!.Text,
                Key: w.FindControl<StackPanel>("KeyRow")!.IsVisible, Shown: Texts(w.FindControl<StackPanel>("Out")!));
            Click(w.FindControl<Button>("CopyAllBtn")!); Settle(); Click(w.FindControl<Button>("SaveBtn")!); Settle();
            string? p3 = w.LoadFile(c, false); Settle();
            var mixed = (p3, w.Kind, Say: Texts(w.FindControl<StackPanel>("Out")!), Copy: w.FindControl<Button>("CopyBtn")!.IsEnabled, Check: w.FindControl<Button>("CheckBtn")!.IsEnabled);
            w.Close();
            return (first, mixed);
        }, CancellationToken.None);
        Assert.Null(r.first.p1); Assert.Null(r.first.p2);
        Assert.Equal(("doc", 2, 1, 1, 1), (r.first.Kind, r.first.Same, r.first.Changed, r.first.Added, r.first.Removed));
        Assert.Equal(("plan v1.txt: 4 paragraphs", "plan v2.docx: 4 paragraphs", false), (r.first.NameA, r.first.NameB, r.first.Key));
        Assert.Contains("<script>alert(2)</script>", r.first.Shown); Assert.Contains("The goal date is Oct 17, 2026 and all is well.", r.first.Shown);      // markup in a file is plain text on the screen
        string report = Assert.Single(copied);
        Assert.Contains("CHANGED\nParagraph 4: The goal date is Oct 3, 2026 and all is well. → The goal date is Oct 17, 2026 and all is well.", report);
        Assert.DoesNotContain(report.Split('\n'), l => l.Length > 0 && "=+-@".Contains(l[0]));
        string html = Assert.Single(saved);
        Assert.Contains("<h1>Comparison: plan v1.txt and plan v2.docx</h1>", html); Assert.Contains("&lt;script&gt;alert(2)&lt;/script&gt;", html); Assert.DoesNotContain("<script", html); Assert.Contains("default-src 'none'", html);
        Assert.Null(r.mixed.p3); Assert.Null(r.mixed.Kind);
        Assert.Contains("One file is a table and the other is a document, so they cannot be compared. Please choose two tables or two documents.", r.mixed.Say);
        Assert.False(r.mixed.Copy); Assert.False(r.mixed.Check);
    }

    [Fact]
    public async Task A_stored_prompt_gets_the_differences_and_the_files_only_when_asked_and_is_always_shown_first()
    {
        var ran = new List<(CustomAction Action, string Prompt, string Holds)>();
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var w = new CompareWindow { Prompts = Stored, RunPrompt = (a, p, h) => ran.Add((a, p, h)), Today = () => Today };
            w.Show(); Settle();
            var box = w.FindControl<ComboBox>("PromptBox")!; var use = w.FindControl<Button>("UseBtn")!;
            var before = (Items: ((IEnumerable<string>)box.ItemsSource!).ToArray(), Use: use.IsEnabled);
            Click(w.FindControl<Button>("SampleDocsBtn")!); Settle();
            bool useNow = use.IsEnabled;
            Click(use); Settle(); string none = w.StatusText;
            box.SelectedIndex = 1; Click(use); Settle(); string opened = w.StatusText;
            w.FindControl<CheckBox>("WholeBox")!.IsChecked = true; box.SelectedIndex = 2; Click(use); Settle();
            w.Close();
            return (before, useNow, none, opened);
        }, CancellationToken.None);
        Assert.Equal(new[] { "Choose one of your stored prompts…", "Compare and contrast · fill and show", "Explain to my director · ask my AI" }, r.before.Items);      // only prompts that are on and that fill or ask
        Assert.False(r.before.Use); Assert.True(r.useNow);
        Assert.Equal("Please choose one of your stored prompts first.", r.none);
        Assert.Equal("A window with exactly what the prompt holds is open. Nothing has been sent.", r.opened);
        Assert.Equal(2, ran.Count);
        Assert.StartsWith("Compare and contrast the two versions. List every difference that matters.\n\n<selection>\nDIFFERENCES, FOUND BY FIXED RULES\nLetter, first draft has 5 paragraphs", ran[0].Prompt);
        Assert.EndsWith("</selection>", ran[0].Prompt); Assert.DoesNotContain("EARLIER FILE", ran[0].Prompt); Assert.DoesNotContain("Dear Dr. Lee", ran[0].Prompt);
        Assert.Equal("It holds the differences found by the rules, not the files.", ran[0].Holds);
        Assert.StartsWith("Explain what changed, for my director, in five lines.\n\n<selection>\nDIFFERENCES", ran[1].Prompt);                 // no place for the text: it goes at the end
        Assert.Contains("\n\nEARLIER FILE: Letter, first draft\nDear Dr. Lee,", ran[1].Prompt); Assert.Contains("\n\nLATER FILE: Letter, second draft\nDear Dr. Lee,", ran[1].Prompt);
        Assert.Equal("It holds the differences found by the rules and, because you chose so, the beginning of each file.", ran[1].Holds);
    }

    private sealed class FakeAi : IChatCompleter
    {
        public string ProviderName => "Fake AI";
        public int Calls;
        public Task<string> CompleteAsync(string system, string user, int maxTokens, CancellationToken ct) { Calls++; return Task.FromResult("- One line"); }
    }

    [Fact]
    public void A_prepared_prompt_is_never_sent_without_the_preview_and_its_note_says_what_it_holds_and_how_long_it_is()
    {
        ActionRunner Runner(IChatCompleter? ai) => new(() => Today, () => new BusinessCalendar(), new TodoService(Path.Combine(Temp.NewDir(), "todo.json")), new AgentService(() => Array.Empty<AgentSettings>(), _ => null, new HttpClientHandler()), _ => null, () => ai, () => "Fake AI");
        var stored = Stored(); string prompt = "P\n\n<selection>\nx\n</selection>", holds = "It holds the differences found by the rules, not the files.";
        var fill = Assert.IsType<ActionShow>(Runner(null).RunPrepared(stored[0], "Compare two files", prompt, holds)).Model;
        Assert.Equal(("YOUR PROMPT, FILLED IN", prompt, $"Nothing was sent anywhere. {holds} It is 29 characters long. Please copy the text into the tool you use."), (fill.PromptHeading, fill.Prompt, fill.Note));
        Assert.Null(fill.Send);
        var noAi = Assert.IsType<ActionShow>(Runner(null).RunPrepared(stored[1], "Compare two files", prompt, holds)).Model;
        Assert.StartsWith("No AI connection is ready, so nothing can be sent.", noAi.Note); Assert.Null(noAi.Send);
        var ai = new FakeAi();
        var ask = Assert.IsType<ActionShow>(Runner(ai).RunPrepared(stored[1], "Compare two files", prompt, holds)).Model;
        Assert.Equal("THIS IS EXACTLY WHAT WILL BE SENT", ask.PromptHeading);
        Assert.Equal($"Nothing has been sent yet. Pressing Send sends only the text above (29 characters), with a short fixed instruction that treats it as data, to your AI connection (Fake AI). {holds}", ask.Note);
        Assert.NotNull(ask.Send);
        Assert.False(ask.SendOnOpen);                          // the stored prompt says "send without the preview"; a prompt that can hold file text never does
        Assert.Equal(0, ai.Calls);                             // and nothing was sent by making the preview
        Assert.IsType<ActionNotice>(Runner(ai).RunPrepared(stored[3], "Compare two files", prompt, holds));      // a to-do action is not a prompt
    }

    [Fact]
    public async Task An_AI_answer_is_checked_against_the_comparison_and_the_files_and_what_stands_nowhere_is_listed()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var w = new CompareWindow(); w.Show(); w.Load(FileCompare.SampleA, FileCompare.SampleB); Settle();
            var box = w.FindControl<TextBox>("AnswerBox")!; var check = w.FindControl<Button>("CheckBtn")!; var outPanel = w.FindControl<StackPanel>("CheckOut")!;
            Click(check); Settle(); var empty = Texts(outPanel);
            box.Text = "Three rows changed. For 216482 the forecast moved 14 days later, to 10/31/2026. ANDA 219120 is gone and 220017 is new. Sertraline (219120) had 0 open items.";
            Click(check); Settle(); var good = Texts(outPanel);
            box.Text = "1. 216482 slipped 21 days to Nov 7, 2026.\n2. ANDA 999999 was added.\n<img src=x onerror=alert(1)>";
            Click(check); Settle(); var bad = Texts(outPanel); int boxes = outPanel.Children.OfType<Border>().Count(x => (x.Tag as string) == "missbox");
            w.Close();
            return (empty, good, bad, boxes);
        }, CancellationToken.None);
        Assert.Equal(new[] { "Please paste the answer first." }, r.empty);
        Assert.Equal(new[] { "All 7 numbers, dates and names with digits in the answer also stand in the comparison or in the two files.", AnswerCheck.Limit }, r.good);
        Assert.Equal(new[] { "3 of the values in the answer do not stand in the comparison or in the two files. Please check them: 21; 999999; Nov 7, 2026.", AnswerCheck.Limit }, r.bad);
        Assert.Equal(1, r.boxes);
    }

    [Fact]
    public async Task A_report_is_checked_against_my_data_and_the_window_asks_for_one_file_only()
    {
        string dir = Temp.NewDir(), letter = Path.Combine(dir, "letter.txt"), nokey = Path.Combine(dir, "no key.csv");
        File.WriteAllText(letter, "A letter, not a table."); File.WriteAllText(nokey, "Product,Goal date\nMetformin,10/3/2026\n");
        var ran = new List<string>();
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var mine = new Dictionary<string, (string, string)> { ["216482"] = ("Oct 3, 2026", "Oct 17, 2026"), ["214990"] = ("Aug 15, 2026", "Aug 6, 2026") };
            var w = new CompareWindow { Mine = id => mine.TryGetValue(id, out var d) ? d : null, Prompts = Stored, RunPrompt = (_, p, _) => ran.Add(p), Today = () => Today };
            w.Show(); Settle(); w.SetMode("data"); Settle();
            var shape = (w.Mode, B: w.FindControl<StackPanel>("PickBPanel")!.IsVisible, Label: w.FindControl<TextBlock>("LabelA")!.Text, Pick: w.FindControl<Button>("PickA")!.Content as string,
                Sample: w.FindControl<Button>("SampleBtn")!.IsVisible, Report: w.FindControl<Button>("SampleReportBtn")!.IsVisible, Key: w.FindControl<StackPanel>("KeyRow")!.IsVisible,
                Name: Avalonia.Automation.AutomationProperties.GetName(w.FindControl<Button>("ModeDataBtn")!));
            Click(w.FindControl<Button>("SampleReportBtn")!); Settle();
            var sample = (w.Kind, w.Checked!.Agree, w.Checked.Differ, Unknown: w.Checked.Unknown.ToArray(), Lines: w.Lines(true).ToArray(), Bar: w.FindControl<StackPanel>("Out")!.GetVisualDescendants().OfType<Grid>().Select(g => Avalonia.Automation.AutomationProperties.GetName(g)).FirstOrDefault(n => n is { Length: > 0 } && n.StartsWith("Agree")));
            w.FindControl<CheckBox>("WholeBox")!.IsChecked = true; w.FindControl<ComboBox>("PromptBox")!.SelectedIndex = 1; Click(w.FindControl<Button>("UseBtn")!); Settle();
            string? p1 = w.LoadFile(nokey, true); Settle(); var noKey = w.Lines(false).ToArray();
            string? p2 = w.LoadFile(letter, true); Settle(); var doc = (w.Kind, Say: Texts(w.FindControl<StackPanel>("Out")!));
            w.SetMode("two"); Settle(); bool back = w.FindControl<StackPanel>("PickBPanel")!.IsVisible && w.Kind is null;
            w.Close();
            return (shape, sample, p1, noKey, p2, doc, back);
        }, CancellationToken.None);
        Assert.Equal(("data", false, "REPORT", "Choose the report…", false, true, false, "A report against my data, selected"), r.shape);
        Assert.Equal(("data", 1, 1), (r.sample.Kind, r.sample.Agree, r.sample.Differ)); Assert.Equal(new[] { "220017" }, r.sample.Unknown);
        Assert.Equal(new[]
        {
            "Weekly report (sample) names 3 ANDAs; 2 are in your data.", "1 agrees with your data on the goal date and the forecast date, and 1 differs.",
            "ANDA 214990: Forecast date: the report says 8/20/2026; your data says Aug 6, 2026 (14 days earlier).", "Not in your data: 220017.", ReportCheck.Close,
        }, r.sample.Lines);
        Assert.Equal("Agree: 1, Differ: 1, Not in my data: 1", r.sample.Bar);
        string prompt = Assert.Single(ran);
        Assert.Contains("\n\nTHE FILE: Weekly report (sample)\nANDA | Goal date | Forecast date\n214990 | Aug 15, 2026 | 8/20/2026", prompt); Assert.DoesNotContain("EARLIER FILE", prompt);      // one file, once
        Assert.Null(r.p1); Assert.Equal(new[] { "no key.csv has no ANDA column, so it cannot be checked against your data.", ReportCheck.Close }, r.noKey);
        Assert.Null(r.p2); Assert.Null(r.doc.Kind); Assert.Contains("A report is a table. Please choose an Excel workbook or a CSV file.", r.doc.Say);
        Assert.True(r.back);
    }

    // ---------------------------------------------------------------- the weekly copy of the sheet

    [Fact]
    public void One_copy_of_the_sheet_is_kept_for_a_week_and_the_app_never_deletes_a_copy_by_itself()
    {
        string dir = Path.Combine(Temp.NewDir(), "sheet-copies"); var copies = new SheetCopies(dir);
        CompareTable T(string v) => new("My sheet", new[] { "ANDA", "Status" }, new[] { new[] { "216482", v } });
        var monday = new DateOnly(2026, 11, 9);
        Assert.Null(copies.LatestBefore(monday)); Assert.Empty(copies.Days());
        Assert.True(copies.Capture(T("week 1"), monday));
        Assert.False(copies.Capture(T("same week"), monday.AddDays(3)));                 // Thursday of the same week: the week already has its copy
        Assert.False(copies.Capture(T("same week"), monday.AddDays(6)));                 // Sunday too
        Assert.Null(copies.LatestBefore(monday));                                         // a copy made today is not "earlier"
        Assert.Equal(("My sheet, copy kept Nov 9", "week 1"), (copies.LatestBefore(monday.AddDays(1))!.Value.Table.Name, copies.LatestBefore(monday.AddDays(1))!.Value.Table.Rows[0][1]));
        for (int w = 1; w <= 6; w++) Assert.True(copies.Capture(T("week " + (w + 1)), monday.AddDays(7 * w)));
        Assert.Equal(7, copies.Days().Count);                                             // seven weeks were kept one after another, and all seven stay: only the person deletes
        Assert.Equal(monday, copies.Days()[0]);
        Assert.Equal("week 6", copies.LatestBefore(monday.AddDays(7 * 6))!.Value.Table.Rows[0][1]);
        // A file that is not one of ours, and a copy that cannot be read, are passed over.
        File.WriteAllText(Path.Combine(dir, "sheet-not-a-date.json"), "{}"); File.WriteAllText(Path.Combine(dir, "other.txt"), "x");
        File.WriteAllText(Path.Combine(dir, "sheet-2026-12-23.json"), "{ this is not json");
        Assert.Equal("week 7", copies.LatestBefore(new DateOnly(2026, 12, 25))!.Value.Table.Rows[0][1]);
        Assert.Equal(8, copies.DeleteAll()); Assert.Empty(copies.Days()); Assert.True(File.Exists(Path.Combine(dir, "other.txt")));      // only its own files are deleted
    }

    [Fact]
    public async Task My_sheet_since_last_week_keeps_nothing_until_switched_on_and_then_compares_with_the_last_copy()
    {
        bool on = false, kept = false; int deleted = 0, read = 0; long tick = 1000; var switched = new List<bool>();
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var w = new CompareWindow
            {
                WeeklyOn = () => on, SetWeekly = v => { on = v; if (v) kept = true; switched.Add(v); },
                HasWeeklyCopies = () => kept, DeleteWeekly = () => { deleted++; kept = false; return 2; },
                WeeklyPair = () => { read++; return (FileCompare.SampleA with { Name = "My sheet, copy kept Nov 10" }, FileCompare.SampleB with { Name = "My sheet, today" }, null); },
                WeeklyCount = () => kept ? 2 : 0,
            };
            w.Ticks = () => tick;
            w.Show(); Settle(); w.SetMode("week"); Settle();
            var box = w.FindControl<CheckBox>("WeekBox")!;
            var off = (box.IsChecked, Pick: w.FindControl<Grid>("PickGrid")!.IsVisible, w.Kind, Say: Texts(w.FindControl<StackPanel>("Out")!).FirstOrDefault(), Copy: w.FindControl<Button>("CopyBtn")!.IsEnabled);
            box.IsChecked = true; Settle();
            var now = (w.Kind, w.Result?.ChangedRows, Name: w.FindControl<TextBlock>("NameA")!.Text, w.StatusText);
            box.IsChecked = false; Settle();
            var del = w.FindControl<Button>("WeekDelBtn")!;
            var after = (w.Kind, w.StatusText, Offered: del.IsVisible);
            string? count = w.FindControl<TextBlock>("WeekCountText")!.Text;
            Click(del); Settle(); var asked = (del.Content as string, deleted, w.Kind);
            Click(del); Settle(); tick += CompareWindow.WeekDeletePauseMs - 1; Click(del); Settle();
            var twice = (del.Content as string, deleted);                                   // a double-click, or a key held down, is one press
            w.SetMode("two"); Settle(); w.SetMode("week"); Settle();
            var left = (del.Content as string, deleted);                                    // leaving the view withdraws the question
            Click(del); Settle(); tick += CompareWindow.WeekDeletePauseMs; Click(del); Settle();
            var gone = (del.IsVisible, deleted, w.Kind, w.StatusText);
            // While keeping is switched on, a delete must not keep a fresh copy in the same press.
            box.IsChecked = true; Settle(); int before = read;
            Click(del); Settle(); tick += CompareWindow.WeekDeletePauseMs; Click(del); Settle();
            var whileOn = (deleted, Read: read - before, w.Kind, w.StatusText, Say: Texts(w.FindControl<StackPanel>("Out")!).FirstOrDefault(), Count: w.FindControl<TextBlock>("WeekCountText")!.IsVisible);
            w.Close();
            return (off, now, after, asked, gone, count, twice, left, whileOn);
        }, CancellationToken.None);
        Assert.Equal((false, false, null, false), (r.off.IsChecked, r.off.Pick, r.off.Kind, r.off.Copy));
        Assert.StartsWith("Nothing is kept yet.", r.off.Say);
        Assert.Equal(("table", 3, "My sheet, copy kept Nov 10: 6 rows, 7 columns", "From now on one copy of your sheet is kept each week, on this PC only."), (r.now.Kind, r.now.ChangedRows, r.now.Name, r.now.StatusText));
        Assert.Equal(("table", "No new copies are kept. The copies that exist stay until you delete them.", true), r.after);      // round 29: switching it off deletes nothing
        Assert.Equal(("Click again to delete", 0, "table"), r.asked);                                                                 // the first press only asks
        Assert.Equal("2 copies are kept on this PC, one for each week. The app never deletes a copy by itself.", r.count);
        Assert.Equal(("Click again to delete", 0), r.twice);
        Assert.Equal(("Delete the kept copies…", 0), r.left);
        Assert.Equal((false, 1, null, "The kept copies were deleted. Nothing else was changed."), r.gone);                            // the second press, after a pause, deletes, and says so
        Assert.Equal((2, 0, null, "The kept copies were deleted. Nothing else was changed.", "No copy is kept at the moment. Keeping is still switched on, so a new copy is made the next time this opens.", false), r.whileOn);
        Assert.Equal(new[] { true, false, true }, switched);
    }

    // ---------------------------------------------------------------- hand-off page, meeting page

    [Fact]
    public async Task The_hand_off_page_is_made_from_my_own_data_and_notes_and_is_copied_or_saved_only_when_asked()
    {
        var notes = new QuickNotesService(Path.Combine(Temp.NewDir(), "quick-notes.json"));
        Assert.True(notes.Add("216482", "<b>bold</b> note for the hand-off").Ok);
        var ctx = new CardContext(Today, new BusinessCalendar());
        var copied = new List<string>(); var saved = new List<string>();
        IReadOnlyList<string> Lines(bool watchOnly, bool withNotes) =>
            Handoff.Text("Hand-off", HandoffData.Entries(HandoffData.Order(Repo, ctx, watchOnly), Repo, ctx, notes, null, null), Today.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture), withNotes);
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var w = new HandoffWindow("Hand-off page", chooseWhich: true, Lines) { WriteClipboard = t => { copied.Add(t); return Task.CompletedTask; }, SaveText = t => { saved.Add(t); return Task.FromResult(true); } };
            w.Show(); Settle();
            bool tickedAtFirst = w.NotesBox.IsChecked == true; string noNotes = w.Page.Text ?? "";
            w.NotesBox.IsChecked = true; Settle(); string all = w.Page.Text ?? "";
            Click(w.WatchButton); Settle(); string watch = w.Page.Text ?? "";
            string name = Avalonia.Automation.AutomationProperties.GetName(w.WatchButton) ?? "";
            Click(w.CopyButton); Settle(); string s1 = w.Note.Text ?? "";
            Click(w.SaveButton); Settle(); string s2 = w.Note.Text ?? "";
            w.Close();
            return (all, noNotes, watch, name, s1, s2, tickedAtFirst);
        }, CancellationToken.None);
        int count = Repo.Numbers.Count;
        Assert.StartsWith($"Hand-off: {count} ANDAs, as of Jul 16, 2026\nFrom my own data and notes on this PC. It is not the system of record.\n\n", r.all);
        Assert.Contains(StatusLine.Build(Repo.Find("216482")!), r.all); Assert.Contains(StatusLine.Build(Repo.Find("214990")!), r.all);
        Assert.Contains("<b>bold</b> note for the hand-off", r.all);                       // a note with markup is plain text on the page
        Assert.Contains("\n  Open: ", r.all); Assert.Contains("\n  Notes: ", r.all);
        Assert.False(r.tickedAtFirst);                                                       // notes go on a page that may be passed on only when the person ticks the box
        Assert.DoesNotContain("Notes:", r.noNotes); Assert.DoesNotContain("note for the hand-off", r.noNotes); Assert.Contains("\n  Open: ", r.noNotes);
        Assert.StartsWith("Hand-off: ", r.watch); Assert.Equal("Only those that need a look, selected", r.name);
        Assert.All(HandoffData.Order(Repo, ctx, true), n => Assert.Contains($"ANDA {n} (", r.watch));
        Assert.Equal(r.watch, Assert.Single(copied)); Assert.Equal("The page was copied. Nothing was sent; you decide who gets it.", r.s1);
        Assert.Equal(r.watch, Assert.Single(saved)); Assert.Equal("The page was saved. Nothing was sent.", r.s2);
        // Those that need a look come first; an ANDA that is not in the data gives no entry.
        var order = HandoffData.Order(Repo, ctx, false); var need = HandoffData.Order(Repo, ctx, true);
        Assert.Equal(need, order.Take(need.Count)); Assert.Equal(count, order.Count);
        Assert.Null(HandoffData.Entry("999999", Repo, ctx, notes, null, null));
    }

    private static AskHooks Hooks(List<IReadOnlyList<string>> pages) =>
        new(() => new List<CustomAction>(), (_, _) => { }, _ => { }, _ => { })
        {
            KnownAndas = t => Proofreader.KnownIds(t, Repo.Find), AddNote = (_, _) => (true, "Note saved."), Today = () => Today, MeetingPage = pages.Add,
        };

    [Fact]
    public async Task A_selection_that_names_ANDAs_offers_a_meeting_page_for_exactly_those()
    {
        var pages = new List<IReadOnlyList<string>>();
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var one = new AskWindow(Hooks(pages), "Thursday huddle: ANDA 216482 and the labeling item."); one.Show(); Settle();
            var b1 = one.FindControl<WrapPanel>("MoreButtons")!.Children.OfType<Button>().Single(b => (b.Tag as string) == "meeting");
            string label1 = b1.Content as string ?? ""; Click(b1); Settle(); one.Close();
            var two = new AskWindow(Hooks(pages), "ANDA 216482 and ANDA 214990, and ANDA 999999 which is not mine."); two.Show(); Settle();
            var b2 = two.FindControl<WrapPanel>("MoreButtons")!.Children.OfType<Button>().Single(b => (b.Tag as string) == "meeting");
            string label2 = b2.Content as string ?? ""; Click(b2); Settle(); two.Close();
            var none = new AskWindow(Hooks(pages), "Nothing here names an ANDA of mine."); none.Show(); Settle();
            bool offered = none.FindControl<WrapPanel>("MoreButtons")!.Children.OfType<Button>().Any(b => (b.Tag as string) == "meeting"); none.Close();
            return (label1, label2, offered);
        }, CancellationToken.None);
        Assert.Equal(("Make a meeting page for ANDA 216482", "Make a meeting page for these 2 ANDAs", false), r);
        Assert.Equal(new[] { "216482" }, pages[0]); Assert.Equal(new[] { "216482", "214990" }, pages[1]);
        var ctx = new CardContext(Today, new BusinessCalendar());
        var page = Handoff.Text("Meeting page", HandoffData.Entries(pages[0], Repo, ctx, null, null, null), "Jul 16, 2026", true);
        Assert.Equal("Meeting page: 1 ANDA, as of Jul 16, 2026", page[0]); Assert.DoesNotContain(page, l => l.StartsWith("ANDA 214990", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- Review a draft

    private static PromptLibraryService Prompts() => new(Path.Combine(Temp.NewDir(), "p.json"));

    [Fact]
    public async Task Review_lists_signs_worth_a_second_look_without_a_verdict_and_offers_a_follow_up_for_a_reply()
    {
        var todo = new TodoService(Path.Combine(Temp.NewDir(), "todo.json")); var opinions = new List<string>();
        const string draft = "Certainly! Here is a revised version.\n\nANDA 216482: under 21 CFR 314.96(a) the amendment is minor. Please send your response by Friday, Dec 4, 2026.\n\nI hope this helps.";
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var w = new DraftWindow(draft, Repo.Find, new WritingService(null), new Settings(), Prompts(), () => Today, new BusinessCalendar())
            {
                AddFollowUp = (title, number, day) => todo.Add(title, number, null, day, "Review a draft", Today), SecondOpinion = opinions.Add,
            };
            w.Show(); Settle(); w.RunCheck(); Settle();
            var signs = w.FindControl<StackPanel>("SignsPanel")!; var remind = w.FindControl<WrapPanel>("RemindPanel")!;
            var all = (Signs: signs.IsVisible, Items: Texts(w.FindControl<StackPanel>("SignsList")!).Where(t => t.Length > 2).ToArray(), Fine: w.FindControl<TextBlock>("SignsFine")!.Text,
                Remind: remind.IsVisible, Says: w.FindControl<TextBlock>("RemindText")!.Text, Whole: string.Join(" ", Texts(signs)));
            Click(w.FindControl<Button>("RemindBtn")!); Settle();
            var added = (Says: w.FindControl<TextBlock>("RemindText")!.Text, Enabled: w.FindControl<Button>("RemindBtn")!.IsEnabled);
            w.RunCheck(); Settle(); Click(w.FindControl<Button>("RemindBtn")!); Settle(); string again = w.FindControl<TextBlock>("RemindText")!.Text ?? "";
            Click(w.FindControl<Button>("SecondOpinionBtn")!); Settle();
            w.SetView("proof"); Settle(); var proof = (signs.IsVisible, remind.IsVisible);
            w.SetView("facts"); Settle(); var facts = (signs.IsVisible, remind.IsVisible);
            w.FindControl<TextBox>("DraftBox")!.Text = "ANDA 216482 is on track. We met on Dec 4, 2026."; w.SetView("all"); Settle();
            var plain = (Items: Texts(w.FindControl<StackPanel>("SignsList")!).Where(t => t.Length > 2).ToArray(), Fine: w.FindControl<TextBlock>("SignsFine")!.IsVisible, Remind: remind.IsVisible);
            w.FindControl<TextBox>("DraftBox")!.Text = ""; w.RunCheck(); Settle(); var empty = (signs.IsVisible, remind.IsVisible);
            w.Close();
            return (all, added, again, proof, facts, plain, empty);
        }, CancellationToken.None);
        Assert.True(r.all.Signs);
        Assert.Equal(new[]
        {
            "“Certainly! Here”: wording that an AI assistant often leaves behind. Please read around it.", "“I hope this helps”: wording that an AI assistant often leaves behind. Please read around it.",
            "Cited: 21 CFR 314.96(a). This tool cannot confirm that the source exists or says this. Please check it.", string.Join("\n", SecondLook.Response),
        }, r.all.Items);
        Assert.Equal(SecondLook.NotAVerdict, r.all.Fine);
        Assert.DoesNotMatch("written by (an )?AI|AI-generated|probably|likely", r.all.Whole);          // never a statement about who or what wrote the text
        Assert.True(r.all.Remind); Assert.Equal("Your draft asks for a reply by Friday, Dec 4, 2026.", r.all.Says);
        Assert.Equal(("Added to your ANDA to-do list for Friday, Dec 4, 2026. This tool does not read your mail, so it cannot know whether a reply came.", false), r.added);
        var item = Assert.Single(todo.Items);
        Assert.Equal("Follow up if no reply: Please send your response by Friday, Dec 4, 2026|216482|2026-12-04|Review a draft", $"{item.Title}|{item.Number}|{item.Due:yyyy-MM-dd}|{item.Source}");
        Assert.Equal("That is already on your to-do list.", r.again);
        Assert.Equal(draft, Assert.Single(opinions));
        Assert.Equal((false, false), r.proof); Assert.Equal((true, true), r.facts);
        Assert.Equal(new[] { SecondLook.None }, r.plain.Items); Assert.False(r.plain.Fine); Assert.False(r.plain.Remind);
        Assert.Equal((false, false), r.empty);
    }
}
