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
    private DispatcherTimer? _sheetSettle, _morningTimer;

    /// <summary>The day the checks reckon from: today, or for the fictional demo records the day their data was taken (they are frozen there).</summary>
    private static string TimelineToday(IReadOnlyList<AndaRecord> records) =>
        Timeline.Iso(records.Count > 0 && records.All(r => r.Demo) && records[0].AsOfDate is { } asOf ? asOf : Today());

    private static TimelineRecord ForTimeline(AndaRecord r) => new(r.Number, r.Drug,
        r.GoalDateValue is { } g ? Timeline.Iso(g) : null, r.HasForecast && r.ForecastDateValue is { } f ? Timeline.Iso(f) : null, r.Latest);

    /// <summary>
    /// The alerts now, most urgent first: the timeline checks over the records, the ANDA to-do list, the look kept when Today last closed (if
    /// that switch is on) and when each status was first seen; and the card's own facts that are not about the timeline.
    /// </summary>
    internal IReadOnlyList<TimelineAlert> CurrentAlerts()
    {
        List<AndaRecord> records;
        try { records = Records().ToList(); } catch (Exception) { return Array.Empty<TimelineAlert>(); }      // an unreadable sheet never takes the app down
        string today = TimelineToday(records);
        var kept = StatusSeen.Load(AppPaths.StatusSeenFile);
        var seen = StatusSeen.Update(kept, records.Select(r => (r.Number, (string?)r.Latest)), today);
        if (kept is null || kept.Count != seen.Count || seen.Any(kv => !kept.TryGetValue(kv.Key, out var k) || k != kv.Value)) StatusSeen.Save(AppPaths.StatusSeenFile, seen);
        var look = _settings.SinceLastLook ? LastLook.Load(AppPaths.LastLookFile) : null;
        var input = new TimelineInput(today, records.Select(ForTimeline).ToList(),
            look?.Items.ToDictionary(kv => kv.Key, kv => (kv.Value.Goal, kv.Value.Forecast)),
            _todo.Items.Select(t => new TimelineTodo(t.Id, t.Number, t.Title, t.Due, t.Done)).ToList(), StatusSeen.Since(seen), _settings.Timeline);
        var ctx = CardCtx();
        var alerts = Timeline.Alerts(input, _calendar).ToList();
        foreach (var r in records) alerts.AddRange(Timeline.FromCard(r, WatchRules.Evaluate(r, ctx.Today, _calendar, ctx.Watch)));
        return Timeline.Sort(alerts);
    }

    /// <summary>The division news, or null when it is off, with the sentence News and alerts shows above it.</summary>
    private (IReadOnlyList<NewsItem>? Items, string Note) CurrentNews()
    {
        if (!_settings.DivisionNews || _settings.NewsDocument.Length == 0) return (null, DivisionNews.SaidOff);
        var (paragraphs, problem) = DivisionNews.ReadDocx(_settings.NewsDocument);
        return paragraphs is null ? (Array.Empty<NewsItem>(), problem!) : (DivisionNews.Parse(paragraphs, Timeline.Iso(Today())), DivisionNews.SaidFrom(Path.GetFileName(_settings.NewsDocument)));
    }

    /// <summary>Looks for what to tell the person, and shows the next notice if today's limit allows. Returns the sentence for Settings.</summary>
    internal string CheckNotices()
    {
        if (SelfTestRun is not null) return Notices.SaidNotRunning;
        bool news = _settings.DivisionNews && _settings.NewsDocument.Length > 0;
        if (!_settings.WatchBack && !news) { _noticeQueue.Clear(); return Notices.SaidOff; }
        var items = new List<NoticeItem>();
        if (news && CurrentNews().Items is { } n) items.AddRange(Notices.FromNews(n));
        if (_settings.WatchBack) items.AddRange(Notices.FromAlerts(CurrentAlerts()));
        string today = Timeline.Iso(Today());
        var state = Notices.Load(AppPaths.NoticesFile);
        _noticeQueue = Notices.Pending(state, items, today);
        Notices.Save(AppPaths.NoticesFile, state);
        int pending = _noticeQueue.Count;
        string said = ShowNextNotice() ?? (pending == 0 ? Notices.SaidNothing : "");
        _newsWindow?.Paint();
        return said;
    }

    /// <summary>The next notice, if none is showing and today's limit allows; the sentence when the limit is reached.</summary>
    private string? ShowNextNotice()
    {
        if (_noticeWindow is not null || _noticeQueue.Count == 0) return null;
        string today = Timeline.Iso(Today());
        var state = Notices.Load(AppPaths.NoticesFile);
        var next = Notices.Take(state, _noticeQueue, _settings.NoticesPerDay, today);
        Notices.Save(AppPaths.NoticesFile, state);
        if (next is null) return Notices.SaidCap(_settings.NoticesPerDay, _noticeQueue.Count);
        var win = new NoticeWindow(next, OpenFromNotice, item =>
        {
            var s = Notices.Load(AppPaths.NoticesFile); Notices.Snooze(s, item.Key, Timeline.Iso(Today())); Notices.Save(AppPaths.NoticesFile, s);
        });
        win.Closed += (_, _) =>
        {
            if (ReferenceEquals(_noticeWindow, win)) _noticeWindow = null;
            DispatcherTimer.RunOnce(() => ShowNextNotice(), TimeSpan.FromMilliseconds(500));
        };
        _noticeWindow = win;
        win.Show();
        return "";
    }

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
            Records: () => Records().Select(ForTimeline).ToList()));
        win.Closed += (_, _) => { if (ReferenceEquals(_newsWindow, win)) _newsWindow = null; };
        _newsWindow = win;
        win.Show();
    }

    /// <summary>
    /// When the checks run by themselves: soon after start, when the portfolio file is saved (a watch on that one file, settled for three
    /// seconds), and each morning at 8. Only while the app runs, and only when the notices are on; Settings' Check now runs them at once.
    /// </summary>
    private void ArmNotices()
    {
        _sheetWatcher?.Dispose(); _sheetWatcher = null;
        _morningTimer?.Stop(); _morningTimer = null;
        if (SelfTestRun is not null || (!_settings.WatchBack && !(_settings.DivisionNews && _settings.NewsDocument.Length > 0))) return;
        _sheetSettle ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _sheetSettle.Tick -= OnSheetSettled; _sheetSettle.Tick += OnSheetSettled;
        string path = _settings.PortfolioPath ?? "";
        if (path.Length > 0 && Path.GetDirectoryName(path) is { Length: > 0 } dir && Directory.Exists(dir))
        {
            try
            {
                _sheetWatcher = new FileSystemWatcher(dir, Path.GetFileName(path)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
                FileSystemEventHandler changed = (_, _) => Dispatcher.UIThread.Post(() => { _sheetSettle!.Stop(); _sheetSettle.Start(); });
                _sheetWatcher.Changed += changed; _sheetWatcher.Created += changed;
                _sheetWatcher.Renamed += (_, _) => Dispatcher.UIThread.Post(() => { _sheetSettle!.Stop(); _sheetSettle.Start(); });
                _sheetWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException) { _sheetWatcher = null; }
        }
        var now = DateTime.Now; var at = now.Date.AddHours(8);
        if (at <= now) at = at.AddDays(1);
        _morningTimer = new DispatcherTimer { Interval = at - now };
        _morningTimer.Tick += (_, _) => { _morningTimer!.Stop(); RefreshTrayState(); CheckNotices(); ArmNotices(); };
        _morningTimer.Start();
    }

    private void OnSheetSettled(object? sender, EventArgs e) { _sheetSettle?.Stop(); RefreshTrayState(); CheckNotices(); }
}
