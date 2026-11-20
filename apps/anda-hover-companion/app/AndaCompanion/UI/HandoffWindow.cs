using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace AndaCompanion.UI;

/// <summary>
/// The hand-off page and the meeting page: plain text made from the person's own data and notes on this PC, by fixed rules. No AI is used and
/// nothing is sent; the person copies or saves the page and decides who gets it. It is what the data says today, not the system of record.
/// </summary>
public sealed class HandoffWindow : Window
{
    internal TextBox Page { get; }
    internal TextBlock Note { get; }
    internal Button AllButton { get; }
    internal Button WatchButton { get; }
    internal Button CopyButton { get; }
    internal Button SaveButton { get; }
    internal CheckBox NotesBox { get; }
    public Func<string, Task>? WriteClipboard { get; set; }
    public Func<string, Task<bool>>? SaveText { get; set; }

    private readonly Func<bool, bool, IReadOnlyList<string>> _lines;
    private readonly bool _meeting;
    private bool _onlyNeedALook;

    /// <param name="lines">Given "only those that need a look" and "include my notes", the lines of the page.</param>
    public HandoffWindow(string title, bool chooseWhich, Func<bool, bool, IReadOnlyList<string>> lines)
    {
        _lines = lines; _meeting = !chooseWhich;
        Title = "ANDA Companion (Notula) — " + title; Width = 660; SizeToContent = SizeToContent.Height; CanResize = false; Topmost = true; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        IBrush Res(string key) => (IBrush)Application.Current!.Resources[key]!;
        Background = Res("C.Bg");
        TextBlock Hint(string text) => new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Res("C.Ink2"), Text = text };

        AllButton = new Button { Content = "Every ANDA in my data" };
        WatchButton = new Button { Content = "Only those that need a look" };
        AllButton.Click += (_, _) => { _onlyNeedALook = false; Paint(); };
        WatchButton.Click += (_, _) => { _onlyNeedALook = true; Paint(); };
        var which = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = chooseWhich };
        AutomationProperties.SetName(which, "Which ANDAs");
        which.Children.Add(AllButton); which.Children.Add(WatchButton);

        // Notes are the person's own and can be candid: a page that may go to someone else holds them only when the person ticks the box, each time.
        NotesBox = new CheckBox { Content = "Include my notes", IsChecked = false };
        NotesBox.IsCheckedChanged += (_, _) => Paint();
        Page = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 300, FontSize = 13, FontFamily = new FontFamily("Consolas, Menlo, monospace") };
        AutomationProperties.SetName(Page, "The page, as text");
        Note = Hint(""); AutomationProperties.SetLiveSetting(Note, AutomationLiveSetting.Polite);

        CopyButton = new Button { Content = "Copy the page" };
        CopyButton.Click += async (_, _) =>
        {
            try
            {
                if (WriteClipboard is not null) await WriteClipboard(Page.Text ?? "");
                else if (Clipboard is { } clipboard) await clipboard.SetTextAsync(Page.Text ?? "");
                else throw new InvalidOperationException("no clipboard");
                Note.Text = "The page was copied. Nothing was sent; you decide who gets it.";
            }
            catch (Exception) { Note.Text = "Windows did not allow copying just now. Please select the text and copy it."; }
        };
        SaveButton = new Button { Content = "Save as a text file…" };
        SaveButton.Click += async (_, _) => await SaveAsync();
        var close = new Button { Content = "Close" };
        close.Click += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(CopyButton); buttons.Children.Add(SaveButton); buttons.Children.Add(close);
        var body = new StackPanel { Spacing = 10, Margin = new Thickness(20) };
        body.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Res("C.Ink") });
        body.Children.Add(Hint("Made from your own data and notes on this PC, by fixed rules. No AI is used and nothing is sent. You decide who gets it."));
        body.Children.Add(which); body.Children.Add(NotesBox); body.Children.Add(Page); body.Children.Add(buttons); body.Children.Add(Note);
        body.Children.Add(Hint("It is what your data and notes say today, not the system of record. Please read it before you pass it on."));
        Content = body;
        Paint();
    }

    private void Paint()
    {
        AllButton.Classes.Set("primary", !_onlyNeedALook); WatchButton.Classes.Set("primary", _onlyNeedALook);
        AutomationProperties.SetName(AllButton, "Every ANDA in my data" + (!_onlyNeedALook ? ", selected" : ""));
        AutomationProperties.SetName(WatchButton, "Only those that need a look" + (_onlyNeedALook ? ", selected" : ""));
        Page.Text = string.Join("\n", _lines(_onlyNeedALook, NotesBox.IsChecked == true));
    }

    private async Task SaveAsync()
    {
        string text = Page.Text ?? "";
        try
        {
            bool saved;
            if (SaveText is not null) saved = await SaveText(text);
            else
            {
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save the page", SuggestedFileName = _meeting ? "meeting-page.txt" : "hand-off.txt", DefaultExtension = "txt",
                    FileTypeChoices = new[] { new FilePickerFileType("Text file") { Patterns = new[] { "*.txt" } } },
                });
                if (file is null) return;
                await using var stream = await file.OpenWriteAsync();
                await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
                await writer.WriteAsync(text);
                saved = true;
            }
            Note.Text = saved ? "The page was saved. Nothing was sent." : "The page was not saved.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { Note.Text = "The page could not be saved there. Please choose another folder."; }
    }
}
