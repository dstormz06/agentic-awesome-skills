using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AndaCompanion.Data;
using AndaCompanion.Services;
using AndaCompanion.Theming;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// Round 26, the windows: Compare two files, the extra buttons in Ask about a selection (reading prompts, a calendar invitation, a note on an
/// ANDA the selection names) and My usage. The rules themselves are pinned to the simulation's answers in <see cref="Round26Tests"/>.
/// </summary>
[Collection("Global state")]
public class Round26UiTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public Round26UiTests(HeadlessFixture fx) => _fx = fx;
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static string[] Texts(Control c) => c.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
    private static readonly MockAndaRepository Repo = new();

    // ---------------------------------------------------------------- Compare two files

    [Fact]
    public async Task The_sample_files_are_compared_and_shown_as_a_summary_charts_and_changes()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var w = new CompareWindow(); w.Show(); Settle();
            var before = (w.FindControl<Button>("CopyBtn")!.IsEnabled, w.FindControl<ComboBox>("KeyBox")!.IsEnabled, w.FindControl<TextBlock>("NameA")!.Text);
            Click(w.FindControl<Button>("SampleBtn")!); Settle();
            var texts = Texts(w.FindControl<StackPanel>("Out")!);
            var res = (before, texts, Keys: ((IEnumerable<string>)w.FindControl<ComboBox>("KeyBox")!.ItemsSource!).ToArray(), Names: (w.FindControl<TextBlock>("NameA")!.Text, w.FindControl<TextBlock>("NameB")!.Text),
                Buttons: new[] { "CopyBtn", "CopyAllBtn", "SaveBtn", "AiBtn" }.Select(n => w.FindControl<Button>(n)!.IsEnabled).ToArray(), w.StatusText, w.Result!.ChangedCells);
            w.Close();
            return res;
        }, CancellationToken.None);
        Assert.Equal((false, false, "No file yet."), r.before);
        Assert.Contains("Portfolio, Nov 10 has 6 rows; Portfolio, Nov 17 has 6 rows. Rows were matched by “ANDA” (it names every row once in both files).", r.texts);
        Assert.Contains("2 rows are the same, 3 changed (6 cells), 1 is new in Portfolio, Nov 17, and 1 is no longer there.", r.texts);
        foreach (string t in new[] { "ROWS", "Same 2", "Changed 3", "New 1", "No longer there 1", "CHANGES BY COLUMN", "WHAT CHANGED", "14 days later", "IR response received", "220017", "219120" }) Assert.Contains(t, r.texts);
        Assert.Equal("Suggested for you", r.Keys[0]); Assert.Contains("Product", r.Keys); Assert.DoesNotContain("Reviewer note", r.Keys);       // only columns both files have
        Assert.Equal(("Portfolio, Nov 10: 6 rows, 7 columns", "Portfolio, Nov 17: 6 rows, 8 columns"), r.Names);
        Assert.Equal(new[] { true, true, true, false }, r.Buttons);                       // the narrative needs the running app's AI path
        Assert.StartsWith("Two fictional versions of one report are in.", r.StatusText);
        Assert.Equal(6, r.ChangedCells);
    }

    [Fact]
    public async Task Copy_save_and_the_narrative_hand_over_only_what_they_say()
    {
        var copied = new List<string>(); var saved = new List<string>(); var sent = new List<string>();
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var w = new CompareWindow { Narrative = sent.Add, WriteClipboard = t => { copied.Add(t); return Task.CompletedTask; }, SaveReport = html => { saved.Add(html); return Task.FromResult(true); } };
            w.Show();
            w.Load(new CompareTable("Old <b>", new[] { "Key", "Note" }, new[] { new[] { "1", "plain" } }), new CompareTable("New \"q\"", new[] { "Key", "Note" }, new[] { new[] { "1", "<script>alert(1)</script> & more" } })); Settle();
            Click(w.FindControl<Button>("CopyBtn")!); Settle(); string s1 = w.StatusText;
            Click(w.FindControl<Button>("CopyAllBtn")!); Settle(); string s2 = w.StatusText;
            Click(w.FindControl<Button>("SaveBtn")!); Settle(); string s3 = w.StatusText;
            Click(w.FindControl<Button>("AiBtn")!); Settle(); string s4 = w.StatusText;
            w.WriteClipboard = _ => throw new InvalidOperationException("locked");
            Click(w.FindControl<Button>("CopyBtn")!); Settle(); string s5 = w.StatusText;
            var shown = Texts(w.FindControl<StackPanel>("Out")!);
            w.Close();
            return (s1, s2, s3, s4, s5, shown);
        }, CancellationToken.None);
        Assert.Equal(("Summary copied.", "Full report copied.", "The report was saved. Nothing was sent."), (r.s1, r.s2, r.s3));
        Assert.StartsWith("A window with exactly what would be sent is open. Nothing has been sent.", r.s4);
        Assert.Equal("Windows did not allow copying just now. Please use “Save a report…” instead.", r.s5);
        Assert.Equal(2, copied.Count); Assert.DoesNotContain("CHANGED", copied[0]); Assert.Contains("CHANGED", copied[1]);
        string html = Assert.Single(saved);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt; &amp; more", html); Assert.DoesNotContain("<script>", html);      // a cell is only ever text in the saved page
        Assert.Contains("Old &lt;b&gt;", html); Assert.Contains("New &quot;q&quot;", html);
        string digest = Assert.Single(sent);
        Assert.True(digest.Length <= 1900); Assert.Contains("1: Note: plain → <script>alert(1)</script> & more", digest);
        Assert.Contains("<script>alert(1)</script> & more", r.shown);                    // and plain text on the screen
    }

    [Fact]
    public async Task Files_are_read_from_disk_and_a_file_that_is_not_a_table_is_said_plainly()
    {
        string dir = Temp.NewDir();
        string a = Path.Combine(dir, "last week.csv"), b = Path.Combine(dir, "this week.csv"), junk = Path.Combine(dir, "broken.xlsx"), other = Path.Combine(dir, "notes.docx");
        File.WriteAllText(a, "ANDA,Status,Goal date\n216482,Under review,10/3/2026\n214990,\"On track, mostly\",8/15/2026\n");
        File.WriteAllText(b, "ANDA,Status,Goal date\n216482,Approved,\"Oct 3, 2026\"\n214990,\"On track, mostly\",8/22/2026\n300001,=HYPERLINK(\"http://x\"),1/1/2027\n");
        File.WriteAllBytes(junk, new byte[] { 1, 2, 3, 4, 5 }); File.WriteAllText(other, "x");
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var w = new CompareWindow(); w.Show();
            string? p1 = w.LoadFile(a, true), p2 = w.LoadFile(b, false); Settle();
            var res = (p1, p2, w.Result!.Key, w.Result.ChangedRows, w.Result.ChangedCells, Added: w.Result.Added.ToArray(), Shift: w.Result.BiggestShift?.Days,
                Bad: w.LoadFile(junk, true), Other: w.LoadFile(other, true), Missing: w.LoadFile(Path.Combine(dir, "nope.csv"), true), StillThere: w.Result is not null,
                Formula: Texts(w.FindControl<StackPanel>("Out")!).Any(t => t.Contains("300001")));
            w.Close();
            return res;
        }, CancellationToken.None);
        Assert.Null(r.p1); Assert.Null(r.p2);
        Assert.Equal(("ANDA", 2, 2), (r.Key, r.ChangedRows, r.ChangedCells));              // "10/3/2026" and "Oct 3, 2026" are the same day
        Assert.Equal(new[] { "300001" }, r.Added); Assert.Equal(7, r.Shift);
        Assert.NotNull(r.Bad); Assert.NotNull(r.Other); Assert.NotNull(r.Missing);         // each said in words, none thrown
        Assert.StartsWith("broken.xlsx", r.Bad);
        Assert.True(r.StillThere); Assert.True(r.Formula);
    }

    [Fact]
    public async Task A_workbook_is_compared_with_a_CSV_and_the_window_names_the_sheet_that_was_read()
    {
        string dir = Temp.NewDir();
        string a = Path.Combine(dir, "portfolio last week.xlsx"), b = Path.Combine(dir, "portfolio this week.csv");
        TestWorkbook.Create(a, new object?[][]
        {
            new object?[] { "ANDA", "Product", "Goal date", "Open items" },
            new object?[] { "216482", "Metformin", TestWorkbook.Date(2026, 10, 3), 2 },
            new object?[] { "214990", "Metformin ER", TestWorkbook.Date(2026, 8, 15), 0 },
        }, sheetName: "Week 46", hiddenFirst: new[] { "Hidden notes" });
        File.WriteAllText(b, "ANDA,Product,Goal date,Open items\n216482,Metformin,\"Oct 17, 2026\",2\n214990,Metformin ER,8/15/2026,0\n");
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var w = new CompareWindow(); w.Show();
            string? p1 = w.LoadFile(a, true), p2 = w.LoadFile(b, false); Settle();
            var res = (p1, p2, NameA: w.FindControl<TextBlock>("NameA")!.Text, NameB: w.FindControl<TextBlock>("NameB")!.Text, w.Result!.Key, w.Result.ChangedRows, w.Result.ChangedCells, Days: w.Result.BiggestShift?.Days);
            w.Load(FileCompare.SampleA, null); Settle();
            string? afterSample = w.FindControl<TextBlock>("NameA")!.Text;
            w.Close();
            return (res, afterSample);
        }, CancellationToken.None);
        Assert.Null(r.res.p1); Assert.Null(r.res.p2);
        Assert.Equal("portfolio last week.xlsx, sheet “Week 46”: 2 rows, 4 columns", r.res.NameA);       // the visible sheet, and it is named
        Assert.Equal("portfolio this week.csv: 2 rows, 4 columns", r.res.NameB);
        Assert.Equal(("ANDA", 1, 1, 14), (r.res.Key, r.res.ChangedRows, r.res.ChangedCells, r.res.Days));   // a date cell in Excel and a written date are compared as days
        Assert.Equal("Portfolio, Nov 10: 6 rows, 7 columns", r.afterSample);
    }

    [Fact]
    public async Task Every_part_of_a_chart_stands_at_least_3_to_1_against_the_window_in_every_look()
    {
        var low = await _fx.Session.Dispatch(() =>
        {
            static double Lum(Avalonia.Media.Color c)
            {
                static double Ch(byte v) { double x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
                return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
            }
            static double Ratio(Avalonia.Media.Color a, Avalonia.Media.Color b) { double x = Lum(a), y = Lum(b); return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05); }
            var found = new List<string>();
            foreach (var look in Appearances.All)
            {
                ThemeManager.Apply(look.Id);
                var w = new CompareWindow(); w.Show(); w.Load(FileCompare.SampleA, FileCompare.SampleB); Settle();
                var bg = ((Avalonia.Media.ISolidColorBrush)Application.Current!.Resources["C.Bg"]!).Color; bg = new Avalonia.Media.Color(255, bg.R, bg.G, bg.B);
                var track = ((Avalonia.Media.ISolidColorBrush)Application.Current!.Resources["C.Track"]!).Color;
                var pieces = w.FindControl<StackPanel>("Out")!.GetVisualDescendants().OfType<Border>().Where(b => b.Background is Avalonia.Media.ISolidColorBrush && b.Bounds.Height is > 0 and <= 18 && b.Child is null).ToList();
                if (pieces.Count < 8) found.Add($"{look.Id}: only {pieces.Count} chart parts were found");
                // "Same" is an outline, so it differs from its neighbor by more than color; the outline itself must stand out too.
                var outlines = w.FindControl<StackPanel>("Out")!.GetVisualDescendants().OfType<Border>().Where(b => (b.Tag as string) == "outline").ToList();
                if (outlines.Count != 2) found.Add($"{look.Id}: {outlines.Count} outlined parts (the bar and its legend make 2)");
                foreach (var o in outlines)
                {
                    double ratio = Ratio(((Avalonia.Media.ISolidColorBrush)o.BorderBrush!).Color, bg);
                    if (ratio < 3 || o.BorderThickness.Left < 2 || o.Background is not null) found.Add($"{look.Id}: the outline of “Same” is {ratio:0.00} to 1, {o.BorderThickness.Left} thick");
                }
                foreach (var p in pieces)
                {
                    var c = ((Avalonia.Media.ISolidColorBrush)p.Background!).Color;
                    if (c == track) continue;                                    // the empty part of a track is not information
                    if (p.Opacity < 1) found.Add($"{look.Id}: a chart part is drawn at {p.Opacity:0.00} strength");
                    double ratio = Ratio(c, bg);
                    if (ratio < 3) found.Add($"{look.Id}: {c} on {bg} is {ratio:0.00} to 1");
                }
                w.Close();
            }
            ThemeManager.Apply(Appearances.Default);
            return found;
        }, CancellationToken.None);
        Assert.True(low.Count() == 0, string.Join("\n", low));
    }

    [Fact]
    public async Task A_pair_of_files_is_counted_once_however_often_it_is_looked_at()
    {
        string log = Path.Combine(Temp.NewDir(), "usage.csv");
        try
        {
            UsageLog.Configure(true, log);
            await _fx.Session.Dispatch(() =>
            {
                ThemeManager.Apply(Appearances.Default);
                var w = new CompareWindow(); w.Show();
                w.Load(FileCompare.SampleA, null); Settle();                      // one file: nothing compared, nothing counted
                w.Load(null, FileCompare.SampleB); Settle();                      // a pair: counted
                var box = w.FindControl<ComboBox>("KeyBox")!;
                box.SelectedIndex = 2; Settle(); box.SelectedIndex = 0; Settle(); box.SelectedIndex = 3; Settle();      // three more looks at the same pair
                w.Load(FileCompare.SampleB, FileCompare.SampleA); Settle();       // another pair: counted
                w.Close();
            }, CancellationToken.None);
            var lines = File.Exists(log) ? File.ReadAllLines(log) : Array.Empty<string>();
            Assert.Equal(2, lines.Count(l => l.Split(',').ElementAtOrDefault(1) == "compare_files"));
            Assert.All(lines, l => Assert.Matches(@"^(date,event,seconds|\d{4}-\d{2}-\d{2},[a-z_]+,[0-9.]*)$", l));      // a date, an event name, nothing else
        }
        finally { UsageLog.Configure(false); }
    }

    /// <summary>Waits, inside the UI session, for background work to come back: the same pattern the other window tests use.</summary>
    private static async Task WaitFor(Func<bool> condition, string what, Func<string> state)
    {
        for (int i = 0; i < 600 && !condition(); i++) { await Task.Delay(15); Settle(); }
        Assert.True(condition(), "waited for: " + what + " (saw: " + state() + ")");
    }

    [Fact]
    public async Task Large_files_are_read_and_compared_off_the_UI_thread_and_a_copied_report_is_safe_to_paste()
    {
        string dir = Temp.NewDir();
        string a = Path.Combine(dir, "big earlier.csv"), b = Path.Combine(dir, "big later.csv");
        const int rows = 1500;                                                  // 3,000 rows together: above CompareWindow.SmallRows
        File.WriteAllLines(a, new[] { "Id,Value,Label", "=1+1,old,Lx" }.Concat(Enumerable.Range(0, rows).Select(i => $"K{i},{i},L{i}")));
        File.WriteAllLines(b, new[] { "Id,Value,Label", "=1+1,new,Lx" }.Concat(Enumerable.Range(0, rows).Select(i => $"K{i},{(i % 2 == 0 ? i + 1 : i)},L{i}")));
        Assert.True(2 * rows > CompareWindow.SmallRows);
        var copied = new List<string>();
        var r = await _fx.Session.Dispatch(async () =>
        {
            ThemeManager.Apply(Appearances.Default);
            var w = new CompareWindow { WriteClipboard = t => { copied.Add(t); return Task.CompletedTask; }, PickFile = earlier => Task.FromResult<string?>(earlier ? a : b) };
            w.Show();
            Click(w.FindControl<Button>("PickA")!); Settle();
            await WaitFor(() => w.StatusText == "big earlier.csv was read on this PC. Nothing was sent.", "the earlier file", () => w.StatusText);
            bool nothingYet = w.Result is null;
            Click(w.FindControl<Button>("PickB")!); Settle();
            await WaitFor(() => w.Result is not null && w.StatusText == "big later.csv was read on this PC. Nothing was sent.", "the comparison", () => w.StatusText);
            var first = (w.Result!.Key, w.Result.ChangedRows, w.Result.Same);
            bool buttons = w.FindControl<Button>("PickA")!.IsEnabled && w.FindControl<Button>("PickB")!.IsEnabled && w.FindControl<ComboBox>("KeyBox")!.IsEnabled && w.FindControl<Button>("SampleBtn")!.IsEnabled;
            Click(w.FindControl<Button>("CopyAllBtn")!); Settle();
            w.FindControl<ComboBox>("KeyBox")!.SelectedIndex = 3; Settle();          // match by "Label" instead: compared off the UI thread again
            await WaitFor(() => w.StatusText == "Rows are now matched by “Label”.", "the other column", () => w.StatusText);
            var second = (w.Result!.Key, w.Result.KeyWhy);
            w.Close();
            return (nothingYet, first, buttons, second);
        }, CancellationToken.None);
        Assert.True(r.nothingYet);
        Assert.Equal(("Id", rows / 2 + 1, rows / 2), r.first);
        Assert.True(r.buttons);
        Assert.Equal(("Label", "chosen by you"), r.second);
        string report = Assert.Single(copied);
        Assert.Contains("\n'=1+1: Value: old → new", report);                      // the row named like a formula is pasted as text
        Assert.DoesNotContain(report.Split('\n'), l => l.Length > 0 && "=+-@".Contains(l[0]));
    }

    // ---------------------------------------------------------------- Ask about a selection

    private static AskHooks Hooks(List<CustomAction> ran, List<(string, string)> notes, List<string> invitations, bool noteOk = true) =>
        new(() => new List<CustomAction>(), (a, _) => ran.Add(a), _ => { }, _ => { })
        {
            KnownAndas = t => Proofreader.KnownIds(t, Repo.Find),
            AddNote = (id, text) => { notes.Add((id, text)); return noteOk ? (true, "Note saved.") : (false, "This ANDA already has 50 quick notes. Delete one to add another."); },
            Today = () => new DateOnly(2026, 7, 16),
            SaveInvitation = ics => { invitations.Add(ics); return Task.FromResult(true); },
        };

    [Fact]
    public async Task Four_reading_prompts_are_offered_and_each_goes_through_the_preview()
    {
        var ran = new List<CustomAction>();
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var w = new AskWindow(Hooks(ran, new(), new()), "The applicant asked to move the meeting to Friday."); w.Show(); Settle();
            var buttons = w.FindControl<WrapPanel>("ReadButtons")!.Children.OfType<Button>().ToList();
            foreach (var b in buttons) Click(b); Settle();
            var res = (Names: buttons.Select(b => (string)b.Content!).ToArray(), Status: w.FindControl<TextBlock>("Status")!.Text);
            w.Close();
            return res;
        }, CancellationToken.None);
        Assert.Equal(new[] { "Give me the TL;DR", "What else should I know?", "Onboard me on this topic", "Why does it matter?" }, r.Names);
        Assert.Equal(4, ran.Count);
        Assert.All(ran, a => { Assert.Equal(ActionKind.AskAi, a.Kind); Assert.Contains("{selection}", a.Template); Assert.False(a.SendWithoutPreview); });
        Assert.Equal("A window with exactly what would be sent is open. Nothing has been sent.", r.Status);
    }

    [Fact]
    public async Task A_selection_with_a_date_offers_an_invitation_and_one_that_names_an_ANDA_offers_a_note()
    {
        var notes = new List<(string, string)>(); var invitations = new List<string>();
        var r = await _fx.Session.Dispatch(async () =>
        {
            ThemeManager.Apply(Appearances.Default);
            var w = new AskWindow(Hooks(new(), notes, invitations), "ANDA 216482: call the applicant about the IR response on Nov 20, then file the memo."); w.Show(); Settle();
            var more = w.FindControl<WrapPanel>("MoreButtons")!.Children.OfType<Button>().ToList();
            var first = (w.FindControl<Control>("MorePanel")!.IsVisible, w.FindControl<TextBlock>("InviteHint")!.Text, Labels: more.Select(b => (string)b.Content!).ToArray());
            Click(more.First(b => (string)b.Tag! == "note:216482")); Settle();
            string? noteStatus = w.FindControl<TextBlock>("Status")!.Text;
            await w.SaveInvitationAsync(); Settle();
            string? inviteStatus = w.FindControl<TextBlock>("Status")!.Text;
            w.FindControl<TextBox>("SelectionBox")!.Text = "Version 3/4 of the plan for ANDA 999999, ratio 10/12."; w.Analyse(); Settle();
            bool none = w.FindControl<Control>("MorePanel")!.IsVisible;
            w.FindControl<TextBox>("SelectionBox")!.Text = "Lunch on Dec 3."; w.Analyse(); Settle();
            var dateOnly = (w.FindControl<Control>("MorePanel")!.IsVisible, w.FindControl<WrapPanel>("MoreButtons")!.Children.Count);
            w.Close();
            return (first, noteStatus, inviteStatus, none, dateOnly);
        }, CancellationToken.None);
        Assert.True(r.first.IsVisible);
        Assert.Equal("A date was found: Friday, Nov 20, 2026. The invitation would read “ANDA 216482: call the applicant about the IR response on Nov 20, then file the…”.", r.first.Text);
        Assert.Equal(new[] { "Save a calendar invitation…", "Add to the notes of ANDA 216482" }, r.first.Labels);
        Assert.Equal(("216482", "ANDA 216482: call the applicant about the IR response on Nov 20, then file the memo."), Assert.Single(notes));
        Assert.Equal("Added to your notes on ANDA 216482. Open the card and press N to see it.", r.noteStatus);
        Assert.Equal("An all-day event for Friday, Nov 20, 2026 was saved. Open the file to add it to your calendar. Nothing was sent, and nothing was added for you.", r.inviteStatus);
        string ics = Assert.Single(invitations);
        Assert.StartsWith("BEGIN:VCALENDAR\r\n", ics); Assert.Contains("DTSTART;VALUE=DATE:20261120\r\n", ics); Assert.EndsWith("END:VCALENDAR\r\n", ics);
        Assert.False(r.none);                                                            // a fraction is not a date, and an unknown number has no notes
        Assert.Equal((true, 1), r.dateOnly);
    }

    [Fact]
    public async Task A_note_that_cannot_be_added_says_why_and_a_long_one_says_it_was_cut()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            string longText = "ANDA 216482 " + new string('x', 700);
            var full = new AskWindow(Hooks(new(), new(), new(), noteOk: false), "ANDA 216482 needs a call."); full.Show(); Settle();
            Click(full.FindControl<WrapPanel>("MoreButtons")!.Children.OfType<Button>().Single()); Settle();
            string? refused = full.FindControl<TextBlock>("Status")!.Text; full.Close();
            var cut = new AskWindow(Hooks(new(), new(), new()), longText); cut.Show(); Settle();
            Click(cut.FindControl<WrapPanel>("MoreButtons")!.Children.OfType<Button>().Single()); Settle();
            string? cutStatus = cut.FindControl<TextBlock>("Status")!.Text; cut.Close();
            var plain = new AskWindow(new AskHooks(() => new List<CustomAction>(), (_, _) => { }, _ => { }, _ => { }), "ANDA 216482 on Dec 3, 2026"); plain.Show(); Settle();
            int withoutHooks = plain.FindControl<WrapPanel>("MoreButtons")!.Children.Count; plain.Close();
            return (refused, cutStatus, withoutHooks);
        }, CancellationToken.None);
        Assert.Equal("This ANDA already has 50 quick notes. Delete one to add another.", r.refused);
        Assert.Equal("Added to your notes on ANDA 216482. Only the first 500 characters were kept. Open the card and press N to see it.", r.cutStatus);
        Assert.Equal(1, r.withoutHooks);                                                 // with no notes hook, only the invitation is offered
    }

    // ---------------------------------------------------------------- My usage

    [Fact]
    public void The_usage_log_is_read_as_counts_and_nothing_else_can_come_out_of_it()
    {
        string log = Path.Combine(Temp.NewDir(), "usage.csv");
        File.WriteAllLines(log, new[]
        {
            "date,event,seconds", "2026-11-18,card_open,2.5", "2026-11-18,card_open,", "2026-11-17,compare_files,", "2026-11-17,ANDA 216482 is late,", "not a date,card_open,", "2026-11-18", "",
            "2026-11-18,reading_prompt,", "2026-10-25,ask_open,", "2026-11-18,<script>,", "2026-11-18,note_added,",
        });
        var lines = UsageSummary.Lines(UsageSummary.Read(log), new DateOnly(2026, 11, 18));
        Assert.Equal(new[]
        {
            "My usage: last 7 days (last 30 days)", "Cards opened: 2 (2)", "Selections asked about: 0 (1)", "File comparisons: 1 (1)", "Reading prompts used: 1 (1)", "Notes added from a selection: 1 (1)",
            "Counts only. No ANDA number, name or text is ever recorded. Kept on this PC, and shared only if you copy it.",
        }, lines);
        Assert.DoesNotContain(lines, l => l.Contains("216482") || l.Contains("script"));
        Assert.Equal("Nothing has been counted yet.", UsageSummary.Lines(UsageSummary.Read(Path.Combine(Temp.NewDir(), "none.csv")), new DateOnly(2026, 11, 18))[1]);
        Assert.Equal("Nothing has been counted yet.", UsageSummary.Lines(UsageSummary.Read(null), new DateOnly(2026, 11, 18))[1]);
    }

    [Fact]
    public async Task The_usage_window_shows_the_summary_and_copies_it_only_when_asked()
    {
        string log = Path.Combine(Temp.NewDir(), "usage.csv");
        File.WriteAllLines(log, new[] { "date,event,seconds", "2026-11-18,draft_check,", "2026-11-18,draft_check," });
        var copied = new List<string>();
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var w = new UsageWindow(log, enabled: false, today: new DateOnly(2026, 11, 18)) { WriteClipboard = t => { copied.Add(t); return Task.CompletedTask; } }; w.Show(); Settle();
            var before = (w.Summary.Text, w.Note.Text, copied.Count);
            Click(w.CopyButton); Settle();
            var res = (before, After: w.Note.Text);
            w.Close();
            return res;
        }, CancellationToken.None);
        Assert.Contains("Drafts reviewed: 2 (2)", r.before.Item1);
        Assert.StartsWith("Counting is off", r.before.Item2); Assert.Equal(0, r.before.Count);
        Assert.Equal("Summary copied.", r.After); Assert.Single(copied);
    }
}
