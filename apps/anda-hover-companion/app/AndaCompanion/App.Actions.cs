using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using AndaCompanion.Data;
using AndaCompanion.Models;
using AndaCompanion.Services;
using AndaCompanion.UI;

namespace AndaCompanion;

/// <summary>
/// The person's own actions (their prompts and agents, started by a shortcut or from the tray menu) and the to-do list.
/// Nothing here runs by itself: an action starts only when its shortcut is pressed or it is chosen from a menu, an AI or agent is asked
/// only after the person clicks Send (unless they chose, for that one action, to send at once), and a reply is text to read.
/// </summary>
public partial class App
{
    /// <summary>The bindings for Settings. The actions are copied, so a shortcut keeps meaning what it meant when it was registered.</summary>
    private IReadOnlyList<HotkeyBinding> HotkeyBindings()
    {
        _boundActions = _settings.MyActions.Select(a => a.Clone()).ToList();
        return HotkeyService.FromCombos(_settings.PeekHotkey, _settings.LookupHotkey, _settings.DraftHotkey, _boundActions, _settings.ShowAllTools ? _settings.MyTodoHotkey : null, _settings.AskHotkey);
    }

    private string AgentsFingerprint() => System.Text.Json.JsonSerializer.Serialize(_settings.Agents);

    private bool IsAtRisk(AndaRecord record) => UrgencyPolicy.IsUrgent(record, Today(), _calendar, _settings.UrgentLateDays);

    // ---------- starting an action ----------

    /// <summary>Runs on the hotkey thread. Same care as the other shortcuts: one capture at a time, the clipboard restored, nothing kept.</summary>
    private async Task OnActionHotkeyAsync(int index)
    {
        var actions = _boundActions;
        if (index < 0 || index >= actions.Count || !actions[index].Enabled) return;
        var action = actions[index];
        if (Interlocked.Exchange(ref _captureBusy, 1) == 1) return;
        try
        {
            var cursor = TextCaptureService.GetCursorPosition();
            var near = new PixelPoint(cursor.X, cursor.Y);
            string? text = null;
            if (_settings.CaptureViaCopy)
            {
                var result = await Task.Run(TextCaptureService.CaptureSelection);
                if (result.Notice is { } notice) Dispatcher.UIThread.Post(() => ShowNotice(notice, near));
                else if (result.Outcome == CaptureOutcome.Captured) text = result.Text;
            }
            Dispatcher.UIThread.Post(() => RunCustomAction(action, text, near));
        }
        finally { Interlocked.Exchange(ref _captureBusy, 0); }
    }

    /// <summary>From the tray menu or Settings. There is no selection, so the person is asked for an ANDA number first.</summary>
    private void RunActionFromMenu(CustomAction action)
    {
        var copy = action.Clone();
        var cursor = TextCaptureService.GetCursorPosition();
        var near = new PixelPoint(cursor.X, cursor.Y);
        if (MyActionRules.Validate(copy, Array.Empty<(string Name, string Hotkey)>()) is { } problem) { ShowNotice($"{copy.Name}: {problem}", near); return; }
        AskForAnda(copy, near);
    }

    private ActionRunner Runner => _runner ??= new ActionRunner(Today, () => _calendar, _todo, _agents, ReferenceFor,
        () => ChatCompleterFactory.CreateReporting(_settings), () => ConnectionStatus.Current.Label);

    /// <summary>Works out what the action runs on (a selected ANDA in the data, or a number the person is asked for), then runs it.</summary>
    /// <param name="withoutRecord">True for a table or list from Ask: it holds many ANDAs, so none is picked as "the" record and {number}, {drug} and the like stay empty.</param>
    private void RunCustomAction(CustomAction action, string? selection, PixelPoint near, bool withoutRecord = false)
    {
        switch (Runner.Start(action, selection, withoutRecord ? _ => null : _repository.Find))
        {
            case StartNotFound missing: ShowNotice(NotFoundMessage(missing.Number), near); break;
            case StartAskForAnda: AskForAnda(action, near); break;
            case StartReady ready: ExecuteAction(action, ready.Record, ready.Number, ready.Selection, near); break;
        }
    }

    private void AskForAnda(CustomAction action, PixelPoint near)
    {
        new LookupWindow(number =>
        {
            if (_repository.Find(number) is { } record) ExecuteAction(action, record, number, "", near);
            else ShowNotice(NotFoundMessage(number), near);
        }).Show();
    }

    private void ExecuteAction(CustomAction action, AndaRecord? record, string? number, string selection, PixelPoint near)
    {
        switch (Runner.Run(action, record, number, selection))
        {
            case ActionNotice notice: ShowNotice(notice.Message, near); break;
            case ActionShow show: ShowActionWindow(show.Model); break;
        }
    }

    private int AddAgentTodos(IReadOnlyList<string> lines, AndaRecord record, string agentName) => Runner.AddLines(lines, record.Number, agentName);

    private void ShowActionWindow(ActionWindowModel model)
    {
        _actionWindow?.Close();
        var win = new ActionWindow(model);
        win.Closed += (_, _) => { if (ReferenceEquals(_actionWindow, win)) _actionWindow = null; };
        _actionWindow = win;
        win.Show();
    }

    // ---------- tray menu ----------

    /// <summary>The "My actions" submenu, rebuilt whenever Settings change so it always matches the list.</summary>
    private void RebuildActionsMenu()
    {
        if (_actionsMenu?.Menu is not { } menu) return;
        menu.Items.Clear();
        var actions = _settings.MyActions.Where(a => a.Enabled).ToList();
        if (actions.Count == 0) menu.Add(new NativeMenuItem("No actions yet. Add one in Settings.") { IsEnabled = false });
        foreach (var a in actions)
        {
            var copy = a.Clone();
            var item = new NativeMenuItem(string.IsNullOrWhiteSpace(a.Hotkey) ? a.Name : $"{a.Name}\t{a.Hotkey}");
            item.Click += (_, _) => RunActionFromMenu(copy);
            menu.Add(item);
        }
    }

    // ---------- to-do list ----------

    private void OpenTodoWindow()
    {
        if (_todoWindow is not null) { _todoWindow.ApplyTimer(); _todoWindow.Reload(force: true); _todoWindow.Activate(); return; }
        var hooks = new TodoHooks(
            Build: () => TodoBuilder.Build(_repository, CardCtx(), _todo, _settings.TodoShowFromData, DateTime.Now),
            Store: _todo, RefreshSeconds: () => _settings.TodoRefreshSeconds, Today: Today, Calendar: () => _calendar,
            OpenCard: (number, near) => { if (_repository.Find(number) is { } record) OpenCard(record, near, mini: false); },
            SourceNote: () => _portfolio is { Status.Ok: true } p ? $"Using {p.FileName}." : "Using the fictional demo records.");
        var win = new TodoWindow(hooks);
        win.Closed += (_, _) => { if (ReferenceEquals(_todoWindow, win)) _todoWindow = null; };
        _todoWindow = win;
        win.Show();
    }
}
