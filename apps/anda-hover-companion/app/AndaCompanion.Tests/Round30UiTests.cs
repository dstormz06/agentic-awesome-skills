using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AndaCompanion.Services;
using AndaCompanion.Theming;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// Round 30 in the Windows app: the Backups and archive window, on real files in a folder of the test's own. The rules are pinned to the
/// simulation in <see cref="Round30Tests"/>.
/// </summary>
[Collection("Global state")]
public class Round30UiTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public Round30UiTests(HeadlessFixture fx) => _fx = fx;
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static string[] Texts(Control c) => c.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
    private static readonly DateOnly Today = new(2026, 11, 18);

    [Fact]
    public async Task The_window_shows_the_copies_of_each_list_restores_one_with_one_press_and_puts_an_archived_item_back()
    {
        string dir = Temp.NewDir(), file = Path.Combine(dir, "todo.json"), mineFile = Path.Combine(dir, "mytodo.json"), log = Path.Combine(dir, "events.csv");
        var now = new DateTime(2026, 11, 18, 20, 0, 0, DateTimeKind.Utc);
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var todo = new TodoService(file); var mine = new MyTodoService(mineFile);
        int keep = 30, days = 30; var keepSet = new List<int>(); var daysSet = new List<int>();
        var b = new Backups(Path.Combine(dir, "backups"), () => keep, () => eastern, () => now);
        b.Register(new("todo", "ANDA to-do list", file, "item", "items", TodoService.CountIn, () => todo.Reload()));
        b.Register(new("mytodo", "My to-do list", mineFile, "item", "items", MyTodoService.CountIn, () => mine.Reload()));
        SafeFile.Written += b.Written;
        try
        {
            EventLog.Configure(log);
            for (int i = 0; i < 12; i++) { todo.Add("Change number " + i, null, null, null, "You", Today); now = now.AddMinutes(1); }
            string gone = todo.Add("<b>Send</b> the meeting minutes", "216482", null, null, "You", Today).Item!.Id; now = now.AddMinutes(1);
            todo.SetDone(gone, true, Today); now = now.AddMinutes(1); todo.ClearCompleted(Today); now = now.AddMinutes(1);
            string other = mine.Add("File the weekly report", TodoCategory.Other, null, false, Repeat.None, -1, null, "You", Today).Item!.Id; now = now.AddMinutes(1);
            mine.SetDone(other, true, Today); mine.ClearFinished(Today);
            string waitingId = mine.Add("Finished ten days ago", TodoCategory.Other, null, false, Repeat.None, -1, null, "You", Today.AddDays(-10)).Item!.Id;      // its list shows a finished item for seven days
            mine.SetDone(waitingId, true, Today.AddDays(-10));

            var r = await _fx.Session.Dispatch(() =>
            {
                ThemeManager.Apply(Appearances.Default);
                var w = new BackupsWindow(b, () => keep, n => { keep = n; keepSet.Add(n); }, () => days, n => { days = n; daysSet.Add(n); todo.ArchiveAfterDays = n; },
                    new[]
                    {
                        new ArchiveSource("ANDA to-do list", "the ANDA to-do list", () => TodoArchive.Lines(file), id => todo.PutBack(id, Today), () => todo.Waiting(Today), id => todo.SetDone(id, false, Today)),
                        new ArchiveSource("My to-do list", "My to-do list", () => TodoArchive.Lines(mineFile), id => mine.PutBack(id, Today), () => mine.Waiting(Today), id => mine.SetDone(id, false, Today)),
                    }, eastern);
                w.Show(); Settle();
                object? Focused() => w.FocusManager?.GetFocusedElement();
                bool startsOnKeep = ReferenceEquals(Focused(), w.KeepBox);
                ComboBox Pick(int k) => w.Lists.GetVisualDescendants().OfType<ComboBox>().Where(c => (c.Tag as string) == "copies").ElementAt(k);
                Button Go(int k) => w.Lists.GetVisualDescendants().OfType<Button>().Where(x => (x.Tag as string) == "restore").ElementAt(k);
                string Sub(int k) => w.Lists.GetVisualDescendants().OfType<TextBlock>().Where(t => (t.Tag as string) == "sub").ElementAt(k).Text ?? "";
                var names = Texts(w.Lists).Where(t => t is "ANDA to-do list" or "My to-do list").ToArray();
                var first = (Sub: Sub(0), Items: ((IEnumerable<string>)Pick(0).ItemsSource!).Take(3).ToArray(), Enabled: Go(0).IsEnabled, Name: AutomationProperties.GetName(Go(0)), KeepShown: w.KeepBox.SelectedItem as string, DaysShown: w.DaysBox.SelectedItem as string);
                // An earlier copy is chosen and put back with one press.
                Pick(0).SelectedIndex = 2; Settle(); bool offered = Go(0).IsEnabled;
                int before = todo.Items.Count; Click(Go(0)); Settle();
                var restored = (Count: todo.Items.Count, Was: before, Status: w.Status.Text ?? "", Copies: b.Copies("todo").Count, Titles: todo.Items.Select(i => i.Title).ToArray());
                bool focusAfterRestore = ReferenceEquals(Focused(), Pick(0));
                // A copy made elsewhere while the window is open does not change which copy is chosen.
                Pick(0).SelectedIndex = 3; Settle(); string chosen = (string)Pick(0).SelectedItem!;
                mine.Add("Made elsewhere", TodoCategory.Other, null, false, Repeat.None, -1, null, "You", Today); now = now.AddMinutes(1); w.Paint(); Settle();
                bool stays = (string)Pick(0).SelectedItem! == chosen;
                Pick(0).SelectedIndex = 0; Settle();
                // The archive: both lists, newest first; searched; an item put back.
                string[] Rows() => w.Archive.Children.OfType<Grid>().Select(g => string.Join(" | ", g.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text))).ToArray();
                var archive = (Rows: Rows(), Count: w.Count.Text ?? "", Buttons: w.Archive.GetVisualDescendants().OfType<Button>().Count());
                w.FindBox.Text = "MINUTES"; Settle(); var found = (Rows: Rows().Length, Count: w.Count.Text ?? "");
                w.FindBox.Text = "no such thing"; Settle(); var none = (Rows: Rows().Length, Count: w.Count.Text ?? "");
                w.FindBox.Text = "216482"; Settle();
                // The copy that was put back holds this item, so the archive does not add it a second time.
                Click(w.Archive.GetVisualDescendants().OfType<Button>().Single()); Settle();
                var already = (Status: w.Status.Text ?? "", Buttons: w.Archive.GetVisualDescendants().OfType<Button>().Count(), Count: todo.Items.Count);
                Pick(0).SelectedIndex = 5; Settle(); Click(Go(0)); Settle();                                   // an earlier copy, from before the item was added
                var earlier = (Count: todo.Items.Count, Copies: b.Copies("todo").Count, Has: todo.Items.Any(i => i.Id == gone));
                Click(w.Archive.GetVisualDescendants().OfType<Button>().Single()); Settle();
                var put = (Status: w.Status.Text ?? "", Buttons: w.Archive.GetVisualDescendants().OfType<Button>().Count(), Row: Rows().Single(), OnList: todo.Items.Any(i => i.Id == gone && !i.Done));
                bool focusAfterPut = ReferenceEquals(Focused(), w.FindBox);
                // A finished item that its list no longer shows and that is not archived yet is found here, and can be opened again.
                w.FindBox.Text = ""; Settle();
                var waiting = (Row: Rows().First(), Count: w.Count.Text ?? "", Shown: MyTodoBuilder.Build(mine.Items, Today, 0, null).Groups.SelectMany(g => g.Items).Any(i => i.Id == waitingId));
                Click(w.Archive.GetVisualDescendants().OfType<Button>().First(x => (x.Tag as string) == "openagain")); Settle();
                var again = (Status: w.Status.Text ?? "", Open: mine.Items.Any(i => i.Id == waitingId && !i.Done), Left: w.Archive.GetVisualDescendants().OfType<Button>().Count(x => (x.Tag as string) == "openagain"));
                // A smaller number of copies would remove copies: it is asked about first, and nothing is removed by choosing it.
                int copiesBefore = b.Copies("todo").Count;
                w.KeepBox.SelectedIndex = 0; Settle();
                var asked = (Status: w.Status.Text ?? "", Copies: b.Copies("todo").Count, Button: w.KeepGo.IsVisible ? w.KeepGo.Content as string : null, Set: keepSet.Count, Shown: w.KeepBox.SelectedItem as string);
                w.KeepBox.SelectedIndex = 1; Settle(); var withdrawn = (w.KeepGo.IsVisible, b.Copies("todo").Count, keepSet.Count);
                w.KeepBox.SelectedIndex = 0; Settle(); Click(w.KeepGo); Settle();
                var fewer = (Status: w.Status.Text ?? "", Copies: b.Copies("todo").Count, Button: w.KeepGo.IsVisible, Focus: ReferenceEquals(Focused(), w.KeepBox));
                w.KeepBox.SelectedIndex = 2; Settle(); string more = w.Status.Text ?? "";
                w.DaysBox.SelectedIndex = 0; Settle(); string daysSaid = w.Status.Text ?? "";
                var buttons = w.GetVisualDescendants().OfType<Button>().Select(x => x.Content as string ?? "").ToArray();
                bool carries = ConnectionBar.Carries(w), scrolls = w.Content is ScrollViewer { MaxHeight: > 100 and < 4000 };
                var live = (AutomationProperties.GetLiveSetting(w.Status), AutomationProperties.GetLiveSetting(w.Count), AutomationProperties.GetName(w.FindBox), AutomationProperties.GetName(w.KeepBox));
                w.Close();
                return (names, first, offered, restored, archive, found, none, put, fewer, more, daysSaid, buttons, carries, scrolls, live, already, earlier, startsOnKeep, focusAfterRestore, stays, focusAfterPut, waiting, again, copiesBefore, asked, withdrawn);
            }, CancellationToken.None);

            Assert.Equal(new[] { "ANDA to-do list", "My to-do list" }, r.names);
            Assert.Equal("15 copies · newest Nov 18, 2026, 3:14:00 PM EST", r.first.Sub);                    // with the year: two copies a year apart must not read alike
            Assert.Equal(new[] { "Nov 18, 2026, 3:14:00 PM EST · 12 items (as it is now)", "Nov 18, 2026, 3:13:00 PM EST · 13 items", "Nov 18, 2026, 3:12:00 PM EST · 13 items" }, r.first.Items);
            Assert.Equal((false, "Restore this copy of ANDA to-do list", "30", "30"), (r.first.Enabled, r.first.Name, r.first.KeepShown, r.first.DaysShown));      // the newest copy is the list as it is: nothing to restore
            Assert.True(r.startsOnKeep);                                                                     // the keyboard starts on the first control, not on nothing
            Assert.True(r.focusAfterRestore); Assert.True(r.focusAfterPut); Assert.True(r.stays);            // and stays where the person was after a press
            Assert.True(r.offered);
            Assert.Equal((13, 12, 16), (r.restored.Count, r.restored.Was, r.restored.Copies));                // the copy came back, and the list as it was is one copy more
            Assert.Equal("ANDA to-do list: restored to the copy of Nov 18, 2026, 3:12:00 PM EST. What was there a moment ago is still among the copies, so this can be undone.", r.restored.Status);
            Assert.Contains("<b>Send</b> the meeting minutes", r.restored.Titles);                           // markup in a title is text
            Assert.Equal(new[]
            {
                "ANDA 216482: <b>Send</b> the meeting minutes | ANDA to-do list · archived Nov 18 · cleared by the person | Put back on the list",
                "File the weekly report | My to-do list · archived Nov 18 · cleared by the person | Put back on the list",
                "Finished ten days ago | My to-do list · finished Nov 8 · still on its list, no longer shown there · archived after 30 days | Open it again",
            }, r.archive.Rows.OrderBy(x => x, StringComparer.Ordinal));
            Assert.Equal(("2 archived items. 1 finished item is still on its list, waiting to be archived.", 3), (r.archive.Count, r.archive.Buttons));
            Assert.Equal((1, "1 of 2 archived items matches. 1 finished item is still on its list, waiting to be archived; 0 match."), r.found);
            Assert.Equal((0, "0 of 2 archived items match. 1 finished item is still on its list, waiting to be archived; 0 match."), r.none);
            Assert.StartsWith("Finished ten days ago | My to-do list · finished Nov 8", r.waiting.Row); Assert.False(r.waiting.Shown);      // its own list no longer shows it; this window does
            Assert.Equal(("“Finished ten days ago” is open again on My to-do list.", true, 0), r.again);
            Assert.Equal(("That item is on the list already. Nothing was changed.", 1, 13), r.already);
            Assert.Equal((11, 17, false), r.earlier);
            Assert.Equal("“<b>Send</b> the meeting minutes” is back on the ANDA to-do list, as an open item.", r.put.Status);
            Assert.Equal((0, true), (r.put.Buttons, r.put.OnList));
            Assert.Equal("ANDA 216482: <b>Send</b> the meeting minutes | ANDA to-do list · archived Nov 18 · cleared by the person · put back Nov 18", r.put.Row);
            int gone30 = r.copiesBefore - 10;
            Assert.Equal(($"Keeping 10 would remove {gone30} older backup copies now, and that cannot be undone. Nothing has been removed. Press “Keep 10 and remove {gone30} older copies” to go ahead, or choose a larger number.", r.copiesBefore, $"Keep 10 and remove {gone30} older copies", 0, "10"),
                (r.asked.Status, r.asked.Copies, r.asked.Button, r.asked.Set, r.asked.Shown));              // choosing it removed nothing and changed no setting
            Assert.Equal((false, r.copiesBefore, 1), r.withdrawn);                                          // choosing 30 again withdraws the question (30 is set, nothing removed)
            Assert.Equal(($"{gone30} older backup copies were removed. Your lists and the archive were not changed.", 10, false, true), (r.fewer.Status, r.fewer.Copies, r.fewer.Button, r.fewer.Focus));
            Assert.Equal("From now on 60 copies of each list are kept. Nothing was removed.", r.more);
            Assert.Equal("From now on a finished item is archived after 7 days. Nothing is deleted.", r.daysSaid);
            Assert.Equal(new[] { 30, 10, 60 }, keepSet); Assert.Equal(new[] { 7 }, daysSet);
            Assert.DoesNotContain(r.buttons, x => x.Contains("Delete", StringComparison.OrdinalIgnoreCase) || x.Contains("Clear", StringComparison.OrdinalIgnoreCase) || x.Contains("Remove", StringComparison.OrdinalIgnoreCase) || x.Contains("Empty", StringComparison.OrdinalIgnoreCase));
            Assert.True(r.carries); Assert.True(r.scrolls);
            Assert.Equal((AutomationLiveSetting.Polite, AutomationLiveSetting.Polite, "Search the archive", "How many backup copies of each list are kept"), r.live);
            // What the event log holds of all this: that it happened, never what.
            Assert.Equal(new[] { "a list was restored from a backup", "a list was restored from a backup", "a to-do item was put back from the archive" }, EventLog.Read().Entries.Select(e => e.Detail));
            Assert.DoesNotContain("minutes", File.ReadAllText(log));
        }
        finally { SafeFile.Written -= b.Written; EventLog.Configure(null); }
    }
}
