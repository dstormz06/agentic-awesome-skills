using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

/// <summary>A reply from an AI or an agent, ready to show. <see cref="Items"/> are suggested to-do lines the person may tick.</summary>
public sealed record ActionReply(bool Ok, string Text, string Message, IReadOnlyList<string> Items);

/// <summary>Everything the action window shows and does. <see cref="Send"/> is null when nothing can be sent (offline, or just a filled prompt).</summary>
public sealed record ActionWindowModel(
    string Title, string Subtitle, string PromptHeading, string Prompt, string Note,
    string ResultHeading, Func<CancellationToken, Task<ActionReply>>? Send, string SendLabel, bool SendOnOpen,
    Func<IReadOnlyList<string>, int>? AddTodos);

/// <summary>
/// Shows what one of the person's actions produced. For a prompt it shows the filled text, ready to copy. For an AI or an agent it first
/// shows exactly what will be sent and sends only after the person clicks (unless they chose, for that one action, to send at once).
/// A reply is plain text: never run, never passed on, and a suggested to-do item is added only when it is ticked and the button is pressed.
/// </summary>
public partial class ActionWindow : Window
{
    private readonly ActionWindowModel _model;
    private CancellationTokenSource? _cts;
    private readonly List<CheckBox> _boxes = new();
    private bool _sent;

    public ActionWindow(ActionWindowModel model)
    {
        _model = model;
        InitializeComponent();
        TitleText.Text = model.Title;
        SubText.Text = model.Subtitle;
        PromptHeading.Text = model.PromptHeading;
        PromptBox.Text = model.Prompt;
        NoteText.Text = model.Note;
        ResultHeading.Text = model.ResultHeading;
        SendBtn.IsVisible = model.Send is not null;
        SendBtn.Content = model.SendLabel;

        CloseBtn.Click += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closed += (_, _) => { _cts?.Cancel(); _cts?.Dispose(); };
        CopyPromptBtn.Click += async (_, _) => await CopyAsync(PromptBox.Text ?? "", CopyPromptBtn, "Copy prompt");
        CopyResultBtn.Click += async (_, _) => await CopyAsync(ResultBox.Text ?? "", CopyResultBtn, "Copy result");
        SendBtn.Click += async (_, _) => await SendAsync();
        AddTodoBtn.Click += (_, _) => AddTicked();
        if (model.SendOnOpen && model.Send is not null) Opened += async (_, _) => await SendAsync();
    }

    public bool WasSent => _sent;
    public string ResultText => ResultBox.Text ?? "";
    public IReadOnlyList<CheckBox> Suggestions => _boxes;

    private async Task CopyAsync(string text, Button button, string label)
    {
        if (Clipboard is not { } clip) { StatusText.Text = "The clipboard is not available. Please select the text and press Ctrl+C."; return; }
        await clip.SetTextAsync(text);
        button.Content = "Copied ✓";
        DispatcherTimer.RunOnce(() => button.Content = label, TimeSpan.FromSeconds(1.4));
    }

    public async Task SendAsync()
    {
        if (_model.Send is null || !SendBtn.IsEnabled) return;
        SendBtn.IsEnabled = false;
        StatusText.Text = "Sending… only the text above is sent.";
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        ActionReply reply;
        try { reply = await _model.Send(_cts.Token); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { reply = new(false, "", $"It did not complete ({ex.GetType().Name}). Nothing was changed.", Array.Empty<string>()); }
        _sent = true;
        SendBtn.IsEnabled = true;
        SendBtn.Content = "Send again";
        StatusText.Text = reply.Message;
        ResultPanel.IsVisible = reply.Ok;
        if (!reply.Ok) return;
        ResultBox.Text = reply.Text;
        _boxes.Clear(); SuggestList.Children.Clear();
        foreach (var item in reply.Items)
        {
            var box = new CheckBox { Content = new TextBlock { Text = item, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, IsChecked = false };
            box.IsCheckedChanged += (_, _) => AddTodoBtn.IsEnabled = _boxes.Any(b => b.IsChecked == true);
            _boxes.Add(box); SuggestList.Children.Add(box);
        }
        SuggestPanel.IsVisible = _boxes.Count > 0 && _model.AddTodos is not null;
        AddTodoBtn.IsEnabled = false;
    }

    private void AddTicked()
    {
        if (_model.AddTodos is null) return;
        var lines = _boxes.Where(b => b.IsChecked == true).Select(b => (string)((TextBlock)b.Content!).Text!).ToList();
        if (lines.Count == 0) return;
        int added = _model.AddTodos(lines);
        StatusText.Text = added == 0 ? "Those are already on your list." : $"Added {added} {(added == 1 ? "item" : "items")} to your to-do list.";
        foreach (var b in _boxes.Where(b => b.IsChecked == true)) { b.IsEnabled = false; }
        AddTodoBtn.IsEnabled = false;
    }
}
