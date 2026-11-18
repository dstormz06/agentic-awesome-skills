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
            // My sheet since last week: off until the person switches it on; switching it off deletes the copies.
            WeeklyOn = () => _settings.KeepWeeklySheetCopy,
            SetWeekly = on => { _settings.KeepWeeklySheetCopy = on; SettingsService.Save(_settings); if (!on) _sheetCopies.DeleteAll(); },
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
        _sheetCopies.Capture(now, today);
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
