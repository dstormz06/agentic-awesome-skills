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

/// <summary>"My checklist" in real windows: the list, quick add, editing, undo, the day changing, "Notes to checklist", and its Settings.</summary>
[Collection("Global state")]
public class ChecklistUiTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public ChecklistUiTests(HeadlessFixture fx) => _fx = fx;

    private static readonly DateOnly Wed = new(2026, 11, 4);

    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static IEnumerable<T> All<T>(Visual v) where T : class => v.GetVisualDescendants().OfType<T>();
    private static T Named<T>(Visual v, string automationName) where T : Control => All<T>(v).First(c => AutomationProperties.GetName(c) == automationName);
    private static Button ByName(Visual v, string automationName) => All<Button>(v).First(b => AutomationProperties.GetName(b) == automationName);
    private static string Text(Window w, string name) => w.FindControl<TextBlock>(name)!.Text ?? "";

    private static ChecklistService NewStore() => new(Path.Combine(Temp.NewDir(), "checklist.json"));

    private static ChecklistWindow NewChecklist(ChecklistService store, Func<DateOnly> today, List<string>? opened = null, List<string>? cards = null)
    {
        var w = new ChecklistWindow(new ChecklistHooks(store, today, () => "Ctrl+Alt+L", s => opened?.Add(s ?? "(empty)"), (n, _) => cards?.Add(n)));
        w.Show(); Settle();
        return w;
    }

    private static NotesWindow NewNotes(ChecklistService store, string text, bool atOnce = false, Func<DateOnly>? today = null)
    {
        var w = new NotesWindow(new NotesHooks(store, today ?? (() => Wed), () => "Ctrl+Alt+L", () => { }), text, atOnce);
        w.Show(); Settle();
        return w;
    }

    private const string Messy = "Don't forget to call the plumber about the leak under the sink tomorrow!! Also email Sam re: the Thursday huddle. Need to submit the IR response for ANDA 216482 by Friday. groceries: milk, eggs, bread";

    // ---------- the list ----------

    [Fact]
    public async Task The_list_is_grouped_worst_first_and_says_what_to_start_with()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var store = NewStore();
            store.Add("Pay the water bill", ChecklistCategory.Home, Wed.AddDays(-1), false, Repeat.None, -1, null, "You", Wed);
            store.Add("Send the status memo", ChecklistCategory.Work, Wed, true, Repeat.None, -1, null, "You", Wed);
            store.Add("Mow the lawn", ChecklistCategory.Home, Wed.AddDays(3), false, Repeat.None, -1, null, "You", Wed);
            var w = NewChecklist(store, () => Wed);
            var headings = w.View.Groups.Select(g => g.Heading).ToList();
            string summary = Text(w, "Summary"), focus = Text(w, "FocusLine");
            bool rollVisible = w.FindControl<Button>("RollBtn")!.IsVisible;
            // the Work filter shows only work
            w.FindControl<ComboBox>("FilterBox")!.SelectedIndex = 1; Settle();
            var workOnly = w.View.Groups.SelectMany(g => g.Items).Select(i => i.Title).ToList();
            w.Close(); Settle();
            return (headings, summary, focus, rollVisible, workOnly);
        }, CancellationToken.None);

        Assert.Equal(new[] { "OVERDUE", "TODAY", "COMING UP · NEXT 7 DAYS" }, r.headings);
        Assert.Contains("3 open", r.summary);
        Assert.Contains("1 overdue", r.summary);
        Assert.Equal("Start with: Pay the water bill", r.focus);
        Assert.True(r.rollVisible);
        Assert.Equal(new[] { "Send the status memo" }, r.workOnly);
    }

    [Fact]
    public async Task Typing_shows_what_will_be_added_and_Add_stores_it_with_the_day_and_the_repeat()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var store = NewStore();
            var w = NewChecklist(store, () => Wed);
            var box = w.FindControl<TextBox>("AddBox")!;
            box.Text = "pay rent on the 1st every month"; Settle();
            string preview = Text(w, "AddPreview");
            Click(w.FindControl<Button>("AddBtn")!); Settle();
            var item = store.Items.SingleOrDefault();
            string status = Text(w, "AddStatus"), boxAfter = box.Text ?? "";
            // empty input explains itself and adds nothing
            box.Text = "   "; Click(w.FindControl<Button>("AddBtn")!); Settle();
            string empty = Text(w, "AddStatus");
            // the same thing again is not added twice
            box.Text = "pay rent on the 1st every month"; Click(w.FindControl<Button>("AddBtn")!); Settle();
            string twice = Text(w, "AddStatus"); int count = store.Items.Count;
            string rows = string.Join("|", All<TextBlock>(w).Select(t => t.Text));
            w.Close(); Settle();
            return (preview, item, status, boxAfter, empty, twice, count, rows);
        }, CancellationToken.None);

        Assert.StartsWith("Will add: ", r.preview);
        Assert.Contains("Home", r.preview);
        Assert.Contains("repeats every month", r.preview);
        Assert.NotNull(r.item);
        Assert.Equal(ChecklistCategory.Home, r.item!.Category);
        Assert.Equal(Repeat.Monthly, r.item.Repeat);
        Assert.Equal(1, r.item.RepeatDay);
        Assert.Equal("2026-12-01", r.item.Due);
        Assert.StartsWith("Added: ", r.status);
        Assert.Equal("", r.boxAfter);
        Assert.Equal("Please type what needs doing.", r.empty);
        Assert.Equal("That is already on your list.", r.twice);
        Assert.Equal(1, r.count);
        Assert.Contains("Pay rent", r.rows);
    }

    [Fact]
    public async Task Ticking_moving_editing_deleting_and_undoing_all_work()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var store = NewStore();
            var memo = store.Add("Send the status memo", ChecklistCategory.Work, Wed, true, Repeat.None, -1, null, "You", Wed).Item!;
            var lawn = store.Add("Mow the lawn", ChecklistCategory.Home, Wed.AddDays(3), false, Repeat.None, -1, null, "You", Wed).Item!;
            var bill = store.Add("Pay the water bill", ChecklistCategory.Home, null, false, Repeat.None, -1, null, "You", Wed).Item!;
            var w = NewChecklist(store, () => Wed);

            // tick
            Named<CheckBox>(w, "Mark finished: Send the status memo").IsChecked = true; Settle();
            bool memoDone = store.Items.First(i => i.Id == memo.Id).Done;
            int doneToday = w.View.DoneToday;
            string summary = Text(w, "Summary");

            // move to tomorrow
            Click(ByName(w, "Move to tomorrow: Mow the lawn")); Settle();
            string lawnDue = store.Items.First(i => i.Id == lawn.Id).Due!;

            // edit: an unreadable day is refused and explained, a readable one is saved
            Click(ByName(w, "Edit: Mow the lawn")); Settle();
            string? editing = w.EditingId;
            Named<TextBox>(w, "What needs doing for Mow the lawn").Text = "Mow the front lawn";
            var when = All<TextBox>(w).First(b => (AutomationProperties.GetName(b) ?? "").StartsWith("When it is due") && AutomationProperties.GetName(b)!.EndsWith("Mow the lawn"));
            when.Text = "banana"; Settle();
            w.SaveEdit(); Settle();
            string titleAfterBad = store.Items.First(i => i.Id == lawn.Id).Title;
            bool stillEditing = w.EditingId is not null;
            bool explained = All<TextBlock>(w).Any(t => (t.Text ?? "").Contains("could not read that date"));
            when.Text = "friday"; Settle();
            w.SaveEdit(); Settle();
            var saved = store.Items.First(i => i.Id == lawn.Id);

            // delete, then Undo
            Click(ByName(w, "Delete: Pay the water bill")); Settle();
            bool gone = store.Items.All(i => i.Id != bill.Id);
            var undo = w.FindControl<Button>("UndoBtn")!;
            bool undoShown = undo.IsVisible;
            Click(undo); Settle();
            bool back = store.Items.Any(i => i.Id == bill.Id);
            w.Close(); Settle();
            return (memoDone, doneToday, summary, lawnDue, editing, titleAfterBad, stillEditing, explained, saved.Title, saved.Due, w.EditingId, gone, undoShown, back, lawn.Id);
        }, CancellationToken.None);

        Assert.True(r.memoDone);
        Assert.Equal(1, r.doneToday);
        Assert.Contains("1 of 1 done today", r.summary);
        Assert.Equal("2026-11-05", r.lawnDue);
        Assert.Equal(r.Id, r.editing);
        Assert.Equal("Mow the lawn", r.titleAfterBad);                     // nothing changed while the date was unreadable
        Assert.True(r.stillEditing);
        Assert.True(r.explained);
        Assert.Equal("Mow the front lawn", r.Title);
        Assert.Equal("2026-11-06", r.Due);                                 // Friday
        Assert.Null(r.EditingId);
        Assert.True(r.gone);
        Assert.True(r.undoShown);
        Assert.True(r.back);
    }

    [Fact]
    public async Task A_repeating_item_finished_yesterday_is_back_today_when_the_day_changes_while_the_window_is_open()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var store = NewStore();
            var day = Wed;
            var item = store.Add("Check the inbox", ChecklistCategory.Work, day, false, Repeat.Daily, -1, null, "You", day).Item!;
            var w = NewChecklist(store, () => day);
            Named<CheckBox>(w, "Mark finished: Check the inbox").IsChecked = true; Settle();
            bool doneNow = store.Items.Single().Done;
            day = Wed.AddDays(1);                                           // midnight passes
            w.Reload(force: true); Settle();
            var after = store.Items.Single();
            string headings = string.Join(",", w.View.Groups.Select(g => g.Heading));
            w.Close(); Settle();
            return (doneNow, after.Done, after.Due, headings);
        }, CancellationToken.None);

        Assert.True(r.doneNow);
        Assert.False(r.Done);
        Assert.Equal("2026-11-05", r.Due);
        Assert.Equal("TODAY", r.headings);
    }

    [Fact]
    public async Task An_item_with_an_ANDA_number_opens_its_card_and_Paste_notes_opens_the_notes_window()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var store = NewStore();
            store.Add("Submit the IR response for ANDA 216482", ChecklistCategory.Work, Wed, false, Repeat.None, -1, "216482", "Notes", Wed);
            var opened = new List<string>(); var cards = new List<string>();
            var w = NewChecklist(store, () => Wed, opened, cards);
            Click(ByName(w, "Open the card for ANDA 216482")); Settle();
            Click(w.FindControl<Button>("NotesBtn")!); Settle();
            string meta = string.Join("|", All<TextBlock>(w).Select(t => t.Text));
            w.Close(); Settle();
            return (cards, opened, meta);
        }, CancellationToken.None);

        Assert.Equal(new[] { "216482" }, r.cards);
        Assert.Equal(new[] { "(empty)" }, r.opened);
        Assert.Contains("from your notes", r.meta);
    }

    [Fact]
    public async Task An_empty_checklist_says_how_to_start_and_names_the_shortcut()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var w = NewChecklist(NewStore(), () => Wed);
            string summary = Text(w, "Summary");
            bool rollHidden = !w.FindControl<Button>("RollBtn")!.IsVisible;
            w.Close(); Settle();
            return (summary, rollHidden);
        }, CancellationToken.None);

        Assert.Contains("empty", r.summary);
        Assert.Contains("Ctrl+Alt+L", r.summary);
        Assert.True(r.rollHidden);
    }

    // ---------- Notes to checklist ----------

    [Fact]
    public async Task Messy_notes_become_suggested_items_that_can_be_changed_and_are_added_only_on_request()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var store = NewStore();
            var w = NewNotes(store, Messy);
            int found = w.SuggestionCount, ticked = w.TickedCount;
            int beforeAdd = store.Items.Count;                              // nothing is added until the person says so
            string label = Text(w, "FoundLabel");

            // rename the first suggestion and untick the last
            Named<TextBox>(w, "What needs doing for Call the plumber about the leak under the sink tomorrow").Text = "Call the plumber about the sink";
            Named<CheckBox>(w, "Add to my checklist: Groceries: milk, eggs, bread").IsChecked = false; Settle();
            int tickedAfter = w.TickedCount;
            string button = w.FindControl<Button>("AddBtn")!.Content as string ?? "";
            Click(w.FindControl<Button>("AddBtn")!); Settle();
            var items = store.Items.ToList();
            string status = Text(w, "Status");
            bool undo = w.FindControl<Button>("UndoBtn")!.IsVisible;
            int tickedDone = w.TickedCount;
            w.Close(); Settle();
            return (found, ticked, beforeAdd, label, tickedAfter, button, items, status, undo, tickedDone);
        }, CancellationToken.None);

        Assert.Equal(4, r.found);
        Assert.Equal(4, r.ticked);
        Assert.Equal(0, r.beforeAdd);
        Assert.Equal("SUGGESTED ITEMS · 4", r.label);
        Assert.Equal(3, r.tickedAfter);
        Assert.Equal("Add the 3 ticked items", r.button);
        Assert.Equal(3, r.items.Count);
        var plumber = r.items.Single(i => i.Title == "Call the plumber about the sink");
        Assert.Equal(ChecklistCategory.Home, plumber.Category);
        Assert.True(plumber.High);
        Assert.Equal("2026-11-05", plumber.Due);
        Assert.Equal("Notes", plumber.Source);
        Assert.Equal("216482", r.items.Single(i => i.Title.StartsWith("Submit the IR response")).Number);
        Assert.StartsWith("Added 3 items to your checklist", r.status);
        Assert.True(r.undo);
        Assert.Equal(0, r.tickedDone);
    }

    [Fact]
    public async Task A_date_that_cannot_be_read_blocks_the_whole_batch_and_is_marked_beside_the_item()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var store = NewStore();
            var w = NewNotes(store, "call the plumber\nemail Sam");
            var when = All<TextBox>(w).First(b => (AutomationProperties.GetName(b) ?? "").StartsWith("When it is due") && AutomationProperties.GetName(b)!.EndsWith("Call the plumber"));
            when.Text = "someday soon-ish"; Settle();
            w.AddTicked(); Settle();
            int stored = store.Items.Count;
            string status = Text(w, "Status");
            bool problemShown = All<TextBlock>(w).Any(t => t.IsVisible && (t.Text ?? "").Contains("could not read that date") && t.Foreground is not null && !ReferenceEquals(t, w.FindControl<TextBlock>("Status")));
            // fixing it lets everything through
            when.Text = "tomorrow"; Settle();
            w.AddTicked(); Settle();
            int after = store.Items.Count;
            w.Close(); Settle();
            return (stored, status, problemShown, after);
        }, CancellationToken.None);

        Assert.Equal(0, r.stored);
        Assert.StartsWith("Nothing was added yet. Please fix 1 item", r.status);
        Assert.True(r.problemShown);
        Assert.Equal(2, r.after);
    }

    [Fact]
    public async Task Add_at_once_adds_the_task_like_items_and_Undo_takes_the_batch_back()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var store = NewStore();
            store.Add("Keep this one", ChecklistCategory.Other, null, false, Repeat.None, -1, null, "You", Wed);
            var w = NewNotes(store, "call the plumber tomorrow\nThe weather was lovely and the plumber was quick.\nemail Sam", atOnce: true);
            int afterAuto = store.Items.Count;
            string status = Text(w, "Status");
            bool undo = w.FindControl<Button>("UndoBtn")!.IsVisible;
            Click(w.FindControl<Button>("UndoBtn")!); Settle();
            int afterUndo = store.Items.Count;
            string undone = Text(w, "Status");
            w.Close(); Settle();
            return (afterAuto, status, undo, afterUndo, undone, store.Items.Single().Title);
        }, CancellationToken.None);

        Assert.Equal(3, r.afterAuto);                                      // the one already there + the two task-like lines; the remark is not added
        Assert.Contains("added at once", r.status);
        Assert.True(r.undo);
        Assert.Equal(1, r.afterUndo);
        Assert.Equal("Keep this one", r.Title);
        Assert.Contains("Removed 2 items", r.undone);
    }

    [Fact]
    public async Task Notes_with_nothing_in_them_and_notes_that_are_only_remarks_add_nothing_and_say_so()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var store = NewStore();
            var empty = NewNotes(store, "");
            string emptyNote = Text(empty, "FoundNote"); bool addOff = !empty.FindControl<Button>("AddBtn")!.IsEnabled;
            empty.Close();
            var remark = NewNotes(store, "The weather was lovely and the plumber was quick.", atOnce: true);
            string status = Text(remark, "Status"); int stored = store.Items.Count; int ticked = remark.TickedCount;
            remark.Close(); Settle();
            return (emptyNote, addOff, status, stored, ticked);
        }, CancellationToken.None);

        Assert.Contains("as soon as there are notes", r.emptyNote);
        Assert.True(r.addOff);
        Assert.Contains("Nothing looked like a task", r.status);
        Assert.Equal(0, r.stored);
        Assert.Equal(0, r.ticked);
    }

    // ---------- Settings: My checklist ----------

    [Fact]
    public async Task Settings_lets_the_shortcut_be_changed_cleared_reset_and_refuses_a_clash()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            using var home = new EnvScope(AppPaths.HomeEnvVar, Temp.NewDir());
            var settings = new Settings();
            var w = new SettingsWindow(settings, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => { });
            w.Show(); w.ExpandAll(); Settle();
            var box = Named<TextBox>(w, "My checklist shortcut");
            string initial = box.Text ?? "";
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.K, KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt }); Settle();
            string changed = settings.ChecklistHotkey;
            // the keys of Select & Peek are refused and nothing changes
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A, KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt }); Settle();
            string afterClash = settings.ChecklistHotkey; string clashText = w.FindControl<TextBlock>("ChecklistKeyStatus")!.Text ?? "";
            // a single modifier is refused
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.L, KeyModifiers = KeyModifiers.Control }); Settle();
            string afterWeak = settings.ChecklistHotkey;
            Click(w.FindControl<Button>("ChecklistKeyClear")!); Settle();
            string cleared = settings.ChecklistHotkey; string clearedBox = box.Text ?? "";
            Click(w.FindControl<Button>("ChecklistKeyReset")!); Settle();
            string reset = settings.ChecklistHotkey;
            // mode
            var add = All<RadioButton>(w).First(b => (b.Content as string ?? "").StartsWith("Add them at once"));
            add.IsChecked = true; Settle();
            var mode = settings.ChecklistMode;
            var bindings = HotkeyService.FromCombos(settings.PeekHotkey, settings.LookupHotkey, settings.DraftHotkey, settings.MyActions, settings.ChecklistHotkey);
            w.Close(); Settle();
            return (initial, changed, afterClash, clashText, afterWeak, cleared, clearedBox, reset, mode, bindings.Any(b => b.Id == HotkeyService.IdChecklist));
        }, CancellationToken.None);

        Assert.Equal("Ctrl+Alt+L", r.initial);
        Assert.Equal("Ctrl+Alt+K", r.changed);
        Assert.Equal("Ctrl+Alt+K", r.afterClash);
        Assert.False(string.IsNullOrWhiteSpace(r.clashText));
        Assert.Equal("Ctrl+Alt+K", r.afterWeak);
        Assert.Equal("", r.cleared);
        Assert.Equal("None", r.clearedBox);
        Assert.Equal("Ctrl+Alt+L", r.reset);
        Assert.Equal(ChecklistMode.AddAtOnce, r.mode);
        Assert.True(r.Item10);
    }
}
