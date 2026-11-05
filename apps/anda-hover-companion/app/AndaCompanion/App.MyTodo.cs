using Avalonia;
using Avalonia.Threading;
using AndaCompanion.Services;
using AndaCompanion.UI;

namespace AndaCompanion;

/// <summary>
/// "My to-do list": a separate feature for any daily workload, ANDA related or not. The person selects rough notes anywhere and presses their to-do shortcut
/// (chosen in Settings); the notes are read by simple rules on this PC and shown as suggested items. It is apart from the ANDA lookup, the draft
/// check and the ANDA to-do list: its own shortcut, its own windows, its own file, no AI and nothing sent anywhere.
/// </summary>
public partial class App
{
    private readonly MyTodoService _myTodo = new();
    private MyTodoWindow? _myTodoWindow;
    private NotesWindow? _notesWindow;

    private string MyTodoShortcutText() => string.IsNullOrWhiteSpace(_settings.MyTodoHotkey) ? "the tray menu (no shortcut is set)" : _settings.MyTodoHotkey;

    /// <summary>Runs on the hotkey thread. Same care as the other shortcuts: one capture at a time, the clipboard restored, nothing kept.</summary>
    private async Task OnMyTodoHotkeyAsync()
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

    /// <summary>Opens "Notes to my to-do list". With text and the "add at once" choice the items are added straight away, and Undo takes them back.</summary>
    private void OpenNotes(string? text)
    {
        _myTodo.Refresh(Today());
        _notesWindow?.Close();
        bool atOnce = _settings.MyTodoMode == MyTodoMode.AddAtOnce && !string.IsNullOrWhiteSpace(text);
        var win = new NotesWindow(new NotesHooks(_myTodo, Today, MyTodoShortcutText, OpenMyTodoWindow), text ?? "", atOnce);
        win.Closed += (_, _) => { if (ReferenceEquals(_notesWindow, win)) _notesWindow = null; };
        _notesWindow = win;
        win.Show();
    }

    private void OpenMyTodoWindow()
    {
        if (_myTodoWindow is not null) { _myTodoWindow.Reload(force: true); _myTodoWindow.Activate(); return; }
        var hooks = new MyTodoHooks(_myTodo, Today, MyTodoShortcutText, OpenNotes,
            OpenCard: (number, near) => { if (_repository.Find(number) is { } record) OpenCard(record, near, mini: false); });
        var win = new MyTodoWindow(hooks);
        win.Closed += (_, _) => { if (ReferenceEquals(_myTodoWindow, win)) _myTodoWindow = null; };
        _myTodoWindow = win;
        win.Show();
    }
}
