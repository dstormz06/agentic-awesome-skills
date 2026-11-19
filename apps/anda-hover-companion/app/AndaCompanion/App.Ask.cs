using Avalonia;
using Avalonia.Threading;
using AndaCompanion.Services;
using AndaCompanion.UI;

namespace AndaCompanion;

/// <summary>
/// "Ask about a selection": the person selects text or cells anywhere and presses the Ask shortcut (or chooses it in the tray menu). The window shows
/// the selection, offers quick checks on this PC and the person's stored prompts, and a prompt is filled in and shown before anything can be sent.
/// It reads the selection the way Select &amp; Peek does (same clipboard hand-off, same refusal in terminals, clipboard restored) and keeps nothing.
/// </summary>
public partial class App
{
    private AskWindow? _askWindow;

    private string AskMenuLabel() => string.IsNullOrWhiteSpace(_settings.AskHotkey) ? "Ask about a selection…" : $"Ask about a selection…\t{_settings.AskHotkey}";

    /// <summary>Runs on the hotkey thread. Same care as the other shortcuts: one capture at a time, the clipboard restored, nothing kept.</summary>
    private async Task OnAskHotkeyAsync()
    {
        if (Interlocked.Exchange(ref _captureBusy, 1) == 1) return;
        try
        {
            var cursor = TextCaptureService.GetCursorPosition();
            var near = new PixelPoint(cursor.X, cursor.Y);
            string text = "";
            if (_settings.CaptureViaCopy)
            {
                var result = await Task.Run(TextCaptureService.CaptureSelection);
                if (result.Notice is { } notice) Dispatcher.UIThread.Post(() => ShowNotice(notice, near));
                else if (result.Outcome == CaptureOutcome.Captured) text = result.Text ?? "";
            }
            Dispatcher.UIThread.Post(() => OpenAsk(text));
        }
        finally { Interlocked.Exchange(ref _captureBusy, 0); }
    }

    private void OpenAsk(string? text)
    {
        _askWindow?.Close();
        var cursor = TextCaptureService.GetCursorPosition();
        var near = new PixelPoint(cursor.X, cursor.Y);
        var hooks = new AskHooks(
            Prompts: () => _settings.MyActions,
            RunPrompt: (action, selection) => RunCustomAction(action, selection, near,
                withoutRecord: SelectionHelpers.Classify(selection).Kind is SelectionKind.Table or SelectionKind.List),
            ReviewDraft: OpenDraft,
            Lookup: number => { if (_repository.Find(number) is { } record) OpenCard(record, near, mini: false); else ShowNotice(NotFoundMessage(number), near); })
        {
            // Round 26: a note on an ANDA the selection names, and a calendar invitation. Both are the person's own action, on this PC.
            KnownAndas = selection => Proofreader.KnownIds(selection, _repository.Find),
            AddNote = (number, note) => _quickNotes.Add(number, note),
            Today = Today,
            MeetingPage = OpenHandoff,
        };
        var win = new AskWindow(hooks, text ?? "");
        win.Closed += (_, _) => { if (ReferenceEquals(_askWindow, win)) _askWindow = null; };
        _askWindow = win;
        win.Show();
    }

    private CompareWindow? _compareWindow;

    /// <summary>"Compare two files": two versions of the same report, compared on this PC. The narrative goes through the same preview as any prompt.</summary>
    private void OpenCompare()
    {
        if (_compareWindow is not null) { _compareWindow.Activate(); return; }
        var cursor = TextCaptureService.GetCursorPosition();
        var near = new PixelPoint(cursor.X, cursor.Y);
        var win = new CompareWindow
        {
            Narrative = digest => RunCustomAction(new CustomAction { Name = "Narrative of a comparison", Kind = ActionKind.AskAi, Template = FileCompare.AiTemplate, Enabled = true }, digest, near, withoutRecord: true),
            // Round 27. One of the person's stored prompts on what was compared: always shown first, never sent without the preview.
            Prompts = () => _settings.MyActions,
            RunPrompt = (action, prompt, holds) =>
            {
                switch (Runner.RunPrepared(action, "Compare two files", prompt, holds))
                {
                    case ActionNotice notice: ShowNotice(notice.Message, near); break;
                    case ActionShow show: ShowActionWindow(show.Model); break;
                }
            },
            Today = Today,
            // A report against my data: the goal and forecast dates the card shows, for an ANDA the data holds.
            Mine = number => _repository.Find(number) is { } r
                ? (r.GoalDateValue?.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture) ?? "",
                   r.HasForecast && r.ForecastDateValue is { } f ? f.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture) : "")
                : null,
            // My sheet since last week: off until the person switches it on. Switching it off keeps the copies that exist; only the person deletes them.
            WeeklyOn = () => _settings.KeepWeeklySheetCopy,
            SetWeekly = on => { _settings.KeepWeeklySheetCopy = on; SettingsService.Save(_settings); EventLog.Record("file", on ? "weekly copy of the sheet switched on" : "weekly copy of the sheet switched off"); },
            HasWeeklyCopies = () => _sheetCopies.Days().Count > 0,
            WeeklyCount = () => _sheetCopies.Days().Count,
            DeleteWeekly = () => { int n = _sheetCopies.DeleteAll(); if (n > 0) EventLog.Record("deleted", "kept copies of the sheet"); return n; },
            WeeklyPair = WeeklySheetPair,
        };
        win.Closed += (_, _) => { if (ReferenceEquals(_compareWindow, win)) _compareWindow = null; };
        _compareWindow = win;
        win.Show();
    }

    private readonly SheetCopies _sheetCopies = new();

    /// <summary>The last kept copy of the person's sheet and the sheet as it is now. Keeps this week's copy first if there is none yet.</summary>
    private (CompareTable? Earlier, CompareTable? Now, string? Problem) WeeklySheetPair()
    {
        if (string.IsNullOrWhiteSpace(_settings.PortfolioPath))
            return (null, null, "Please choose your portfolio file in Settings, My data, first. A copy can only be kept of a sheet the app reads.");
        CompareTable now;
        try { var raw = Services.Portfolio.TableReader.Read(_settings.PortfolioPath); now = new CompareTable("My sheet, today", raw.Headers, raw.Rows); }
        catch (Exception ex) when (ex is Services.Portfolio.PortfolioException or IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or System.Xml.XmlException)
        { return (null, null, "Your portfolio file could not be read just now, so there is nothing to compare."); }
        var today = Today();
        if (_settings.KeepWeeklySheetCopy) _sheetCopies.Capture(now, today);      // a new copy is kept only while keeping is switched on
        return _sheetCopies.LatestBefore(today) is { } earlier
            ? (earlier.Table, now, null)
            : (null, null, "The first copy of your sheet was kept today. What changed will show here once a copy from an earlier day exists.");
    }

    /// <summary>Keeps this week's copy of the sheet if the person switched that on. Called when the data is loaded; reads the file off the UI thread.</summary>
    private void KeepWeeklyCopyIfAsked()
    {
        if (!_settings.KeepWeeklySheetCopy || string.IsNullOrWhiteSpace(_settings.PortfolioPath) || SelfTestRun is not null) return;
        string path = _settings.PortfolioPath!; var today = Today();
        Task.Run(() =>
        {
            try { var raw = Services.Portfolio.TableReader.Read(path); _sheetCopies.Capture(new CompareTable("My sheet", raw.Headers, raw.Rows), today); }
            catch (Exception ex) when (ex is Services.Portfolio.PortfolioException or IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or System.Xml.XmlException) { /* no copy this time */ }
        });
    }

    private HandoffWindow? _handoffWindow;
    private ConnectionsWindow? _connectionsWindow;
    private AboutWindow? _aboutWindow;

    /// <summary>What is switched on, with what each connection sends, where and what is kept; and the event log.</summary>
    private void OpenConnections()
    {
        if (_connectionsWindow is not null) { _connectionsWindow.Paint(); _connectionsWindow.Activate(); return; }
        var win = new ConnectionsWindow(() => Connections.Describe(Connections.From(_settings)), () => EventLog.Read(), LogForClipboard, EventLog.FilePath) { WriteAll = EventLog.Export };
        void Follow() => Dispatcher.UIThread.Post(() => { if (ReferenceEquals(_connectionsWindow, win)) win.Paint(); });      // a line added while the window is open shows at once
        EventLog.Recorded += Follow;
        win.Closed += (_, _) => { EventLog.Recorded -= Follow; if (ReferenceEquals(_connectionsWindow, win)) _connectionsWindow = null; };
        _connectionsWindow = win;
        win.Show();
    }

    private Backups? _backups;
    private BackupsWindow? _backupsWindow;

    /// <summary>
    /// Backup copies of the person's lists: a copy after every change, and one look at start. Not in a self-test, which must leave nothing behind.
    /// </summary>
    private void StartBackups()
    {
        _todo.ArchiveAfterDays = _myTodo.ArchiveAfterDays = _settings.ArchiveAfterDays;
        if (SelfTestRun is not null) return;
        try
        {
            // Each copy is counted, and checked before it is put back, by the list's own reader: a copy the list could not read is never put in its place.
            var backups = new Backups(Path.Combine(AppPaths.Root, "backups"), () => _settings.BackupCopies);
            backups.Register(new("todo", "ANDA to-do list", AppPaths.TodoFile, "item", "items", TodoService.CountIn, () => _todo.Reload()));
            backups.Register(new("mytodo", "My to-do list", AppPaths.MyTodoFile, "item", "items", MyTodoService.CountIn, () => _myTodo.Reload()));
            backups.Register(new("notes", "Notes on ANDAs", AppPaths.QuickNotesFile, "note", "notes", QuickNotesService.CountIn, () => _quickNotes.Reload()));
            backups.Register(new("prompts", "Prompt library", AppPaths.PromptsFile, "prompt", "prompts", PromptLibraryService.CountIn, () => { _prompts.Reload(); RefreshTrayLabels(); }));
            backups.Register(new("replies", "Quick replies", AppPaths.RepliesFile, "reply", "replies", QuickReplyService.CountIn, () => _replies.Reload()));
            _backups = backups;
            SafeFile.Written += backups.Written;
            backups.Sweep();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { /* copies are a help, never a reason not to start */ }
    }

    private void OpenBackups()
    {
        if (_backups is not { } backups) return;
        if (_backupsWindow is not null) { _backupsWindow.Paint(); _backupsWindow.Activate(); return; }
        var win = new BackupsWindow(backups, () => _settings.BackupCopies, n => { _settings.BackupCopies = n; ApplySettings(); },
            () => _settings.ArchiveAfterDays, n => { _settings.ArchiveAfterDays = n; ApplySettings(); },
            new[]
            {
                new ArchiveSource("ANDA to-do list", "the ANDA to-do list", () => TodoArchive.Lines(AppPaths.TodoFile), id => _todo.PutBack(id, Today()), () => _todo.Waiting(Today()), id => _todo.SetDone(id, false, Today())),
                new ArchiveSource("My to-do list", "My to-do list", () => TodoArchive.Lines(AppPaths.MyTodoFile), id => _myTodo.PutBack(id, Today()), () => _myTodo.Waiting(Today()), id => _myTodo.SetDone(id, false, Today())),
            });
        void Follow() => Dispatcher.UIThread.Post(() => { if (ReferenceEquals(_backupsWindow, win)) win.Paint(); });      // a copy made while the window is open shows at once
        backups.Changed += Follow;
        win.Closed += (_, _) => { backups.Changed -= Follow; if (ReferenceEquals(_backupsWindow, win)) _backupsWindow = null; };
        _backupsWindow = win;
        win.Show();
    }

    private void OpenAbout()
    {
        if (_aboutWindow is not null) { _aboutWindow.Activate(); return; }
        var win = new AboutWindow();
        win.Closed += (_, _) => { if (ReferenceEquals(_aboutWindow, win)) _aboutWindow = null; };
        _aboutWindow = win;
        win.Show();
    }

    /// <summary>The whole log for the clipboard, or null when it is too long to copy (about 25,000 lines): saving has no such limit.</summary>
    private static string? LogForClipboard()
    {
        try
        {
            if (EventLog.FilePath is { } path && File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024) return null;
            using var text = new StringWriter();
            EventLog.Export(text);
            return text.ToString();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private ConnState _connectionState = new(null, Array.Empty<ConnAgent>(), null);

    /// <summary>
    /// Called when settings change: every window's line follows, and a connection that was switched on or off, or that now goes somewhere
    /// else, is written to the event log once.
    /// </summary>
    private void ConnectionsChanged()
    {
        var now = Connections.From(_settings);
        foreach (string line in Connections.Changes(_connectionState, now)) EventLog.Record("connection", line);
        _connectionState = now;
        ConnectionBar.Refresh();
        _connectionsWindow?.Paint();
    }

    /// <summary>The hand-off page (every ANDA, or those that need a look) or, with numbers, a meeting page for those ANDAs. Made on this PC; nothing is sent.</summary>
    private void OpenHandoff(IReadOnlyList<string>? numbers)
    {
        _handoffWindow?.Close();
        bool meeting = numbers is { Count: > 0 };
        var win = new HandoffWindow(meeting ? "Meeting page" : "Hand-off page", chooseWhich: !meeting, (onlyNeedALook, withNotes) =>
        {
            var ctx = CardCtx();
            var which = meeting ? numbers! : HandoffData.Order(_repository, ctx, onlyNeedALook);
            return Handoff.Text(meeting ? "Meeting page" : "Hand-off", HandoffData.Entries(which, _repository, ctx, _quickNotes, _todo.Items, _myTodo.Items),
                ctx.Today.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture), withNotes);
        });
        win.Closed += (_, _) => { if (ReferenceEquals(_handoffWindow, win)) _handoffWindow = null; };
        _handoffWindow = win;
        win.Show();
    }
}
