using System.IO.Compression;
using System.Text;
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
/// Round 42 beyond the simulation's answers: the notices' day count, reminders and memory; when a status was first seen; the Word reader;
/// what stays on this PC in a backup; and the windows (a notice, News and alerts, Today's first group, Settings). Fictional data only.
/// </summary>
[Collection("Global state")]
public class Round42LogicTests
{
    private static TimelineAlert Alert(string n, string code, string horizon, string sev, string text = "Fictional reason.") =>
        new($"{n}|{code}|2026-11-20", n, code, horizon, sev, "2026-11-20", text);

    [Fact]
    public void Notices_keep_to_the_days_limit_wait_for_tomorrow_and_forget_old_ones()
    {
        var s = new NoticeState();
        var items = Notices.FromAlerts(new[] { Alert("100001", "goal-passed", "now", "urgent"), Alert("100002", "thin-margin", "later", "soon"), Alert("100003", "psg", "later", "info") })
            .Concat(Notices.FromNews(new[] { new NewsItem("n|2026-11-19|0000abcd", "2026-11-19", "Fictional division meeting moved.") })).ToList();
        Assert.Equal(3, items.Count);                                                        // an alert only for information is never a notice
        Assert.Equal(new[] { "FROM YOUR DATA · IMMEDIATE", "FROM YOUR DATA · LONGER TERM", "DIVISION NEWS" }, items.Select(i => i.KindLabel));
        Assert.Equal(new[] { "Open the card", "Open the card", "Open News and alerts" }, items.Select(i => i.OpenLabel));
        var pending = Notices.Pending(s, items, "2026-11-20");
        Assert.Equal("news", pending[0].Kind);                                               // news first, then the data in its own order
        var first = Notices.Take(s, pending, 1, "2026-11-20");
        Assert.NotNull(first);
        Assert.Null(Notices.Take(s, pending, 1, "2026-11-20"));                              // a limit of one a day
        Assert.Equal(Notices.SaidCap(1, 2), "Today's limit of 1 notice is reached. 2 more are in News and alerts.");
        Assert.NotNull(Notices.Take(s, pending, 3, "2026-11-20"));                           // a higher limit lets the next come
        var data = Notices.Take(s, pending, 3, "2026-11-20")!;
        Assert.Equal(3, s.Shown);
        Assert.Null(Notices.Take(s, pending, 7, "2026-11-20"));                              // a limit that is not a choice is read as 3
        // "Remind me tomorrow": not today, back tomorrow; what was shown stays shown
        Notices.Snooze(s, data.Key, "2026-11-20");
        Assert.DoesNotContain(Notices.Pending(s, items, "2026-11-20"), i => i.Key == data.Key);
        var tomorrow = Notices.Pending(s, items, "2026-11-21");
        Assert.Equal(new[] { data.Key }, tomorrow.Select(i => i.Key));
        Assert.Equal(0, s.Shown);                                                            // a new day counts again
        // after 60 days, what was shown is forgotten (the same fact can be told again)
        Assert.Equal(3, Notices.Pending(s, items, "2027-01-20").Count);
    }

    [Fact]
    public void A_notices_file_that_cannot_be_read_is_set_aside_and_a_fresh_day_starts()
    {
        string dir = Temp.NewDir(), path = Path.Combine(dir, "notices.json");
        File.WriteAllText(path, "{ not json");
        var s = Notices.Load(path);
        Assert.Equal(0, s.Shown); Assert.Empty(s.Seen);
        Assert.False(File.Exists(path));                                                     // set aside, not read again
        s.Day = "2026-11-20"; s.Shown = 2; s.Seen["d|100001|goal-passed|2026-11-20"] = "2026-11-20";
        Assert.True(Notices.Save(path, s));
        var back = Notices.Load(path);
        Assert.Equal(2, back.Shown); Assert.Equal("2026-11-20", back.Seen["d|100001|goal-passed|2026-11-20"]);
        Assert.DoesNotContain("Fictional", File.ReadAllText(path));                          // keys and days only, never a sentence
    }

    [Fact]
    public void A_status_keeps_its_first_day_until_it_changes_and_only_a_fingerprint_is_kept()
    {
        var first = StatusSeen.Update(null, new[] { ("100001", (string?)"Fictional status A"), ("100002", "Fictional status B"), ("not-a-number", "x"), ("100001", "duplicate") }, "2026-10-01");
        Assert.Equal(new[] { "100001", "100002" }, first.Keys.OrderBy(k => k));
        var later = StatusSeen.Update(first, new[] { ("100001", (string?)"Fictional status A"), ("100002", "Fictional status C"), ("100004", null) }, "2026-11-20");
        Assert.Equal("2026-10-01", later["100001"].Since);                                   // the same status keeps its day
        Assert.Equal("2026-11-20", later["100002"].Since);                                   // a changed status starts today
        Assert.Equal("2026-11-20", later["100004"].Since);
        Assert.Equal(3, later.Count);                                                        // an ANDA no longer in the sheet would be gone
        var future = StatusSeen.Update(new Dictionary<string, StatusSeen.Seen> { ["100001"] = new(first["100001"].Fp, "2027-01-01") }, new[] { ("100001", (string?)"Fictional status A") }, "2026-11-20");
        Assert.Equal("2026-11-20", future["100001"].Since);                                  // a day in the future is not believed
        string path = Path.Combine(Temp.NewDir(), "status-seen.json");
        Assert.True(StatusSeen.Save(path, later));
        Assert.DoesNotContain("Fictional", File.ReadAllText(path));                          // never the status's words
        Assert.Equal(later["100002"], StatusSeen.Load(path)!["100002"]);
        File.WriteAllText(path, "[1,2");
        Assert.Null(StatusSeen.Load(path)); Assert.False(File.Exists(path));
    }

    /// <summary>A small Word document made here: the parts Word needs to call it a document, and the given body.</summary>
    internal static string Docx(string body, string? doctype = null)
    {
        string path = Path.Combine(Temp.NewDir(), "fictional-news.docx");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            void Put(string name, string text) { using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false)); w.Write(text); }
            Put("[Content_Types].xml", "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>");
            Put("word/document.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + (doctype ?? "") +
                "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" + body + "</w:body></w:document>");
        }
        return path;
    }

    [Fact]
    public void The_Word_reader_keeps_every_piece_of_text_tabs_breaks_and_text_boxes()
    {
        var path = Docx(
            "<w:p><w:r><w:t>2026-11-19</w:t><w:tab/><w:t>Fictional</w:t><w:t xml:space=\"preserve\"> meeting moved</w:t><w:br/><w:t>to Room 1.</w:t></w:r></w:p>" +
            "<w:p/>" +
            "<w:p><w:r><w:t>Nov 18, 2026: outer</w:t></w:r><w:r><w:pict><w:txbxContent><w:p><w:r><w:t>inner box</w:t></w:r></w:p></w:txbxContent></w:pict></w:r><w:r><w:t> line</w:t></w:r></w:p>");
        var (paragraphs, problem) = DivisionNews.ReadDocx(path);
        Assert.Null(problem);
        Assert.Equal(new[] { "2026-11-19 Fictional meeting moved to Room 1.", "", "inner box", "Nov 18, 2026: outer line" }, paragraphs);
        var news = DivisionNews.Parse(paragraphs!, "2026-11-20");
        Assert.Equal(new[] { "Fictional meeting moved to Room 1.", "outer line" }, news.Select(n => n.Text));
    }

    [Fact]
    public void The_Word_reader_refuses_a_DTD_a_wrong_file_and_a_missing_one_with_a_sentence()
    {
        var dtd = Docx("<w:p><w:r><w:t>&x;</w:t></w:r></w:p>", "<!DOCTYPE w:document [<!ENTITY x \"fictional\">]>");
        Assert.Equal("The news document could not be read just now. It may be open elsewhere, not yet synced, or not a Word document.", DivisionNews.ReadDocx(dtd).Problem);
        string notZip = Path.Combine(Temp.NewDir(), "plain.docx"); File.WriteAllText(notZip, "fictional");
        Assert.NotNull(DivisionNews.ReadDocx(notZip).Problem);
        Assert.Equal("The news document must be a Word document (.docx).", DivisionNews.ReadDocx(Path.Combine(Temp.NewDir(), "news.doc")).Problem);
        Assert.Equal("Please choose the division's news document first.", DivisionNews.ReadDocx("").Problem);
        Assert.NotNull(DivisionNews.ReadDocx(Path.Combine(Temp.NewDir(), "missing.docx")).Problem);
        string noBody = Path.Combine(Temp.NewDir(), "empty.docx");
        using (ZipFile.Open(noBody, ZipArchiveMode.Create)) { }
        Assert.Equal("The news document could not be read as a Word document.", DivisionNews.ReadDocx(noBody).Problem);
    }

    [Fact]
    public void The_news_document_and_its_switch_stay_on_this_PC_and_bad_values_are_put_right()
    {
        var s = new Settings { WatchBack = true, DivisionNews = true, NewsDocument = @"C:\Users\Fictional\OneDrive\news.docx", NoticesPerDay = 5 };
        s.Timeline.Set("goalClose", 15);
        string json = SettingsBackup.Export(s, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), new QuickReplyService(Path.Combine(Temp.NewDir(), "r.json")));
        Assert.DoesNotContain("news.docx", json);
        var target = new Settings { NewsDocument = @"C:\Users\Fictional\Mine\kept.docx" };
        Assert.True(SettingsBackup.Import(json, target, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), new QuickReplyService(Path.Combine(Temp.NewDir(), "r.json"))).Ok);
        Assert.Equal(@"C:\Users\Fictional\Mine\kept.docx", target.NewsDocument);           // this PC's document is kept
        Assert.True(target.WatchBack); Assert.Equal(5, target.NoticesPerDay); Assert.Equal(15, target.Timeline.Get("goalClose"));
        // put right on reading: a limit that is not a choice, a document that is not .docx, news without a document
        string dir = Temp.NewDir(), path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, "{\"NoticesPerDay\":4,\"DivisionNews\":true,\"NewsDocument\":\"C:\\\\x\\\\news.txt\",\"Timeline\":{\"goalClose\":12}}");
        var read = SettingsService.Load(path);
        Assert.Equal(Notices.DefaultCap, read.NoticesPerDay);
        Assert.Equal("", read.NewsDocument); Assert.False(read.DivisionNews);
        Assert.Equal(10, read.Timeline.Get("goalClose"));
    }
}

[Collection("Global state")]
public class Round42UiTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public Round42UiTests(HeadlessFixture fx) => _fx = fx;
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static List<string> Texts(Control c) => c.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").Where(t => t.Length > 0).ToList();

    private static TimelineAlert Alert(string? n, string code, string horizon, string sev, string text) => new($"{n}|{code}", n, code, horizon, sev, "2026-11-20", text);

    [Fact]
    public async Task A_notice_never_takes_the_keyboard_and_its_buttons_do_what_they_say()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var opened = new List<string>(); var snoozed = new List<string>();
            var data = new NoticeItem("d|100001|goal-passed", "data", "100001", "now", "ANDA 100001: the goal date passed.");
            var news = new NoticeItem("n|2026-11-19|0000abcd", "news", null, "now", "Fictional division meeting moved.");
            var a = new NoticeWindow(data, i => opened.Add(i.Key), i => snoozed.Add(i.Key));
            var b = new NoticeWindow(news, i => opened.Add(i.Key), i => snoozed.Add(i.Key));
            var c = new NoticeWindow(data, i => opened.Add(i.Key), i => snoozed.Add(i.Key));
            a.Show(); b.Show(); c.Show(); Settle();
            var facts = (a.ShowActivated, a.Topmost, a.ShowInTaskbar, a.SystemDecorations, kind: a.KindText.Text, open: a.OpenButton.Content as string, snoozeShown: a.SnoozeButton.IsVisible,
                newsKind: b.KindText.Text, newsOpen: b.OpenButton.Content as string, newsSnooze: b.SnoozeButton.IsVisible, name: AutomationProperties.GetName(a), live: AutomationProperties.GetLiveSetting(a.Body));
            bool closedA = false, closedC = false; a.Closed += (_, _) => closedA = true; c.Closed += (_, _) => closedC = true;
            Click(a.SnoozeButton); Click(b.OpenButton); Click(c.CloseButton); Settle();
            return (facts, opened, snoozed, closedA, bVisible: b.IsVisible, closedC);
        }, CancellationToken.None);
        Assert.False(r.facts.ShowActivated); Assert.True(r.facts.Topmost); Assert.False(r.facts.ShowInTaskbar);
        Assert.Equal(SystemDecorations.None, r.facts.SystemDecorations);
        Assert.Equal("FROM YOUR DATA · IMMEDIATE", r.facts.kind); Assert.Equal("Open the card", r.facts.open); Assert.True(r.facts.snoozeShown);
        Assert.Equal("DIVISION NEWS", r.facts.newsKind); Assert.Equal("Open News and alerts", r.facts.newsOpen); Assert.False(r.facts.newsSnooze);   // news is not reminded
        Assert.Equal("Notice near the clock", r.facts.name); Assert.Equal(AutomationLiveSetting.Polite, r.facts.live);
        Assert.Equal(new[] { "d|100001|goal-passed" }, r.snoozed); Assert.True(r.closedA);
        Assert.Equal(new[] { "n|2026-11-19|0000abcd" }, r.opened); Assert.False(r.bVisible);
        Assert.True(r.closedC);
    }

    [Fact]
    public async Task News_and_alerts_shows_both_horizons_and_the_news_and_its_buttons_work()
    {
        var r = await _fx.Session.Dispatch(async () =>
        {
            ThemeManager.Apply(Appearances.Default);
            var alerts = new[]
            {
                Alert("100001", "goal-passed", "now", "urgent", "The goal date passed 2 business days ago (Wed Nov 18, 2026)."),
                Alert("100002", "psg", "later", "info", "Fictional information only."),
                Alert(null, "stale", "later", "soon", "Your data is 40 days old."),
            };
            var records = new[] { new TimelineRecord("100001", "Fictional drug A", "2026-12-01", null), new TimelineRecord("100002", null, "2026-11-01", null) };
            string? copied = null, saved = null; bool? saveAnswer = true; var opened = new List<string>();
            var news = (Items: (IReadOnlyList<NewsItem>?)new[] { new NewsItem("n|2026-11-19|1", "2026-11-19", "Fictional division meeting moved.") }, Note: DivisionNews.SaidFrom("news.docx"));
            var w = new NewsWindow(new NewsHooks(() => alerts, () => news, id => opened.Add(id), () => "2026-11-20", n => records.FirstOrDefault(x => x.Number == n)?.Drug, () => records,
                t => { copied = t; return Task.FromResult(true); }, t => { saved = t; return Task.FromResult(saveAnswer); }));
            var off = new NewsWindow(new NewsHooks(() => Array.Empty<TimelineAlert>(), () => (null, DivisionNews.SaidOff), _ => { }, () => "2026-11-20", _ => null, () => records));
            try
            {
                w.Show(); off.Show(); Settle();
                var now = Texts(w.NowList); var later = Texts(w.LaterList); var newsTexts = Texts(w.NewsList); string note = w.NewsNote.Text ?? "";
                var offNow = Texts(off.NowList); var offLater = Texts(off.LaterList); string offNote = off.NewsNote.Text ?? ""; int offNews = off.NewsList.Children.Count;
                var row = w.NowList.Children.OfType<Button>().Single(); Click(row); Settle();
                bool laterIsText = w.LaterList.Children.OfType<Button>().Any();
                await w.CopySummaryAsync(); string sayCopy = w.Say.Text ?? "";
                await w.SaveCalendarAsync(); string saySave = w.Say.Text ?? "";
                saveAnswer = null; w.Say.Text = "kept"; await w.SaveCalendarAsync(); string sayCancel = w.Say.Text ?? "";
                saveAnswer = false; await w.SaveCalendarAsync(); string sayFail = w.Say.Text ?? "";
                return (now, later, newsTexts, note, offNow, offLater, offNote, offNews, opened, laterIsText, copied, sayCopy, saved, saySave, sayCancel, sayFail);
            }
            finally { w.Close(); off.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.Single(r.now); Assert.Contains("100001", r.now[0]);
        Assert.Single(r.later); Assert.Contains("40 days old", r.later[0]);                  // the alert only for information is not listed
        Assert.False(r.laterIsText);                                                       // an alert with no ANDA is text, not a button
        Assert.Equal(new[] { "100001" }, r.opened);
        Assert.Single(r.newsTexts); Assert.EndsWith("Fictional division meeting moved.", r.newsTexts[0]);
        Assert.Equal(DivisionNews.SaidFrom("news.docx"), r.note);
        Assert.Equal(new[] { NewsWindow.SaidNowNone }, r.offNow); Assert.Equal(new[] { NewsWindow.SaidLaterNone }, r.offLater);
        Assert.Equal(DivisionNews.SaidOff, r.offNote); Assert.Equal(0, r.offNews);
        Assert.Equal(Timeline.Summary(new[] { Alert("100001", "goal-passed", "now", "urgent", "The goal date passed 2 business days ago (Wed Nov 18, 2026)."), Alert(null, "stale", "later", "soon", "Your data is 40 days old.") }, "2026-11-20", _ => "Fictional drug A"), r.copied);
        Assert.Equal(Timeline.SaidCopied(1, 1), r.sayCopy);
        Assert.StartsWith("BEGIN:VCALENDAR", r.saved);
        Assert.Equal(NewsWindow.SaidSaved(1), r.saySave);                                   // only the goal date in the next 180 days
        Assert.Equal("kept", r.sayCancel);                                                 // cancelled: nothing said
        Assert.Equal(NewsWindow.SaidNotSaved, r.sayFail);
    }

    [Fact]
    public async Task Today_opens_with_what_needs_a_look_most_urgent_first_one_line_per_ANDA()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var repo = new AndaCompanion.Data.MockAndaRepository();
            Today.Look? Find(string id) => repo.Find(id) is { } rec ? Round33Tests.Look(rec) : null;
            var all = repo.Numbers.Select(n => Find(n)!).ToList();
            var alerts = new List<TimelineAlert> { Alert("216482", "goal-passed", "now", "urgent", "First reason."), Alert("216482", "thin-margin", "later", "soon", "Second reason."), Alert("216482", "psg", "later", "info", "Info only.") };
            for (int i = 0; i < 9; i++) alerts.Add(Alert((300000 + i).ToString(), "goal-close", "now", "soon", $"Reason {i}."));
            var w = new TodayWindow(new TodayHooks(() => all, Find, () => Array.Empty<string>(), () => Array.Empty<string>(), _ => { }, Alerts: () => alerts));
            var none = new TodayWindow(new TodayHooks(() => all, Find, () => Array.Empty<string>(), () => Array.Empty<string>(), _ => { }, Alerts: () => new[] { Alert("216482", "psg", "later", "info", "Info only.") }));
            var off = new TodayWindow(new TodayHooks(() => all, Find, () => Array.Empty<string>(), () => Array.Empty<string>(), _ => { }));
            try
            {
                w.Show(); none.Show(); off.Show(); Settle();
                return (texts: Texts(w.List), noneTexts: Texts(none.List), offTexts: Texts(off.List));
            }
            finally { w.Close(); none.Close(); off.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.Equal(Timeline.TodayTitle, r.texts[0]);
        Assert.Contains(r.texts, t => t.Contains("First reason. (+1 more)"));             // the first reason, and how many more (info is not counted)
        Assert.DoesNotContain(r.texts, t => t.Contains("Second reason."));
        Assert.DoesNotContain(r.texts, t => t.Contains("Reason 7."));                     // at most eight ANDAs ...
        Assert.Contains(Timeline.TodayMore(2), r.texts);                                  // ... and how many more are in News and alerts
        Assert.Equal(Timeline.TodayTitle, r.noneTexts[0]); Assert.Contains(Timeline.TodayNone, r.noneTexts);
        Assert.DoesNotContain(Timeline.TodayTitle, r.offTexts);
    }

    [Fact]
    public async Task Settings_holds_the_notices_the_news_document_and_the_limits()
    {
        var r = await _fx.Session.Dispatch(async () =>
        {
            ThemeManager.Apply(Appearances.Default);
            ConnectionStatus.Reset();
            var s = new Settings(); int checks = 0, opens = 0;
            var hooks = new SettingsHooks(() => "", () => null, () => "", CheckNotices: () => { checks++; return Notices.SaidNothing; }, OpenNews: () => opens++);
            var w = new SettingsWindow(s, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => { }, hooks, new QuickReplyService(Path.Combine(Temp.NewDir(), "r.json")));
            SettingsWindow? bare = null;
            try
            {
                w.Show(); Settle();
                var news = w.FindControl<CheckBox>("NewsBox")!; var watch = w.FindControl<CheckBox>("WatchBackBox")!; var status = w.FindControl<TextBlock>("WatchStatus")!;
                var caps = w.FindControl<WrapPanel>("NoticeCapPanel") is { } wp ? wp.Children.OfType<RadioButton>().ToList() : w.FindControl<Panel>("NoticeCapPanel")!.Children.OfType<RadioButton>().ToList();
                var combos = w.FindControl<Grid>("TimelineGrid")!.Children.OfType<ComboBox>().ToList();
                var startCaps = caps.Select(c => c.IsChecked == true).ToList();
                news.IsChecked = true; Settle();
                string refused = status.Text ?? ""; bool newsOn = news.IsChecked == true;
                w.PickNewsDocument = () => Task.FromResult<string?>(@"C:\Users\Fictional\notes.txt");
                Click(w.FindControl<Button>("NewsPick")!); await Task.Delay(1); Settle();
                string wrongType = status.Text ?? ""; string pathAfterWrong = s.NewsDocument;
                w.PickNewsDocument = () => Task.FromResult<string?>(@"C:\Users\Fictional\OneDrive\news.docx");
                Click(w.FindControl<Button>("NewsPick")!); await Task.Delay(1); Settle();
                string chosen = status.Text ?? ""; string path = s.NewsDocument;
                news.IsChecked = true; Settle(); bool divisionNews = s.DivisionNews;
                watch.IsChecked = true; Settle(); bool watchBack = s.WatchBack;
                caps[2].IsChecked = true; Settle(); int perDay = s.NoticesPerDay;
                combos[0].SelectedItem = 15; Settle(); int goalClose = s.Timeline.Get("goalClose");
                Click(w.FindControl<Button>("WatchNow")!); Click(w.FindControl<Button>("NewsOpen")!); Settle();
                Click(w.FindControl<Button>("NewsClear")!); Settle();
                bool clearedNews = s.DivisionNews; string clearedPath = s.NewsDocument;
                // a Settings without the app behind it (radio groups are shared until a window shows, so this one is made only now)
                bare = new SettingsWindow(new Settings(), new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => { }); bare.Show(); Settle();
                var bareNow = bare.FindControl<Button>("WatchNow")!; Click(bareNow); Settle();
                return (startCaps, refused, newsOn, wrongType, pathAfterWrong, chosen, path, divisionNews, watchBack, perDay, goalClose, checks, opens, clearedNews, clearedPath,
                    combos: combos.Count, comboNames: combos.Select(c => AutomationProperties.GetName(c)).ToList(), bareSaid: bare.FindControl<TextBlock>("WatchStatus")!.Text, bareOpen: bare.FindControl<Button>("NewsOpen")!.IsEnabled,
                    hint: w.FindControl<TextBlock>("WatchHint")!.Text, tlHint: w.FindControl<TextBlock>("TimelineHint")!.Text);
            }
            finally { w.Close(); bare?.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.Equal(new[] { false, true, false }, r.startCaps);                            // three a day unless the person chooses
        Assert.Equal("Please choose the division's news document first.", r.refused); Assert.False(r.newsOn);
        Assert.Equal("The news document must be a Word document (.docx).", r.wrongType); Assert.Equal("", r.pathAfterWrong);
        Assert.Equal("Document chosen. Tick the box above to show its news.", r.chosen); Assert.Equal(@"C:\Users\Fictional\OneDrive\news.docx", r.path);
        Assert.True(r.divisionNews); Assert.True(r.watchBack); Assert.Equal(5, r.perDay); Assert.Equal(15, r.goalClose);
        Assert.True(r.checks >= 3);                                                          // each switch checks at once, and Check now does
        Assert.Equal(1, r.opens);
        Assert.False(r.clearedNews); Assert.Equal("", r.clearedPath);
        Assert.Equal(TimelineSettings.Limits.Count, r.combos);
        Assert.Equal(TimelineSettings.Limits.Select(l => l.Label), r.comboNames);
        Assert.Equal(Notices.SaidNotRunning, r.bareSaid); Assert.False(r.bareOpen);
        Assert.Equal(Notices.Hint, r.hint); Assert.Equal(Timeline.Hint, r.tlHint);
    }
}
