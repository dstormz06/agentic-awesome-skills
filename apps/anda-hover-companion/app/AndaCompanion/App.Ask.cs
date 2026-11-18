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
        };
        win.Closed += (_, _) => { if (ReferenceEquals(_compareWindow, win)) _compareWindow = null; };
        _compareWindow = win;
        win.Show();
    }
}
