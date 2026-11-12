using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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
/// Round 24, the port of the simulation's notes to the Windows app: quick notes typed on the card and kept by the app, Enter goes straight
/// to a new note, Copy status is a small button and the S key, and "Open ANDA page" opens an https address the person set.
/// </summary>
[Collection("Global state")]
public class Round24NotesTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public Round24NotesTests(HeadlessFixture fx) => _fx = fx;
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static AndaRecord Late => new MockAndaRepository().Find("216482")!;
    private static string NewStore() => Path.Combine(Temp.NewDir(), "quick-notes.json");

    // ---------------------------------------------------------------- the store

    [Fact]
    public void A_note_is_cleaned_the_way_the_simulation_cleans_it()
    {
        Assert.Equal("a\nb", QuickNotesService.Clean("  a\r\nb  "));
        Assert.Equal("a\nb", QuickNotesService.Clean("a\rb"));
        Assert.Equal("a\n\nb", QuickNotesService.Clean("a\n\n\n\n\nb"));
        Assert.Equal("ab\tc", QuickNotesService.Clean("a\u0000b\tc\u0007\u001f"));          // control characters go; a tab stays
        Assert.Equal("", QuickNotesService.Clean(null));
        Assert.Equal("", QuickNotesService.Clean(" \r\n \t "));
        Assert.Equal(500, QuickNotesService.Clean(new string('x', 5000)).Length);
        Assert.Equal(499, QuickNotesService.Clean(new string('x', 499) + "😀" + "tail").Length);   // never half of a character pair
        Assert.Equal("<b>bold</b> & <script>alert(1)</script>", QuickNotesService.Clean("<b>bold</b> & <script>alert(1)</script>"));   // kept as text; nothing ever renders it as markup
    }

    [Fact]
    public void Notes_are_added_listed_newest_first_changed_deleted_and_restored()
    {
        var at = new DateTime(2026, 11, 12, 9, 0, 0);
        var store = new QuickNotesService(NewStore(), () => at);
        Assert.Equal((false, "Type a note first."), store.Add("216482", "   "));
        Assert.Equal((false, "Open an ANDA first."), store.Add("21648", "five digits"));
        Assert.Equal((false, "Open an ANDA first."), store.Add("../../x", "a path, not a number"));
        Assert.Equal((false, "Open an ANDA first."), store.Add(null, "no number"));
        Assert.Equal((true, "Note saved."), store.Add("216482", "Called the US agent."));
        at = at.AddMinutes(5);
        Assert.Equal((true, "Note saved."), store.Add("216482", "Response comes Tuesday."));
        Assert.Equal(new[] { "Response comes Tuesday.", "Called the US agent." }, store.For("216482").Select(n => n.Text));
        Assert.Equal(2, store.Count("216482"));
        Assert.Empty(store.For("214990"));                       // one ANDA's notes are never shown on another
        Assert.Equal(2, store.For("216482").Select(n => n.Id).Distinct().Count());

        string first = store.For("216482")[1].Id;
        Assert.Equal((true, "Note changed."), store.Edit("216482", first, " Called the US agent again. "));
        Assert.Equal("Called the US agent again.", store.For("216482")[1].Text);
        Assert.Equal((false, "A note cannot be empty. Use Delete to remove it."), store.Edit("216482", first, "  "));
        Assert.Equal((false, "That note is no longer here."), store.Edit("216482", "nope", "x"));
        Assert.Equal((false, "That note is no longer here."), store.Edit("214990", first, "x"));

        var gone = store.Delete("216482", first);
        Assert.NotNull(gone);
        Assert.Single(store.For("216482"));
        Assert.Null(store.Delete("216482", first));              // already gone
        Assert.True(store.Restore("216482", gone!.Value.At, gone.Value.Note));
        Assert.Equal(new[] { "Response comes Tuesday.", "Called the US agent again." }, store.For("216482").Select(n => n.Text));
        Assert.False(store.Restore("216482", 0, gone.Value.Note));   // not twice
    }

    [Fact]
    public void One_ANDA_holds_fifty_notes_and_says_so()
    {
        var store = new QuickNotesService(NewStore());
        for (int i = 0; i < 50; i++) Assert.True(store.Add("216482", "note " + i).Ok);
        Assert.Equal((false, "This ANDA already has 50 quick notes. Delete one to add another."), store.Add("216482", "one too many"));
        Assert.True(store.Add("214990", "another ANDA still has room").Ok);
    }

    [Fact]
    public void Notes_survive_a_restart_and_a_damaged_file_is_set_aside_not_overwritten()
    {
        string path = NewStore();
        var a = new QuickNotesService(path);
        a.Add("216482", "kept across a restart");
        Assert.Equal("kept across a restart", new QuickNotesService(path).For("216482").Single().Text);
        Assert.False(File.Exists(path + ".tmp"));                // written to a temporary file, then moved

        File.WriteAllText(path, "{ this is not json");
        var b = new QuickNotesService(path);
        Assert.Empty(b.For("216482"));
        Assert.Contains("could not be read", b.Problem);
        string aside = Path.Combine(Path.GetDirectoryName(path)!, "quick-notes.unreadable.json");
        Assert.Equal("{ this is not json", File.ReadAllText(aside));      // the person's file is still there, untouched
        Assert.True(b.Add("216482", "a new file starts").Ok);
        Assert.Null(b.Problem);
        Assert.Equal("a new file starts", new QuickNotesService(path).For("216482").Single().Text);
    }

    [Fact]
    public void A_file_edited_by_hand_is_read_safely()
    {
        string path = NewStore();
        File.WriteAllText(path, """
            { "Version": 1, "Notes": {
                "216482": [ { "Id": "a", "Text": "good", "When": "2026-11-12T09:00:00" }, { "Id": "a", "Text": "same id twice", "When": "2026-11-12T09:01:00" },
                            { "Id": "", "Text": "no id", "When": "2026-11-12T09:02:00" }, { "Id": "b", "Text": "   ", "When": "2026-11-12T09:03:00" }, null,
                            { "Id": "c", "Text": "ctrl\u0000char", "When": "2026-11-12T09:04:00" } ],
                "../../evil": [ { "Id": "x", "Text": "a key that is not a number", "When": "2026-11-12T09:00:00" } ],
                "21648": [ { "Id": "y", "Text": "five digits", "When": "2026-11-12T09:00:00" } ],
                "214990": null } }
            """);
        var store = new QuickNotesService(path);
        Assert.Null(store.Problem);
        Assert.Equal(new[] { "ctrlchar", "good" }, store.For("216482").Select(n => n.Text));
        Assert.Empty(store.For("../../evil"));
        Assert.Empty(store.For("21648"));
        Assert.Empty(store.For("214990"));
    }

    [Fact]
    public void A_folder_that_cannot_be_written_is_reported_and_the_note_is_still_shown()
    {
        string blocked = Path.Combine(Temp.NewDir(), "a-file-not-a-folder");
        File.WriteAllText(blocked, "x");
        var store = new QuickNotesService(Path.Combine(blocked, "quick-notes.json"));
        var (ok, message) = store.Add("216482", "cannot be saved");
        Assert.True(ok);
        Assert.StartsWith("Note added, but it could not be saved to this PC:", message);
        Assert.NotNull(store.Problem);
        Assert.Single(store.For("216482"));
    }

    // ---------------------------------------------------------------- the address of the ANDA page

    [Theory]
    [InlineData("https://tracker.example/anda/{anda}", "https://tracker.example/anda/216482", null)]
    [InlineData("  https://tracker.example/a?id={anda}&x={anda}  ", "https://tracker.example/a?id=216482&x=216482", null)]
    [InlineData("", null, "No web address is set for ANDA pages. Add one in Settings, My files and writing.")]
    [InlineData(null, null, "No web address is set for ANDA pages. Add one in Settings, My files and writing.")]
    [InlineData("http://tracker.example/anda/{anda}", null, "The ANDA page address must start with https:// and have no spaces. Please check it in Settings, My files and writing.")]
    [InlineData("javascript:alert({anda})", null, "The ANDA page address must start with https:// and have no spaces. Please check it in Settings, My files and writing.")]
    [InlineData("file:///C:/Windows/{anda}", null, "The ANDA page address must start with https:// and have no spaces. Please check it in Settings, My files and writing.")]
    [InlineData("https://tracker.example/anda/ {anda}", null, "The ANDA page address must start with https:// and have no spaces. Please check it in Settings, My files and writing.")]
    [InlineData("https://tracker.example/\"onclick=x/{anda}", null, "The ANDA page address must start with https:// and have no spaces. Please check it in Settings, My files and writing.")]
    [InlineData("https://tracker.example/anda/216482", null, "The ANDA page address needs {anda} where the six-digit number goes. Please check it in Settings, My files and writing.")]
    [InlineData("https://user:secret@tracker.example/{anda}", null, "The ANDA page address must be a plain https address. Please check it in Settings, My files and writing.")]
    public void Only_a_plain_https_address_with_the_number_in_it_is_opened(string? template, string? url, string? why)
    {
        Assert.Equal((url, why), AndaPage.Build("216482", template));
    }

    [Fact]
    public void Only_six_digits_are_ever_put_into_the_address_and_a_long_address_is_refused()
    {
        const string t = "https://tracker.example/anda/{anda}";
        foreach (string? bad in new[] { "21648", "2164822", "21648a", "216482/../../admin", "", null, " 216482" })
            Assert.Equal((null, "Open an ANDA first."), AndaPage.Build(bad, t));
        Assert.Equal((null, "That address is too long. Please check it in Settings, My files and writing."), AndaPage.Build("216482", "https://tracker.example/" + new string('a', 300) + "{anda}"));
        Assert.Null(AndaPage.Check(t));
        Assert.NotNull(AndaPage.Check("ftp://x/{anda}"));
        // a saved setting is trimmed and cut, never trusted
        string path = Path.Combine(Temp.NewDir(), "settings.json");
        File.WriteAllText(path, "{ \"AndaPageUrl\": \"   " + "https://tracker.example/" + new string('b', 600) + "   \" }");
        Assert.Equal(300, SettingsService.Load(path).AndaPageUrl.Length);
        Assert.Equal("", new Settings().AndaPageUrl);
    }

    // ---------------------------------------------------------------- the card

    private async Task<T> Card<T>(Func<CardWindow, QuickNotesService, List<string>, T> read, bool mini = false, AppearanceId id = AppearanceId.X, string template = "https://tracker.example/anda/{anda}", bool store = true)
    {
        return await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(id);
            var notes = new QuickNotesService(NewStore(), () => new DateTime(2026, 11, 12, 9, 30, 0));
            var opened = new List<string>();
            var w = new CardWindow(new OfflineBriefService(), new MockAndaRepository().Find)
            {
                CloseWhenDeactivated = false, Today = () => new DateOnly(2026, 7, 16),
                QuickNotes = store ? notes : null, AndaPageTemplate = () => template, OpenUrl = opened.Add,
            };
            try { w.Show(Late, new PixelPoint(40, 40), mini); Settle(); return read(w, notes, opened); }
            finally { w.Close(); Settle(); ThemeManager.Apply(Appearances.Default); }
        }, CancellationToken.None);
    }

    private static void Key(Interactive target, Key key, KeyModifiers mods = KeyModifiers.None) { target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods }); Settle(); }
    private static string MainLabel(CardWindow w) => string.Join(" ", w.FindControl<Button>("NotesChip")!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
    private static List<string> Texts(Control c) => c.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToList();

    [Fact]
    public async Task Enter_goes_straight_to_a_new_note_and_Ctrl_Enter_saves_it()
    {
        var r = await Card((w, notes, _) =>
        {
            string before = MainLabel(w);
            Key(w, Avalonia.Input.Key.Enter);
            var box = w.FindControl<TextBox>("QuickNoteBox")!;
            bool drawer = w.FindControl<StackPanel>("Drawer")!.IsVisible, open = w.FindControl<Expander>("MyNotesExpander")!.IsExpanded;
            bool focused = ReferenceEquals(w.FocusManager?.GetFocusedElement(), box);
            box.Text = "Called the US agent: the response will come next Tuesday.";
            string counter = w.FindControl<TextBlock>("QuickNoteCount")!.Text!;
            Key(box, Avalonia.Input.Key.Enter, KeyModifiers.Control);
            return (before, drawer, open, focused, counter, after: MainLabel(w), saved: notes.For("216482").Select(n => n.Text).ToList(), boxAfter: box.Text,
                    message: w.FindControl<TextBlock>("QuickNoteMsg")!.Text, header: w.FindControl<TextBlock>("MyNotesHeader")!.Text,
                    listed: Texts(w.FindControl<StackPanel>("QuickNotesList")!), hint: w.FindControl<TextBlock>("ActHint")!.Text);
        });
        Assert.Equal("Add a note · 3 notes Enter", r.before);                     // three project notes in the sample record
        Assert.True(r.drawer && r.open && r.focused);
        Assert.Equal("57 / 500", r.counter);
        Assert.Equal(new[] { "Called the US agent: the response will come next Tuesday." }, r.saved);
        Assert.Equal("Add a note · 4 notes Enter", r.after);
        Assert.True(string.IsNullOrEmpty(r.boxAfter));
        Assert.Equal("Note saved.", r.message);
        Assert.Equal("My notes · 1", r.header);
        Assert.Contains("Called the US agent: the response will come next Tuesday.", r.listed);
        Assert.Contains("Nov 12, 9:30 AM", r.listed);
        Assert.Equal("Enter adds a note · S or Copy status copies the status line", r.hint);
    }

    [Fact]
    public async Task A_key_typed_in_the_note_box_belongs_to_the_box()
    {
        var r = await Card((w, notes, _) =>
        {
            bool closed = false; w.Closed += (_, _) => closed = true;
            Key(w, Avalonia.Input.Key.Enter);
            var box = w.FindControl<TextBox>("QuickNoteBox")!;
            box.Text = "status";
            foreach (var k in new[] { Avalonia.Input.Key.S, Avalonia.Input.Key.E, Avalonia.Input.Key.R, Avalonia.Input.Key.C, Avalonia.Input.Key.Space, Avalonia.Input.Key.N }) Key(box, k);
            string copy = (string)w.FindControl<Button>("CopyChip")!.Content!;
            bool drawerStill = w.FindControl<StackPanel>("Drawer")!.IsVisible;
            Key(box, Avalonia.Input.Key.Enter);                    // a plain Enter in the box is a new line, not a save
            int savedAfterPlainEnter = notes.Count("216482");
            Key(box, Avalonia.Input.Key.Escape);
            bool closedAfterFirstEscape = closed;
            return (copy, drawerStill, savedAfterPlainEnter, closedAfterFirstEscape, text: box.Text);
        });
        Assert.Equal("Copy status", r.copy);                       // S typed in a note did not copy the status line
        Assert.True(r.drawerStill);                                // E did not fold the drawer
        Assert.Equal(0, r.savedAfterPlainEnter);
        Assert.False(r.closedAfterFirstEscape);                    // Escape leaves the box; it does not throw the note away with the card
        Assert.Contains("status", r.text);                         // the plain Enter added a line; nothing was lost
    }

    [Fact]
    public async Task A_half_typed_note_keeps_the_card_open_when_another_window_is_clicked()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.X);
            var w = new CardWindow(new OfflineBriefService(), new MockAndaRepository().Find) { CloseWhenDeactivated = true, QuickNotes = new QuickNotesService(NewStore()) };
            try
            {
                bool closed = false; w.Closed += (_, _) => closed = true;
                w.Show(Late, new PixelPoint(40, 40), false); Settle();
                Key(w, Avalonia.Input.Key.Enter);
                var box = w.FindControl<TextBox>("QuickNoteBox")!;
                bool emptyAtFirst = w.HasUnsavedNote;
                box.Text = "half a note";
                bool unsaved = w.HasUnsavedNote;
                Key(box, Avalonia.Input.Key.Enter, KeyModifiers.Control);
                return (emptyAtFirst, unsaved, afterSave: w.HasUnsavedNote, closed);
            }
            finally { w.Close(); Settle(); ThemeManager.Apply(Appearances.Default); }
        }, CancellationToken.None);
        Assert.Equal((false, true, false, false), r);        // with text in the box the card would stay; once saved it closes on a click elsewhere as before
    }

    [Fact]
    public async Task A_note_can_be_changed_deleted_put_back_and_copied()
    {
        var r = await Card((w, notes, _) =>
        {
            notes.Add("216482", "first"); notes.Add("216482", "second");
            Key(w, Avalonia.Input.Key.N);
            var list = w.FindControl<StackPanel>("QuickNotesList")!;
            Button Btn(int item, string label) => ((StackPanel)list.Children[item]).GetVisualDescendants().OfType<Button>().First(b => (string)b.Content! == label);
            var order = Texts(list).Where(t => t is "first" or "second").ToList();
            Btn(0, "Edit").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle();
            var box = w.FindControl<TextBox>("QuickNoteBox")!;
            string loaded = box.Text!, saveLabel = (string)w.FindControl<Button>("QuickNoteSave")!.Content!;
            box.Text = "second, corrected";
            w.FindControl<Button>("QuickNoteSave")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle();
            var afterEdit = notes.For("216482").Select(n => n.Text).ToList();
            Btn(1, "Delete").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle();
            var afterDelete = notes.For("216482").Select(n => n.Text).ToList();
            bool undoShown = w.FindControl<Button>("QuickNoteUndo")!.IsVisible; string label = MainLabel(w);
            w.FindControl<Button>("QuickNoteUndo")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle();
            return (order, loaded, saveLabel, afterEdit, afterDelete, undoShown, label, afterUndo: notes.For("216482").Select(n => n.Text).ToList(), msg: w.FindControl<TextBlock>("QuickNoteMsg")!.Text, labelEnd: MainLabel(w));
        });
        Assert.Equal(new[] { "second", "first" }, r.order);                       // newest first
        Assert.Equal(("second", "Save change"), (r.loaded, r.saveLabel));
        Assert.Equal(new[] { "second, corrected", "first" }, r.afterEdit);
        Assert.Equal(new[] { "second, corrected" }, r.afterDelete);
        Assert.True(r.undoShown);
        Assert.Equal("Add a note · 4 notes Enter", r.label);
        Assert.Equal(new[] { "second, corrected", "first" }, r.afterUndo);
        Assert.Equal("Note restored.", r.msg);
        Assert.Equal("Add a note · 5 notes Enter", r.labelEnd);
    }

    [Fact]
    public async Task On_the_small_card_Enter_opens_the_full_card_with_the_note_box()
    {
        var r = await Card((w, _, _) =>
        {
            bool miniBefore = w.FindControl<Button>("MoreBtn")!.IsVisible;
            Key(w, Avalonia.Input.Key.Enter);
            return (miniBefore, more: w.FindControl<Button>("MoreBtn")!.IsVisible, drawer: w.FindControl<StackPanel>("Drawer")!.IsVisible, open: w.FindControl<Expander>("MyNotesExpander")!.IsExpanded);
        }, mini: true);
        Assert.Equal((true, false, true, true), r);
    }

    [Theory]
    [InlineData(AppearanceId.K)]
    [InlineData(AppearanceId.P)]
    [InlineData(AppearanceId.T)]
    public async Task Every_full_look_has_the_same_notes_button_and_the_same_Enter(AppearanceId id)
    {
        var r = await Card((w, notes, _) =>
        {
            string label = MainLabel(w).Replace(" Enter", "");
            if (label.Length == 0) label = (string)w.FindControl<Button>("NotesChip")!.Content!;
            Key(w, Avalonia.Input.Key.Enter);
            return (label, open: w.FindControl<Expander>("MyNotesExpander")!.IsExpanded, open2: (string)w.FindControl<Button>("OpenChip")!.Content!);
        }, id: id);
        Assert.Equal(("Add a note · 3 notes", true, "Open ANDA page ↗"), r);
    }

    [Fact]
    public async Task Without_a_store_the_box_says_that_notes_cannot_be_kept()
    {
        var msg = await Card((w, _, _) =>
        {
            Key(w, Avalonia.Input.Key.Enter);
            var box = w.FindControl<TextBox>("QuickNoteBox")!; box.Text = "x";
            Key(box, Avalonia.Input.Key.Enter, KeyModifiers.Control);
            return w.FindControl<TextBlock>("QuickNoteMsg")!.Text;
        }, store: false);
        Assert.Equal("Notes cannot be kept in this window.", msg);
    }

    [Fact]
    public async Task Open_ANDA_page_hands_the_browser_one_https_address_and_the_card_steps_aside()
    {
        var r = await Card((w, _, opened) =>
        {
            bool closed = false; w.Closed += (_, _) => closed = true;
            w.FindControl<Button>("OpenChip")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle();
            return (opened: opened.ToList(), closed);
        });
        Assert.Equal(new[] { "https://tracker.example/anda/216482" }, r.opened);
        Assert.True(r.closed);
    }

    [Theory]
    [InlineData("", "No web address is set for ANDA pages. Add one in Settings, My files and writing.")]
    [InlineData("http://tracker.example/{anda}", "The ANDA page address must start with https:// and have no spaces. Please check it in Settings, My files and writing.")]
    [InlineData("javascript:alert({anda})", "The ANDA page address must start with https:// and have no spaces. Please check it in Settings, My files and writing.")]
    public async Task With_no_address_or_a_bad_one_nothing_is_opened_and_the_card_says_why(string template, string why)
    {
        var r = await Card((w, _, opened) =>
        {
            bool closed = false; w.Closed += (_, _) => closed = true;
            w.FindControl<Button>("OpenChip")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle();
            var msg = w.FindControl<TextBlock>("ChipMsg")!;
            return (opened.Count, closed, msg.IsVisible, msg.Text);
        }, template: template);
        Assert.Equal((0, false, true, why), r);
    }

    [Fact]
    public async Task A_browser_that_cannot_be_started_is_reported_and_the_card_stays()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.X);
            var w = new CardWindow(new OfflineBriefService(), new MockAndaRepository().Find) { CloseWhenDeactivated = false, AndaPageTemplate = () => "https://tracker.example/{anda}", OpenUrl = _ => throw new InvalidOperationException("no browser") };
            try
            {
                bool closed = false; w.Closed += (_, _) => closed = true;
                w.Show(Late, new PixelPoint(40, 40), false); Settle();
                w.FindControl<Button>("OpenChip")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle();
                return (closed, w.FindControl<TextBlock>("ChipMsg")!.Text);
            }
            finally { w.Close(); Settle(); ThemeManager.Apply(Appearances.Default); }
        }, CancellationToken.None);
        Assert.Equal((false, "The page could not be opened: no browser"), r);
    }

    [Fact]
    public async Task Settings_has_the_address_box_says_whether_it_would_work_and_keeps_what_was_typed()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            var settings = new Settings(); int applied = 0;
            var w = new SettingsWindow(settings, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => applied++);
            try
            {
                w.Show(); Settle();
                var box = w.FindControl<TextBox>("AndaPageBox")!; var status = w.FindControl<TextBlock>("AndaPageStatus")!;
                string empty = status.Text!;
                box.Text = "http://tracker.example/{anda}"; Settle(); string bad = status.Text!;
                box.Text = "https://tracker.example/anda/{anda}"; Settle(); string good = status.Text!;
                box.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(InputElement.LostFocusEvent)); Settle();
                return (empty, bad, good, settings.AndaPageUrl, applied, box.MaxLength);
            }
            finally { w.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.Equal("No address yet. The button on the card will say so.", r.empty);
        Assert.Equal("The ANDA page address must start with https:// and have no spaces. Please check it in Settings, My files and writing.", r.bad);
        Assert.Equal("This address can be opened. Example: https://tracker.example/anda/123456", r.good);
        Assert.Equal("https://tracker.example/anda/{anda}", r.AndaPageUrl);
        Assert.True(r.applied >= 1);
        Assert.Equal(300, r.MaxLength);
    }

    [Fact]
    public void The_notes_file_is_one_named_file_in_the_apps_own_folder()
    {
        Assert.Equal(Path.Combine(AppPaths.Root, "quick-notes.json"), AppPaths.QuickNotesFile);
    }

    [ShotFact]
    public async Task Shots_of_the_card_with_notes()
    {
        string dir = Environment.GetEnvironmentVariable("ANDA_SHOTS_DIR")!;
        Directory.CreateDirectory(dir);
        await Card((w, notes, _) =>
        {
            for (int i = 0; i < 30; i++) { Settle(); Thread.Sleep(12); }
            w.CaptureRenderedFrame()?.Save(Path.Combine(dir, "r24-card-x-main-action.png"));
            notes.Add("216482", "Called the US agent: the response will come next Tuesday.");
            Key(w, Avalonia.Input.Key.Enter);
            w.FindControl<TextBox>("QuickNoteBox")!.Text = "Ask Biopharmaceutics whether the dissolution data are enough.";
            for (int i = 0; i < 10; i++) { Settle(); Thread.Sleep(12); }
            w.CaptureRenderedFrame()?.Save(Path.Combine(dir, "r24-card-x-notes-open.png"));
            return 0;
        });
    }
}
