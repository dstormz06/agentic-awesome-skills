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

/// <summary>"My to-do list" in real windows: the list, quick add, editing, undo, the day changing, "Notes to my to-do list", and its Settings.</summary>
[Collection("Global state")]
public class MyTodoUiTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public MyTodoUiTests(HeadlessFixture fx) => _fx = fx;

    private static readonly DateOnly Wed = new(2026, 11, 4);

    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static IEnumerable<T> All<T>(Visual v) where T : class => v.GetVisualDescendants().OfType<T>();
    private static T Named<T>(Visual v, string automationName) where T : Control => All<T>(v).First(c => AutomationProperties.GetName(c) == automationName);
    private static Button ByName(Visual v, string automationName) => All<Button>(v).First(b => AutomationProperties.GetName(b) == automationName);
    private static string Text(Window w, string name) => w.FindControl<TextBlock>(name)!.Text ?? "";

    private static MyTodoService NewStore() => new(Path.Combine(Temp.NewDir(), "mytodo.json"));

    private static MyTodoWindow NewMyTodo(MyTodoService store, Func<DateOnly> today, List<string>? opened = null, List<string>? cards = null)
    {
        var w = new MyTodoWindow(new MyTodoHooks(store, today, () => "Ctrl+Alt+Shift+L", s => opened?.Add(s ?? "(empty)"), (n, _) => cards?.Add(n)));
        w.Show(); Settle();
        return w;
    }

    private static NotesWindow NewNotes(MyTodoService store, string text, bool atOnce = false, Func<DateOnly>? today = null)
    {
        var w = new NotesWindow(new NotesHooks(store, today ?? (() => Wed), () => "Ctrl+Alt+Shift+L", () => { }), text, atOnce);
        w.Show(); Settle();
        return w;
    }

    private const string Messy = "Don't forget to confirm the facility inspection date with the team tomorrow ASAP. Also email the team lead re: the Thursday huddle. Need to submit the IR response for ANDA 216482 by Friday. errands: book the dentist, pick up the dry cleaning";

    // ---------- the list ----------

    [Fact]
    public async Task The_list_is_grouped_worst_first_and_says_what_to_start_with()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var store = NewStore();
            store.Add("Renew the vehicle registration", TodoCategory.Personal, Wed.AddDays(-1), false, Repeat.None, -1, null, "You", Wed);
            store.Add("Send the status memo", TodoCategory.Work, Wed, true, Repeat.None, -1, null, "You", Wed);
            store.Add("Book the dentist appointment", TodoCategory.Personal, Wed.AddDays(3), false, Repeat.None, -1, null, "You", Wed);
            var w = NewMyTodo(store, () => Wed);
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
        Assert.Equal("Start with: Renew the vehicle registration", r.focus);
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
            var w = NewMyTodo(store, () => Wed);
            var box = w.FindControl<TextBox>("AddBox")!;
            box.Text = "submit the expense report on the 1st every month"; Settle();
            string preview = Text(w, "AddPreview");
            Click(w.FindControl<Button>("AddBtn")!); Settle();
            var item = store.Items.SingleOrDefault();
            string status = Text(w, "AddStatus"), boxAfter = box.Text ?? "";
            // empty input explains itself and adds nothing
            box.Text = "   "; Click(w.FindControl<Button>("AddBtn")!); Settle();
            string empty = Text(w, "AddStatus");
            // the same thing again is not added twice
            box.Text = "submit the expense report on the 1st every month"; Click(w.FindControl<Button>("AddBtn")!); Settle();
            string twice = Text(w, "AddStatus"); int count = store.Items.Count;
            string rows = string.Join("|", All<TextBlock>(w).Select(t => t.Text));
            w.Close(); Settle();
            return (preview, item, status, boxAfter, empty, twice, count, rows);
        }, CancellationToken.None);

        Assert.StartsWith("Will add: ", r.preview);
        Assert.Contains("Work", r.preview);
        Assert.Contains("repeats every month", r.preview);
        Assert.NotNull(r.item);
        Assert.Equal(TodoCategory.Work, r.item!.Category);
        Assert.Equal(Repeat.Monthly, r.item.Repeat);
        Assert.Equal(1, r.item.RepeatDay);
        Assert.Equal("2026-12-01", r.item.Due);
        Assert.StartsWith("Added: ", r.status);
        Assert.Equal("", r.boxAfter);
        Assert.Equal("Please type what needs doing.", r.empty);
        Assert.Equal("That is already on your list.", r.twice);
        Assert.Equal(1, r.count);
        Assert.Contains("Submit the expense report", r.rows);
    }

    [Fact]
    public async Task Ticking_moving_editing_deleting_and_undoing_all_work()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var store = NewStore();
            var memo = store.Add("Send the status memo", TodoCategory.Work, Wed, true, Repeat.None, -1, null, "You", Wed).Item!;
            var dentist = store.Add("Book the dentist appointment", TodoCategory.Personal, Wed.AddDays(3), false, Repeat.None, -1, null, "You", Wed).Item!;
            var vehicle = store.Add("Renew the vehicle registration", TodoCategory.Personal, null, false, Repeat.None, -1, null, "You", Wed).Item!;
            var w = NewMyTodo(store, () => Wed);

            // tick
            Named<CheckBox>(w, "Mark finished: Send the status memo").IsChecked = true; Settle();
            bool memoDone = store.Items.First(i => i.Id == memo.Id).Done;
            int doneToday = w.View.DoneToday;
            string summary = Text(w, "Summary");

            // move to tomorrow
            Click(ByName(w, "Move to tomorrow: Book the dentist appointment")); Settle();
            string dentistDue = store.Items.First(i => i.Id == dentist.Id).Due!;

            // edit: an unreadable day is refused and explained, a readable one is saved
            Click(ByName(w, "Edit: Book the dentist appointment")); Settle();
            string? editing = w.EditingId;
            Named<TextBox>(w, "What needs doing for Book the dentist appointment").Text = "Book the dentist appointment for December";
            var when = All<TextBox>(w).First(b => (AutomationProperties.GetName(b) ?? "").StartsWith("When it is due") && AutomationProperties.GetName(b)!.EndsWith("Book the dentist appointment"));
            when.Text = "not a date"; Settle();
            w.SaveEdit(); Settle();
            string titleAfterBad = store.Items.First(i => i.Id == dentist.Id).Title;
            bool stillEditing = w.EditingId is not null;
            bool explained = All<TextBlock>(w).Any(t => (t.Text ?? "").Contains("could not read that date"));
            when.Text = "friday"; Settle();
            w.SaveEdit(); Settle();
            var saved = store.Items.First(i => i.Id == dentist.Id);

            // delete, then Undo
            Click(ByName(w, "Delete: Renew the vehicle registration")); Settle();
            bool gone = store.Items.All(i => i.Id != vehicle.Id);
            var undo = w.FindControl<Button>("UndoBtn")!;
            bool undoShown = undo.IsVisible;
            Click(undo); Settle();
            bool back = store.Items.Any(i => i.Id == vehicle.Id);
            w.Close(); Settle();
            return (memoDone, doneToday, summary, dentistDue, editing, titleAfterBad, stillEditing, explained, saved.Title, saved.Due, w.EditingId, gone, undoShown, back, dentist.Id);
        }, CancellationToken.None);

        Assert.True(r.memoDone);
        Assert.Equal(1, r.doneToday);
        Assert.Contains("1 of 1 done today", r.summary);
        Assert.Equal("2026-11-05", r.dentistDue);
        Assert.Equal(r.Id, r.editing);
        Assert.Equal("Book the dentist appointment", r.titleAfterBad);                     // nothing changed while the date was unreadable
        Assert.True(r.stillEditing);
        Assert.True(r.explained);
        Assert.Equal("Book the dentist appointment for December", r.Title);
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
            var item = store.Add("Check the inbox", TodoCategory.Work, day, false, Repeat.Daily, -1, null, "You", day).Item!;
            var w = NewMyTodo(store, () => day);
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
            store.Add("Submit the IR response for ANDA 216482", TodoCategory.Work, Wed, false, Repeat.None, -1, "216482", "Notes", Wed);
            var opened = new List<string>(); var cards = new List<string>();
            var w = NewMyTodo(store, () => Wed, opened, cards);
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
    public async Task An_empty_myTodo_says_how_to_start_and_names_the_shortcut()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var w = NewMyTodo(NewStore(), () => Wed);
            string summary = Text(w, "Summary");
            bool rollHidden = !w.FindControl<Button>("RollBtn")!.IsVisible;
            w.Close(); Settle();
            return (summary, rollHidden);
        }, CancellationToken.None);

        Assert.Contains("empty", r.summary);
        Assert.Contains("Ctrl+Alt+Shift+L", r.summary);
        Assert.True(r.rollHidden);
    }

    // ---------- Notes to my to-do list ----------

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
            Named<TextBox>(w, "What needs doing for Confirm the facility inspection date with the team tomorrow ASAP").Text = "Confirm the inspection date with the facility team";
            Named<CheckBox>(w, "Add to my to-do list: Errands: book the dentist, pick up the dry cleaning").IsChecked = false; Settle();
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
        var inspection = r.items.Single(i => i.Title == "Confirm the inspection date with the facility team");
        Assert.Equal(TodoCategory.Work, inspection.Category);
        Assert.True(inspection.High);
        Assert.Equal("2026-11-05", inspection.Due);
        Assert.Equal("Notes", inspection.Source);
        Assert.Equal("216482", r.items.Single(i => i.Title.StartsWith("Submit the IR response")).Number);
        Assert.StartsWith("Added 3 items to your to-do list", r.status);
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
            var w = NewNotes(store, "call the applicant\nemail the team lead");
            var when = All<TextBox>(w).First(b => (AutomationProperties.GetName(b) ?? "").StartsWith("When it is due") && AutomationProperties.GetName(b)!.EndsWith("Call the applicant"));
            when.Text = "sometime soon"; Settle();
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
            store.Add("Keep this one", TodoCategory.Other, null, false, Repeat.None, -1, null, "You", Wed);
            var w = NewNotes(store, "call the applicant tomorrow\nThe inspection report was clear and complete.\nemail the team lead", atOnce: true);
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
            var remark = NewNotes(store, "The inspection report was clear and complete.", atOnce: true);
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

    // ---------- the actions editor knows the to-do shortcut ----------

    [Fact]
    public async Task An_action_cannot_take_the_myTodo_shortcut_and_says_why()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            using var home = new EnvScope(AppPaths.HomeEnvVar, Temp.NewDir());
            var settings = new Settings { ShowAllTools = true };
            var w = new SettingsWindow(settings, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => { });
            w.Show(); w.ExpandAll(); Settle();
            Click(w.FindControl<Button>("ActionAdd")!); Settle();
            var added = settings.MyActions[^1];
            var box = Named<TextBox>(w, "Shortcut for " + added.Name);
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.L, KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift }); Settle();
            string hotkey = added.Hotkey, status = w.FindControl<TextBlock>("ActionsStatus")!.Text ?? "";
            w.Close(); Settle();
            return (hotkey, status);
        }, CancellationToken.None);

        Assert.Equal("", r.hotkey);
        Assert.Contains("My to-do list", r.status);
    }

    // ---------- Settings: My to-do list ----------

    [Fact]
    public async Task Settings_lets_the_shortcut_be_changed_cleared_reset_and_refuses_a_clash()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            using var home = new EnvScope(AppPaths.HomeEnvVar, Temp.NewDir());
            var settings = new Settings { ShowAllTools = true };
            var w = new SettingsWindow(settings, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => { });
            w.Show(); w.ExpandAll(); Settle();
            var box = Named<TextBox>(w, "My to-do list shortcut");
            string initial = box.Text ?? "";
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.K, KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt }); Settle();
            string changed = settings.MyTodoHotkey;
            // the keys of Select & Peek are refused and nothing changes
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A, KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt }); Settle();
            string afterClash = settings.MyTodoHotkey; string clashText = w.FindControl<TextBlock>("MyTodoKeyStatus")!.Text ?? "";
            // a single modifier is refused
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.L, KeyModifiers = KeyModifiers.Control }); Settle();
            string afterWeak = settings.MyTodoHotkey;
            Click(w.FindControl<Button>("MyTodoKeyClear")!); Settle();
            string cleared = settings.MyTodoHotkey; string clearedBox = box.Text ?? "";
            Click(w.FindControl<Button>("MyTodoKeyReset")!); Settle();
            string reset = settings.MyTodoHotkey;
            // mode
            var add = All<RadioButton>(w).First(b => (b.Content as string ?? "").StartsWith("Add them at once"));
            add.IsChecked = true; Settle();
            var mode = settings.MyTodoMode;
            var bindings = HotkeyService.FromCombos(settings.PeekHotkey, settings.LookupHotkey, settings.DraftHotkey, settings.MyActions, settings.MyTodoHotkey);
            w.Close(); Settle();
            return (initial, changed, afterClash, clashText, afterWeak, cleared, clearedBox, reset, mode, bindings.Any(b => b.Id == HotkeyService.IdMyTodo));
        }, CancellationToken.None);

        Assert.Equal("Ctrl+Alt+Shift+L", r.initial);
        Assert.Equal("Ctrl+Alt+K", r.changed);
        Assert.Equal("Ctrl+Alt+K", r.afterClash);
        Assert.False(string.IsNullOrWhiteSpace(r.clashText));
        Assert.Equal("Ctrl+Alt+K", r.afterWeak);
        Assert.Equal("", r.cleared);
        Assert.Equal("None", r.clearedBox);
        Assert.Equal("Ctrl+Alt+Shift+L", r.reset);
        Assert.Equal(MyTodoMode.AddAtOnce, r.mode);
        Assert.True(r.Item10);
    }
}
