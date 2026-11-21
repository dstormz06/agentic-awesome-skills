using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

/// <summary>What News and alerts needs from the app: the alerts, the news (null when off, with its sentence), and what its buttons do.</summary>
public sealed record NewsHooks(Func<IReadOnlyList<TimelineAlert>> Alerts, Func<(IReadOnlyList<NewsItem>? Items, string Note)> News, Action<string> OpenCard,
    Func<string> Today, Func<string, string?> DrugOf, Func<IReadOnlyList<TimelineRecord>> Records,
    Func<string, Task<bool>>? WriteClipboard = null, Func<string, Task<bool?>>? SaveCalendar = null);

/// <summary>
/// Round 42 (round 19 in the simulation): News and alerts. What the timeline checks find, immediate apart from longer term, each row opening
/// its card; division news, when on; a summary to copy for a team lead and the goal dates as a calendar file. Nothing here is sent anywhere,
/// and silence does not mean all is well.
/// </summary>
public sealed class NewsWindow : Window
{
    internal StackPanel NowList { get; } = new() { Spacing = 2 };
    internal StackPanel LaterList { get; } = new() { Spacing = 2 };
    internal StackPanel NewsList { get; } = new() { Spacing = 2 };
    internal TextBlock NewsNote { get; } = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    internal Button SummaryButton { get; } = new() { Content = "Copy a summary for my team lead" };
    internal Button CalendarButton { get; } = new() { Content = "Save goal dates as a calendar file…" };
    internal TextBlock Say { get; } = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly NewsHooks _hooks;
    private IReadOnlyList<TimelineAlert> _alerts = Array.Empty<TimelineAlert>();

    public const string SaidNowNone = "Nothing immediate in your data by these rules.", SaidLaterNone = "Nothing longer term in your data by these rules.";
    public const string Footer = "Division news is the text of one Word document your division keeps in SharePoint, read from the copy synced to this PC. The rest follows fixed rules over your own data, with the limits you chose in Settings, How it starts. Nothing here is sent anywhere, and silence does not mean all is well.";
    public const string SaidCopyFailed = "Windows did not allow copying just now. Please try again.";
    public static string SaidSaved(int n) => $"Saved {n} goal date{(n == 1 ? "" : "s")} as a calendar file. It holds each ANDA number and its goal date, nothing else. Outlook can open it.";
    public const string SaidNotSaved = "The calendar file could not be saved there. Nothing was written.";

    public NewsWindow(NewsHooks hooks)
    {
        _hooks = hooks;
        Title = "ANDA Companion (Notula) — News and alerts"; Width = 560; SizeToContent = SizeToContent.Height; CanResize = false; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        IBrush Res(string key) => (IBrush)Application.Current!.Resources[key]!;
        Background = Res("C.Bg"); NewsNote.Foreground = Say.Foreground = Res("C.Ink2");
        AutomationProperties.SetLiveSetting(Say, AutomationLiveSetting.Polite);
        TextBlock Label(string t) => new() { Text = t, FontSize = 11, FontWeight = FontWeight.Bold, LetterSpacing = 1.3, Foreground = Res("C.Ink3"), Margin = new Thickness(0, 10, 0, 2) };
        SummaryButton.Click += async (_, _) => await CopySummaryAsync();
        CalendarButton.Click += async (_, _) => await SaveCalendarAsync();
        var done = new Button { Content = "Done" }; done.Click += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        var buttons = new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var b in new[] { SummaryButton, CalendarButton, done }) buttons.Children.Add(b);
        var body = new StackPanel { Spacing = 2, Margin = new Thickness(20) };
        body.Children.Add(new TextBlock { Text = "News and alerts", FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Res("C.Ink") });
        foreach (var c in new Control[] { Label("IMMEDIATE · FROM YOUR DATA"), NowList, Label("LONGER TERM · FROM YOUR DATA"), LaterList, Label("DIVISION NEWS"), NewsNote, NewsList, buttons, Say,
                     new TextBlock { Text = Footer, TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Res("C.Ink3"), Margin = new Thickness(0, 8, 0, 0) } })
            body.Children.Add(c);
        Content = new ScrollViewer { Content = body, MaxHeight = 760 };
        Paint();
    }

    /// <summary>Reads the alerts and the news again and shows them.</summary>
    public void Paint()
    {
        IBrush Res(string key) => (IBrush)Application.Current!.Resources[key]!;
        _alerts = _hooks.Alerts().Where(a => a.Sev != "info").ToList();
        Control Row(TimelineAlert a)
        {
            var text = new TextBlock { Text = Timeline.Line(a), TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Res("C.Ink") };
            if (a.Number is not { } n) return new Border { Padding = new Thickness(6, 4), Child = text };
            var b = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent };
            AutomationProperties.SetName(b, Timeline.Line(a));
            b.Click += (_, _) => _hooks.OpenCard(n);
            return b;
        }
        Control Note(string t) => new TextBlock { Text = t, FontSize = 12, Foreground = Res("C.Ink2"), Margin = new Thickness(6, 4), TextWrapping = TextWrapping.Wrap };
        foreach (var (list, horizon, none) in new[] { (NowList, "now", SaidNowNone), (LaterList, "later", SaidLaterNone) })
        {
            list.Children.Clear();
            var rows = _alerts.Where(a => a.Horizon == horizon).ToList();
            if (rows.Count == 0) list.Children.Add(Note(none));
            foreach (var a in rows) list.Children.Add(Row(a));
        }
        var (items, note) = _hooks.News();
        NewsNote.Text = note; NewsList.Children.Clear();
        if (items is not null)
        {
            if (items.Count == 0) NewsList.Children.Add(Note(DivisionNews.SaidNone));
            foreach (var n in items)
                NewsList.Children.Add(new TextBlock { Text = $"{WatchRules.Short(Timeline.Iso(n.Date)!.Value)}  {n.Text}", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Res("C.Ink"), Margin = new Thickness(6, 2) });
        }
    }

    internal async Task CopySummaryAsync()
    {
        string text = Timeline.Summary(_alerts, _hooks.Today(), _hooks.DrugOf);
        int now = _alerts.Count(a => a.Horizon == "now"), later = _alerts.Count - now;
        Say.Text = await Copying.TextAsync(this, text, _hooks.WriteClipboard) ? Timeline.SaidCopied(now, later) : SaidCopyFailed;
    }

    internal async Task SaveCalendarAsync()
    {
        var (text, count) = Timeline.Calendar(_hooks.Records(), _hooks.Today());
        bool? saved = _hooks.SaveCalendar is { } save ? await save(text) : await SaveWithPickerAsync(text);
        if (saved is null) return;                       // the person cancelled: nothing to say
        Say.Text = saved == true ? SaidSaved(count) : SaidNotSaved;
    }

    private async Task<bool?> SaveWithPickerAsync(string text)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save goal dates as a calendar file", SuggestedFileName = "goal-dates.ics", DefaultExtension = "ics",
            FileTypeChoices = new[] { new FilePickerFileType("Calendar file") { Patterns = new[] { "*.ics" } } },
        });
        if (file is null) return null;
        try
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
            await writer.WriteAsync(text);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
