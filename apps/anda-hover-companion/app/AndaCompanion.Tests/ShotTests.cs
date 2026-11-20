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

/// <summary>Pictures of the round-16 windows, for a person to look at. Not run unless ANDA_SHOTS_DIR is set.</summary>
[Collection("Global state")]
public class ShotTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public ShotTests(HeadlessFixture fx) => _fx = fx;

    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }

    private static void Save(Window w, string dir, string name)
    {
        Settle(); Settle();
        w.CaptureRenderedFrame()?.Save(Path.Combine(dir, name + ".png"));
    }

    [ShotFact]
    public async Task Save_pictures_of_the_new_windows()
    {
        string dir = Environment.GetEnvironmentVariable("ANDA_SHOTS_DIR")!;
        Directory.CreateDirectory(dir);
        await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            using var home = new EnvScope(AppPaths.HomeEnvVar, Temp.NewDir());
            var day = new DateOnly(2026, 11, 4);
            var settings = new Settings { ShowAllTools = true };
            var w = new SettingsWindow(settings, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => { });
            w.Show(); Settle(); Save(w, dir, "settings-new-install");
            w.ExpandAll(); Settle(); Save(w, dir, "settings-all-open");
            w.Close();

            var store = new MyTodoService(Path.Combine(Temp.NewDir(), "c.json"));
            var notes = new NotesWindow(new NotesHooks(store, () => day, () => "Ctrl+Alt+Shift+L", () => { }),
                "Don't forget to confirm the facility inspection date with the team tomorrow ASAP. Also email the team lead re: the Thursday huddle. Need to submit the IR response for ANDA 216482 by Friday. errands: book the dentist, pick up the dry cleaning\nThe inspection report was clear.\nSubmit the expense report on the 1st every month", false);
            notes.Show(); Settle(); Save(notes, dir, "notes-to-my-todo-list");
            notes.AddTicked(); Settle(); Save(notes, dir, "notes-added");
            notes.Close();

            store.Add("Renew the vehicle registration", TodoCategory.Personal, day.AddDays(-2), false, Repeat.None, -1, null, "You", day);
            store.Add("Call the applicant about the inspection", TodoCategory.Work, day, true, Repeat.None, -1, "216482", "You", day);
            var w2 = new MyTodoWindow(new MyTodoHooks(store, () => day, () => "Ctrl+Alt+Shift+L", _ => { }, (_, _) => { }));
            w2.Show(); Settle(); Save(w2, dir, "my-todo-list");
            Avalonia.Automation.AutomationProperties.GetName(w2);
            var edit = w2.GetVisualDescendants().OfType<Button>().First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Edit: Renew the vehicle registration");
            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(); Save(w2, dir, "my-todo-list-editing");
            w2.Close();
        }, CancellationToken.None);
    }

    [ShotFact]
    public async Task Save_pictures_of_the_focused_edition()
    {
        string dir = Environment.GetEnvironmentVariable("ANDA_SHOTS_DIR")!;
        Directory.CreateDirectory(dir);
        await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            using var home = new EnvScope(AppPaths.HomeEnvVar, Temp.NewDir());
            var focused = new SettingsWindow(new Settings(), new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => { });
            focused.Show(); Settle(); Save(focused, dir, "focus-settings-new-install");
            focused.ExpandAll(); Settle(); Save(focused, dir, "focus-settings-all-open");
            focused.Close();

            var calls = new List<string>();
            var hooks = new AskHooks(() => MyActionRules.Starters(), (a, s) => calls.Add(a.Name), t => calls.Add("review"), n => calls.Add("lookup " + n));
            const string table = "ANDA\tApplicant\tGoal date\tForecast\n216482\tFictional Pharma A\tOct 10, 2026\tOct 17, 2026\n214990\tFictional Pharma B\tAug 15, 2026\tAug 15, 2026\n216482\tFictional Pharma A\tOct 10, 2026\tOct 17, 2026\n123456\tFictional Pharma C\tNov 2, 2026\t";
            var ask = new AskWindow(hooks, table);
            ask.Show(); Settle(); Save(ask, dir, "ask-table");
            Avalonia.Automation.AutomationProperties.GetName(ask);
            ask.FindControl<Button>("SuggestBtn")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(); Save(ask, dir, "ask-table-duplicates");
            ask.Close();
            var one = new AskWindow(hooks, "Quick status. ANDA 216482 is on track, with a goal date of Oct 10, 2026 and a forecast of Oct 17, 2026.");
            one.Show(); Settle(); Save(one, dir, "ask-text");
            one.Close();
            var none = new AskWindow(hooks, "");
            none.Show(); Settle(); Save(none, dir, "ask-empty");
            none.Close();

            var draft = new DraftWindow("Hi all, quick status. ANDA 216482 is on track, with a goal date of Oct 10, 2026 and a forecast of Oct 17, 2026. ANDA 214990 is running late. Please send it by Saturday, Nov 7, 2026, [name] and and thank you. ANDA 123456 needs a call.",
                new MockAndaRepository().Find, new WritingService(null), new Settings(), new PromptLibraryService(Path.Combine(Temp.NewDir(), "p2.json")), () => new DateOnly(2026, 11, 4));
            draft.Show(); Settle(); Save(draft, dir, "review-a-draft");
            draft.Close();

            // Round 25: the three views with the sample draft, and the recall row opened.
            var quick = new QuickNotesService(Path.Combine(Temp.NewDir(), "q.json"), () => new DateTime(2026, 7, 16, 10, 42, 0));
            quick.Add("216482", "Call with the applicant on Jul 21 about the IR response.");
            var review = new DraftWindow(DraftWindow.SampleDraft, new MockAndaRepository().Find, new WritingService(null), new Settings(), new PromptLibraryService(Path.Combine(Temp.NewDir(), "p3.json")),
                () => new DateOnly(2026, 7, 16)) { QuickNotes = quick, OpenCard = _ => { } };
            review.Show(); Settle(); Save(review, dir, "review-25-all");
            review.SetView("proof"); Settle(); Save(review, dir, "review-25-proofread");
            review.SetView("facts"); Settle();
            foreach (var box in review.FindControl<StackPanel>("RecallPanel")!.Children.OfType<Expander>().Take(1)) box.IsExpanded = true;
            for (int i = 0; i < 30; i++) { Settle(); Thread.Sleep(12); }
            Save(review, dir, "review-25-facts-recall");
            review.Close();
            var messy = new DraftWindow("hi team, i recieved the the ammendment for anda 216482 .It is a ANDA for Metfromin tablets,500 mg (cycle 2.", new MockAndaRepository().Find, new WritingService(null), new Settings(),
                new PromptLibraryService(Path.Combine(Temp.NewDir(), "p4.json")), () => new DateOnly(2026, 7, 16));
            messy.Show(); messy.SetView("proof"); Settle(); Save(messy, dir, "review-25-messy");
            messy.Close();

            // Round 26: Compare two files with the sample files, and Ask with the extra buttons.
            var compare = new CompareWindow { Narrative = _ => { } };
            compare.Show(); compare.Load(FileCompare.SampleA, FileCompare.SampleB);
            for (int i = 0; i < 30; i++) { Settle(); Thread.Sleep(12); }
            Save(compare, dir, "compare-26");
            compare.Close();
            var askMore = new AskWindow(new AskHooks(() => new List<CustomAction>(), (_, _) => { }, _ => { }, _ => { })
                { KnownAndas = t => Proofreader.KnownIds(t, new MockAndaRepository().Find), AddNote = (_, _) => (true, "Note saved."), Today = () => new DateOnly(2026, 7, 16) },
                "ANDA 216482: call the applicant about the IR response on Nov 20, then file the memo.");
            askMore.Show(); for (int i = 0; i < 30; i++) { Settle(); Thread.Sleep(12); }
            Save(askMore, dir, "ask-26");
            askMore.Close();

            // Round 27: two documents with a checked answer, a report against my data, the hand-off page, and Review with its two new rows.
            var repo27 = new MockAndaRepository(); var day27 = new DateOnly(2026, 7, 16); var ctx27 = new CardContext(day27, new BusinessCalendar());
            var docs = new CompareWindow
            {
                Narrative = _ => { }, RunPrompt = (_, _, _) => { }, Today = () => day27,
                Prompts = () => new List<CustomAction> { new() { Name = "Compare and contrast", Kind = ActionKind.AskAi, Template = "Compare.\n\n{selection}", Enabled = true } },
                Mine = id => repo27.Find(id) is { } rec ? (rec.GoalDateValue?.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture) ?? "", rec.ForecastDateValue?.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture) ?? "") : null,
            };
            docs.Show(); Settle();
            docs.FindControl<Button>("SampleDocsBtn")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            docs.FindControl<TextBox>("AnswerBox")!.Text = "The goal date moved from Oct 3, 2026 to Oct 17, 2026, a slip of 21 days, and paragraph 9 was dropped.";
            docs.FindControl<Button>("CheckBtn")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 30; i++) { Settle(); Thread.Sleep(12); }
            Save(docs, dir, "compare-27-documents");
            docs.SetMode("data"); docs.FindControl<Button>("SampleReportBtn")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 30; i++) { Settle(); Thread.Sleep(12); }
            Save(docs, dir, "compare-27-report");
            docs.Close();
            var handoff = new HandoffWindow("Hand-off page", true, (watchOnly, withNotes) => Handoff.Text("Hand-off", HandoffData.Entries(HandoffData.Order(repo27, ctx27, watchOnly), repo27, ctx27, null, null, null), "Jul 16, 2026", withNotes));
            handoff.Show(); handoff.NotesBox.IsChecked = true; for (int i = 0; i < 30; i++) { Settle(); Thread.Sleep(12); }
            Save(handoff, dir, "handoff-27");
            // Round 29: the Connections and event log window, and a window with the line that says what is switched on.
            var st29 = new ConnState(new ConnAi("Claude", "api.anthropic.com"), new[] { new ConnAgent("Alert Hub", "", false) }, null);
            var t29 = new DateTime(2026, 7, 16, 13, 5, 7, DateTimeKind.Utc);
            var ev29 = new List<EventEntry> { EventLog.Entry("connection", "AI switched on", t29), EventLog.Entry("sent", "AI request, 1,234 characters", t29.AddMinutes(2)), EventLog.Entry("file", "settings restored from a backup", t29.AddMinutes(9)), EventLog.Entry("deleted", "kept copies of the sheet", t29.AddMinutes(12)) };
            var conn29 = new ConnectionsWindow(() => Connections.Describe(st29), () => (ev29, ev29.Count, 0), () => EventLog.Csv(ev29), @"C:\Users\me\AppData\Roaming\AndaCompanion\events.csv", TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
            conn29.Show(); for (int i = 0; i < 30; i++) { Settle(); Thread.Sleep(12); }
            Save(conn29, dir, "connections-29");
            var about29 = new AboutWindow();
            var keepLine29 = ConnectionBar.Line; ConnectionBar.Line = () => Connections.Describe(st29).Line;
            about29.Show(); ConnectionBar.Attach(about29); for (int i = 0; i < 30; i++) { Settle(); Thread.Sleep(12); }
            Save(about29, dir, "about-29");
            ConnectionBar.Line = keepLine29;
            conn29.Close(); about29.Close();
            // Round 30: Backups and archive, on files in a folder of the test's own.
            string dir30 = Temp.NewDir(), file30 = Path.Combine(dir30, "todo.json"); var now30 = new DateTime(2026, 11, 18, 20, 0, 0, DateTimeKind.Utc); var day30 = new DateOnly(2026, 11, 18);
            var todo30 = new TodoService(file30); var zone30 = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
            var b30 = new Backups(Path.Combine(dir30, "backups"), () => 30, () => zone30, () => now30);
            b30.Register(new("todo", "ANDA to-do list", file30, "item", "items", TodoService.CountIn, () => todo30.Reload()));
            b30.Register(new("notes", "Notes on ANDAs", Path.Combine(dir30, "quick-notes.json"), "note", "notes", t => Backups.ItemsIn(t, "Notes"), () => { }));
            foreach (string title in new[] { "Send the meeting minutes", "Ask about the facility inspection", "File the weekly report" }) { string id30 = todo30.Add(title, title.StartsWith("Send") ? "216482" : null, null, null, "You", day30).Item!.Id; b30.Keep("todo"); now30 = now30.AddMinutes(7); if (title.StartsWith("Send")) { todo30.SetDone(id30, true, day30); todo30.ClearCompleted(day30); b30.Keep("todo"); now30 = now30.AddMinutes(3); } }
            var bak30 = new BackupsWindow(b30, () => 30, _ => { }, () => 30, _ => { }, new[] { new ArchiveSource("ANDA to-do list", "the ANDA to-do list", () => TodoArchive.Lines(file30), id => todo30.PutBack(id, day30), () => todo30.Waiting(day30), id => todo30.SetDone(id, false, day30)) }, zone30);
            bak30.Show(); ConnectionBar.Attach(bak30); for (int i = 0; i < 30; i++) { Settle(); Thread.Sleep(12); }
            Save(bak30, dir, "backups-30");
            bak30.Close();
            // Round 32: the guide of five steps, at its second step and at its last.
            var guide32 = new FirstStepsWindow(new FirstStepsHooks(() => "Ctrl+Alt+A", () => "Offline · Local only", () => "The card of the sample ANDA 216482 is open. It is fictional, and its card says so. Press Esc to close it.", () => null, () => null));
            guide32.Show(); ConnectionBar.Attach(guide32); guide32.Go(1); guide32.ActionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); for (int i = 0; i < 20; i++) { Settle(); Thread.Sleep(12); }
            Save(guide32, dir, "first-steps-32-step2");
            guide32.Go(1); guide32.Go(1); guide32.Go(1); for (int i = 0; i < 20; i++) { Settle(); Thread.Sleep(12); }
            Save(guide32, dir, "first-steps-32-step5");
            guide32.Close();
            // Round 33: the card's details and its list button, the card at the largest text with compact spacing, One Bar, and Today.
            {
                var repo33 = new Data.MockAndaRepository(); var pins33 = new List<string>();
                CardWindow Card33() => new(new OfflineBriefService(), repo33.Find) { CloseWhenDeactivated = false, AutoHideSeconds = 0, CardFields = () => CardDetails.DefaultFields, DataFileName = () => null,
                    IsKept = n => pins33.Contains(n), ToggleKept = n => { var (after, result) = Recents.PinAfter(pins33, n); pins33 = after; return result; } };
                var card33 = Card33(); card33.Show(repo33.Find("214990")!, new PixelPoint(40, 40), false); for (int i = 0; i < 20; i++) { Settle(); Thread.Sleep(12); }
                card33.KeyPress(Avalonia.Input.Key.E, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.E, "e"); for (int i = 0; i < 30; i++) { Settle(); Thread.Sleep(12); }
                Save(card33, dir, "card-33-details"); card33.Close();
                var big33 = Card33(); big33.ApplySpacing(Display.Spacing("compact")); DisplayScale.Apply(big33, Display.TextScale("xl"));
                big33.Show(repo33.Find("216482")!, new PixelPoint(40, 40), false); for (int i = 0; i < 30; i++) { Settle(); Thread.Sleep(12); }
                Save(big33, dir, "card-33-largest-compact"); big33.Close();
                var plain33 = Card33(); plain33.Show(repo33.Find("216482")!, new PixelPoint(40, 40), false); for (int i = 0; i < 30; i++) { Settle(); Thread.Sleep(12); }
                Save(plain33, dir, "card-33-standard"); plain33.Close();
                var bar33 = new LookupWindow(_ => { }, new LookupOptions(true, () => new[] { ("214990", true), ("216482", false) }, repo33.Find, _ => { }, _ => { }));
                bar33.Show(); bar33.FindControl<TextBox>("Input")!.Text = "216482"; for (int i = 0; i < 20; i++) { Settle(); Thread.Sleep(12); }
                Save(bar33, dir, "one-bar-33"); bar33.Close();
                Today.Look? Find33(string id) => repo33.Find(id) is { } rec ? Round33Tests.Look(rec) : null;
                var today33 = new TodayWindow(new TodayHooks(() => repo33.Numbers.Select(n => Find33(n)!).ToList(), Find33, () => new[] { "214990" }, () => new[] { "216482", "214990" }, _ => { }));
                today33.Show(); ConnectionBar.Attach(today33); for (int i = 0; i < 20; i++) { Settle(); Thread.Sleep(12); }
                Save(today33, dir, "today-33"); today33.Close();
                // Round 34: the answer-first card at standard and at the largest text, the same card opened with More, and Today with Show all.
                var first34 = Card33(); first34.Show(repo33.Find("216482")!, new PixelPoint(40, 40), false); for (int i = 0; i < 30; i++) { Settle(); Thread.Sleep(12); }
                Save(first34, dir, "card-34-answer-first");
                first34.FindControl<Button>("MoreChip")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); for (int i = 0; i < 30; i++) { Settle(); Thread.Sleep(12); }
                Save(first34, dir, "card-34-more"); first34.Close();
                var big34 = Card33(); DisplayScale.Apply(big34, Display.TextScale("xl"));
                big34.Show(repo33.Find("216482")!, new PixelPoint(40, 40), false); for (int i = 0; i < 30; i++) { Settle(); Thread.Sleep(12); }
                Save(big34, dir, "card-34-answer-first-largest"); big34.Close();
                var today34 = new TodayWindow(new TodayHooks(() => repo33.Numbers.Select(n => Find33(n)!).ToList(), Find33, () => new[] { "214990" }, () => new[] { "216482", "214990" }, _ => { }, ShowAll: () => { }));
                today34.Show(); ConnectionBar.Attach(today34); for (int i = 0; i < 20; i++) { Settle(); Thread.Sleep(12); }
                Save(today34, dir, "today-34-show-all"); today34.Close();
            }
            // Round 31: the card's data line, fresh and STALE, and the Time part of Settings.
            using (new ZoneScope("America/New_York"))
            {
                var asOf31 = new DateTime(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc); var now31 = asOf31.AddDays(9).AddHours(2);
                var mine31 = new Data.MockAndaRepository().Find("216482")! with { Demo = false, AsOfUtc = asOf31, AsOfDate = new DateOnly(2026, 11, 11), DataSource = "Your portfolio file · my-portfolio.xlsx", SourceLabel = "Your portfolio: my-portfolio.xlsx" };
                var card31 = new CardWindow(new OfflineBriefService(), _ => mine31) { CloseWhenDeactivated = false, Today = () => new DateOnly(2026, 11, 20), UtcNow = () => now31, StaleDays = () => 7 };
                card31.Show(mine31, new PixelPoint(40, 40), false); for (int i = 0; i < 30; i++) { Settle(); Thread.Sleep(12); }
                Save(card31, dir, "card-stale-31");
                card31.Close();
                foreach (var look31 in new[] { AppearanceId.L, AppearanceId.O })
                {
                    ThemeManager.Apply(look31);
                    var box31 = new CompactCardWindow(mine31, () => { }, () => Clock.DataLine(mine31, Clock.Ms(now31), 7, Clock.Zone)) { CloseWhenDeactivated = false };
                    box31.Show(); for (int i = 0; i < 20; i++) { Settle(); Thread.Sleep(12); }
                    Save(box31, dir, "compact-stale-31-" + look31);
                    box31.Close();
                }
                ThemeManager.Apply(Appearances.Default);
                var set31 = new SettingsWindow(new Settings { TimeZone = "America/New_York", StaleAfterDays = 7 }, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p31.json")), () => { }) { UtcNow = () => now31 };
                set31.Show(); Settle(); set31.FindControl<Expander>("SecLook")!.IsExpanded = true; for (int i = 0; i < 30; i++) { Settle(); Thread.Sleep(12); }
                set31.FindControl<TextBlock>("TimeHint")!.IsVisible = true; set31.FindControl<TextBlock>("TimeHint")!.Text = Clock.ZoneSaid("America/New_York", Clock.Ms(now31));
                foreach (var sv31 in set31.GetVisualDescendants().OfType<ScrollViewer>().Where(v => v.Extent.Height > v.Viewport.Height + 200).Take(1)) sv31.Offset = new Vector(0, 540);
                for (int i = 0; i < 10; i++) { Settle(); Thread.Sleep(12); }
                Save(set31, dir, "settings-time-31");
                set31.Close();
            }
            handoff.Close();
            var review27 = new DraftWindow("Certainly! Here is a revised version.\n\nANDA 216482: under 21 CFR 314.96(a) the amendment is minor. Please send your response by Friday, Dec 4, 2026.\n\nI hope this helps.",
                repo27.Find, new WritingService(null), new Settings(), new PromptLibraryService(Path.Combine(Temp.NewDir(), "p5.json")), () => day27)
                { AddFollowUp = (_, _, _) => new TodoAddResult(null, true, null), SecondOpinion = _ => { } };
            review27.Show(); review27.SetView("facts"); for (int i = 0; i < 30; i++) { Settle(); Thread.Sleep(12); }
            Save(review27, dir, "review-27");
            review27.Close();
        }, CancellationToken.None);
    }
}
