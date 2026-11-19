using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using AndaCompanion.Models;
using AndaCompanion.Services;
using AndaCompanion.Theming;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>Round 32 in the Windows app's windows: the reference file's line on the card, and its own number of days in Settings.</summary>
[Collection("Global state")]
public class Round32UiTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public Round32UiTests(HeadlessFixture fx) => _fx = fx;
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }

    [Fact]
    public async Task The_reference_line_on_the_card_turns_STALE_in_words_and_by_its_look_and_keeps_step_while_the_card_is_open()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            using var zone = new ZoneScope("America/New_York");
            ThemeManager.Apply(Appearances.Default);
            var saved = new DateTime(2026, 11, 1, 14, 30, 0, DateTimeKind.Utc); var now = saved.AddDays(18); int days = 45;
            var repo = new Data.MockAndaRepository(); var record = repo.Find("216482")!;
            ReferenceInfo Line(AndaRecord _) { var s = Clock.RefSaved(Clock.Ms(saved), Clock.Ms(now), days, Clock.Zone); return new ReferenceInfo(null, "Sep 17, 2025 · Drugs@FDA, file " + s.Text, s.Stale); }
            var w = new CardWindow(new OfflineBriefService(), repo.Find) { CloseWhenDeactivated = false, UtcNow = () => now, Reference = Line };
            try
            {
                w.Show(record, new PixelPoint(40, 40), false); Settle();
                var rld = w.FindControl<TextBlock>("RldText")!; var warn = Application.Current!.Resources["C.Warn"];
                var seen = new List<(string Text, bool Bold, bool Warn)>();
                void Look() { Settle(); seen.Add((rld.Text ?? "", rld.FontWeight == FontWeight.SemiBold, ReferenceEquals(rld.Foreground, warn))); }
                Look();                                                       // 0: 18 days old, fresh
                now = saved.AddDays(19); w.MinutePassed(); Look();            // 1: a day on, nobody told the card
                now = saved.AddDays(46); w.MinutePassed(); Look();            // 2: older than 45 days
                days = 60; w.TimeChanged(); Look();                           // 3: the person chose 60 days in Settings
                return seen;
            }
            finally { w.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.Equal(("Sep 17, 2025 · Drugs@FDA, file saved Nov 1, 2026 (18 days ago)", false, false), r[0]);
        Assert.Equal(("Sep 17, 2025 · Drugs@FDA, file saved Nov 1, 2026 (19 days ago)", false, false), r[1]);
        Assert.Equal(("Sep 17, 2025 · Drugs@FDA, file saved Nov 1, 2026 (46 days ago) · STALE: older than 45 days", true, true), r[2]);
        Assert.Equal(("Sep 17, 2025 · Drugs@FDA, file saved Nov 1, 2026 (46 days ago)", false, false), r[3]);
    }

    [Fact]
    public async Task Settings_offers_the_reference_files_own_days_with_a_name_and_says_what_was_chosen()
    {
        string home = Temp.NewDir();
        var r = await _fx.Session.Dispatch(() =>
        {
            using var env = new EnvScope(AppPaths.HomeEnvVar, home);
            ThemeManager.Apply(Appearances.Default);
            ConnectionStatus.Reset();
            var s = new Settings(); int changed = 0;
            var w = new SettingsWindow(s, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => changed++, null, new QuickReplyService(Path.Combine(Temp.NewDir(), "r.json")));
            try
            {
                w.Show(); Settle();
                w.FindControl<Expander>("SecLook")!.IsExpanded = true; Settle();
                var box = w.FindControl<ComboBox>("RefStaleBox")!; var mine = w.FindControl<ComboBox>("StaleBox")!; var hint = w.FindControl<TextBlock>("TimeHint")!;
                var open = (AutomationProperties.GetName(box), box.Items.Cast<string>().ToArray(), box.SelectedIndex, box.IsEffectivelyVisible, hint.IsVisible);
                box.SelectedIndex = 4; Settle();
                var ninety = (hint.Text, s.RefStaleAfterDays, s.StaleAfterDays, mine.SelectedIndex, changed, File.ReadAllText(Path.Combine(home, "settings.json")).Contains("\"RefStaleAfterDays\": 90"));
                box.SelectedIndex = 0; Settle();
                return (open, ninety, fourteen: (hint.Text, s.RefStaleAfterDays));
            }
            finally { w.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.Equal("Mark the Drugs@FDA reference file as stale when it is older than", r.open.Item1);
        Assert.Equal(new[] { "14 days", "30 days", "45 days", "60 days", "90 days" }, r.open.Item2);
        Assert.Equal((2, true, false), (r.open.Item3, r.open.Item4, r.open.Item5));
        Assert.Equal(("A Drugs@FDA file older than 90 days is now marked STALE.", 90, 7, 2, 1, true), r.ninety);      // the days for my own data are a separate choice and stay
        Assert.Equal(("A Drugs@FDA file older than 14 days is now marked STALE.", 14), r.fourteen);
    }

    [Fact]
    public async Task The_guide_walks_five_steps_can_be_left_at_any_of_them_and_its_buttons_do_what_they_say()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var calls = new List<string>(); string on = "Offline · Local only";
            var hooks = new FirstStepsHooks(() => "Ctrl+Alt+A", () => on,
                () => { calls.Add("sample"); return "The card of the sample ANDA 216482 is open. It is fictional, and its card says so. Press Esc to close it."; },
                () => { calls.Add("settings"); return "Settings is open at “My files and writing”. This guide stays open behind it."; },
                () => { calls.Add("connections"); return "“Connections and event log” is open. This guide stays open behind it."; });
            void Click(Button b) => b.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            var w = new FirstStepsWindow(hooks);
            bool closed = false; w.Closed += (_, _) => closed = true;
            Button? bar = null; w.Opened += (_, _) => bar = ConnectionBar.Attach(w);      // what the app does for every window that opens
            w.Show(); Settle();
            (string Place, string Title, string[] Lines, string? Act, bool Back, string Next, string Leave, string Status) Look() =>
                (w.Place.Text ?? "", w.Heading.Text ?? "", w.Lines.Children.OfType<TextBlock>().Select(t => t.Text ?? "").ToArray(), w.ActionButton.IsVisible ? w.ActionButton.Content as string : null, w.BackButton.IsVisible, (string)w.NextButton.Content!, (string)w.LeaveButton.Content!, w.Status.Text ?? "");
            var seen = new List<(string Place, string Title, string[] Lines, string? Act, bool Back, string Next, string Leave, string Status)> { Look() };
            var first = (w.FocusManager?.GetFocusedElement() == w.NextButton, w.NextButton.IsDefault, AutomationProperties.GetName(w), bar is not null, AutomationProperties.GetLiveSetting(w.Place), AutomationProperties.GetLiveSetting(w.Status));
            Click(w.NextButton); Settle(); seen.Add(Look());                       // 2
            Click(w.ActionButton); Settle(); string said2 = w.Status.Text ?? "";
            Click(w.NextButton); Settle(); seen.Add(Look());                       // 3: the status of the step before is gone
            Click(w.ActionButton); Settle(); string said3 = w.Status.Text ?? "";
            Click(w.NextButton); Settle(); seen.Add(Look());                       // 4
            on = "Switched on: Claude (AI)"; Click(w.BackButton); Settle(); bool backHasFocus = w.FocusManager?.GetFocusedElement() == w.BackButton;
            Click(w.NextButton); Settle(); string line4 = w.Lines.Children.OfType<TextBlock>().First().Text ?? "";
            Click(w.ActionButton); Settle(); string said4 = w.Status.Text ?? "";
            Click(w.NextButton); Settle(); seen.Add(Look());                       // 5
            string name5 = AutomationProperties.GetName(w) ?? "";
            w.Go(1); Settle(); bool doneCloses = closed;                           // Done
            var again = new FirstStepsWindow(hooks); bool left = false; again.Closed += (_, _) => left = true;
            again.Show(); Settle(); Click(again.NextButton); Settle(); Click(again.LeaveButton); Settle();      // left at step 2
            var third = new FirstStepsWindow(hooks); bool esc = false; third.Closed += (_, _) => esc = true;
            third.Show(); Settle(); third.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Escape }); Settle();
            return (seen, first, said2, said3, said4, backHasFocus, line4, name5, doneCloses, left, esc, calls);
        }, CancellationToken.None);

        Assert.Equal(new[] { "Step 1 of 5: What this is", "Step 2 of 5: Try it on a sample", "Step 3 of 5: Use your own data", "Step 4 of 5: What is switched on", "Step 5 of 5: Where things are" }, r.seen.Select(s => s.Place + ": " + s.Title));
        Assert.Equal(new string?[] { null, "Show the sample card", "Open Settings", "Open Connections", null }, r.seen.Select(s => s.Act));
        Assert.Equal(new[] { false, true, true, true, true }, r.seen.Select(s => s.Back));
        Assert.Equal(new[] { "Next", "Next", "Next", "Next", "Done" }, r.seen.Select(s => s.Next));
        Assert.Equal(new[] { "Skip the guide", "Skip the guide", "Skip the guide", "Skip the guide", "Close" }, r.seen.Select(s => s.Leave));
        Assert.All(r.seen, s => Assert.Equal("", s.Status));                                                      // what a button said belongs to its step only
        Assert.Equal(Guide.Step(0, "Ctrl+Alt+A", "Offline · Local only").Lines, r.seen[0].Lines);
        Assert.Equal("Select an ANDA number in any window, then press Ctrl+Alt+A. Or press the button below to open the card of the sample ANDA 216482.", r.seen[1].Lines[0]);
        Assert.Equal("Every window says at its foot what is switched on. At the moment: Offline · Local only.", r.seen[3].Lines[0]);
        Assert.Equal((true, true, "First steps. Step 1 of 5: What this is", true, AutomationLiveSetting.Polite, AutomationLiveSetting.Polite), r.first);
        Assert.Equal("The card of the sample ANDA 216482 is open. It is fictional, and its card says so. Press Esc to close it.", r.said2);
        Assert.Equal("Settings is open at “My files and writing”. This guide stays open behind it.", r.said3);
        Assert.Equal("“Connections and event log” is open. This guide stays open behind it.", r.said4);
        Assert.True(r.backHasFocus);
        Assert.Equal("Every window says at its foot what is switched on. At the moment: Switched on: Claude (AI).", r.line4);      // asked each time it is painted
        Assert.Equal("First steps. Step 5 of 5: Where things are", r.name5);
        Assert.True(r.doneCloses); Assert.True(r.left); Assert.True(r.esc);
        Assert.Equal(new[] { "sample", "settings", "connections" }, r.calls);                                     // each button did its one thing, once, and nothing else was called
    }

    [Fact]
    public async Task Settings_opens_at_the_section_the_guide_names()
    {
        string home = Temp.NewDir();
        var r = await _fx.Session.Dispatch(() =>
        {
            using var env = new EnvScope(AppPaths.HomeEnvVar, home);
            ThemeManager.Apply(Appearances.Default);
            ConnectionStatus.Reset();
            var w = new SettingsWindow(new Settings(), new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => { }, null, new QuickReplyService(Path.Combine(Temp.NewDir(), "r.json")));
            try
            {
                w.Show(); Settle();
                var files = w.FindControl<Expander>("SecFiles")!;
                bool before = files.IsExpanded, ok = w.OpenSection("SecFiles"); Settle();
                string heading = string.Join(" ", Avalonia.VisualTree.VisualExtensions.GetVisualDescendants((Control)files.Header!).OfType<TextBlock>().Select(t => t.Text));
                return (before, ok, files.IsExpanded, w.OpenSection("NoSuchSection"), heading);
            }
            finally { w.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.Equal((false, true, true, false), (r.before, r.ok, r.Item3, r.Item4));
        Assert.Contains("My files and writing", r.heading);                                                       // the name the guide gives is the name on the screen
    }
}
