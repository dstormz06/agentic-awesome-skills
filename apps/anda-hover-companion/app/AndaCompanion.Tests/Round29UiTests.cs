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
/// Round 29 in the Windows app: the line that says what is switched on at the foot of every window, the Connections and event log window,
/// the About window, and the places where the app writes to the event log. The rules are pinned to the simulation in <see cref="Round29Tests"/>.
/// </summary>
[Collection("Global state")]
public class Round29UiTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public Round29UiTests(HeadlessFixture fx) => _fx = fx;
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static string[] Texts(Control c) => c.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();

    [Fact]
    public async Task Every_app_window_carries_the_line_that_says_what_is_switched_on_and_it_follows_a_change()
    {
        string line = Connections.Offline; int opened = 0;
        var keepLine = ConnectionBar.Line; var keepOpen = ConnectionBar.Open;
        try
        {
            ConnectionBar.Line = () => line; ConnectionBar.Open = () => opened++;
            var r = await _fx.Session.Dispatch(async () =>
            {
                ThemeManager.Apply(Appearances.Default);
                var windows = new Window[] { new CompareWindow(), new AboutWindow(), new HandoffWindow("Hand-off page", true, (_, _) => new[] { "x" }), new UsageWindow(null, false) };
                var bars = new List<Button>();
                foreach (var w in windows) { w.Show(); Settle(); bars.Add(ConnectionBar.Attach(w)!); Settle(); }
                var first = bars.Select(b => (b.Content as string, AutomationProperties.GetName(b), b.MinHeight >= 24, b.IsVisible)).ToArray();
                bool once = ConnectionBar.Attach(windows[0]) is null;                                       // a second call adds no second line
                bool namesStillFound = windows[0].FindControl<Button>("SampleBtn") is not null;            // the window's own controls are where they were
                line = "Switched on: Claude (AI)"; ConnectionBar.Refresh();
                for (int i = 0; i < 10; i++) { await Task.Delay(15); Settle(); }
                var after = bars.Select(b => b.Content as string).ToArray();
                Click(bars[1]); Settle();
                // The card, the invite and the Connections window itself carry no such line.
                var conn = new ConnectionsWindow(() => Connections.Describe(new ConnState(null, Array.Empty<ConnAgent>(), null, Array.Empty<ConnMcp>())), () => (Array.Empty<EventEntry>(), 0, 0), () => "", null);
                bool none = ConnectionBar.Attach(conn) is null && ConnectionBar.Attach(new ChipWindow()) is null;
                foreach (var w in windows) w.Close();
                line = "changed after closing"; ConnectionBar.Refresh(); Settle();                       // a closed window is no longer told
                return (first, once, namesStillFound, after, none);
            }, CancellationToken.None);
            Assert.All(r.first, f => Assert.Equal((Connections.Offline, Connections.Offline + ". Opens the list of connections and the event log.", true, true), f));
            Assert.True(r.once); Assert.True(r.namesStillFound); Assert.True(r.none);
            Assert.All(r.after, a => Assert.Equal("Switched on: Claude (AI)", a));
            Assert.Equal(1, opened);
        }
        finally { ConnectionBar.Line = keepLine; ConnectionBar.Open = keepOpen; }
    }

    [Fact]
    public async Task The_connections_window_lists_what_each_connection_sends_and_keeps_and_the_event_log_newest_first()
    {
        var state = new ConnState(new ConnAi("Claude", "api.anthropic.com"), new[] { new ConnAgent("<b>Alert</b> Hub", "", true) }, new ConnOutlook("graph.microsoft.com"), Array.Empty<ConnMcp>());
        var t0 = new DateTime(2026, 7, 16, 13, 5, 7, 42, DateTimeKind.Utc);
        var entries = new List<EventEntry> { EventLog.Entry("connection", "AI switched on", t0), EventLog.Entry("sent", "AI request, 1,234 characters", t0.AddMinutes(1)), EventLog.Entry("sent", "about ANDA 216482", t0.AddMinutes(2)) };
        var copied = new List<string>(); var saved = new List<string>();
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var w = new ConnectionsWindow(() => Connections.Describe(state), () => (entries, 250, 2), () => EventLog.Csv(entries), @"C:\Users\me\AppData\Roaming\AndaCompanion\events.csv", eastern)
                { WriteClipboard = t => { copied.Add(t); return Task.CompletedTask; }, SaveText = t => { saved.Add(t); return Task.FromResult(true); } };
            w.Show(); Settle();
            var list = Texts(w.List); var rows = w.Log.Children.OfType<Grid>().Select(g => g.Children.OfType<TextBlock>().Select(t => t.Text ?? "").ToArray()).ToArray();
            string count = w.Count.Text ?? "", line = w.LineText.Text ?? "";
            Click(w.CopyListButton); Settle(); string s1 = w.Note.Text ?? "";
            Click(w.CopyCsvButton); Settle(); string s2 = w.Note.Text ?? "";
            Click(w.SaveCsvButton); Settle(); string s3 = w.Note.Text ?? "";
            var buttons = w.GetVisualDescendants().OfType<Button>().Select(b => b.Content as string ?? "").ToArray();
            var all = Texts(w);
            bool scrolls = w.List.Parent is ScrollViewer { MaxHeight: > 100 and < 2000 };
            w.Close();
            return (list, rows, count, line, s1, s2, s3, buttons, all, scrolls);
        }, CancellationToken.None);
        Assert.Contains("Claude (AI)", r.list); Assert.Contains("To: api.anthropic.com", r.list);
        Assert.Contains("<b>Alert</b> Hub (agent)", r.list);                                                        // a name with markup is text
        Assert.Contains("To: no address yet, so nothing can be sent", r.list); Assert.Contains("Outlook (read-only)", r.list);
        Assert.Contains("Kept: Mail is shown, never stored. The sign-in is held in memory until you sign out or close the app.", r.list);
        Assert.Equal(new[] { "Jul 16, 2026, 9:07:07 AM EDT", "Data sent", EventLog.Withheld }, r.rows[0]);                // newest first, in the person's zone, and never an ANDA number
        Assert.Equal(new[] { "Jul 16, 2026, 9:05:07 AM EDT", "Connection switched on, off or changed", "AI switched on" }, r.rows[2]);
        Assert.Equal("250 events; the newest 3 are shown, and the export holds them all. 2 lines of the file are not lines of the log and are left out.", r.count);
        Assert.Contains("Sends: When you sign in: this app's ID and the read-only permission it asks for. After that, only a search for the six-digit number of the open ANDA.", r.list);
        Assert.Contains("Kept: Its answer is kept in memory until you close the app, and counts as fresh for the minutes you chose. It is never written to a file.", r.list);
        Assert.True(r.scrolls);                                                                                      // with many connections the list scrolls and the buttons stay on the screen
        Assert.Equal("Switched on: Claude (AI) · <b>Alert</b> Hub (agent) · Outlook (read-only)", r.line);
        Assert.Equal(new[] { string.Join("\n", Connections.Describe(state).Text), EventLog.Csv(entries) }, copied);
        Assert.Equal(EventLog.Csv(entries), Assert.Single(saved));
        Assert.Equal(("The list was copied. Nothing was sent.", "The event log was copied as CSV. Nothing was sent.", "The event log was saved. Nothing was sent."), (r.s1, r.s2, r.s3));
        Assert.DoesNotContain(r.buttons, b => b.Contains("Clear", StringComparison.OrdinalIgnoreCase) || b.Contains("Delete", StringComparison.OrdinalIgnoreCase) || b.Contains("Reset", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(r.all, t => t.StartsWith("The log is only ever added to.") && t.EndsWith(@"events.csv"));
    }

    [Fact]
    public async Task With_nothing_switched_on_and_nothing_logged_the_window_says_so_and_offers_nothing_to_export()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var w = new ConnectionsWindow(() => Connections.Describe(new ConnState(null, Array.Empty<ConnAgent>(), null, Array.Empty<ConnMcp>())), () => (Array.Empty<EventEntry>(), 0, 0), () => EventLog.Csv(Array.Empty<EventEntry>()), null);
            w.Show(); Settle();
            var v = (Texts(w.List), w.LineText.Text, w.CopyCsvButton.IsEnabled, w.SaveCsvButton.IsEnabled, w.Count.Text, w.Log.Children.Count);
            w.Close(); return v;
        }, CancellationToken.None);
        Assert.Equal((Connections.Offline, false, false, "", 0), (r.Item2, r.Item3, r.Item4, r.Item5, r.Item6));
        Assert.Equal(new[] { "Nothing is switched on, so nothing leaves this PC." }, r.Item1);
    }

    [Fact]
    public async Task The_about_window_names_the_version_and_what_changed_and_can_be_copied()
    {
        var copied = new List<string>();
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var w = new AboutWindow { WriteClipboard = t => { copied.Add(t); return Task.CompletedTask; } };
            w.Show(); Settle();
            var texts = Texts(w); Click(w.CopyButton); Settle(); string note = w.Note.Text ?? "";
            w.Close(); return (texts, note);
        }, CancellationToken.None);
        Assert.Contains("ANDA Companion (Notula) · " + ChangeLog.Version, r.texts); Assert.Equal("Prototype, round " + ChangeLog.Entries[0].Round, ChangeLog.Version);   // the newest entry names the version (checked against the simulation via round 29's answers)
        Assert.Contains("A prototype with fictional data. Not an FDA product. It does not update itself.", r.texts);
        Assert.Equal(ChangeLog.Entries.Select(e => "ROUND " + e.Round), r.texts.Where(t => t.StartsWith("ROUND ")));   // every round, newest first
        Assert.Equal(ChangeLog.Entries.Select(e => e.Round).OrderByDescending(n => n), ChangeLog.Entries.Select(e => e.Round));
        Assert.All(ChangeLog.Entries.SelectMany(e => e.Items), i => Assert.Contains("• " + i, r.texts));
        Assert.Equal(string.Join("\n", ChangeLog.Text()), Assert.Single(copied)); Assert.Equal("Copied.", r.note);
    }

    private sealed class FakeAi(bool fail) : IChatCompleter
    {
        public string ProviderName => "Fake AI";
        public Task<string> CompleteAsync(string system, string user, int maxTokens, CancellationToken ct) =>
            fail ? Task.FromException<string>(new HttpRequestException("ANDA 216482 could not be sent to https://secret.example.test")) : Task.FromResult("- an answer about ANDA 216482");
    }

    /// <summary>A service that never answers in time: what an HTTP timeout looks like to the caller.</summary>
    private sealed class SlowAi : IChatCompleter
    {
        public string ProviderName => "Slow AI";
        public Task<string> CompleteAsync(string system, string user, int maxTokens, CancellationToken ct) => Task.FromException<string>(new TaskCanceledException("the request timed out"));
    }

    [Fact]
    public async Task A_request_to_an_AI_is_written_to_the_event_log_as_a_kind_and_a_size_never_as_text()
    {
        string path = Path.Combine(Temp.NewDir(), "events.csv");
        try
        {
            EventLog.Configure(path);
            string system = "You are careful.", user = "<selection>\nANDA 216482 is late; the applicant is Example Pharma (fictional).\n</selection>";
            await new ReportingChatCompleter(new FakeAi(false)).CompleteAsync(system, user, 100, CancellationToken.None);
            await Assert.ThrowsAsync<HttpRequestException>(() => new ReportingChatCompleter(new FakeAi(true)).CompleteAsync(system, user, 100, CancellationToken.None));
            await Assert.ThrowsAsync<TaskCanceledException>(() => new ReportingChatCompleter(new SlowAi()).CompleteAsync(system, user, 100, CancellationToken.None));
            var (entries, total, _) = EventLog.Read();
            // Each request is written before it goes out. One that fails, times out or is canceled has a second line: the text may have left all the same.
            Assert.Equal(5, total);
            string sent = $"AI request, {system.Length + user.Length} characters";
            Assert.Equal(new[] { ("sent", sent), ("sent", sent), ("send_failed", "AI request: no answer"), ("sent", sent), ("send_failed", "AI request: timed out or canceled") }, entries.Select(e => (e.Type, e.Detail)));
            string file = File.ReadAllText(path);
            foreach (string secret in new[] { "216482", "Example Pharma", "selection", "secret.example.test", "an answer about", "careful" }) Assert.DoesNotContain(secret, file);
        }
        finally { EventLog.Configure(null); ConnectionStatus.Reset(); }
    }
}
