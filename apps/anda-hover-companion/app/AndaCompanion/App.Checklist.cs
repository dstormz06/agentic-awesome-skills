using Avalonia;
using Avalonia.Threading;
using AndaCompanion.Services;
using AndaCompanion.UI;

namespace AndaCompanion;

/// <summary>
/// "My checklist": a separate feature for anything, work or home. The person selects messy notes anywhere and presses their checklist shortcut
/// (chosen in Settings); the notes are read by simple rules on this PC and shown as suggested items. It is apart from the ANDA lookup, the draft
/// check and the ANDA to-do list: its own shortcut, its own windows, its own file, no AI and nothing sent anywhere.
/// </summary>
public partial class App
{
    private readonly ChecklistService _checklist = new();
    private ChecklistWindow? _checklistWindow;
    private NotesWindow? _notesWindow;

    private string ChecklistShortcutText() => string.IsNullOrWhiteSpace(_settings.ChecklistHotkey) ? "the tray menu (no shortcut is set)" : _settings.ChecklistHotkey;

    /// <summary>Runs on the hotkey thread. Same care as the other shortcuts: one capture at a time, the clipboard restored, nothing kept.</summary>
    private async Task OnChecklistHotkeyAsync()
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
            Dispatcher.UIThread.Post(() => OpenNotes(text));
        }
        finally { Interlocked.Exchange(ref _captureBusy, 0); }
    }

    /// <summary>Opens "Notes to checklist". With text and the "add at once" choice the items are added straight away, and Undo takes them back.</summary>
    private void OpenNotes(string? text)
    {
        _checklist.Refresh(Today());
        _notesWindow?.Close();
        bool atOnce = _settings.ChecklistMode == ChecklistMode.AddAtOnce && !string.IsNullOrWhiteSpace(text);
        var win = new NotesWindow(new NotesHooks(_checklist, Today, ChecklistShortcutText, OpenChecklistWindow), text ?? "", atOnce);
        win.Closed += (_, _) => { if (ReferenceEquals(_notesWindow, win)) _notesWindow = null; };
        _notesWindow = win;
        win.Show();
    }

    private void OpenChecklistWindow()
    {
        if (_checklistWindow is not null) { _checklistWindow.Reload(force: true); _checklistWindow.Activate(); return; }
        var hooks = new ChecklistHooks(_checklist, Today, ChecklistShortcutText, OpenNotes,
            OpenCard: (number, near) => { if (_repository.Find(number) is { } record) OpenCard(record, near, mini: false); });
        var win = new ChecklistWindow(hooks);
        win.Closed += (_, _) => { if (ReferenceEquals(_checklistWindow, win)) _checklistWindow = null; };
        _checklistWindow = win;
        win.Show();
    }
}
