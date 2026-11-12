using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AndaCompanion.Services;
using AndaCompanion.Theming;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// "Ask about a selection": the window in real controls (what it says about a selection, the quick checks, stored prompts, an instruction typed here, and
/// that nothing runs unless a button is pressed), the Ask shortcut (repair, clashes, backup) and the focused edition (the default look and a solid card).
/// </summary>
[Collection("Global state")]
public class AskTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public AskTests(HeadlessFixture fx) => _fx = fx;

    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static string Text(Window w, string name) => w.FindControl<TextBlock>(name)!.Text ?? "";

    private const string Table = "ANDA\tApplicant\tGoal date\n216482\tAcme\tOct 3, 2026\n214990\tBeta\tAug 15, 2026\n216482\tAcme\tOct 3, 2026";

    private sealed class Calls
    {
        public List<(CustomAction Action, string Selection)> Prompts = new();
        public List<string> Reviews = new(), Lookups = new();
    }

    private static (AskWindow Window, Calls Calls) Open(string text, IReadOnlyList<CustomAction>? prompts = null)
    {
        ThemeManager.Apply(AppearanceId.K);
        var calls = new Calls();
        var list = prompts ?? MyActionRules.Starters();
        var hooks = new AskHooks(() => list, (a, s) => calls.Prompts.Add((a, s)), calls.Reviews.Add, calls.Lookups.Add);
        var w = new AskWindow(hooks, text);
        w.Show(); Settle();
        return (w, calls);
    }

    // ---------- what the window says about a selection ----------

    [Fact]
    public async Task A_table_is_described_and_Find_duplicates_is_offered_but_nothing_runs_by_itself()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            var (w, calls) = Open(Table);
            string shape = Text(w, "ShapeText"), suggest = Text(w, "SuggestText");
            var btn = w.FindControl<Button>("SuggestBtn")!;
            bool quick = w.FindControl<StackPanel>("QuickPanel")!.IsVisible, resultBefore = w.FindControl<TextBox>("ResultBox")!.IsVisible;
            string label = btn.Content?.ToString() ?? "";
            Click(btn); Settle();
            string result = w.Result;
            bool copyVisible = w.FindControl<Button>("CopyResultBtn")!.IsVisible;
            int hooks = calls.Prompts.Count + calls.Reviews.Count + calls.Lookups.Count;
            w.Close(); Settle();
            return (shape, suggest, quick, resultBefore, label, result, copyVisible, hooks);
        }, CancellationToken.None);

        Assert.Equal("A table of 4 rows and 3 columns.", r.shape);
        Assert.Contains("Find duplicates", r.suggest);
        Assert.True(r.quick);
        Assert.False(r.resultBefore);                       // nothing is run until a button is pressed
        Assert.Equal("Find duplicates", r.label);
        Assert.Contains("Found 1 repeated row", r.result);
        Assert.True(r.copyVisible);
        Assert.Equal(0, r.hooks);                           // a quick check never calls a prompt, a lookup or a review
    }

    [Fact]
    public async Task A_late_change_notice_for_the_same_text_does_not_wipe_a_result_that_was_just_shown()
    {
        // On a real Windows machine the text box can announce a change after the window is shown, even though the text is the same.
        // The result of a quick check must survive that (the Windows self-test found it: the result was gone 250 ms later).
        var r = await _fx.Session.Dispatch(async () =>
        {
            var (w, _) = Open(Table);
            Click(w.FindControl<Button>("SuggestBtn")!); Settle();
            string before = w.Result;
            var box = w.FindControl<TextBox>("SelectionBox")!;
            box.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent));
            await Task.Delay(500); Settle(); Settle();
            string after = w.Result;
            // a real edit still re-reads the selection and clears the old result
            box.Text = "ANDA 216482";
            box.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent));       // what typing does
            await Task.Delay(500); Settle(); Settle();
            var res = (before, after, w.Result, Text(w, "ShapeText"));
            w.Close(); Settle();
            return res;
        }, CancellationToken.None);

        Assert.StartsWith("Found 1 repeated row", r.before);
        Assert.Equal(r.before, r.after);
        Assert.Equal("", r.Item3);
        Assert.Equal("An ANDA number: 216482.", r.Item4);
    }

    [Fact]
    public async Task Compare_columns_reads_the_two_numbers_and_explains_a_bad_pair()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            var (w, _) = Open("Our list\tTheir list\n216482\t216482\n214990\t999999\n123456\t214990");
            w.FindControl<TextBox>("ColA")!.Text = "1"; w.FindControl<TextBox>("ColB")!.Text = "2";
            Click(w.FindControl<Button>("CompareBtn")!); Settle();
            string good = w.Result;
            w.FindControl<TextBox>("ColB")!.Text = "x";
            Click(w.FindControl<Button>("CompareBtn")!); Settle();
            string bad = w.Result;
            w.FindControl<TextBox>("ColB")!.Text = "9";
            Click(w.FindControl<Button>("CompareBtn")!); Settle();
            string far = w.Result;
            w.Close(); Settle();
            return (good, bad, far);
        }, CancellationToken.None);

        Assert.Contains("In both column 1 (Our list) and column 2 (Their list): 2 values.", r.good);
        Assert.Contains("Only in column 1 (Our list): 1: “123456”.", r.good);
        Assert.Equal("Please type two column numbers, for example 1 and 2.", r.bad);
        Assert.Equal("Please choose two different columns between 1 and 2.", r.far);
    }

    [Fact]
    public async Task One_ANDA_number_offers_Look_it_up_and_a_paragraph_offers_Review_as_a_draft()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            var (w, calls) = Open("ANDA 216482");
            string shape = Text(w, "ShapeText"); string label = w.FindControl<Button>("SuggestBtn")!.Content?.ToString() ?? "";
            bool quick = w.FindControl<StackPanel>("QuickPanel")!.IsVisible;
            Click(w.FindControl<Button>("SuggestBtn")!); Settle();
            var lookups = calls.Lookups.ToList();
            w.FindControl<TextBox>("SelectionBox")!.Text = "Quick status. ANDA 216482 is on track, goal date Oct 10, 2026.";
            w.Analyse(); Settle();
            string label2 = w.FindControl<Button>("SuggestBtn")!.Content?.ToString() ?? "";
            Click(w.FindControl<Button>("SuggestBtn")!); Settle();
            var reviews = calls.Reviews.ToList();
            w.Close(); Settle();
            return (shape, label, quick, lookups, label2, reviews);
        }, CancellationToken.None);

        Assert.Equal("An ANDA number: 216482.", r.shape);
        Assert.Equal("Look it up", r.label);
        Assert.False(r.quick);                              // quick checks are for cells and lists only
        Assert.Equal(new[] { "216482" }, r.lookups);
        Assert.Equal("Review as a draft", r.label2);
        Assert.Single(r.reviews);
        Assert.StartsWith("Quick status.", r.reviews[0]);
    }

    [Fact]
    public async Task Nothing_selected_says_so_hides_the_checks_and_offers_no_button()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            var (w, _) = Open("");
            var res = (Text(w, "ShapeText"), w.FindControl<StackPanel>("QuickPanel")!.IsVisible, w.FindControl<Button>("SuggestBtn")!.IsVisible);
            w.Close(); Settle();
            return res;
        }, CancellationToken.None);

        Assert.Equal("Nothing is selected.", r.Item1);
        Assert.False(r.Item2);
        Assert.False(r.Item3);
    }

    [Fact]
    public async Task A_very_long_paste_is_cut_to_the_limit()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            var (w, _) = Open(new string('a', SelectionHelpers.MaxChars + 500));
            int n = (w.FindControl<TextBox>("SelectionBox")!.Text ?? "").Length;
            w.Close(); Settle();
            return n;
        }, CancellationToken.None);

        Assert.Equal(SelectionHelpers.MaxChars, r);
    }

    // ---------- prompts ----------

    [Fact]
    public async Task A_stored_prompt_gets_the_selection_added_and_is_only_handed_over_when_the_button_is_pressed()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            var (w, calls) = Open(Table);
            var box = w.FindControl<ComboBox>("PromptBox")!;
            var items = box.ItemsSource!.Cast<string>().ToList();
            Click(w.FindControl<Button>("UsePromptBtn")!); Settle();                  // nothing chosen yet
            string none = Text(w, "Status"); int before = calls.Prompts.Count;
            box.SelectedIndex = 0; Click(w.FindControl<Button>("UsePromptBtn")!); Settle();
            string status = Text(w, "Status");
            var call = calls.Prompts.SingleOrDefault();
            w.Close(); Settle();
            return (items, none, before, status, call);
        }, CancellationToken.None);

        Assert.Contains("Prepare a status question · fill and show", r.items);
        Assert.Contains("Ask my AI for next steps · ask my AI", r.items);
        Assert.DoesNotContain(r.items, i => i.StartsWith("Add to my to-do list"));      // only prompts, not the to-do action
        Assert.Equal("Please choose a stored prompt first.", r.none);
        Assert.Equal(0, r.before);
        Assert.Contains("Nothing was sent", r.status);
        Assert.Contains("details of one ANDA", r.status);                              // a table holds many ANDAs
        Assert.NotNull(r.call.Action);
        Assert.EndsWith("Selected text:\n{selection}", r.call.Action.Template);        // the selection is not silently left out
        Assert.Equal(Table, r.call.Selection);
    }

    [Fact]
    public async Task An_AI_prompt_says_a_preview_comes_first_and_the_stored_prompt_itself_is_not_changed()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            var prompts = MyActionRules.Starters();
            string original = prompts.First(p => p.Kind == ActionKind.AskAi).Template;
            var (w, calls) = Open("Some text to ask about.", prompts);
            var box = w.FindControl<ComboBox>("PromptBox")!;
            box.SelectedIndex = prompts.Where(p => p.Kind is ActionKind.FillPrompt or ActionKind.AskAi).ToList().FindIndex(p => p.Kind == ActionKind.AskAi);
            Click(w.FindControl<Button>("UsePromptBtn")!); Settle();
            string status = Text(w, "Status");
            string after = prompts.First(p => p.Kind == ActionKind.AskAi).Template;
            var call = calls.Prompts.Single().Action.Kind;
            w.Close(); Settle();
            return (original, after, status, call);
        }, CancellationToken.None);

        Assert.Equal(ActionKind.AskAi, r.call);
        Assert.Contains("exactly what would be sent", r.status);
        Assert.Contains("Nothing has been sent", r.status);
        Assert.Equal(r.original, r.after);                  // the copy that was handed over carries the selection; the stored prompt does not
    }

    [Fact]
    public async Task With_no_stored_prompts_the_picker_is_switched_off_and_says_where_to_add_one()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            var (w, _) = Open("text", new List<CustomAction>());
            var res = (w.FindControl<ComboBox>("PromptBox")!.IsEnabled, w.FindControl<Button>("UsePromptBtn")!.IsEnabled, w.FindControl<ComboBox>("PromptBox")!.PlaceholderText);
            w.Close(); Settle();
            return res;
        }, CancellationToken.None);

        Assert.False(r.Item1);
        Assert.False(r.Item2);
        Assert.Contains("Settings", r.Item3);
    }

    [Fact]
    public async Task Your_own_instruction_is_checked_then_filled_in_or_sent_to_the_preview()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            var (w, calls) = Open(Table);
            var own = w.FindControl<TextBox>("OwnBox")!;
            Click(w.FindControl<Button>("FillOwnBtn")!); Settle();
            string empty = Text(w, "Status");
            own.Text = "Compare {nonsense} please"; Click(w.FindControl<Button>("FillOwnBtn")!); Settle();
            string unknown = Text(w, "Status");
            own.Text = "list anything inconsistent";
            Click(w.FindControl<Button>("FillOwnBtn")!); Settle();
            Click(w.FindControl<Button>("AskOwnBtn")!); Settle();
            var calls2 = calls.Prompts.ToList();
            w.FindControl<TextBox>("SelectionBox")!.Text = "   ";
            Click(w.FindControl<Button>("AskOwnBtn")!); Settle();
            string blankSelection = Text(w, "Status"); int count = calls.Prompts.Count;
            w.Close(); Settle();
            return (empty, unknown, calls2, blankSelection, count);
        }, CancellationToken.None);

        Assert.Equal("Please type your instruction first.", r.empty);
        Assert.Contains("is not something Ask can fill in", r.unknown);
        Assert.Equal(2, r.calls2.Count);
        Assert.Equal(ActionKind.FillPrompt, r.calls2[0].Action.Kind);
        Assert.Equal(ActionKind.AskAi, r.calls2[1].Action.Kind);
        Assert.Equal("list anything inconsistent\n\nSelected text:\n{selection}", r.calls2[0].Action.Template);
        Assert.Equal("Please select or paste something first.", r.blankSelection);
        Assert.Equal(2, r.count);                           // nothing was handed over for an empty selection
    }

    // ---------- the helpers behind the buttons ----------

    [Fact]
    public void The_selection_is_added_once_and_a_prompt_that_is_too_long_is_refused()
    {
        Assert.Equal("Compare\n\nSelected text:\n{selection}", AskWindow.WithSelection("Compare"));
        Assert.Equal("Compare {selection}", AskWindow.WithSelection("Compare {selection}"));
        Assert.Equal("\n\nSelected text:\n{selection}", AskWindow.WithSelection(null));
        Assert.Null(AskWindow.WithSelection(new string('x', MyActionRules.MaxTemplateChars - 5)));
        Assert.NotNull(AskWindow.WithSelection(new string('x', 100) + "{selection}"));
    }

    [Theory]
    [InlineData("List anything odd in {selection}", false)]
    [InlineData("Today is {today}. {selection}", false)]
    [InlineData("Status of {number} ({drug})", true)]
    [InlineData("No placeholders at all", false)]
    public void Details_of_one_ANDA_are_told_apart_from_the_selection_and_the_date(string template, bool expected) =>
        Assert.Equal(expected, AskWindow.UsesRecordDetails(template));

    // ---------- the shortcut ----------

    [Fact]
    public void The_default_is_three_modifiers_so_Words_own_shortcuts_stay_free()
    {
        Assert.Equal("Ctrl+Alt+Shift+P", Settings.DefaultAskHotkey);
        Assert.Equal(3, Settings.DefaultAskHotkey.Split('+').Length - 1);
        Assert.Equal(Settings.DefaultAskHotkey, new Settings().AskHotkey);
        var others = new[] { "Ctrl+Alt+A", "Ctrl+Alt+Space", "Ctrl+Alt+Shift+R" };
        Assert.DoesNotContain(Settings.DefaultAskHotkey, others);
    }

    [Theory]
    [InlineData(null, "Ctrl+Alt+Shift+P")]                  // never saved: the default
    [InlineData("", "")]                                    // chosen to have none: stays none
    [InlineData("   ", "")]
    [InlineData("nonsense", "Ctrl+Alt+Shift+P")]            // unreadable: the default
    [InlineData("Ctrl+C", "Ctrl+Alt+Shift+P")]              // too few modifiers is unreadable
    [InlineData("ctrl+alt+shift+q", "Ctrl+Alt+Shift+Q")]    // readable: normalised
    [InlineData("Ctrl+Alt+A", "")]                          // clashes with Select & Peek: none
    [InlineData("Ctrl+Alt+Shift+R", "")]                          // clashes with Review a draft: none
    public void A_saved_Ask_shortcut_is_repaired(string? saved, string expected) =>
        Assert.Equal(expected, Settings.RepairAskHotkey(saved, new[] { "Ctrl+Alt+A", "Ctrl+Alt+Space", "Ctrl+Alt+Shift+R" }));

    [Fact]
    public void The_Ask_shortcut_is_registered_with_its_own_id_unless_it_clashes_and_the_to_do_shortcut_cannot_take_it()
    {
        var withAsk = HotkeyService.FromCombos("Ctrl+Alt+A", "Ctrl+Alt+Space", "Ctrl+Alt+Shift+R", null, "Ctrl+Alt+Shift+L", "Ctrl+Alt+Shift+P");
        var ask = Assert.Single(withAsk, b => b.Id == HotkeyService.IdAsk);
        Assert.Equal("Ctrl+Alt+Shift+P", ask.Combo);
        Assert.Equal(5, withAsk.Count);

        var none = HotkeyService.FromCombos("Ctrl+Alt+A", "Ctrl+Alt+Space", "Ctrl+Alt+Shift+R", null, null, "");
        Assert.DoesNotContain(none, b => b.Id == HotkeyService.IdAsk);

        var clash = HotkeyService.FromCombos("Ctrl+Alt+A", "Ctrl+Alt+Space", "Ctrl+Alt+Shift+R", null, "Ctrl+Alt+Shift+L", "Ctrl+Alt+Shift+L");
        Assert.Single(clash, b => b.Combo == "Ctrl+Alt+Shift+L");   // the second one asking for it is left out, never registered twice
    }

    [Fact]
    public void A_settings_file_without_an_Ask_shortcut_gets_the_default_and_one_that_clashes_is_dropped()
    {
        string dir = Temp.NewDir(), path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, "{\"PeekHotkey\":\"Ctrl+Alt+A\"}");
        Assert.Equal(Settings.DefaultAskHotkey, SettingsService.Load(path).AskHotkey);
        File.WriteAllText(path, "{\"AskHotkey\":\"Ctrl+Alt+A\"}");
        Assert.Equal("", SettingsService.Load(path).AskHotkey);
        File.WriteAllText(path, "{\"AskHotkey\":\"Ctrl+Alt+Shift+Q\",\"MyTodoHotkey\":\"Ctrl+Alt+Shift+Q\"}");
        var loaded = SettingsService.Load(path);
        Assert.Equal("Ctrl+Alt+Shift+Q", loaded.AskHotkey);
        Assert.Equal("", loaded.MyTodoHotkey);              // the later one yields to the earlier one
    }

    [Fact]
    public void The_Ask_shortcut_travels_with_a_backup_and_a_reset_restores_the_default()
    {
        string dir = Temp.NewDir();
        var lenses = new PromptLibraryService(Path.Combine(dir, "p.json")); var replies = new QuickReplyService(Path.Combine(dir, "r.json"));
        var source = new Settings { AskHotkey = "Ctrl+Alt+Shift+Q" };
        string json = SettingsBackup.Export(source, lenses, replies);
        var target = new Settings();
        var (ok, _) = SettingsBackup.Import(json, target, lenses, replies);
        Assert.True(ok);
        Assert.Equal("Ctrl+Alt+Shift+Q", target.AskHotkey);
        SettingsBackup.Reset(target, lenses, replies);
        Assert.Equal(Settings.DefaultAskHotkey, target.AskHotkey);
    }

    // ---------- the focused edition ----------

    [Fact]
    public void The_focused_edition_shows_the_default_look_and_a_solid_card_until_all_tools_are_shown()
    {
        var s = new Settings { Appearance = AppearanceId.P, CardOpacity = 0.6 };
        Assert.False(s.ShowAllTools);                       // focused by default
        Assert.Equal(Appearances.Default, Edition.Appearance(s));
        Assert.Equal(1.0, Edition.Opacity(s));
        s.ShowAllTools = true;
        Assert.Equal(AppearanceId.P, Edition.Appearance(s));  // the choice was kept and comes back
        Assert.Equal(0.6, Edition.Opacity(s));
    }

    [Fact]
    public void The_Settings_sections_in_the_focused_edition_are_the_five_that_matter()
    {
        var focused = Enum.GetValues<SettingsSection>().Where(s => SettingsSections.Visible(false).Contains(s)).ToList();
        Assert.Equal(5, focused.Count);
        Assert.True(Enum.GetValues<SettingsSection>().Length > focused.Count);
        Assert.Equal(Enum.GetValues<SettingsSection>().Length, SettingsSections.Visible(true).Count());
        Assert.Equal("Ask: my stored prompts", SettingsSections.Title(SettingsSection.Actions, false));
    }
}
