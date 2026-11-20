using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

/// <summary>
/// Connections and event log. First: what is switched on and, for each connection, what it sends, where, and what is kept. Then the event
/// log, newest first: times, kinds and counts only. The log is only ever added to; this window has no way to clear or change it.
/// </summary>
public sealed class ConnectionsWindow : Window
{
    internal StackPanel List { get; }
    internal StackPanel Log { get; }
    internal TextBlock LineText { get; }
    internal TextBlock Count { get; }
    internal TextBlock Note { get; }
    internal Button CopyListButton { get; }
    internal Button CopyCsvButton { get; }
    internal Button SaveCsvButton { get; }
    public Func<string, Task>? WriteClipboard { get; set; }
    public Func<string, Task<bool>>? SaveText { get; set; }

    private readonly Func<ConnResult> _describe;
    private readonly Func<(IReadOnlyList<EventEntry> Entries, int Total, int Unreadable)> _read;
    private readonly Func<string?> _csv;
    private readonly ScrollViewer _listScroll;

    /// <summary>Writes the whole log to a file, line by line. When set, saving uses it off the UI thread, so a long log never holds the window.</summary>
    public Func<TextWriter, int>? WriteAll { get; set; }
    private readonly TimeZoneInfo? _zone;      // a test may fix it; otherwise the zone the tool writes in, asked each time
    private readonly ScrollViewer _logScroll;
    private readonly TextBlock _logEmpty;

    /// <param name="csv">The whole log as CSV for the clipboard, or null when it is too long to copy.</param>
    public ConnectionsWindow(Func<ConnResult> describe, Func<(IReadOnlyList<EventEntry> Entries, int Total, int Unreadable)> read, Func<string?> csv, string? file, TimeZoneInfo? zone = null)
    {
        _describe = describe; _read = read; _csv = csv; _zone = zone;
        Title = "ANDA Companion (Notula) — Connections and event log"; Width = 680; SizeToContent = SizeToContent.Height; CanResize = false; Topmost = true; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        IBrush Res(string key) => (IBrush)Application.Current!.Resources[key]!;
        Background = Res("C.Bg");
        TextBlock Hint(string text) => new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Res("C.Ink2"), Text = text };
        TextBlock Label(string text) => new() { FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Res("C.Ink2"), Text = text, Margin = new Thickness(0, 6, 0, 0) };

        List = new StackPanel { Spacing = 6 }; AutomationProperties.SetName(List, "What is switched on");
        Log = new StackPanel { Spacing = 2 };
        _logScroll = new ScrollViewer { MaxHeight = 200, Content = Log, IsVisible = false }; AutomationProperties.SetName(_logScroll, "Event log, newest first");
        _logEmpty = Hint("Nothing has happened yet that is written here: a connection switched on or off, data sent, a file set aside or restored, something deleted at your request, an error.");
        Count = Hint(""); AutomationProperties.SetLiveSetting(Count, AutomationLiveSetting.Polite);
        Note = Hint(""); AutomationProperties.SetLiveSetting(Note, AutomationLiveSetting.Polite);
        LineText = new TextBlock { FontSize = 12, Foreground = Res("C.Ink2"), Margin = new Thickness(0, 4, 0, 0) };

        CopyListButton = new Button { Content = "Copy this list" };
        CopyListButton.Click += async (_, _) => await CopyAsync(string.Join("\n", _describe().Text), "The list was copied. Nothing was sent.");
        CopyCsvButton = new Button { Content = "Copy as CSV" };
        CopyCsvButton.Click += async (_, _) =>
        {
            if (_csv() is { } text) await CopyAsync(text, "The event log was copied as CSV. Nothing was sent.");
            else Note.Text = "The event log is too long to copy. Please use “Save as CSV…” instead.";
        };
        SaveCsvButton = new Button { Content = "Save as CSV…" };
        SaveCsvButton.Click += async (_, _) => await SaveAsync();
        var close = new Button { Content = "Close" };
        close.Click += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        var logButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        logButtons.Children.Add(CopyCsvButton); logButtons.Children.Add(SaveCsvButton); logButtons.Children.Add(close);
        var body = new StackPanel { Spacing = 8, Margin = new Thickness(20) };
        body.Children.Add(new TextBlock { Text = "Connections and event log", FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Res("C.Ink") });
        body.Children.Add(Hint("What is switched on, what each one sends, where it goes and what is kept. “Switched on” is about your settings; whether a service answers is said on the card, only after a real request."));
        // The list scrolls inside the window, so that with many connections the buttons below stay on the screen.
        _listScroll = new ScrollViewer { MaxHeight = 360, Content = List, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Opened += (_, _) => { if (Screens.ScreenFromWindow(this) is { } screen) _listScroll.MaxHeight = Math.Max(140, screen.WorkingArea.Height / screen.Scaling - 52 - 480); };
        body.Children.Add(_listScroll);
        body.Children.Add(Hint(Connections.Note));
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { CopyListButton } });
        body.Children.Add(Label("EVENT LOG · TIMES, KINDS AND COUNTS ONLY"));
        body.Children.Add(_logEmpty); body.Children.Add(_logScroll); body.Children.Add(Count); body.Children.Add(logButtons); body.Children.Add(Note);
        body.Children.Add(Hint("The log is only ever added to. It never holds an ANDA number, a name, an address or any text of yours. It is a plain file on this PC that you can open"
            + (file is null ? "." : ": " + file)));
        body.Children.Add(LineText);
        Content = body;
        Paint();
    }

    /// <summary>Reads what is switched on and the log again.</summary>
    public void Paint()
    {
        IBrush Res(string key) => (IBrush)Application.Current!.Resources[key]!;
        var r = _describe();
        List.Children.Clear();
        if (r.Items.Count == 0) List.Children.Add(new TextBlock { Text = "Nothing is switched on, so nothing leaves this PC.", FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = Res("C.Ink") });
        foreach (var i in r.Items)
        {
            var box = new StackPanel { Spacing = 2 };
            box.Children.Add(new TextBlock { Text = i.Name, FontSize = 13, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = Res("C.Ink") });
            foreach (string line in new[] { "Sends: " + i.Sends, "To: " + i.Where, "Kept: " + i.Kept })
                box.Children.Add(new TextBlock { Text = line, FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = Res("C.Ink") });
            List.Children.Add(new Border { BorderBrush = Res("C.Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8), Child = box });
        }
        LineText.Text = r.Line;

        var (entries, total, unreadable) = _read();
        Log.Children.Clear();
        foreach (var e in entries.Reverse())
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("170,230,*") };
            void Cell(int column, string text) { var t = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Res("C.Ink"), Margin = new Thickness(0, 0, 8, 0) }; Grid.SetColumn(t, column); row.Children.Add(t); }
            Cell(0, EventLog.Local(e.T, _zone)); Cell(1, EventLog.Meaning(e.Type)); Cell(2, e.Detail);
            Log.Children.Add(row);
        }
        _logEmpty.IsVisible = total == 0; _logScroll.IsVisible = total > 0;
        CopyCsvButton.IsEnabled = SaveCsvButton.IsEnabled = total > 0;
        string counted = total == 0 ? "" : $"{total:#,0} {(total == 1 ? "event" : "events")}{(total > entries.Count ? $"; the newest {entries.Count:#,0} are shown, and the export holds them all" : "")}.";
        string leftOut = unreadable == 0 ? "" : $"{unreadable:#,0} {(unreadable == 1 ? "line of the file is not a line of the log and is" : "lines of the file are not lines of the log and are")} left out.";
        Count.Text = string.Join(" ", new[] { counted, leftOut }.Where(t => t.Length > 0));
    }

    private async Task CopyAsync(string text, string done)
    {
        try
        {
            if (WriteClipboard is not null) await WriteClipboard(text);
            else if (Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
            else throw new InvalidOperationException("no clipboard");
            Note.Text = done;
        }
        catch (Exception) { Note.Text = "Windows did not allow copying just now."; }
    }

    private async Task SaveAsync()
    {
        try
        {
            bool saved;
            if (SaveText is not null) saved = await SaveText(_csv() ?? "");
            else
            {
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save the event log", SuggestedFileName = "anda-companion-events.csv", DefaultExtension = "csv" });
                if (file is null) { Note.Text = "Nothing was saved."; return; }
                await using var stream = await file.OpenWriteAsync();
                await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
                if (WriteAll is { } all) await Task.Run(() => all(writer));      // line by line, off the UI thread
                else await writer.WriteAsync(_csv() ?? "");
                saved = true;
            }
            Note.Text = saved ? "The event log was saved. Nothing was sent." : "Nothing was saved.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { Note.Text = "The file could not be saved there. Please choose another place."; }
    }
}

/// <summary>About and what changed: the version the tool shows, and the changes of the last rounds, newest first. The tool does not update itself.</summary>
public sealed class AboutWindow : Window
{
    internal TextBlock VersionText { get; }
    internal StackPanel Changes { get; }
    internal TextBlock Note { get; }
    internal Button CopyButton { get; }
    public Func<string, Task>? WriteClipboard { get; set; }

    public AboutWindow()
    {
        Title = "ANDA Companion (Notula) — About and what changed"; Width = 540; SizeToContent = SizeToContent.Height; CanResize = false; Topmost = true; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        IBrush Res(string key) => (IBrush)Application.Current!.Resources[key]!;
        Background = Res("C.Bg");
        TextBlock Hint(string text) => new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Res("C.Ink2"), Text = text };
        VersionText = new TextBlock { Text = "ANDA Companion (Notula) · " + ChangeLog.Version, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Res("C.Ink") };
        Changes = new StackPanel { Spacing = 3 };
        foreach (var (round, items) in ChangeLog.Entries)
        {
            Changes.Children.Add(new TextBlock { Text = $"ROUND {round}", FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Res("C.Ink2"), Margin = new Thickness(0, 8, 0, 0) });
            foreach (string item in items) Changes.Children.Add(new TextBlock { Text = "• " + item, FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = Res("C.Ink") });
        }
        Note = Hint(""); AutomationProperties.SetLiveSetting(Note, AutomationLiveSetting.Polite);
        CopyButton = new Button { Content = "Copy this page" };
        CopyButton.Click += async (_, _) =>
        {
            try
            {
                string text = string.Join("\n", ChangeLog.Text());
                if (WriteClipboard is not null) await WriteClipboard(text);
                else if (Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
                else throw new InvalidOperationException("no clipboard");
                Note.Text = "Copied.";
            }
            catch (Exception) { Note.Text = "Windows did not allow copying just now."; }
        };
        var close = new Button { Content = "Close" };
        close.Click += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(CopyButton); buttons.Children.Add(close);
        var body = new StackPanel { Spacing = 8, Margin = new Thickness(20) };
        body.Children.Add(new TextBlock { Text = "About and what changed", FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Res("C.Ink") });
        body.Children.Add(VersionText);
        body.Children.Add(Hint("A prototype with fictional data. Not an FDA product. It does not update itself."));
        body.Children.Add(Changes); body.Children.Add(buttons); body.Children.Add(Note);
        Content = body;
    }
}
