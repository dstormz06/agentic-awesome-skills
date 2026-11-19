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
        b.Register(new("todo", "ANDA to-do list", file, "item", "items", t => Backups.ItemsIn(t, "Items"), () => todo.Reload()));
        b.Register(new("mytodo", "My to-do list", mineFile, "item", "items", t => Backups.ItemsIn(t, "Items"), () => mine.Reload()));
        SafeFile.Written += b.Written;
        try
        {
            EventLog.Configure(log);
            for (int i = 0; i < 12; i++) { todo.Add("Change number " + i, null, null, null, "You", Today); now = now.AddMinutes(1); }
            string gone = todo.Add("<b>Send</b> the meeting minutes", "216482", null, null, "You", Today).Item!.Id; now = now.AddMinutes(1);
            todo.SetDone(gone, true, Today); now = now.AddMinutes(1); todo.ClearCompleted(Today); now = now.AddMinutes(1);
            string other = mine.Add("File the weekly report", TodoCategory.Other, null, false, Repeat.None, -1, null, "You", Today).Item!.Id; now = now.AddMinutes(1);
            mine.SetDone(other, true, Today); mine.ClearFinished(Today);

            var r = await _fx.Session.Dispatch(() =>
            {
                ThemeManager.Apply(Appearances.Default);
                var w = new BackupsWindow(b, () => keep, n => { keep = n; keepSet.Add(n); }, () => days, n => { days = n; daysSet.Add(n); todo.ArchiveAfterDays = n; },
                    new[] { new ArchiveSource("ANDA to-do list", () => TodoArchive.Lines(file), id => todo.PutBack(id, Today)), new ArchiveSource("My to-do list", () => TodoArchive.Lines(mineFile), id => mine.PutBack(id, Today)) }, eastern);
                w.Show(); Settle();
                ComboBox Pick(int k) => w.Lists.GetVisualDescendants().OfType<ComboBox>().Where(c => (c.Tag as string) == "copies").ElementAt(k);
                Button Go(int k) => w.Lists.GetVisualDescendants().OfType<Button>().Where(x => (x.Tag as string) == "restore").ElementAt(k);
                string Sub(int k) => w.Lists.GetVisualDescendants().OfType<TextBlock>().Where(t => (t.Tag as string) == "sub").ElementAt(k).Text ?? "";
                var names = Texts(w.Lists).Where(t => t is "ANDA to-do list" or "My to-do list").ToArray();
                var first = (Sub: Sub(0), Items: ((IEnumerable<string>)Pick(0).ItemsSource!).Take(3).ToArray(), Enabled: Go(0).IsEnabled, Name: AutomationProperties.GetName(Go(0)), KeepShown: w.KeepBox.SelectedItem as string, DaysShown: w.DaysBox.SelectedItem as string);
                // An earlier copy is chosen and put back with one press.
                Pick(0).SelectedIndex = 2; Settle(); bool offered = Go(0).IsEnabled;
                int before = todo.Items.Count; Click(Go(0)); Settle();
                var restored = (Count: todo.Items.Count, Was: before, Status: w.Status.Text ?? "", Copies: b.Copies("todo").Count, Titles: todo.Items.Select(i => i.Title).ToArray());
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
                // How many copies are kept, and after how many days an item is archived: changed here, at once.
                w.KeepBox.SelectedIndex = 0; Settle(); var fewer = (Status: w.Status.Text ?? "", Copies: b.Copies("todo").Count);
                w.KeepBox.SelectedIndex = 2; Settle(); string more = w.Status.Text ?? "";
                w.DaysBox.SelectedIndex = 0; Settle(); string daysSaid = w.Status.Text ?? "";
                var buttons = w.GetVisualDescendants().OfType<Button>().Select(x => x.Content as string ?? "").ToArray();
                bool carries = ConnectionBar.Carries(w), scrolls = w.Content is ScrollViewer { MaxHeight: > 100 and < 4000 };
                var live = (AutomationProperties.GetLiveSetting(w.Status), AutomationProperties.GetLiveSetting(w.Count), AutomationProperties.GetName(w.FindBox), AutomationProperties.GetName(w.KeepBox));
                w.Close();
                return (names, first, offered, restored, archive, found, none, put, fewer, more, daysSaid, buttons, carries, scrolls, live, already, earlier);
            }, CancellationToken.None);

            Assert.Equal(new[] { "ANDA to-do list", "My to-do list" }, r.names);
            Assert.Equal("15 copies · newest Nov 18, 3:14:00 PM EST", r.first.Sub);
            Assert.Equal(new[] { "Nov 18, 3:14:00 PM EST · 12 items (as it is now)", "Nov 18, 3:13:00 PM EST · 13 items", "Nov 18, 3:12:00 PM EST · 13 items" }, r.first.Items);
            Assert.Equal((false, "Restore this copy of the ANDA to-do list", "30", "30"), (r.first.Enabled, r.first.Name, r.first.KeepShown, r.first.DaysShown));      // the newest copy is the list as it is: nothing to restore
            Assert.True(r.offered);
            Assert.Equal((13, 12, 16), (r.restored.Count, r.restored.Was, r.restored.Copies));                // the copy came back, and the list as it was is one copy more
            Assert.Equal("The ANDA to-do list is as it was on Nov 18, 3:12:00 PM EST. The list as it was a moment ago is still among the copies, so this can be undone.", r.restored.Status);
            Assert.Contains("<b>Send</b> the meeting minutes", r.restored.Titles);                           // markup in a title is text
            Assert.Equal(new[]
            {
                "ANDA 216482: <b>Send</b> the meeting minutes | ANDA to-do list · archived Nov 18 · cleared by the person | Put back on the list",
                "File the weekly report | My to-do list · archived Nov 18 · cleared by the person | Put back on the list",
            }, r.archive.Rows.OrderBy(x => x, StringComparer.Ordinal));
            Assert.Equal(("2 archived items.", 2), (r.archive.Count, r.archive.Buttons));
            Assert.Equal((1, "1 of 2 archived items matches."), r.found); Assert.Equal((0, "0 of 2 archived items match."), r.none);
            Assert.Equal(("That item is on the list already. Nothing was changed.", 1, 13), r.already);
            Assert.Equal((11, 17, false), r.earlier);
            Assert.Equal("“<b>Send</b> the meeting minutes” is back on the ANDA to-do list, as an open item.", r.put.Status);
            Assert.Equal((0, true), (r.put.Buttons, r.put.OnList));
            Assert.Equal("ANDA 216482: <b>Send</b> the meeting minutes | ANDA to-do list · archived Nov 18 · cleared by the person · put back Nov 18", r.put.Row);
            Assert.Matches("^[0-9]+ older backup copies were removed\\. Your lists and the archive were not changed\\.$", r.fewer.Status); Assert.Equal(10, r.fewer.Copies);
            Assert.Equal("From now on 60 copies of each list are kept. Nothing was removed.", r.more);
            Assert.Equal("From now on a finished item is archived after 7 days. Nothing is deleted.", r.daysSaid);
            Assert.Equal(new[] { 10, 60 }, keepSet); Assert.Equal(new[] { 7 }, daysSet);
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
