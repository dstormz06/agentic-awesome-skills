using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AndaCompanion.Services;
using AndaCompanion.Theming;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>Settings arrange themselves: the rules that choose what opens, and the window that shows it.</summary>
public class SettingsAdvisorTests
{
    private static SettingsFacts Facts(SettingsSection[]? shortcuts = null, string? portfolio = null, string? connection = null, string? actions = null, bool chosen = false) =>
        new(new HashSet<SettingsSection>(shortcuts ?? Array.Empty<SettingsSection>()), portfolio, connection, actions, chosen);

    private static List<SettingsSection> Open(Settings s, SettingsFacts f) => SettingsAdvisor.Advise(s, f).Where(a => a.Open).Select(a => a.Section).ToList();

    [Fact]
    public void A_new_user_sees_the_two_things_needed_first_and_everything_else_collapsed()
    {
        var advice = SettingsAdvisor.Advise(new Settings(), SettingsFacts.AllWell);
        Assert.Equal(8, advice.Count);
        Assert.Equal(new[] { SettingsSection.Start, SettingsSection.Files }, advice.Where(a => a.Open).Select(a => a.Section));
        Assert.All(advice.Where(a => a.Open), a => { Assert.False(a.Attention); Assert.StartsWith("Start here", a.Reason); });
        Assert.All(advice.Where(a => !a.Open), a => Assert.Null(a.Reason));
    }

    [Fact]
    public void Once_getting_started_is_hidden_and_all_is_well_every_section_is_collapsed()
    {
        Assert.Empty(Open(new Settings { GettingStartedDone = true }, SettingsFacts.AllWell));
    }

    [Fact]
    public void A_chosen_portfolio_means_the_choose_your_portfolio_hint_goes_away()
    {
        Assert.Equal(new[] { SettingsSection.Start }, Open(new Settings(), Facts(chosen: true)));
    }

    [Fact]
    public void Whatever_needs_a_look_is_open_with_a_reason_however_many_there_are()
    {
        var s = new Settings { GettingStartedDone = true };
        var advice = SettingsAdvisor.Advise(s, Facts(new[] { SettingsSection.Start, SettingsSection.MyTodo, SettingsSection.Actions }, "your portfolio file could not be read", "Claude did not answer", null, true));
        var open = advice.Where(a => a.Open).ToList();
        Assert.Equal(new[] { SettingsSection.Start, SettingsSection.Files, SettingsSection.MyTodo, SettingsSection.Actions, SettingsSection.Connections }, open.Select(a => a.Section));
        Assert.All(open, a => { Assert.True(a.Attention); Assert.StartsWith("Needs a look: ", a.Reason); });
        Assert.Contains("portfolio file could not be read", open.First(a => a.Section == SettingsSection.Files).Reason);
    }

    [Fact]
    public void An_action_or_agent_that_is_not_ready_opens_its_section_unless_its_shortcut_is_the_problem()
    {
        var s = new Settings { GettingStartedDone = true };
        Assert.Equal("Needs a look: the agent “Alert Hub” is switched on but not ready",
            SettingsAdvisor.Advise(s, Facts(actions: "the agent “Alert Hub” is switched on but not ready")).First(a => a.Section == SettingsSection.Actions).Reason);
        Assert.Equal("Needs a look: an action's shortcut is used by another program",
            SettingsAdvisor.Advise(s, Facts(new[] { SettingsSection.Actions }, actions: "x")).First(a => a.Section == SettingsSection.Actions).Reason);
    }

    [Fact]
    public void The_two_sections_opened_most_often_open_by_themselves_but_a_couple_of_visits_do_not()
    {
        var s = new Settings { GettingStartedDone = true, SectionUse = new() { ["Look"] = 5, ["Dates"] = 4, ["Connections"] = 9, ["MyTodo"] = 2 } };
        var advice = SettingsAdvisor.Advise(s, SettingsFacts.AllWell);
        Assert.Equal(new[] { SettingsSection.Look, SettingsSection.Connections }, advice.Where(a => a.Open).Select(a => a.Section));
        Assert.All(advice.Where(a => a.Open), a => Assert.Equal("You open this often", a.Reason));
    }

    [Fact]
    public void Maintenance_is_never_opened_by_habit()
    {
        var s = new Settings { GettingStartedDone = true, SectionUse = new() { ["Maintenance"] = 50 } };
        Assert.Empty(Open(s, SettingsFacts.AllWell));
    }

    [Fact]
    public void At_most_three_sections_open_for_reasons_other_than_attention()
    {
        var s = new Settings { SectionUse = new() { ["Look"] = 9, ["Dates"] = 8, ["MyTodo"] = 7 } };
        var advice = SettingsAdvisor.Advise(s, SettingsFacts.AllWell);
        Assert.Equal(3, advice.Count(a => a.Open && !a.Attention));              // Start + Files (day 1) + the single most used one
        // attention does not count against the limit
        var withAttention = SettingsAdvisor.Advise(s, Facts(new[] { SettingsSection.Actions }, connection: "x did not answer"));
        Assert.Equal(3, withAttention.Count(a => a.Open && !a.Attention));
        Assert.Equal(2, withAttention.Count(a => a.Open && a.Attention));
    }

    [Fact]
    public void With_arranging_switched_off_nothing_opens_and_nothing_is_labelled()
    {
        var s = new Settings { AdaptiveSettings = false, SectionUse = new() { ["Look"] = 9 } };
        var advice = SettingsAdvisor.Advise(s, Facts(new[] { SettingsSection.Start }, "your portfolio file could not be read"));
        Assert.All(advice, a => { Assert.False(a.Open); Assert.Null(a.Reason); });
    }

    [Fact]
    public void Counts_keep_only_known_sections_with_sensible_numbers_and_old_use_fades()
    {
        var clean = SettingsAdvisor.CleanUse(new() { ["Look"] = 5000, ["Hacked<script>"] = 3, ["Dates"] = -4, ["Files"] = 0, ["Start"] = 2 });
        Assert.Equal(new[] { "Look", "Start" }, clean.Keys.OrderBy(k => k));
        Assert.Equal(SettingsAdvisor.MaxUsesKept, clean["Look"]);
        Assert.Empty(SettingsAdvisor.CleanUse(null));

        var use = new Dictionary<string, int> { ["Look"] = 100, ["Dates"] = 19 };
        SettingsAdvisor.Count(use, SettingsSection.Files);                           // total 120 → not above the limit yet
        Assert.Equal(100, use["Look"]);
        SettingsAdvisor.Count(use, SettingsSection.Files);                           // 121 → everything is halved (rounded up)
        Assert.Equal(50, use["Look"]);
        Assert.Equal(10, use["Dates"]);
        Assert.Equal(1, use["Files"]);
    }

    [Fact]
    public void The_counts_and_the_hidden_getting_started_card_belong_to_this_PC_not_to_a_backup_and_a_reset_forgets_them()
    {
        var dir = Temp.NewDir();
        var lenses = new PromptLibraryService(Path.Combine(dir, "p.json")); var replies = new QuickReplyService(Path.Combine(dir, "r.json"));
        var source = new Settings { SectionUse = new() { ["Look"] = 7 }, GettingStartedDone = true, AdaptiveSettings = false };
        string json = SettingsBackup.Export(source, lenses, replies);
        Assert.DoesNotContain("SectionUse\": {\n", json.Replace("\r", ""));        // the exported copy holds the default (empty) counts
        var target = new Settings { SectionUse = new() { ["Dates"] = 3 } };
        var (ok, _) = SettingsBackup.Import(json, target, lenses, replies);
        Assert.True(ok);
        Assert.False(target.AdaptiveSettings);                                       // a preference travels with a backup
        Assert.Equal(3, target.SectionUse["Dates"]);                                 // the counts stay this PC's own
        Assert.False(target.GettingStartedDone);
        target.GettingStartedDone = true;
        SettingsBackup.Reset(target, lenses, replies);
        Assert.Empty(target.SectionUse);
        Assert.False(target.GettingStartedDone);
        Assert.True(target.AdaptiveSettings);
    }

    [Fact]
    public void A_saved_file_with_odd_counts_is_repaired_on_load()
    {
        string path = Path.Combine(Temp.NewDir(), "settings.json");
        File.WriteAllText(path, "{\"SectionUse\":{\"Look\":4,\"Nope\":9,\"Dates\":-1},\"AdaptiveSettings\":true}");
        var s = SettingsService.Load(path);
        Assert.Equal(new[] { "Look" }, s.SectionUse.Keys);
    }
}

[Collection("Global state")]
public class AdaptiveSettingsUiTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public AdaptiveSettingsUiTests(HeadlessFixture fx) => _fx = fx;

    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static SettingsWindow NewWindow(Settings s, SettingsHooks? hooks = null)
    {
        ConnectionStatus.Reset();           // the connection state is shared by the whole process; each test starts from "not connected"
        var w = new SettingsWindow(s, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => { }, hooks, new QuickReplyService(Path.Combine(Temp.NewDir(), "r.json")));
        w.Show(); Settle();
        return w;
    }

    private static SettingsHooks Hooks(IReadOnlyList<HotkeyStatus>? hotkeys = null) => new(() => "", () => null, () => "", Hotkeys: hotkeys is null ? null : () => hotkeys);

    private static string Text(Window w, string name) => w.FindControl<TextBlock>(name)!.Text ?? "";

    [Fact]
    public async Task A_new_install_opens_two_sections_with_reasons_shows_getting_started_and_collapses_the_rest()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            using var home = new EnvScope(AppPaths.HomeEnvVar, Temp.NewDir());
            var w = NewWindow(new Settings());
            var open = w.OpenSections.ToList();
            bool gs = w.FindControl<Border>("GettingStarted")!.IsVisible;
            string s1 = Text(w, "GsStep1"), s2 = Text(w, "GsStep2"), s3 = Text(w, "GsStep3"), adaptive = Text(w, "AdaptiveText");
            string pillFiles = Text(w, "PillTextFiles"); bool pillShown = w.FindControl<Border>("PillFiles")!.IsVisible, pillLook = w.FindControl<Border>("PillLook")!.IsVisible;
            // the collapsed sections have no controls on screen yet; the open ones do
            bool filesOnScreen = w.GetVisualDescendants().OfType<TextBox>().Any(t => t.Name == "PortfolioBox");
            bool agentsOnScreen = w.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "AgentAdd");
            w.Close(); Settle();
            return (open, gs, s1, s2, s3, adaptive, pillFiles, pillShown, pillLook, filesOnScreen, agentsOnScreen);
        }, CancellationToken.None);

        Assert.Equal(new[] { SettingsSection.Start, SettingsSection.Files }, r.open);
        Assert.True(r.gs);
        Assert.Contains("Ctrl+Alt+A", r.s1);
        Assert.Contains("fictional demo records", r.s2);
        Assert.Contains("Ctrl+Alt+L", r.s3);
        Assert.Contains("Opened for you: How it starts, My files and writing", r.adaptive);
        Assert.Equal("Start here: choose your portfolio", r.pillFiles);
        Assert.True(r.pillShown);
        Assert.False(r.pillLook);
        Assert.True(r.filesOnScreen);
        Assert.False(r.agentsOnScreen);
    }

    [Fact]
    public async Task Hiding_getting_started_is_remembered_and_the_next_window_opens_fully_collapsed()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            using var home = new EnvScope(AppPaths.HomeEnvVar, Temp.NewDir());
            var s = new Settings();
            var w = NewWindow(s);
            Click(w.FindControl<Button>("GsHide")!); Settle();
            bool hidden = !w.FindControl<Border>("GettingStarted")!.IsVisible; bool saved = s.GettingStartedDone;
            w.Close(); Settle();
            var again = NewWindow(s);
            var open = again.OpenSections.ToList(); string text = Text(again, "AdaptiveText");
            // show it again from Maintenance
            again.ExpandAll(); Settle();
            Click(again.FindControl<Button>("GsShow")!); Settle();
            bool back = again.FindControl<Border>("GettingStarted")!.IsVisible; var openBack = again.OpenSections.ToList();
            again.Close(); Settle();
            return (hidden, saved, open, text, back, openBack);
        }, CancellationToken.None);

        Assert.True(r.hidden);
        Assert.True(r.saved);
        Assert.Empty(r.open);
        Assert.Contains("Everything is in order", r.text);
        Assert.True(r.back);
        Assert.Equal(new[] { SettingsSection.Start, SettingsSection.Files }, r.openBack);
    }

    [Fact]
    public async Task A_shortcut_another_program_owns_opens_its_section_with_a_warning_reason()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            using var home = new EnvScope(AppPaths.HomeEnvVar, Temp.NewDir());
            var todo = new HotkeyBinding(HotkeyService.IdMyTodo, "My to-do list", "Ctrl+Alt+L", 0, 0, "");
            var peek = new HotkeyBinding(HotkeyService.IdPeek, "Select & Peek", "Ctrl+Alt+A", 0, 0, "");
            var w = NewWindow(new Settings { GettingStartedDone = true }, Hooks(new[] { new HotkeyStatus(peek, true, 0), new HotkeyStatus(todo, false, 1409) }));
            var open = w.OpenSections.ToList(); string pill = Text(w, "PillTextMyTodo"); string adaptive = Text(w, "AdaptiveText");
            w.Close(); Settle();
            return (open, pill, adaptive);
        }, CancellationToken.None);

        Assert.Equal(new[] { SettingsSection.MyTodo }, r.open);
        Assert.StartsWith("Needs a look: ", r.pill);
        Assert.Contains("My to-do list", r.adaptive);
    }

    [Fact]
    public async Task Go_to_a_section_opens_a_collapsed_section_and_lists_the_group_names()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            using var home = new EnvScope(AppPaths.HomeEnvVar, Temp.NewDir());
            var w = NewWindow(new Settings { GettingStartedDone = true });
            var jump = w.FindControl<ComboBox>("JumpBox")!;
            var items = jump.ItemsSource!.Cast<string>().ToList();
            bool closedBefore = !w.OpenSections.Contains(SettingsSection.Actions);
            jump.SelectedIndex = items.FindIndex(i => i.StartsWith("MY AGENTS")); Settle();
            bool openAfter = w.OpenSections.Contains(SettingsSection.Actions);
            bool reset = jump.SelectedIndex == -1;
            jump.SelectedIndex = items.IndexOf("Maintenance"); Settle();
            bool maint = w.OpenSections.Contains(SettingsSection.Maintenance);
            w.Close(); Settle();
            return (items, closedBefore, openAfter, reset, maint);
        }, CancellationToken.None);

        Assert.Contains("Look and feel", r.items);
        Assert.Contains("My to-do list", r.items);
        Assert.Contains(r.items, i => i.StartsWith("APPEARANCE"));
        Assert.True(r.closedBefore);
        Assert.True(r.openAfter);
        Assert.True(r.reset);
        Assert.True(r.maint);
    }

    [Fact]
    public async Task Expand_all_Collapse_all_and_Suggested_view_work_and_do_not_count_as_use()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            using var home = new EnvScope(AppPaths.HomeEnvVar, Temp.NewDir());
            var s = new Settings { GettingStartedDone = true };
            var w = NewWindow(s);
            Click(w.FindControl<Button>("ExpandAllBtn")!); Settle();
            int all = w.OpenSections.Count;
            Click(w.FindControl<Button>("CollapseAllBtn")!); Settle();
            int none = w.OpenSections.Count;
            s.GettingStartedDone = false;
            Click(w.FindControl<Button>("SuggestedBtn")!); Settle();
            var suggested = w.OpenSections.ToList();
            int counted = s.SectionUse.Count;
            w.Close(); Settle();
            return (all, none, suggested, counted);
        }, CancellationToken.None);

        Assert.Equal(8, r.all);
        Assert.Equal(0, r.none);
        Assert.Equal(new[] { SettingsSection.Start, SettingsSection.Files }, r.suggested);
        Assert.Equal(0, r.counted);
    }

    [Fact]
    public async Task Opening_a_section_counts_once_per_visit_is_saved_and_can_be_forgotten_and_the_third_visit_opens_it_by_itself()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            string dir = Temp.NewDir();
            using var home = new EnvScope(AppPaths.HomeEnvVar, dir);
            var s = new Settings { GettingStartedDone = true };
            int AfterVisit()
            {
                var w = NewWindow(s);
                var box = w.FindControl<Expander>("SecDates")!;
                box.IsExpanded = true; Settle();            // the person opens it
                box.IsExpanded = false; Settle(); box.IsExpanded = true; Settle();      // and again: still one visit
                w.Close(); Settle();
                return s.SectionUse.GetValueOrDefault("Dates");
            }
            int c1 = AfterVisit(), c2 = AfterVisit(), c3 = AfterVisit();
            string saved = File.ReadAllText(Path.Combine(dir, "settings.json"));
            var fourth = NewWindow(s);
            var open = fourth.OpenSections.ToList(); string pill = Text(fourth, "PillTextDates");
            fourth.ExpandAll(); Settle();
            Click(fourth.FindControl<Button>("AdaptiveReset")!); Settle();
            int afterReset = s.SectionUse.Count;
            fourth.Close(); Settle();
            return (c1, c2, c3, saved, open, pill, afterReset);
        }, CancellationToken.None);

        Assert.Equal(1, r.c1);
        Assert.Equal(2, r.c2);
        Assert.Equal(3, r.c3);
        Assert.Contains("\"Dates\": 3", r.saved);
        Assert.Equal(new[] { SettingsSection.Dates }, r.open);
        Assert.Equal("You open this often", r.pill);
        Assert.Equal(0, r.afterReset);
    }

    [Fact]
    public async Task With_arranging_off_nothing_opens_nothing_is_counted_and_turning_it_on_applies_the_rules()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            using var home = new EnvScope(AppPaths.HomeEnvVar, Temp.NewDir());
            var s = new Settings { AdaptiveSettings = false };
            var w = NewWindow(s);
            var open = w.OpenSections.ToList(); string text = Text(w, "AdaptiveText");
            w.FindControl<Expander>("SecDates")!.IsExpanded = true; Settle();
            int counted = s.SectionUse.Count;
            w.ExpandAll(); Settle();
            var check = w.FindControl<CheckBox>("AdaptiveCheck")!;
            check.IsChecked = true; Settle();
            var after = w.OpenSections.ToList();
            w.Close(); Settle();
            return (open, text, counted, after, s.AdaptiveSettings);
        }, CancellationToken.None);

        Assert.Empty(r.open);
        Assert.Contains("stay as you leave them", r.text);
        Assert.Equal(0, r.counted);
        Assert.Equal(new[] { SettingsSection.Start, SettingsSection.Files }, r.after);
        Assert.True(r.AdaptiveSettings);
    }

    [Fact]
    public async Task A_connection_that_failed_or_was_never_tested_opens_its_section_and_a_working_one_does_not()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            using var home = new EnvScope(AppPaths.HomeEnvVar, Temp.NewDir());
            var s = new Settings { GettingStartedDone = true };
            var w = NewWindow(s);
            var calm = w.OpenSections.ToList(); w.Close(); Settle();

            ConnectionStatus.Failure("Claude", "timed out");
            var failed = new SettingsWindow(s, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => { }); failed.Show(); Settle();
            var failedOpen = failed.OpenSections.ToList(); string pill = Text(failed, "PillTextConnections"); failed.Close(); Settle();

            ConnectionStatus.Reset("Claude");
            var setUp = new SettingsWindow(s, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => { }); setUp.Show(); Settle();
            string pill2 = Text(setUp, "PillTextConnections"); setUp.Close(); Settle();

            ConnectionStatus.Success("Claude");
            var good = new SettingsWindow(s, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => { }); good.Show(); Settle();
            var goodOpen = good.OpenSections.ToList(); good.Close(); Settle();
            ConnectionStatus.Reset();
            return (calm, failedOpen, pill, pill2, goodOpen);
        }, CancellationToken.None);

        Assert.Empty(r.calm);
        Assert.Equal(new[] { SettingsSection.Connections }, r.failedOpen);
        Assert.Equal("Needs a look: Claude did not answer", r.pill);
        Assert.Equal("Needs a look: Claude is set up but not tested", r.pill2);
        Assert.Empty(r.goodOpen);
    }

    [Fact]
    public async Task Choosing_a_portfolio_updates_the_getting_started_card_and_a_unreadable_one_opens_its_section()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            using var home = new EnvScope(AppPaths.HomeEnvVar, Temp.NewDir());
            var chosen = NewWindow(new Settings { PortfolioPath = Path.Combine(Temp.NewDir(), "missing.xlsx") });
            string step2 = Text(chosen, "GsStep2"); bool button = chosen.FindControl<Button>("GsFiles")!.IsVisible;
            var open = chosen.OpenSections.ToList();
            chosen.Close(); Settle();
            return (step2, button, open);
        }, CancellationToken.None);

        Assert.StartsWith("2. Done: your portfolio file is chosen", r.step2);
        Assert.False(r.button);
        Assert.Equal(new[] { SettingsSection.Start }, r.open);             // chosen, so no "choose your portfolio"; nothing else to show without a hook that reports a problem
    }
}
