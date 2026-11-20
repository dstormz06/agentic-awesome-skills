using Avalonia.Automation;
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

/// <summary>Round 36 in the Windows app's Today window: "Since you last looked" first, then "Next week" from Thursday noon. Fictional data.</summary>
[Collection("Global state")]
public class Round36UiTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public Round36UiTests(HeadlessFixture fx) => _fx = fx;
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    private static List<string> Texts(Control c) => c.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").Where(t => t.Length > 0).ToList();

    [Fact]
    public async Task Today_lists_what_moved_first_then_next_week_and_keeps_the_look_when_it_closes()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var repo = new MockAndaRepository();
            Today.Look? Find(string id) => repo.Find(id) is { } rec ? Round33Tests.Look(rec) : null;
            var all = repo.Numbers.Select(n => Find(n)!).ToList();
            var now = LastLook.Of(repo.Numbers.Select(n => repo.Find(n)!), new DateTime(2026, 7, 16, 14, 42, 0, DateTimeKind.Utc));
            var items = now.Items.ToDictionary(k => k.Key, k => k.Value);
            items["216482"] = items["216482"] with { Forecast = "2026-10-10", Print = null };           // a week earlier, and no item yet
            items["209998"] = new LastLook.Item("2026-09-01", "2026-09-01", null);                       // since gone
            var kept = new LastLook.Look("2026-07-15T21:05:00.000Z", items);
            int keptTimes = 0; LastLook.Look? keptLook = null;
            var week = new NextWeek.Result("NEXT WEEK · MON NOV 23 TO FRI NOV 27", Array.Empty<NextWeek.Row>(), new[] { "Thu Nov 26: Thanksgiving Day (federal holiday)" }, NextWeek.SaidEmpty);
            var w = new TodayWindow(new TodayHooks(() => all, Find, () => Array.Empty<string>(), () => Array.Empty<string>(), _ => { },
                Looks: () => (kept, now, "Compared with what this PC kept when you last closed Today: Jul 15, 2026, 5:05 PM EDT.", true), KeepLook: l => { keptTimes++; keptLook = l; }, Week: () => week));
            var first = new TodayWindow(new TodayHooks(() => all, Find, () => Array.Empty<string>(), () => Array.Empty<string>(), _ => { }, Looks: () => (null, now, null, true)));
            // the person's sheet could not be read: nothing compared, and the look kept last time is not written over
            int unreadKept = 0;
            var unread = new TodayWindow(new TodayHooks(() => all, Find, () => Array.Empty<string>(), () => Array.Empty<string>(), _ => { },
                Looks: () => { var (c, n, k) = LastLook.Decide(kept, now, readable: false); return (c, now, n, k); }, KeepLook: _ => unreadKept++));
            var off = new TodayWindow(new TodayHooks(() => all, Find, () => Array.Empty<string>(), () => Array.Empty<string>(), _ => { }));
            try
            {
                w.Show(); first.Show(); off.Show(); unread.Show(); Settle();
                var unreadTexts = Texts(unread.List); unread.Close(); Settle();
                var texts = Texts(w.List);
                var gone = w.List.Children.OfType<StackPanel>().Select(p => AutomationProperties.GetName(p)).Where(n => !string.IsNullOrEmpty(n)).ToList();
                var rowNames = w.List.Children.OfType<Button>().Select(b => AutomationProperties.GetName(b)).ToList();
                // pressing the row in "since" keeps the keyboard on that row, not on the same ANDA further down
                // the same ANDA is in "since" and again under NEEDS A LOOK: pressing the second keeps the keyboard there, not on the first
                var both = w.List.Children.OfType<Button>().Where(b => (string?)b.Tag == "216482").ToList();
                both[^1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle();
                var focused = TopLevel.GetTopLevel(w)!.FocusManager!.GetFocusedElement() as Button;
                int idxFocused = w.List.Children.IndexOf(focused!), idxFirst = w.List.Children.IndexOf(w.List.Children.OfType<Button>().Where(b => (string?)b.Tag == "216482").Last()), copies = both.Count;
                w.Close(); Settle();
                return (texts, gone, rowNames, idxFocused, idxFirst, copies, keptTimes, keptSame: ReferenceEquals(keptLook, now), firstTexts: Texts(first.List), offTexts: Texts(off.List), unreadTexts, unreadKept);
            }
            finally { first.Close(); off.Close(); Settle(); }
        }, CancellationToken.None);
        int since = r.texts.IndexOf("SINCE YOU LAST LOOKED"), week = r.texts.IndexOf("NEXT WEEK · MON NOV 23 TO FRI NOV 27"), need = r.texts.IndexOf("NEEDS A LOOK");
        Assert.True(since == 0 && week > since && need > week, string.Join(" | ", r.texts.Take(12)));
        Assert.Contains("Compared with what this PC kept when you last closed Today: Jul 15, 2026, 5:05 PM EDT.", r.texts);
        Assert.Contains("Forecast moved from Sat Oct 10 to Sat Oct 17 (7 days later). New item needs you: IR response due in 5 days — Biopharmaceutics.", r.texts);
        Assert.Contains("ANDA 209998. No longer in your data.", r.gone);                     // an ANDA no longer in the data is text, not a button
        Assert.Contains("Thu Nov 26: Thanksgiving Day (federal holiday)", r.texts);
        Assert.Contains(NextWeek.SaidEmpty, r.texts);
        Assert.Contains(r.rowNames, n => n!.StartsWith("ANDA 216482, urgent. Forecast moved"));
        Assert.True(r.copies >= 2, "216482 should be listed in two groups");
        Assert.Equal(r.idxFirst, r.idxFocused);                                               // the keyboard stays on the row pressed, in its own group
        Assert.Equal(1, r.keptTimes);                                                          // the look is kept when Today closes ...
        Assert.True(r.keptSame);                                                               // ... and it is the look that was shown, not a fresh one
        Assert.Contains(LastLook.SaidUnreadable, r.unreadTexts);
        Assert.Equal(0, r.unreadKept);
        Assert.Equal("SINCE YOU LAST LOOKED", r.firstTexts[0]); Assert.Contains(LastLook.SaidFirst, r.firstTexts);
        Assert.DoesNotContain("SINCE YOU LAST LOOKED", r.offTexts);                            // without the hooks, Today is as it was
    }

    [Fact]
    public void The_app_reads_and_keeps_the_look_in_its_own_folder_and_takes_next_week_from_the_persons_clock()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "anda-hover-companion") dir = dir.Parent;
        string more = Round37Tests.Code(Path.Combine(dir!.FullName, "app", "AndaCompanion", "App.Round33.cs"));     // without comments: a line put out of action does not count
        Assert.Contains("LastLook.Decide(LastLook.Load(AppPaths.LastLookFile), now, DataReadable());", more);
        Assert.Contains("KeepLook: look => LastLook.KeepIfOn(_settings, AppPaths.LastLookFile, look, Clock.UtcNow()),", more);
        Assert.Contains("var wall = Clock.Wall(Clock.UtcNow()); return NextWeek.Of(DateOnly.FromDateTime(wall), wall.Hour, records, _calendar, NextWeek.TasksOf(_todo.Items, _myTodo.Items));", more);
        Assert.Contains("private bool DataReadable() => _portfolio is null || _portfolio.Status.Ok;", more);
        Assert.Contains("var records = Records().ToList();", more);                         // read once when Today opens
        Assert.EndsWith("last-look.json", AppPaths.LastLookFile);
    }
}
