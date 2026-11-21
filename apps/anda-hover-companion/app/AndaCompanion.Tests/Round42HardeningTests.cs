using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AndaCompanion.Services;
using AndaCompanion.Theming;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// Round 42, hardened at the owner's choice (Nov 21): the news document read away from the app's thread; the days each status was first
/// seen kept for up to five sheets; a choice to keep each notice until it is closed; News and alerts saying why notices wait. Fictional data.
/// </summary>
[Collection("Global state")]
public class Round42HardeningTests
{
    private static readonly string[] Paras = { "2026-11-20: Fictional line." };

    [Fact]
    public void The_news_document_is_read_away_from_the_apps_thread_once_per_copy()
    {
        var background = new Queue<Action>(); var posted = new Queue<Action>(); int reads = 0, changed = 0;
        var reader = new NewsReader(path => { reads++; return (Paras, null); }, background.Enqueue, posted.Enqueue);
        var t0 = new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc);
        Assert.Equal((null, null, true), reader.Get("C:\\f\\news.docx", t0, 10, _ => changed++));      // nothing read yet: the app does not wait
        Assert.Equal(0, reads);
        reader.Get("C:\\f\\news.docx", t0, 10, _ => changed++);
        Assert.Single(background);                                                           // asked twice, read once
        background.Dequeue()(); Assert.Equal(1, reads); Assert.Equal(0, changed);           // read away from the app's thread ...
        posted.Dequeue()(); Assert.Equal(1, changed);                                       // ... and told on it, once
        var (p, problem, reading) = reader.Get("C:\\f\\news.docx", t0, 10, _ => changed++);
        Assert.Same(Paras, p); Assert.Null(problem); Assert.False(reading); Assert.Empty(background);
        // a newer copy: the last read is shown while it is read
        var (p2, _, reading2) = reader.Get("C:\\f\\news.docx", t0.AddMinutes(5), 12, _ => changed++);
        Assert.Same(Paras, p2); Assert.True(reading2); Assert.Single(background);
        // an older read that ends after a newer one was asked for is not used
        var third = reader.Get("C:\\f\\news.docx", t0.AddMinutes(9), 14, _ => changed++);
        var older = background.Dequeue(); var newer = background.Dequeue();
        older(); posted.Dequeue()(); Assert.Equal(1, changed);
        newer(); posted.Dequeue()(); Assert.Equal(2, changed);
        Assert.False(reader.Get("C:\\f\\news.docx", t0.AddMinutes(9), 14, _ => { }).Reading);
        // another document: nothing of the first is shown while it is read
        Assert.Equal((null, null, true), reader.Get("C:\\f\\other.docx", t0, 3, _ => { }));
        // a read that throws is a sentence, not a crash; it is tried again after a minute, not sooner (no loop of reads), even if the
        // file's time and size did not change (a lock or an online-only copy can clear by itself)
        var clock = t0; int tries = 0; var oks = new List<bool>();
        var failing = new NewsReader(_ => { tries++; throw new IOException("fictional"); }, a => a(), a => a(), () => clock);
        failing.Get("C:\\f\\news.docx", t0, 1, oks.Add);
        var (fp, fproblem, freading) = failing.Get("C:\\f\\news.docx", t0, 1, oks.Add);
        Assert.Null(fp); Assert.StartsWith("The news document could not be read just now.", fproblem); Assert.False(freading);
        Assert.Equal(1, tries); Assert.Equal(new[] { false }, oks);                         // told that it failed, once
        clock = t0.AddSeconds(59); failing.Get("C:\\f\\news.docx", t0, 1, oks.Add); Assert.Equal(1, tries);
        clock = t0.AddSeconds(61); failing.Get("C:\\f\\news.docx", t0, 1, oks.Add); Assert.Equal(2, tries);
        // a newer copy that cannot be read keeps the last good read of the same file, with the reason
        bool fail = false;
        var keeper = new NewsReader(_ => fail ? (null, "Fictional lock.") : (Paras, null), a => a(), a => a(), () => clock);
        keeper.Get("C:\\f\\news.docx", t0, 1, _ => { });
        fail = true; keeper.Get("C:\\f\\news.docx", t0.AddMinutes(1), 2, _ => { });
        var kept = keeper.Get("C:\\f\\news.docx", t0.AddMinutes(1), 2, _ => { });
        Assert.Same(Paras, kept.Paragraphs); Assert.Equal("Fictional lock.", kept.Problem); Assert.False(kept.Reading);
    }

    [Fact]
    public void Each_sheet_keeps_its_own_first_seen_days_up_to_five()
    {
        string path = Path.Combine(Temp.NewDir(), "status-seen.json");
        var a = StatusSeen.Update(null, new[] { ("100001", (string?)"Fictional A") }, "2026-10-01");
        var b = StatusSeen.Update(null, new[] { ("200001", (string?)"Fictional B") }, "2026-11-01");
        Assert.True(StatusSeen.Save(path, a, "file:aaaaaaaa", "2026-10-01"));
        Assert.True(StatusSeen.Save(path, b, "demo", "2026-11-01"));
        Assert.Equal("2026-10-01", StatusSeen.Load(path, "file:aaaaaaaa").Items!["100001"].Since);     // switching data does not lose the sheet's days
        Assert.Equal("2026-11-01", StatusSeen.Load(path, "demo").Items!["200001"].Since);
        for (int i = 0; i < 5; i++) StatusSeen.Save(path, b, $"file:0000000{i}", $"2026-11-1{i}");
        Assert.Null(StatusSeen.Load(path, "file:aaaaaaaa").Items);                          // six sources: the one used longest ago is let go
        Assert.NotNull(StatusSeen.Load(path, "file:00000004").Items);
        // round 42's file, one source, is still read
        File.WriteAllText(path, "{\"source\":\"demo\",\"items\":{\"300001\":{\"fp\":\"0000abcd\",\"since\":\"2026-09-01\"}}}");
        Assert.Equal("2026-09-01", StatusSeen.Load(path, "demo").Items!["300001"].Since);
        Assert.True(StatusSeen.Save(path, a, "file:aaaaaaaa", "2026-11-20"));
        Assert.Equal("2026-09-01", StatusSeen.Load(path, "demo").Items!["300001"].Since);   // kept beside the new one
        // an entry that is null is dropped, never a crash
        File.WriteAllText(path, "{\"sources\":{\"demo\":null,\"file:aaaaaaaa\":{\"used\":\"2026-11-20\",\"items\":{\"100001\":{\"fp\":\"0000abcd\",\"since\":\"2026-10-01\"}}}}}");
        Assert.Null(StatusSeen.Load(path, "demo").Items);
        Assert.Equal("2026-10-01", StatusSeen.Load(path, "file:aaaaaaaa").Items!["100001"].Since);
        Assert.True(StatusSeen.Save(path, a, "demo", "2026-11-21"));
    }
}

[Collection("Global state")]
public class Round42HardeningUiTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public Round42HardeningUiTests(HeadlessFixture fx) => _fx = fx;
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    private static List<string> Texts(Control c) => c.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").Where(t => t.Length > 0).ToList();

    [Fact]
    public async Task A_notice_can_stay_until_closed_and_News_and_alerts_says_why_notices_wait()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var item = new NoticeItem("d|100001|goal-passed", "data", "100001", "now", "ANDA 100001: The goal date passed.");
            var stays = new NoticeWindow(item, _ => { }, _ => { }, stay: true);
            var hides = new NoticeWindow(item, _ => { }, _ => { });
            string? waiting = Notices.SaidCap(3, 2);
            var records = Array.Empty<TimelineRecord>();
            var news = new NewsWindow(new NewsHooks(() => Array.Empty<TimelineAlert>(), () => (Array.Empty<NewsItem>(), NewsReader.SaidReading), _ => { }, () => "2026-11-20", _ => null, () => records, NoticeNote: () => waiting));
            try
            {
                stays.Show(); hides.Show(); news.Show(); Settle();
                var first = (stays.HidesBySelf, hides.HidesBySelf, stays.Stays, note: news.NoticeNote.IsVisible ? news.NoticeNote.Text : null, newsTexts: Texts(news.NewsList), newsNote: news.NewsNote.Text);
                waiting = null; news.Paint(); Settle();
                return (first, noteAfter: news.NoticeNote.IsVisible);
            }
            finally { stays.Close(); hides.Close(); news.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.False(r.first.Item1); Assert.True(r.first.Item2); Assert.True(r.first.Stays);       // kept until closed; the other hides by itself
        Assert.Equal(Notices.SaidCap(3, 2), r.first.note);
        Assert.Equal(NewsReader.SaidReading, r.first.newsNote);
        Assert.Empty(r.first.newsTexts);                                                    // "no lines" is not said while the document is read
        Assert.False(r.noteAfter);                                                          // nothing waits: nothing said
    }

    [Fact]
    public async Task Settings_offers_to_keep_each_notice_and_a_backup_carries_it()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            ConnectionStatus.Reset();
            var s = new Settings();
            var w = new SettingsWindow(s, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => { });
            try
            {
                w.Show(); Settle();
                var box = w.FindControl<CheckBox>("NoticeStayBox")!;
                bool before = box.IsChecked == true;
                box.IsChecked = true; Settle();
                return (before, after: s.NoticesStay, label: box.Content as string);
            }
            finally { w.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.False(r.before); Assert.True(r.after); Assert.Equal("Keep each notice until I close it", r.label);
        var src = new Settings { NoticesStay = true };
        string json = SettingsBackup.Export(src, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), new QuickReplyService(Path.Combine(Temp.NewDir(), "r.json")));
        var target = new Settings();
        Assert.True(SettingsBackup.Import(json, target, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), new QuickReplyService(Path.Combine(Temp.NewDir(), "r.json"))).Ok);
        Assert.True(target.NoticesStay);
    }
}
