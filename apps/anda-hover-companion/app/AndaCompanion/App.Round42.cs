using Avalonia;
using Avalonia.Threading;
using AndaCompanion.Models;
using AndaCompanion.Services;
using AndaCompanion.UI;

namespace AndaCompanion;

// Round 42: the timeline checks over the person's own data, the notices near the clock, division news and News and alerts. Everything
// runs on this PC while the app runs; nothing is sent. The notices are off until the person switches them on.
public partial class App
{
    private NoticeWindow? _noticeWindow;
    private NewsWindow? _newsWindow;
    private List<NoticeItem> _noticeQueue = new();
    private FileSystemWatcher? _sheetWatcher;
    private DispatcherTimer? _sheetSettle, _clockTimer, _holdTimer;
    private string? _armedFor, _morningDone;
    private NewsReader? _newsReader;
    private bool _newsReading, _checkAfterRead;
    private string? _noticeNote;       // round 42, hardened: why notices wait, shown at the top of News and alerts
    private (object Read, string Day, IReadOnlyList<NewsItem> Items)? _newsParsed;

    /// <summary>The day the checks reckon from: today, or for the fictional demo records the day their data was taken (they are frozen there).</summary>
    private static string TimelineToday(IReadOnlyList<AndaRecord> records) =>
        Timeline.Iso(records.Count > 0 && records.All(r => r.Demo) && records[0].AsOfDate is { } asOf ? asOf : Today());

    private static TimelineRecord ForTimeline(AndaRecord r) => new(r.Number, r.Drug,
        r.GoalDateValue is { } g ? Timeline.Iso(g) : null, r.HasForecast && r.ForecastDateValue is { } f ? Timeline.Iso(f) : null, r.Latest);

    /// <summary>
    /// The alerts now, most urgent first, or null when the person's sheet cannot be read: then the app shows the demo records in its place, and
    /// the checks must not run on those (round 42 review). The timeline checks over the records, the ANDA to-do list, the look kept when Today
    /// last closed (if that switch is on, and only for the same data), when each status was first seen; and the card's own facts.
    /// </summary>
    internal IReadOnlyList<TimelineAlert>? CurrentAlerts()
    {
        List<AndaRecord> records;
        try { records = Records().ToList(); } catch (Exception) { return null; }      // an unreadable sheet never takes the app down
        if (InputFor(DataReadable(), records, LookSource(), AppPaths.StatusSeenFile, _settings.SinceLastLook ? LastLook.Load(AppPaths.LastLookFile) : null,
                _todo.Items.Select(t => new TimelineTodo(t.Id, t.Number, t.Title, t.Due, t.Done)), _settings.Timeline) is not { } input) return null;
        var ctx = CardCtx();
        var alerts = Timeline.Alerts(input, _calendar).ToList();
        foreach (var r in records) alerts.AddRange(Timeline.FromCard(r, WatchRules.Evaluate(r, ctx.Today, _calendar, ctx.Watch)));
        return Timeline.Sort(alerts);
    }

    /// <summary>
    /// What the checks read, or null when the data is not the person's own readable sheet (then nothing is checked and status-seen.json is left
    /// alone). Updates when each status was first seen, for this data only, and not when the file is locked; uses the kept look only when it
    /// was kept for the same data (round 42 review).
    /// </summary>
    internal static TimelineInput? InputFor(bool readable, IReadOnlyList<AndaRecord> records, string source, string statusPath, LastLook.Look? look,
        IEnumerable<TimelineTodo> todos, TimelineSettings settings)
    {
        if (!readable) return null;
        string today = TimelineToday(records);
        var (kept, canWrite) = StatusSeen.Load(statusPath, source);
        var seen = StatusSeen.Update(kept, records.Select(r => (r.Number, (string?)r.Latest)), today);
        bool changed = kept is null || kept.Count != seen.Count || seen.Any(kv => !kept.TryGetValue(kv.Key, out var k) || k != kv.Value);
        if (changed && canWrite) StatusSeen.Save(statusPath, seen, source, today);
        if (look is not null && look.Source != source) look = null;          // a look kept for other data says nothing about this one
        return new TimelineInput(today, records.Select(ForTimeline).ToList(),
            look?.Items.ToDictionary(kv => kv.Key, kv => (kv.Value.Goal, kv.Value.Forecast)), todos.ToList(), StatusSeen.Since(seen), settings);
    }

    /// <summary>The goal dates for the calendar file: none when the sheet cannot be read (never the demo's in its place).</summary>
    private IReadOnlyList<TimelineRecord> CalendarRecords() => DataReadable() ? Records().Select(ForTimeline).ToList() : Array.Empty<TimelineRecord>();

    /// <summary>
    /// The division news, or null when it is off, with the sentence News and alerts shows above it. The document is read away from the app's
    /// thread, and again only when its time or size changes (round 42, hardened); while it is read, the last read is shown, or "Reading…".
    /// </summary>
    private (IReadOnlyList<NewsItem>? Items, string Note) CurrentNews()
    {
        if (!_settings.DivisionNews || _settings.NewsDocument.Length == 0) return (null, DivisionNews.SaidOff);
        string path = _settings.NewsDocument;
        DateTime written = DateTime.MinValue; long length = -1;
        try { var info = new FileInfo(path); if (info.Exists) { written = info.LastWriteTimeUtc; length = info.Length; } }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        _newsReader ??= new NewsReader(DivisionNews.ReadDocx, work => Task.Run(work), work => Dispatcher.UIThread.Post(work));
        // when a read ends, News and alerts is painted again; the notices are checked again only when a check was waiting for this read, and
        // only when it worked, so opening News and alerts never brings up a notice by itself, and a failure never starts a loop (review)
        var (paragraphs, problem, reading) = _newsReader.Get(path, written, length, ok =>
        {
            _newsWindow?.Paint();
            if (ok && _checkAfterRead) { _checkAfterRead = false; if (NewsOn) CheckNotices(); }
        });
        _newsReading = reading;
        if (paragraphs is null) return (Array.Empty<NewsItem>(), problem ?? NewsReader.SaidReading);
        string day = Timeline.Iso(Today());       // parsed again only for a new read or a new day
        if (_newsParsed is not { } p || !ReferenceEquals(p.Read, paragraphs) || p.Day != day) _newsParsed = p = (paragraphs, day, DivisionNews.Parse(paragraphs, day));
        return (p.Items, problem is null ? DivisionNews.SaidFrom(Path.GetFileName(path)) : problem + " " + DivisionNews.SaidEarlier);
    }

    private bool NewsOn => _settings.DivisionNews && _settings.NewsDocument.Length > 0;

    /// <summary>Looks for what to tell the person, and shows the next notice if today's limit allows. Returns the sentence for Settings.</summary>
    internal string CheckNotices()
    {
        if (SelfTestRun is not null) return Notices.SaidNotRunning;
        if (!_settings.WatchBack && !NewsOn) { DropNotices(); NoteNotices(null); return Notices.SaidOff; }
        var items = new List<NoticeItem>();
        if (NewsOn && CurrentNews().Items is { } n) { items.AddRange(Notices.FromNews(n)); _checkAfterRead |= _newsReading; }     // still being read: checked again when it is
        if (_settings.WatchBack && CurrentAlerts() is { } alerts) items.AddRange(Notices.FromAlerts(alerts));
        string today = Timeline.Iso(Today());
        if (Notices.Load(AppPaths.NoticesFile) is not { } state) return NoteNotices(Notices.SaidBusy);      // locked just now: not written over, tried at the next check
        _noticeQueue = Notices.Pending(state, items, today);
        Notices.Save(AppPaths.NoticesFile, state);
        int pending = _noticeQueue.Count;
        string said = ShowNextNotice() ?? (pending == 0 ? Notices.SaidNothing : "");
        _newsWindow?.Paint();
        return said;
    }

    /// <summary>The switches are off: what waits is dropped, and a notice showing of a kind now off is closed (round 42 review).</summary>
    private void DropNotices()
    {
        _noticeQueue.RemoveAll(i => i.Kind == "news" ? !NewsOn : !_settings.WatchBack);
        if (_noticeQueue.Count == 0) NoteNotices(null);         // nothing waits: nothing is said to wait (review)
        if (_noticeWindow is { } w && (w.Item.Kind == "news" ? !NewsOn : !_settings.WatchBack)) w.Close();
    }

    /// <summary>The next notice, if none is showing, the switch for its kind is on, the screen is not presenting, and today's limit allows.</summary>
    private string? ShowNextNotice()
    {
        DropNotices();
        if (_noticeQueue.Count == 0) NoteNotices(null);
        if (_noticeWindow is not null || _noticeQueue.Count == 0) return null;
        if (Presenting.Now())
        {
            // a presentation, a full-screen program or Focus assist: the notice waits (it would show ANDA numbers on a shared screen)
            _holdTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
            _holdTimer.Tick -= OnHoldEnded; _holdTimer.Tick += OnHoldEnded; _holdTimer.Start();
            return NoteNotices(Notices.SaidHeld);
        }
        string today = Timeline.Iso(Today());
        if (Notices.Load(AppPaths.NoticesFile) is not { } state) return NoteNotices(Notices.SaidBusy);
        var next = Notices.Take(state, _noticeQueue, _settings.NoticesPerDay, today);
        Notices.Save(AppPaths.NoticesFile, state);
        if (next is null) return NoteNotices(Notices.SaidCap(_settings.NoticesPerDay, _noticeQueue.Count));
        NoteNotices(null);
        var win = new NoticeWindow(next, OpenFromNotice, item =>
        {
            if (Notices.Load(AppPaths.NoticesFile) is { } s) { Notices.Snooze(s, item.Key, Timeline.Iso(Today())); Notices.Save(AppPaths.NoticesFile, s); }
        }, _settings.NoticesStay);
        win.Closed += (_, _) =>
        {
            if (ReferenceEquals(_noticeWindow, win)) _noticeWindow = null;
            DispatcherTimer.RunOnce(() => ShowNextNotice(), TimeSpan.FromMilliseconds(500));
        };
        _noticeWindow = win;
        win.Show();
        return "";
    }

    /// <summary>Keeps the reason notices wait for News and alerts, and repaints it when the reason changes; returns the sentence.</summary>
    private string? NoteNotices(string? note)
    {
        if (note != _noticeNote) { _noticeNote = note; _newsWindow?.Paint(); }
        return note;
    }

    private void OnHoldEnded(object? sender, EventArgs e) { _holdTimer?.Stop(); ShowNextNotice(); }

    private void OpenFromNotice(NoticeItem item)
    {
        if (item.Kind == "news" || item.Number is null || _repository.Find(item.Number) is not { } r) { OpenNews(); return; }
        var cursor = TextCaptureService.GetCursorPosition();
        OpenCard(r, new PixelPoint(cursor.X, cursor.Y), mini: false);
    }

    /// <summary>News and alerts, once; opened from the tray menu's More, from Settings and from a notice.</summary>
    private void OpenNews()
    {
        if (_newsWindow is not null) { _newsWindow.Paint(); _newsWindow.Activate(); return; }
        var win = new NewsWindow(new NewsHooks(
            Alerts: CurrentAlerts, News: CurrentNews,
            OpenCard: id =>
            {
                if (_repository.Find(id) is not { } r) return;
                var cursor = TextCaptureService.GetCursorPosition();
                OpenCard(r, new PixelPoint(cursor.X, cursor.Y), mini: false);
            },
            Today: () => TimelineToday(Records().ToList()),
            DrugOf: n => _repository.Find(n)?.Drug,
            Records: CalendarRecords, NoticeNote: () => _noticeNote));
        win.Closed += (_, _) => { if (ReferenceEquals(_newsWindow, win)) _newsWindow = null; };
        _newsWindow = win;
        win.Show();
    }

    /// <summary>
    /// When the checks run by themselves: soon after start, when the portfolio file is saved (a watch on that one file, settled for three
    /// seconds), and each morning from 8 (a look at the wall clock every five minutes, so a change of daylight time or a PC asleep at 8 does
    /// not move it). Only while the app runs, and only when a switch is on. Built again only when the file or the switches change.
    /// </summary>
    private void ArmNotices()
    {
        bool on = SelfTestRun is null && (_settings.WatchBack || NewsOn);
        string armFor = on ? (_settings.PortfolioPath ?? "") + "|" + _settings.WatchBack + "|" + NewsOn : "off";
        if (armFor == _armedFor) return;
        _armedFor = armFor;
        _sheetWatcher?.Dispose(); _sheetWatcher = null;
        _clockTimer?.Stop(); _clockTimer = null;
        if (!on) { DropNotices(); return; }
        _sheetSettle ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _sheetSettle.Tick -= OnSheetSettled; _sheetSettle.Tick += OnSheetSettled;
        string path = _settings.PortfolioPath ?? "";
        if (path.Length > 0 && Path.GetDirectoryName(path) is { Length: > 0 } dir && Directory.Exists(dir))
        {
            try
            {
                var watcher = new FileSystemWatcher(dir, Path.GetFileName(path)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
                void Settle() => Dispatcher.UIThread.Post(() => { _sheetSettle!.Stop(); _sheetSettle.Start(); });
                watcher.Changed += (_, _) => Settle(); watcher.Created += (_, _) => Settle(); watcher.Renamed += (_, _) => Settle();
                // too many changes at once, or the folder went away: watch again from scratch, and check once (round 42 review)
                watcher.Error += (_, _) => Dispatcher.UIThread.Post(() => { if (ReferenceEquals(_sheetWatcher, watcher)) { _armedFor = null; ArmNotices(); Settle(); } });
                watcher.EnableRaisingEvents = true;
                _sheetWatcher = watcher;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException) { _sheetWatcher = null; }
        }
        _morningDone ??= DateTime.Now.Hour >= 8 ? DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : null;   // the start already checks
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _clockTimer.Tick += (_, _) =>
        {
            if (MorningDue(DateTime.Now, _morningDone) is not { } day) return;
            _morningDone = day; RefreshTrayState(); CheckNotices();
        };
        _clockTimer.Start();
    }

    /// <summary>The day to check now, at or after 8 in the morning once a day, or null (round 42: a look at the wall clock, not a fixed wait).</summary>
    internal static string? MorningDue(DateTime now, string? done)
    {
        string day = now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return now.Hour < 8 || done == day ? null : day;
    }

    private void OnSheetSettled(object? sender, EventArgs e) { _sheetSettle?.Stop(); RefreshTrayState(); CheckNotices(); }
}
