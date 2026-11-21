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
        // after 60 days, what was shown is forgotten (the same fact can be told again); on the 60th day it is still kept
        var day60 = new NoticeState { Day = "2026-11-20", Seen = new(s.Seen) };
        Assert.Equal(1, Notices.Pending(day60, items, "2027-01-19").Count);
        Assert.Equal(3, Notices.Pending(s, items, "2027-01-20").Count);
    }

    [Fact]
    public void Notices_hold_the_rule_not_the_persons_words_and_a_line_given_twice_is_one_notice()
    {
        var todo = new TimelineAlert("100006|todo-overdue|0000abcd|2026-11-19", "100006", "todo-overdue", "now", "bad", "2026-11-19", "To-do item overdue since Thu Nov 19: Fictional private words");
        var attention = new TimelineAlert("100001|attention|0000abcd", "100001", "attention", "now", "warn", null, "Needs action: Fictional private words");
        var items = Notices.FromAlerts(new[] { todo, attention });
        Assert.Equal(new[] { "ANDA 100006: To-do item overdue since Thu Nov 19.", "ANDA 100001: Your sheet marks an item that needs action (see the card)." }, items.Select(i => i.Text));
        Assert.DoesNotContain("private", Timeline.Summary(new[] { todo, attention }, "2026-11-20", _ => null));      // nor in the summary for a team lead
        var news = new NewsItem("n|2026-11-19|0000abcd", "2026-11-19", "Fictional line.");
        Assert.Single(Notices.Pending(new NoticeState(), Notices.FromNews(new[] { news, news }), "2026-11-20"));
        // the memory is full: the oldest shown is let go, and what is shown now is kept as shown
        var full = new NoticeState { Day = "2026-11-20" };
        for (int i = 0; i < Notices.MaxKept; i++) full.Seen[$"k{i:D5}"] = i == 7 ? "2026-11-01" : "2026-11-19";
        var pending = new List<NoticeItem>(items);
        var shown = Notices.Take(full, pending, 3, "2026-11-20")!;
        Assert.Equal("2026-11-20", full.Seen[shown.Key]); Assert.False(full.Seen.ContainsKey("k00007")); Assert.Equal(Notices.MaxKept, full.Seen.Count);
        Assert.Null(Notices.FromText(null, Path.Combine(Temp.NewDir(), "notices.json")));       // a file that cannot be read just now is not written over
    }

    [Fact]
    public void The_checks_run_only_on_the_persons_own_readable_data_and_a_look_only_for_the_same_data()
    {
        var repo = new AndaCompanion.Data.MockAndaRepository();
        var records = repo.Numbers.Select(n => repo.Find(n)!).ToList();
        string dir = Temp.NewDir(), status = Path.Combine(dir, "status-seen.json");
        var none = Array.Empty<TimelineTodo>();
        Assert.Null(App.InputFor(false, records, "file:0000abcd", status, null, none, new TimelineSettings()));
        Assert.False(File.Exists(status));                                                   // an unreadable sheet leaves the kept days alone
        var look = LastLook.Of(records, new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc), "demo");
        var mine = App.InputFor(true, records, "file:0000abcd", status, look, none, new TimelineSettings())!;
        Assert.Null(mine.Look);                                                              // the demo's look is not this sheet's
        Assert.Equal(records.Count, StatusSeen.Load(status, "file:0000abcd").Items!.Count);
        Assert.NotNull(App.InputFor(true, records, "demo", status, look, none, new TimelineSettings())!.Look);
        Assert.Null(StatusSeen.Load(status, "file:0000abcd").Items);                         // the other data's days are not mixed in
    }

    [Fact]
    public void A_placeholder_date_far_ahead_is_quick_and_said_with_its_year()
    {
        var recs = Enumerable.Range(0, 200).Select(i => new TimelineRecord((200000 + i).ToString(), null, i % 2 == 0 ? "9999-12-31" : "2026-12-16", i % 2 == 0 ? null : "9999-12-31")).ToList();
        var todos = new[] { new TimelineTodo("t", "200000", "Fictional", "9999-12-31", false) };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var alerts = Timeline.Alerts(new TimelineInput("2026-11-20", recs, null, todos, new Dictionary<string, string>(), new TimelineSettings()), new BusinessCalendar());
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), watch.Elapsed.ToString());
        Assert.Contains(alerts, a => a.Text == "The forecast (Fri Dec 31, 9999) is more than a year after the goal date (Wed Dec 16, 2026).");
        var (cal, _) = Timeline.Calendar(recs.Take(3), "2026-11-20");
        Assert.All(cal.Split("\r\n"), l => Assert.True(System.Text.Encoding.UTF8.GetByteCount(l) <= 75, l));      // folded (RFC 5545)
    }

    [Fact]
    public void The_morning_check_is_once_a_day_from_eight_by_the_wall_clock()
    {
        Assert.Null(App.MorningDue(new DateTime(2026, 11, 20, 7, 59, 0), null));
        Assert.Equal("2026-11-20", App.MorningDue(new DateTime(2026, 11, 20, 8, 0, 0), null));
        Assert.Null(App.MorningDue(new DateTime(2026, 11, 20, 15, 0, 0), "2026-11-20"));          // once a day
        Assert.Equal("2026-11-21", App.MorningDue(new DateTime(2026, 11, 21, 10, 30, 0), "2026-11-20"));   // a PC asleep at 8 checks when it wakes
    }

    [Fact]
    public void A_mapped_network_drive_is_not_a_file_on_this_PC_and_a_second_calendar_the_same_day_is_newer()
    {
        try
        {
            DivisionNews.NetworkDriveForTests = p => p.StartsWith("Z:", StringComparison.OrdinalIgnoreCase);
            Assert.Equal(DivisionNews.SaidNotLocal, DivisionNews.PathProblem(@"Z:\News\fictional.docx"));
            Assert.Null(DivisionNews.PathProblem(@"C:\Users\Fictional\OneDrive\news.docx"));
        }
        finally { DivisionNews.NetworkDriveForTests = null; }
        var recs = new[] { new TimelineRecord("100001", null, "2026-12-01", null) };
        string Line(string text, string key) => text.Split("\r\n").Single(l => l.StartsWith(key));
        var (morning, _) = Timeline.Calendar(recs, "2026-11-20", new DateTime(2026, 11, 20, 13, 0, 0, DateTimeKind.Utc));
        var (afternoon, _) = Timeline.Calendar(recs, "2026-11-20", new DateTime(2026, 11, 20, 19, 30, 5, DateTimeKind.Utc));
        Assert.True(long.Parse(Line(afternoon, "SEQUENCE:")[9..]) > long.Parse(Line(morning, "SEQUENCE:")[9..]));
        Assert.Equal("DTSTAMP:20261120T193005Z", Line(afternoon, "DTSTAMP:"));
        Assert.Equal("SEQUENCE:0", Line(Timeline.Calendar(new[] { new TimelineRecord("100001", null, "2025-12-01", null) }, "2025-11-20").Text, "SEQUENCE:"));   // never negative
    }

    [Fact]
    public void A_window_filling_the_screen_holds_the_notices()
    {
        try
        {
            Presenting.StateForTests = () => true; Assert.True(Presenting.Now());             // a window fills the screen
            Presenting.StateForTests = () => false; Assert.False(Presenting.Now());
            Presenting.StateForTests = () => null; Assert.False(Presenting.Now());            // not known (not Windows)
            Presenting.StateForTests = null; if (!OperatingSystem.IsWindows()) Assert.False(Presenting.Now());
        }
        finally { Presenting.StateForTests = null; }
    }

    [Fact]
    public void A_notices_file_that_cannot_be_read_is_set_aside_and_a_fresh_day_starts()
    {
        string dir = Temp.NewDir(), path = Path.Combine(dir, "notices.json");
        File.WriteAllText(path, "{ not json");
        var s = Notices.Load(path)!;
        Assert.Equal(0, s.Shown); Assert.Empty(s.Seen);
        Assert.False(File.Exists(path));                                                     // set aside, not read again
        s.Day = "2026-11-20"; s.Shown = 2; s.Seen["d|100001|goal-passed|2026-11-20"] = "2026-11-20";
        Assert.True(Notices.Save(path, s));
        var back = Notices.Load(path)!;
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
        Assert.True(StatusSeen.Save(path, later, "file:0000abcd"));
        Assert.DoesNotContain("Fictional", File.ReadAllText(path));                          // never the status's words
        Assert.Equal(later["100002"], StatusSeen.Load(path, "file:0000abcd").Items!["100002"]);
        Assert.Equal((null, true), (StatusSeen.Load(path, "demo").Items, StatusSeen.Load(path, "demo").CanWrite));      // kept for other data: starts again
        File.WriteAllText(path, "[1,2");
        Assert.Null(StatusSeen.Load(path, "file:0000abcd").Items); Assert.False(File.Exists(path));
        Assert.Equal((null, false), StatusSeen.FromText(null, path, "file:0000abcd"));      // there but not readable just now: not written over
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
        // a text box as Word writes it, once for new readers and once for old ones (mc:Fallback): read once
        const string mc = "xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"";
        var box = Docx($"<w:p><w:r><mc:AlternateContent {mc}><mc:Choice Requires=\"wps\"><w:txbxContent><w:p><w:r><w:t>2026-11-19: Fictional box</w:t></w:r></w:p></w:txbxContent></mc:Choice>" +
            "<mc:Fallback><w:pict><w:txbxContent><w:p><w:r><w:t>2026-11-19: Fictional box</w:t></w:r></w:p></w:txbxContent></w:pict></mc:Fallback></mc:AlternateContent></w:r></w:p>");
        Assert.Equal(new[] { "2026-11-19: Fictional box", "" }, DivisionNews.ReadDocx(box).Paragraphs);
        // a long document, the newest line at its end: read
        var longDoc = Docx(string.Concat(Enumerable.Repeat("<w:p/>", 9000)) + "<w:p><w:r><w:t>2026-11-20: Fictional newest</w:t></w:r></w:p>");
        Assert.Equal("Fictional newest", DivisionNews.Parse(DivisionNews.ReadDocx(longDoc).Paragraphs!, "2026-11-20").Single().Text);
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
        // only a file on this PC: not a network share (Windows would sign in to it), not a web address, not too long to be kept
        Assert.Equal(DivisionNews.SaidNotLocal, DivisionNews.ReadDocx(@"\\fictional-server\share\news.docx").Problem);
        Assert.Equal(DivisionNews.SaidNotLocal, DivisionNews.ReadDocx("https://fictional.example/news.docx").Problem);
        Assert.NotNull(DivisionNews.PathProblem(@"C:\" + new string('a', 500) + ".docx"));
        Assert.Null(DivisionNews.PathProblem(@"C:\Users\Fictional\OneDrive\news.docx"));
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
        File.WriteAllText(path, "{\"DivisionNews\":true,\"NewsDocument\":\"\\\\\\\\fictional-server\\\\share\\\\news.docx\"}");
        var network = SettingsService.Load(path);
        Assert.Equal("", network.NewsDocument); Assert.False(network.DivisionNews);          // a network path saved earlier is not kept
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
            string? unreadCopied = null;
            var unread = new NewsWindow(new NewsHooks(() => null, () => (null, DivisionNews.SaidOff), _ => { }, () => "2026-11-20", _ => null, () => records, t => { unreadCopied = t; return Task.FromResult(true); }));
            try
            {
                w.Show(); off.Show(); unread.Show(); Settle();
                await unread.CopySummaryAsync();
                var unreadSaid = (now: Texts(unread.NowList), say: unread.Say.Text, copied: unreadCopied);
                var now = Texts(w.NowList); var later = Texts(w.LaterList); var newsTexts = Texts(w.NewsList); string note = w.NewsNote.Text ?? "";
                var offNow = Texts(off.NowList); var offLater = Texts(off.LaterList); string offNote = off.NewsNote.Text ?? ""; int offNews = off.NewsList.Children.Count;
                var row = w.NowList.Children.OfType<Button>().Single(); Click(row); Settle();
                bool laterIsText = w.LaterList.Children.OfType<Button>().Any();
                await w.CopySummaryAsync(); string sayCopy = w.Say.Text ?? "";
                await w.SaveCalendarAsync(); string saySave = w.Say.Text ?? "";
                saveAnswer = null; w.Say.Text = "kept"; await w.SaveCalendarAsync(); string sayCancel = w.Say.Text ?? "";
                saveAnswer = false; await w.SaveCalendarAsync(); string sayFail = w.Say.Text ?? "";
                return (now, later, newsTexts, note, offNow, offLater, offNote, offNews, opened, laterIsText, copied, sayCopy, saved, saySave, sayCancel, sayFail, unreadSaid);
            }
            finally { w.Close(); off.Close(); unread.Close(); Settle(); }
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
        Assert.Equal(new[] { Timeline.SaidUnreadable }, r.unreadSaid.now);                    // an unreadable sheet: said, and no summary of the demo
        Assert.Equal(Timeline.SaidUnreadable, r.unreadSaid.say); Assert.Null(r.unreadSaid.copied);
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
            int asked = 0;
            var w = new TodayWindow(new TodayHooks(() => all, Find, () => Array.Empty<string>(), () => Array.Empty<string>(), _ => { }, Alerts: () => { asked++; return alerts; }));
            var unread = new TodayWindow(new TodayHooks(() => all, Find, () => Array.Empty<string>(), () => Array.Empty<string>(), _ => { }, Alerts: () => null));
            var none = new TodayWindow(new TodayHooks(() => all, Find, () => Array.Empty<string>(), () => Array.Empty<string>(), _ => { }, Alerts: () => new[] { Alert("216482", "psg", "later", "info", "Info only.") }));
            var off = new TodayWindow(new TodayHooks(() => all, Find, () => Array.Empty<string>(), () => Array.Empty<string>(), _ => { }));
            // no ANDA needs a look by either rule: the sentence that nothing meets a check
            var calm = all.Where(x => !Today.NeedsLook(x)).ToList();
            var empty = new TodayWindow(new TodayHooks(() => calm, Find, () => Array.Empty<string>(), () => Array.Empty<string>(), _ => { }, Alerts: () => Array.Empty<TimelineAlert>()));
            var needIds = Today.Groups(Array.Empty<string>(), Array.Empty<string>(), all, Find)[0].Ids;
            try
            {
                w.Show(); none.Show(); off.Show(); unread.Show(); empty.Show(); Settle();
                var texts = Texts(w.List);
                Click(w.List.Children.OfType<Button>().First()); Click(w.List.Children.OfType<Button>().Last()); Settle();   // two row clicks repaint
                int askedAfterClicks = asked; w.Refresh(); Settle();
                var noneRows = none.List.Children.OfType<Button>().Select(b => (b.Tag as TodayWindow.RowKey)!).ToList();
                return (texts, noneTexts: Texts(none.List), offTexts: Texts(off.List), unreadTexts: Texts(unread.List), askedAfterClicks, askedAfterRefresh: asked,
                    emptyTexts: Texts(empty.List), noneRows, needIds);
            }
            finally { w.Close(); none.Close(); off.Close(); unread.Close(); empty.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.Equal(Timeline.TodayTitle, r.texts[0]);
        Assert.Contains(r.texts, t => t.Contains("First reason. (+1 more)"));             // the first reason, and how many more (info is not counted)
        Assert.DoesNotContain(r.texts, t => t.Contains("Second reason."));
        Assert.DoesNotContain(r.texts, t => t.Contains("Reason 7."));                     // at most eight ANDAs ...
        Assert.Contains(Timeline.TodayMore(2), r.texts);                                  // ... and how many more are in News and alerts
        // the owner's choice: one group. The round 34 "needs a look" ANDAs no timeline check names follow in it, and that group is not shown apart
        Assert.DoesNotContain("NEEDS A LOOK", r.texts); Assert.DoesNotContain("NEEDS A LOOK", r.noneTexts); Assert.Contains("NEEDS A LOOK", r.offTexts);
        Assert.Equal(Timeline.TodayTitle, r.noneTexts[0]);
        Assert.True(r.needIds.Count > 0);
        Assert.Equal(r.needIds, r.noneRows.Where(k => k.Section == "urgent").Select(k => k.Id));
        Assert.DoesNotContain(Timeline.TodayNone, r.noneTexts);
        Assert.Equal(new[] { Timeline.TodayTitle, Timeline.TodayNone }, r.emptyTexts.Take(2));
        Assert.DoesNotContain(Timeline.TodayTitle, r.offTexts);
        Assert.Equal(new[] { Timeline.TodayTitle, Timeline.SaidUnreadable }, r.unreadTexts.Take(2));   // the sheet could not be read: said, nothing from the demo
        Assert.Equal(1, r.askedAfterClicks);                                               // read once when Today opens, not at each paint
        Assert.Equal(2, r.askedAfterRefresh);                                              // and again when a setting changes
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
                w.PickNewsDocument = () => Task.FromResult<string?>(@"\\fictional-server\share\news.docx");
                Click(w.FindControl<Button>("NewsPick")!); await Task.Delay(1); Settle();
                string network = status.Text ?? ""; string pathAfterNetwork = s.NewsDocument;
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
                return (network, pathAfterNetwork, startCaps, refused, newsOn, wrongType, pathAfterWrong, chosen, path, divisionNews, watchBack, perDay, goalClose, checks, opens, clearedNews, clearedPath,
                    combos: combos.Count, comboNames: combos.Select(c => AutomationProperties.GetName(c)).ToList(), bareSaid: bare.FindControl<TextBlock>("WatchStatus")!.Text, bareOpen: bare.FindControl<Button>("NewsOpen")!.IsEnabled,
                    hint: w.FindControl<TextBlock>("WatchHint")!.Text, tlHint: w.FindControl<TextBlock>("TimelineHint")!.Text);
            }
            finally { w.Close(); bare?.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.Equal(DivisionNews.SaidNotLocal, r.network); Assert.Equal("", r.pathAfterNetwork);
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
