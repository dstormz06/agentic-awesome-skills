using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
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
/// Round 25: the rules behind Proofread, the extra fact checks, the recall row and the one question before sending, and the Review window
/// that shows them. The answers themselves are pinned to the simulation's in <see cref="Round25ProofreadTests"/>; these tests cover what
/// the shared cases cannot: the app's own data shapes, the limits, and the window.
/// </summary>
[Collection("Global state")]
public class Round25UiTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public Round25UiTests(HeadlessFixture fx) => _fx = fx;
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static readonly MockAndaRepository Repo = new();
    private static readonly DateOnly Today = new(2026, 7, 16);
    private static PromptLibraryService Prompts() => new(Path.Combine(Temp.NewDir(), "p.json"));
    private static string[] Texts(Control w) => w.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static IReadOnlyList<DraftNote> Notes(AndaRecord r) => DraftNotes.For(r, null, Today);

    private static AndaRecord Plain(string number, string drug, string? strength, string meta, params NoteItem[] notes) => new()
    {
        Number = number, Drug = drug, Meta = meta, GoalDate = "Oct 3, 2026", GoalDays = "", ProgressPct = -1, ForecastDate = AndaRecord.Unknown, ForecastDelta = "",
        ForecastLate = false, ForecastConfidence = "", Latest = "", Pending = Array.Empty<PendingItem>(), Notes = notes, ForecastDetail = "", Sisters = Array.Empty<SisterRef>(), Strength = strength,
    };

    // ---------------------------------------------------------------- the rules, beyond the shared cases

    [Fact]
    public void A_pattern_is_read_as_the_simulation_reads_it()
    {
        Assert.True(JsRegex.Make(@"\bto\b").IsMatch("é to é"));
        Assert.True(JsRegex.Make(@"\bto\b").IsMatch("éto"));                       // in JavaScript an accented letter is not a word character, so "to" starts at a boundary
        Assert.False(JsRegex.Make(@"\d").IsMatch("٣"));                            // an Arabic-Indic digit is not a digit there either
        Assert.True(JsRegex.Make(@"a\sb").IsMatch("a b"));                    // a no-break space is white space
        Assert.False(JsRegex.Make(@"end$").IsMatch("end\n"));                      // "$" is the end of the text, not the end of a line
        Assert.Equal("a b", JsRegex.Squash("  a \t\n b  "));
        Assert.Equal(60, JsRegex.Squash(new string('x', 200)).Length);
        Assert.Equal(3, JsRegex.WordCount("  one two \n three "));
        Assert.Equal("", JsRegex.Slice("abc", 5, 9));
        Assert.Equal("bc", JsRegex.Slice("abc", 1, 99));
    }

    [Fact]
    public void An_empty_draft_gives_nothing_and_a_very_long_one_is_answered_quickly()
    {
        foreach (string? empty in new[] { null, "", "   ", "\r\n\t", " " })
        {
            var r = Proofreader.Check(empty, Repo.Find);
            Assert.Empty(r.Items); Assert.Equal(0, r.Fixes); Assert.Equal(empty ?? "", r.Corrected);
            Assert.Empty(FactExtras.Check(empty, Repo.Find, Notes, Today).Differences);
            Assert.Empty(Recall.For(empty, Repo.Find, Notes));
        }
        string big = string.Concat(Enumerable.Repeat("The applicant will submit the responce tommorow ,and i think its a good plan for ANDA 216482 (750 mg). ", 2500));     // about 260,000 characters
        var clock = Stopwatch.StartNew();
        var pr = Proofreader.Check(big, Repo.Find);
        var fx = FactExtras.Check(big, Repo.Find, Notes, Today);
        var rc = Recall.For(big, Repo.Find, Notes);
        clock.Stop();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), "took " + clock.Elapsed);
        Assert.True(pr.Items.Count <= Proofreader.MaxItems);
        Assert.Single(fx.Differences);                                              // one line for the strength, however often it is said
        Assert.Single(rc);
    }

    [Fact]
    public void The_corrected_draft_is_the_draft_with_exactly_the_marked_changes()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(GoldenFixture.ExtensionDir(), "..", "mockup", "proofread-stress-golden.json")));
        int n = 0, withFixes = 0;
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            string draft = c.GetProperty("draft").GetString()!;
            var r = Proofreader.Check(draft, Repo.Find);
            var sb = new System.Text.StringBuilder(); int at = 0;
            foreach (var k in r.Marks)
            {
                Assert.True(k.Start >= at && k.End >= k.Start && k.End <= draft.Length, "marks are in order and do not overlap");
                sb.Append(draft, at, k.Start - at).Append(k.To); at = k.End;
            }
            sb.Append(draft, at, draft.Length - at);
            Assert.Equal(r.Corrected, sb.ToString());
            Assert.Equal(r.Fixes, r.Marks.Count);
            n++; if (r.Fixes > 0) withFixes++;
        }
        Assert.True(n > 300 && withFixes > 200, $"{n} cases, {withFixes} with fixes");
    }

    [Fact]
    public void Proofreading_twice_changes_nothing_more_in_the_shared_drafts()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(GoldenFixture.ExtensionDir(), "..", "mockup", "proofread-golden.json")));
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            string once = Proofreader.Check(c.GetProperty("draft").GetString(), Repo.Find).Corrected;
            string twice = Proofreader.Check(once, Repo.Find).Corrected, thrice = Proofreader.Check(twice, Repo.Find).Corrected;
            Assert.Equal(twice, thrice);          // a second pass may still find something the first uncovered; a third never does
        }
    }

    [Fact]
    public void A_slip_in_a_name_from_the_persons_data_is_seen_and_markup_is_only_ever_text()
    {
        var r = Proofreader.Check("ANDA 216482: Meridan sent the metformin hydrocloride data.", Repo.Find);
        Assert.Contains(r.Items, i => i.Text == "“Meridan” looks like “Meridian”, a name in your data for ANDA 216482." && i.Fixable);
        Assert.Contains(r.Items, i => i.Text == "“hydrocloride” looks like “hydrochloride”, a name in your data for ANDA 216482.");
        Assert.Contains("Meridian sent the metformin hydrochloride data.", r.Corrected);
        Assert.DoesNotContain(Proofreader.Check("Meridan sent it.", Repo.Find).Items, i => i.Text.Contains("Meridian"));       // no ANDA named: no names to compare with

        var odd = Proofreader.Check("<script>alert(1)</script> ANDA 216482 ,ok <b>i</b>", Repo.Find);
        Assert.All(odd.Items, i => Assert.DoesNotContain("\n", i.Text));
        Assert.Contains("<script>alert(1)</script>", odd.Corrected);                 // kept as it was typed; nothing interprets it
    }

    [Fact]
    public void The_due_date_of_the_open_item_is_read_from_the_record()
    {
        Assert.Equal((new DateOnly(2026, 7, 21), "IR response"), FactExtras.OpenItemDue(Repo.Find("216482")!, Today));
        Assert.Null(FactExtras.OpenItemDue(Repo.Find("214990")!, Today));              // no open item
        var r = Plain("300001", "Example Tablets", "10 MG", "Example Co · Cycle 1") with
        {
            Pending = new[] { new PendingItem("Labeling review", "in queue", false), new PendingItem("Response — Team", "due Jan 5", true), new PendingItem("Later", "due Mar 1, 2027", false) },
            AsOfDate = new DateOnly(2026, 12, 20),
        };
        Assert.Equal((new DateOnly(2027, 1, 5), "Response"), FactExtras.OpenItemDue(r, new DateOnly(2026, 12, 21)));     // the nearest Jan 5 to the day the data was taken
        Assert.Null(FactExtras.OpenItemDue(r with { Pending = new[] { new PendingItem("x", "due Feb 30", false), new PendingItem("y", "due soon", false) } }, Today));
        Assert.Equal((new DateOnly(2028, 2, 29), "open item"), FactExtras.OpenItemDue(r with { Pending = new[] { new PendingItem(" — Team", "due Feb 29, 2028", false) } }, Today));
    }

    [Fact]
    public void Strength_cycle_and_notes_are_compared_with_the_apps_own_record_shapes()
    {
        var r = Plain("300001", "Example Tablets", "10 MG", "Example Co · Cycle 2", new NoteItem("Your sheet", "Applicant call set for Aug 4."), new NoteItem("notes.txt · Jul 9", "Second call on 8/4 as well."));
        AndaRecord? Find(string n) => n == "300001" ? r : null;
        IReadOnlyList<DraftNote> NotesOf(AndaRecord x) => DraftNotes.For(x, null, Today);

        var a = FactExtras.Check("ANDA 300001 is 20 mg, in cycle 0, with a call on Aug 4.", Find, NotesOf, Today);
        Assert.Equal(new[]
        {
            "ANDA 300001: the draft says 20 mg, but your data says Example Tablets, 10 MG.",       // the strength is a column of its own here, so it is named
            "ANDA 300001: the draft says cycle 0, but your data says cycle 2.",
        }, a.Differences);
        Assert.Equal(new[]
        {
            "“Aug 4” is also in your note of Jul 9 on ANDA 300001: “Second call on 8/4 as well.”",
            "“Aug 4” is also in your notes (Your sheet) on ANDA 300001: “Applicant call set for Aug 4.”",
        }, a.Notes);
        Assert.Null(a.AlsoChecked);

        var b = FactExtras.Check("ANDA 300001 is 10 mg and in its second cycle.", Find, NotesOf, Today);
        Assert.Empty(b.Differences);
        Assert.Equal("Also checked 1 strength and 1 review cycle against your sheet: no difference.", b.AlsoChecked);

        var none = Plain("300002", "Other Capsules", null, "Other Co");
        Assert.Empty(FactExtras.Check("ANDA 300002 is 5 mg in cycle 3, due Jul 1.", n => n == "300002" ? none : null, NotesOf, Today).Differences);     // the record says nothing about these: nothing is claimed
        Assert.Equal(new[] { "“2/30/2027” is not a date: February 2027 has 28 days.", "“Feb 30” is not a date: February has at most 29 days." },
            FactExtras.Check("Sent 2/30/2027 and again Feb 30.", Find, NotesOf, Today).Differences);
    }

    [Fact]
    public void Notes_are_put_in_order_of_time_and_a_note_without_a_date_comes_last()
    {
        Assert.Equal(new DateTime(2026, 7, 8), DateTime.UnixEpoch.AddMilliseconds(DraftNotes.NoteTime("You · Jul 8", Today)));
        Assert.Equal(new DateTime(2025, 12, 30), DateTime.UnixEpoch.AddMilliseconds(DraftNotes.NoteTime("You · Dec 30", Today)));       // a note has no year: it is the most recent such day
        Assert.Equal(new DateTime(2026, 7, 17), DateTime.UnixEpoch.AddMilliseconds(DraftNotes.NoteTime("You · Jul 17", Today)));        // tomorrow is allowed for a clock that is a little off
        Assert.Equal(0, DraftNotes.NoteTime("Your sheet", Today));
        Assert.Equal(0, DraftNotes.NoteTime(null, Today));
        using var zone = new ZoneScope("America/New_York");
        var quick = new QuickNotesService(Path.Combine(Temp.NewDir(), "q.json"), () => new DateTime(2026, 7, 16, 13, 5, 0, DateTimeKind.Utc));
        quick.Add("216482", "Mine, today.");
        var notes = DraftNotes.For(Repo.Find("216482")!, quick, Today);
        Assert.Equal(new[] { "You · Jul 16, 2026, 9:05 AM EDT", "You · Jul 8", "You · Jun 30", "Team lead · Jun 12" }, notes.Select(n => n.Who));
        Assert.True(notes[0].Mine); Assert.Equal("Jul 16, 2026, 9:05 AM EDT", notes[0].When);
    }

    [Fact]
    public void Recall_lists_open_items_from_the_record_and_both_to_do_lists_for_the_first_three_ANDAs()
    {
        var todo = new[]
        {
            new TodoItem { Title = "Ask about the site", Number = "216482", Due = "2026-07-20" }, new TodoItem { Title = "Done already", Number = "216482", Done = true },
            new TodoItem { Title = "Another ANDA", Number = "214990" },
        };
        var mine = new[] { new MyTodoItem { Title = "Send 216482 status to the team lead" }, new MyTodoItem { Title = "216482 old", Done = true }, new MyTodoItem { Title = "Timesheet" } };
        var g = Recall.For("Status of ANDA 216482 and the IR response.", Repo.Find, Notes, todo, mine);
        var only = Assert.Single(g);
        Assert.Equal(new[]
        {
            "IR response — Biopharmaceutics | due Jul 21 | True | your data", "Labeling carve-out review | in queue | False | your data",
            "Ask about the site | due Jul 20, 2026 | False | ANDA to-do list", "Send 216482 status to the team lead |  | False | My to-do list",
        }, only.Items.Select(i => $"{i.Title} | {i.Due} | {i.Urgent} | {i.Source}"));
        Assert.Equal("Jul 8", only.Latest);
        Assert.Equal("You · Jul 8", only.Notes[0].Note.Who);                            // the note the draft touches ("response") comes first
        Assert.Contains("response", only.Notes[0].Shared);

        var four = new Dictionary<string, AndaRecord> { ["300001"] = Plain("300001", "A", null, "x"), ["300002"] = Plain("300002", "B", null, "x"), ["300003"] = Plain("300003", "C", null, "x"), ["300004"] = Plain("300004", "D", null, "x") };
        Assert.Equal(new[] { "300003", "300001", "300004" }, Recall.For("300003, 300001, 300004, 300002 and 999999.", n => four.GetValueOrDefault(n), Notes).Select(x => x.Number));
    }

    [Fact]
    public void One_question_is_chosen_in_a_fixed_order()
    {
        var cal = new BusinessCalendar();
        SendQuestion? Ask(string draft) => BeforeSend.Ask(draft, DraftReview.Check(draft, Repo.Find, Today, cal), FactExtras.Check(draft, Repo.Find, Notes, Today));
        Assert.Null(Ask("")); Assert.Null(Ask("   "));
        Assert.Equal(new SendQuestion(true, "For ANDA 216482, which goal date is right: Oct 10, 2026 (your draft) or Oct 3, 2026 (the record)?"), Ask(DraftWindow.SampleDraft));
        Assert.Equal(new SendQuestion(true, "Is ANDA 216482 really on track? The record's forecast is 14 days later than the goal."), Ask("ANDA 216482 is on track. ANDA 123456 too."));
        Assert.Equal(new SendQuestion(true, "In “Friday, July 16, 2026”, which is right: the day or the date? July 16, 2026 is a Thursday."), Ask("We meet Friday, July 16, 2026."));
        Assert.Equal(new SendQuestion(true, "Is 123456 the right ANDA number? It is not in your data."), Ask("ANDA 123456 needs a call."));
        Assert.Equal(new SendQuestion(true, "Is this difference intended? ANDA 216482: the draft says 750 mg, but your data says Metformin HCl ER Tablets, 500 mg."), Ask("ANDA 216482 is 750 mg."));
        Assert.StartsWith("Is this intended? ", Ask("Please reply by Jul 18, 2026 about ANDA 216482.")!.Text);                    // a Saturday
        Assert.Equal(new SendQuestion(true, "Is the file attached? The draft mentions an attachment, and this tool cannot see attachments."), Ask("The memo for ANDA 216482 is attached."));
        Assert.Equal(new SendQuestion(false, "No open gaps."), Ask("ANDA 216482 was discussed."));
    }

    // ---------------------------------------------------------------- the window

    private DraftWindow Open(string draft, Func<string, AndaRecord?>? lookup = null, IChatCompleter? ai = null, QuickNotesService? quick = null, Action<string>? openCard = null)
    {
        ThemeManager.Apply(Appearances.Default);
        var w = new DraftWindow(draft, lookup ?? Repo.Find, new WritingService(ai), new Settings(), Prompts(), () => Today, new BusinessCalendar()) { QuickNotes = quick, OpenCard = openCard };
        w.Show(); Settle();
        return w;
    }

    [Fact]
    public async Task The_window_shows_facts_wording_the_question_and_recall_at_once()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            var w = Open(DraftWindow.SampleDraft);
            var texts = Texts(w);
            var res = (Summary: w.FindControl<TextBlock>("SummaryText")!.Text, Texts: texts,
                Question: w.FindControl<TextBlock>("SendQuestionText")!.Text, SendVisible: w.FindControl<Border>("SendBox")!.IsVisible,
                Fix: w.FindControl<Control>("FixPanel")!.IsVisible, FixStatus: w.FindControl<TextBlock>("FixStatus")!.Text,
                Runs: w.FindControl<SelectableTextBlock>("MarkedText")!.Inlines!.OfType<Run>().Select(x => (x.Text, Struck: x.TextDecorations == TextDecorations.Strikethrough, Under: x.TextDecorations == TextDecorations.Underline)).ToList(),
                Recall: w.FindControl<StackPanel>("RecallPanel")!.Children.OfType<Expander>().Select(e => ((TextBlock)e.Header!).Text).ToList(),
                AiOpen: w.FindControl<Expander>("AiExpander")!.IsExpanded, AiHead: w.FindControl<TextBlock>("AiHeader")!.Text, Sample: w.FindControl<Button>("SampleBtn")!.IsVisible,
                Last: w.RunCheck().Findings[^1].Text);
            w.Close();
            return res;
        }, CancellationToken.None);
        Assert.Equal("7 differences from your data · 1 wording item · 1 can be fixed for you", r.Summary);
        Assert.Contains("“responce” is usually spelled “response”.", r.Texts);
        Assert.Contains("ANDA 216482: the draft says cycle 3, but your data says cycle 2.", r.Texts);
        Assert.Contains("ANDA 216482: the draft gives a due date of Jul 28, but your data says the IR response is due Jul 21, 2026.", r.Texts);
        Assert.Contains(DraftReview.ReceiptNoteWithProofreading, r.Texts);            // the window says what Proofreading now covers
        Assert.DoesNotContain(DraftReview.ReceiptNote, r.Texts);
        Assert.Equal(DraftReview.ReceiptNote, r.Last);                                // the shared result is unchanged, so the extension and the simulation still agree with it
        Assert.True(r.SendVisible);
        Assert.Equal("For ANDA 216482, which goal date is right: Oct 10, 2026 (your draft) or Oct 3, 2026 (the record)?", r.Question);
        Assert.True(r.Fix); Assert.Equal("1 change can be made without guessing.", r.FixStatus);
        Assert.Contains(("responce", true, false), r.Runs); Assert.Contains(("response", false, true), r.Runs);
        Assert.Equal(DraftWindow.SampleDraft, string.Concat(r.Runs.Where(x => !x.Under).Select(x => x.Text)));       // what is not underlined is the draft, whole
        Assert.Equal(new[] { "ANDA 216482 · 3 notes (latest Jul 8) · 2 open items", "ANDA 214990 · 2 notes (latest Jul 2) · no open items" }, r.Recall);
        Assert.False(r.AiOpen); Assert.Equal("AI options · off", r.AiHead); Assert.True(r.Sample);
    }

    [Fact]
    public async Task Proofread_and_Fact_check_each_show_their_own_part_and_say_which_is_selected()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            var w = Open("The the plan for ANDA 216482 is TBD, and teh applicant agreed.");
            (bool Facts, bool Proof, bool Send, string? Summary, bool[] Primary, string?[] Names, bool Twice) Snap() => (
                w.FindControl<Control>("FactWrap")!.IsVisible, w.FindControl<Control>("ProofWrap")!.IsVisible, w.FindControl<Control>("SendBox")!.IsVisible, w.FindControl<TextBlock>("SummaryText")!.Text,
                new[] { "ViewAllBtn", "ViewProofBtn", "ViewFactBtn" }.Select(n => w.FindControl<Button>(n)!.Classes.Contains("primary")).ToArray(),
                new[] { "ViewAllBtn", "ViewProofBtn", "ViewFactBtn" }.Select(n => AutomationProperties.GetName(w.FindControl<Button>(n)!)).ToArray(),
                Texts(w.FindControl<StackPanel>("ProofPanel")!).Any(t => t.StartsWith("The word “the” appears twice")));
            var all = Snap();
            Click(w.FindControl<Button>("ViewProofBtn")!); Settle(); var proof = Snap();
            Click(w.FindControl<Button>("ViewFactBtn")!); Settle(); var facts = Snap();
            Click(w.FindControl<Button>("ViewAllBtn")!); Settle(); var back = Snap();
            w.Close();
            return (all, proof, facts, back);
        }, CancellationToken.None);
        Assert.True(r.all.Facts && r.all.Proof && r.all.Send);
        Assert.Equal("1 difference from your data · 3 wording items · 2 can be fixed for you", r.all.Summary);
        Assert.False(r.all.Twice);                                                     // Fact-check already reports the repeated word; it is not said twice
        Assert.Equal(new[] { true, false, false }, r.all.Primary);
        Assert.False(r.proof.Facts); Assert.True(r.proof.Proof); Assert.False(r.proof.Send);
        Assert.Equal("3 wording items · 2 can be fixed for you", r.proof.Summary);
        Assert.True(r.proof.Twice);                                                    // alone, Proofread lists it
        Assert.Equal(new[] { false, true, false }, r.proof.Primary);
        Assert.Equal(new[] { "Check everything", "Proofread, selected", "Fact-check" }, r.proof.Names);
        Assert.True(r.facts.Facts); Assert.False(r.facts.Proof); Assert.True(r.facts.Send);
        Assert.Equal("1 difference from your data", r.facts.Summary);
        Assert.Equal(r.all, r.back with { Names = r.all.Names, Primary = r.all.Primary });
        Assert.Equal(new[] { "Check everything, selected", "Proofread", "Fact-check" }, r.back.Names);
    }

    [Fact]
    public async Task Apply_here_changes_only_the_apps_own_box_and_Copy_says_what_it_did()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            var w = Open("i recieved teh responce for anda 216482 .Thanks");
            string corrected = w.Proof.Corrected; int fixes = w.Proof.Fixes;
            Click(w.FindControl<Button>("CopyFixedBtn")!); Settle();
            string? afterCopy = w.FindControl<TextBlock>("FixStatus")!.Text, boxAfterCopy = w.FindControl<TextBox>("DraftBox")!.Text;
            Click(w.FindControl<Button>("ApplyFixedBtn")!); Settle();
            var res = (corrected, fixes, afterCopy, boxAfterCopy, Box: w.FindControl<TextBox>("DraftBox")!.Text, Fix: w.FindControl<Control>("FixPanel")!.IsVisible,
                Summary: w.FindControl<TextBlock>("SummaryText")!.Text, Nothing: Texts(w).Contains(DraftWindow.NothingFound));
            w.Close();
            return res;
        }, CancellationToken.None);
        Assert.Equal("I received the response for ANDA 216482. Thanks", r.corrected);
        Assert.Equal(6, r.fixes);
        Assert.Equal("Corrected draft copied. Your own text was not changed.", r.afterCopy);
        Assert.Equal("i recieved teh responce for anda 216482 .Thanks", r.boxAfterCopy);     // copying never changes the box
        Assert.Equal(r.corrected, r.Box);
        Assert.False(r.Fix); Assert.True(r.Nothing);
        Assert.Equal("No difference from your data · no wording items", r.Summary);
    }

    [Fact]
    public async Task An_empty_box_says_so_and_a_clean_draft_has_no_open_gaps()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            var w = Open("");
            var empty = (Summary: w.FindControl<TextBlock>("SummaryText")!.Text, Send: w.FindControl<Control>("SendBox")!.IsVisible, Recall: w.FindControl<Control>("RecallPanel")!.IsVisible,
                Fine: w.FindControl<Control>("ProofFine")!.IsVisible, Proof: Texts(w.FindControl<StackPanel>("ProofPanel")!));
            w.FindControl<TextBox>("DraftBox")!.Text = "ANDA 216482 was discussed with the team."; w.RunCheck(); Settle();
            var clean = (Question: w.FindControl<TextBlock>("SendQuestionText")!.Text, Fine: w.FindControl<TextBlock>("SendFine")!.Text, Send: w.FindControl<Control>("SendBox")!.IsVisible,
                Recall: w.FindControl<StackPanel>("RecallPanel")!.Children.OfType<Expander>().Count());
            Click(w.FindControl<Button>("SampleBtn")!); Settle();
            string? sample = w.FindControl<TextBox>("DraftBox")!.Text;
            w.Close();
            var real = Open("ANDA 300001 is fine.", n => null);
            bool sampleOffered = real.FindControl<Button>("SampleBtn")!.IsVisible;
            real.Close();
            return (empty, clean, sample, sampleOffered);
        }, CancellationToken.None);
        Assert.StartsWith("Nothing was selected. Select text in any window and press Ctrl+Alt+Shift+R, or paste a draft above.", r.empty.Summary);
        Assert.False(r.empty.Send); Assert.False(r.empty.Recall); Assert.False(r.empty.Fine);
        Assert.Equal(new[] { "ℹ", "Nothing to proofread yet. Select your draft and press the shortcut, or paste it above." }, r.empty.Proof);
        Assert.True(r.clean.Send); Assert.Equal("No open gaps.", r.clean.Question); Assert.Equal(BeforeSend.NoGapNote, r.clean.Fine);
        Assert.Equal(1, r.clean.Recall);
        Assert.Equal(DraftWindow.SampleDraft, r.sample);
        Assert.False(r.sampleOffered);                                                 // the sample draft is about the sample records; with the person's own data it is not offered
    }

    [Fact]
    public async Task Recall_shows_the_persons_own_note_first_and_opens_the_card_that_was_asked_for()
    {
        using var zone = new ZoneScope("America/New_York");
        var quick = new QuickNotesService(Path.Combine(Temp.NewDir(), "q.json"), () => new DateTime(2026, 7, 16, 14, 42, 0, DateTimeKind.Utc));
        quick.Add("216482", "Call with the applicant on Jul 21 about the IR response.");
        var opened = new List<string>();
        var r = await _fx.Session.Dispatch(() =>
        {
            var w = Open("ANDA 216482: the IR response is due Jul 21.", quick: quick, openCard: opened.Add);
            var box = w.FindControl<StackPanel>("RecallPanel")!.Children.OfType<Expander>().Single();
            box.IsExpanded = true; Settle();
            var texts = Texts(box);
            var btn = ((StackPanel)box.Content!).Children.OfType<Button>().Single();
            string? name = AutomationProperties.GetName(btn);
            Click(btn); Settle();
            var res = (Head: ((TextBlock)box.Header!).Text, texts, name, Facts: Texts(w.FindControl<StackPanel>("FindingsPanel")!));
            w.Close();
            return res;
        }, CancellationToken.None);
        Assert.Equal("ANDA 216482 · 4 notes (latest Jul 16, 2026, 10:42 AM EDT) · 2 open items", r.Head);
        Assert.Contains("You · Jul 16, 2026, 10:42 AM EDT · quick note · also in your draft: Jul 21, response", r.texts);
        Assert.Contains("IR response — Biopharmaceutics", r.texts); Assert.Contains("due Jul 21", r.texts);
        Assert.Equal("Open the card of ANDA 216482", r.name);
        Assert.Equal(new[] { "216482" }, opened);
        Assert.Contains("“Jul 21” is also in your note of Jul 16, 2026, 10:42 AM EDT on ANDA 216482: “Call with the applicant on Jul 21 about the IR response.”", r.Facts);
        Assert.Contains("Also checked 1 due date against your sheet: no difference.", r.Facts);
    }
}
