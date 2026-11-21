using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AndaCompanion.Data;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

/// <summary>What Today reads and can do. Everything is asked for when the window opens or a row is pressed; nothing runs while it is closed.</summary>
/// <param name="All">Every record the data holds, each with whether it is urgent and its watch flags.</param>
/// <param name="Find">One record by number, or null.</param>
/// <param name="Pins">The ANDAs kept in the person's list.</param>
/// <param name="Recents">The recent ones.</param>
/// <param name="OpenCard">Opens the card of an ANDA.</param>
/// <param name="CopyText">Puts text on the clipboard and says whether it got there.</param>
public sealed record TodayHooks(Func<IReadOnlyList<Today.Look>> All, Func<string, Today.Look?> Find, Func<IReadOnlyList<string>> Pins, Func<IReadOnlyList<string>> Recents,
    Action<string> OpenCard, Func<string, Task<bool>>? CopyText = null, Action? ShowAll = null,
    Func<LastLook.View?>? Looks = null, Action<LastLook.Look>? KeepLook = null, Func<NextWeek.Result?>? Week = null,
    Func<IReadOnlyList<TimelineAlert>?>? Alerts = null);

/// <summary>
/// Today (round 33): a small two-pane window. Left: what needs a look, the ANDAs kept in my list, recents. Right: the selected one, with
/// Copy status. The same rules as the card and the watch list, read from the data when this window is opened.
/// </summary>
public sealed class TodayWindow : Window
{
    private readonly TodayHooks _hooks;
    private readonly IReadOnlyList<Today.Look> _all;
    private readonly StackPanel _list = new() { Spacing = 2 };
    private readonly StackPanel _main = new() { Spacing = 8 };
    private readonly TextBlock _say = new() { FontSize = 1, Opacity = 0, Height = 1, IsHitTestVisible = false };
    private string? _selected;
    /// <summary>What Today shows of "Since you last looked": its sentence, what moved (none the first time), and the look to keep on closing.</summary>
    private sealed record LookShown(string Note, (List<LastLook.Change> Rows, int More)? Since, LastLook.Look? Keep);
    private LookShown? _look;          // null: switched off, no group and nothing kept
    private NextWeek.Result? _week;
    /// <summary>A row of the list: the section it is in and its ANDA, so that the keyboard stays on the row that was pressed.</summary>
    public sealed record RowKey(string Section, string Id);

    public TextBox Search { get; } = new() { Watermark = "Type an ANDA number", FontSize = 13 };
    public StackPanel List => _list;
    public StackPanel Main => _main;
    public Button CopyButton { get; private set; } = new();
    public Button OpenButton { get; private set; } = new();
    public Button CloseButton { get; } = new() { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right };
    /// <summary>Round 34: the watch list has no tray item; it opens from here, with the reason for each ANDA. Shown while something needs a look.</summary>
    public Button ShowAllButton { get; } = new() { Content = ShowAllLabel, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
    public const string ShowAllLabel = "Show all, with reasons…";
    public string? Selected => _selected;
    /// <summary>What was last said to a screen reader, for a test to read.</summary>
    public string Said => _say.Text ?? "";

    private static IBrush Res(string key) => (IBrush)Application.Current!.Resources[key]!;

    public TodayWindow(TodayHooks hooks)
    {
        _hooks = hooks; _all = hooks.All();
        // Round 36: read once when the window opens: what moved since the look kept when Today was last closed, and next week from Thursday noon.
        ReadLooks();
        // what is kept is the look that was shown, so that a change that comes in while Today is open is listed the next time, not lost;
        // the handler is always there, and the look to keep follows the switch (round 37 review)
        Closed += (_, _) => { if (_look?.Keep is { } shown) hooks.KeepLook?.Invoke(shown); };
        Title = "ANDA Companion (Notula) — Today"; Width = 640; Height = 430; MinWidth = 520; MinHeight = 320; CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Res("C.Bg");
        AutomationProperties.SetName(Search, "Find an ANDA number");
        AutomationProperties.SetLiveSetting(_say, AutomationLiveSetting.Polite);      // only a short sentence is said; the right-hand side is not read again whole

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 10) };
        head.Children.Add(new TextBlock { Text = "Today", FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Res("C.Ink"), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(CloseButton, 1); head.Children.Add(CloseButton);

        var side = new DockPanel();
        DockPanel.SetDock(Search, Dock.Top); Search.Margin = new Thickness(0, 0, 0, 8); side.Children.Add(Search);
        AutomationProperties.SetName(ShowAllButton, "Show all that need a look, with reasons, in the watch list");
        ShowAllButton.Click += (_, _) => _hooks.ShowAll?.Invoke();
        // Show all is drawn below the list and comes after it with Tab too (round 34).
        var listAndAll = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        var listScroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        listAndAll.Children.Add(listScroll);
        Grid.SetRow(ShowAllButton, 1); listAndAll.Children.Add(ShowAllButton);
        side.Children.Add(listAndAll);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("240,16,*") };
        grid.Children.Add(side);
        var right = new ScrollViewer { Content = _main, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Grid.SetColumn(right, 2); grid.Children.Add(right);

        var hint = new TextBlock { Text = "The same rules as the card and the watch list, read from your data when you open this window. Nothing runs while it is closed.", FontSize = 11, Foreground = Res("C.Ink3"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        var root = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(head, Dock.Top); DockPanel.SetDock(hint, Dock.Bottom); DockPanel.SetDock(_say, Dock.Bottom);
        root.Children.Add(head); root.Children.Add(hint); root.Children.Add(_say); root.Children.Add(grid);
        Content = root;

        var groups = Groups();
        _selected = groups[0].Ids.FirstOrDefault() ?? _all.FirstOrDefault()?.Record.Number;
        Search.TextChanged += (_, _) => Paint();
        CloseButton.Click += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        Opened += (_, _) => Search.Focus();
        Paint();
    }

    private List<TodayGroup> Groups() => Today.Groups(_hooks.Pins(), _hooks.Recents(), _all, _hooks.Find);

    // round 42 review: the alerts are read when Today opens and when a setting changes, not at each paint (a row click repaints)
    private IReadOnlyList<TimelineAlert>? _alerts;
    private bool _alertsAsked;

    private void ReadLooks()
    {
        _alerts = _hooks.Alerts?.Invoke(); _alertsAsked = _hooks.Alerts is not null;
        if (_hooks.Looks?.Invoke() is not { } looks) { _look = null; return; }      // round 37: switched off, no group and nothing kept
        var since = looks.Kept is null ? ((List<LastLook.Change>, int)?)null : LastLook.Since(looks.Kept, looks.Now, id => _hooks.Find(id)?.Record.Attention?.Text);
        _look = new LookShown(looks.Note ?? (since is null ? LastLook.SaidFirst : LastLook.SaidUndated), since, looks.Keep ? looks.Now : null);
    }

    /// <summary>A setting changed while Today is open (rounds 37 and 39): the look and next week are asked again and the list painted again at once.</summary>
    public void Refresh() { ReadLooks(); Paint(); }

    /// <summary>A click on the tray icon while Today is open: next week is asked again, and the list is painted again only if it changed.</summary>
    public void RefreshWeek() { if (_hooks.Week?.Invoke()?.Title != _week?.Title) Paint(); }

    private void Paint()
    {
        _week = _hooks.Week?.Invoke();            // asked again each time, so that a Today left open into Monday does not keep last week's
        string? typed = AndaValidator.Extract(Search.Text);
        if (typed is not null && _hooks.Find(typed) is not null) _selected = typed;
        _list.Children.Clear();
        // the sentences of one ANDA together, in the order the rule gives them
        static IEnumerable<(string Id, string Line, string? Mark)> ById(IEnumerable<(string Id, string Text)> rows) =>
            rows.GroupBy(r => r.Id).Select(g => (g.Key, string.Join(" ", g.Select(r => r.Text)), (string?)null));
        // Round 42: what needs a look, most urgent first, by the timeline checks: the first reason of each ANDA, and how many more
        if (_alertsAsked && _alerts is null)
            Section("urgent", Timeline.TodayTitle, new[] { Timeline.SaidUnreadable }, Array.Empty<(string, string, string?)>(), Array.Empty<string>());
        else if (_alerts is { } alerts)
        {
            var firsts = alerts.Where(a => a.Number is not null && a.Sev != "info").GroupBy(a => a.Number!).Select(g => (Id: g.Key, Line: g.First().Text + (g.Count() > 1 ? $" (+{g.Count() - 1} more)" : ""))).ToList();
            Section("urgent", Timeline.TodayTitle, firsts.Count == 0 ? new[] { Timeline.TodayNone } : Array.Empty<string>(),
                firsts.Take(Timeline.TodayMax).Select(f => (f.Id, f.Line, (string?)null)), firsts.Count > Timeline.TodayMax ? new[] { Timeline.TodayMore(firsts.Count - Timeline.TodayMax) } : Array.Empty<string>());
        }
        // Round 36: what moved since the last look comes first; then next week, from Thursday noon; then the groups
        if (_look is { } look)
            Section("since", "SINCE YOU LAST LOOKED", new[] { look.Note }.Concat(look.Since is { Rows.Count: 0 } ? new[] { LastLook.SaidNone } : Array.Empty<string>()),
                ById((look.Since?.Rows ?? new()).Select(r => (r.Id, r.Text))), look.Since is { More: > 0 } s ? new[] { LastLook.SaidMore(s.More) } : Array.Empty<string>());
        if (_week is { } week)
            Section("week", week.Title, week.Holidays.Concat(week.Empty is { } empty ? new[] { empty } : Array.Empty<string>()), ById(week.Rows.Select(r => (r.Id, r.Text))),
                (week.Tasks ?? Array.Empty<NextWeek.TaskRow>()).Select(t => t.Text).Concat(week.TasksMore > 0 ? new[] { NextWeek.SaidTasksMore(week.TasksMore) } : Array.Empty<string>()));   // round 38: to-do items, as text
        var groups = Groups().Where(g => g.Rows.Count > 0).ToList();
        foreach (var g in groups)
            Section(g.Title, g.Title, Array.Empty<string>(), g.Rows.Select(r => (r.Id, r.Line, (string?)r.Mark)), g.More > 0 ? new[] { Today.More(g.More) } : Array.Empty<string>());
        ShowAllButton.IsVisible = _hooks.ShowAll is not null && Groups()[0].Ids.Count > 0;
        if (groups.Count == 0) _list.Children.Add(new TextBlock { Text = Today.SaidNothing, FontSize = 12, Foreground = Res("C.Ink3"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 8, 0, 0) });
        PaintMain();
    }

    /// <summary>One section of the list: its title, its sentences, its rows, and what follows them.</summary>
    private void Section(string key, string title, IEnumerable<string> notes, IEnumerable<(string Id, string Line, string? Mark)> rows, IEnumerable<string> after)
    {
        TextBlock Note(string text) => new() { Text = text, FontSize = 11, Foreground = Res("C.Ink2"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 4) };
        _list.Children.Add(new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeight.Bold, LetterSpacing = 1.3, Foreground = Res("C.Ink3"), Margin = new Thickness(2, 8, 0, 3) });
        foreach (var n in notes) _list.Children.Add(Note(n));
        foreach (var (id, line, mark) in rows)
            _list.Children.Add(_hooks.Find(id) is { } found ? Row(new RowKey(key, id), line, mark ?? Today.Mark(found)) : Gone(id, line));
        foreach (var n in after) _list.Children.Add(Note(n));
    }

    /// <summary>An ANDA no longer in the data: shown as text, since there is nothing to open.</summary>
    private Control Gone(string id, string line)
    {
        var gone = new StackPanel { Margin = new Thickness(8, 4, 0, 4) };
        gone.Children.Add(new TextBlock { Text = id, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Res("C.Ink2") });
        gone.Children.Add(new TextBlock { Text = line, FontSize = 12, Foreground = Res("C.Ink3"), TextWrapping = TextWrapping.Wrap });
        AutomationProperties.SetName(gone, $"ANDA {id}. {line}");
        return gone;
    }

    /// <summary>A row that can be pressed: its mark as a dot and as a word, its line, and a border when it is the one shown.</summary>
    private Button Row(RowKey key, string line, string mark)
    {
        string id = key.Id; bool current = id == _selected;
        var dot = new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center, Background = Res(mark == "r" ? "C.Bad" : mark == "w" ? "C.Warn" : "C.Good") };
        var text = new StackPanel();
        var idLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        idLine.Children.Add(new TextBlock { Text = id, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Res("C.Ink") });
        idLine.Children.Add(new TextBlock { Text = Today.MarkWord(mark), FontSize = 11, Foreground = Res("C.Ink3"), VerticalAlignment = VerticalAlignment.Center });
        text.Children.Add(idLine);
        text.Children.Add(new TextBlock { Text = line, FontSize = 12, Foreground = Res("C.Ink3"), TextWrapping = TextWrapping.Wrap });
        var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("18,*") };
        rowGrid.Children.Add(dot); Grid.SetColumn(text, 1); rowGrid.Children.Add(text);
        var b = new Button { Content = rowGrid, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 5), Background = Brushes.Transparent,
            BorderThickness = new Thickness(current ? 2 : 1), BorderBrush = Res(current ? "C.Accent" : "C.Hair"), Tag = key };
        // the mark is a color; its meaning is also in the name a screen reader reads
        AutomationProperties.SetName(b, $"ANDA {id}, {Today.MarkWord(mark)}. {line}" + (current ? ". Shown on the right" : ""));
        b.Click += (_, _) => Choose(key);
        return b;
    }

    /// <summary>A row was pressed: show that ANDA, keep the keyboard on its row, and say so in one short sentence.</summary>
    private void Choose(RowKey key)
    {
        string id = key.Id;
        _selected = id; Paint();
        var rows = _list.Children.OfType<Button>().Where(b => b.Tag is RowKey k && k.Id == id).ToList();
        (rows.FirstOrDefault(b => Equals(b.Tag, key)) ?? rows.FirstOrDefault())?.Focus();
        if (_hooks.Find(id) is { } look) _say.Text = Today.Shown(id, Today.Main(look));
    }

    private void PaintMain()
    {
        _main.Children.Clear();
        var look = _selected is null ? null : _hooks.Find(_selected);
        if (look is null) { _main.Children.Add(new TextBlock { Text = Today.SaidChoose, FontSize = 12, Foreground = Res("C.Ink3"), TextWrapping = TextWrapping.Wrap }); return; }
        var v = Today.Main(look);
        _main.Children.Add(new TextBlock { Text = v.Meta, FontSize = 12, Foreground = Res("C.Ink2"), TextWrapping = TextWrapping.Wrap });
        var headline = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        headline.Children.Add(new TextBlock { Text = v.Figure, FontSize = v.Figure == "On" ? 20 : 34, FontWeight = FontWeight.Bold, Foreground = Res(v.Tone == "late" ? "C.Warn" : "C.Good"), VerticalAlignment = VerticalAlignment.Bottom });
        headline.Children.Add(new TextBlock { Text = v.Words, FontSize = 15, Foreground = Res("C.Ink"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4) });
        _main.Children.Add(headline);
        _main.Children.Add(new TextBlock { Text = v.Dates, FontSize = 12, Foreground = Res("C.Ink3"), TextWrapping = TextWrapping.Wrap });
        if (v.Attn is not null)
        {
            var attn = new StackPanel { Spacing = 2 };
            attn.Children.Add(new TextBlock { Text = "⚠ NEEDS YOU", FontSize = 11, FontWeight = FontWeight.Bold, LetterSpacing = 1.2, Foreground = Res("C.Warn") });
            attn.Children.Add(new TextBlock { Text = v.Attn, FontSize = 13, Foreground = Res("C.Ink"), TextWrapping = TextWrapping.Wrap });
            _main.Children.Add(new Border { BorderBrush = Res("C.Warn"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7), Child = attn });
        }
        if (v.Watch is not null) _main.Children.Add(new TextBlock { Text = v.Watch, FontSize = 12, Foreground = Res("C.Ink3"), TextWrapping = TextWrapping.Wrap });
        CopyButton = new Button { Content = "Copy status" }; OpenButton = new Button { Content = "Open the card" };
        var status = new TextBlock { FontSize = 12, Foreground = Res("C.Ink2"), TextWrapping = TextWrapping.Wrap, IsVisible = false };
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        string id = look.Record.Number, line = v.Status;
        CopyButton.Click += async (_, _) =>
        {
            bool ok = await Copying.TextAsync(this, line, _hooks.CopyText);
            status.Text = ok ? "Copied: the status line." : "Windows did not allow copying, so here is the status line: " + line; status.IsVisible = true;
        };
        OpenButton.Click += (_, _) => _hooks.OpenCard(id);
        var acts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        acts.Children.Add(CopyButton); acts.Children.Add(OpenButton);
        _main.Children.Add(acts); _main.Children.Add(status);
    }
}
