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
    Action<string> OpenCard, Func<string, Task<bool>>? CopyText = null, Action? ShowAll = null);

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

    private void Paint()
    {
        string? typed = AndaValidator.Extract(Search.Text);
        if (typed is not null && _hooks.Find(typed) is not null) _selected = typed;
        _list.Children.Clear();
        bool any = false;
        foreach (var g in Groups())
        {
            if (g.Rows.Count == 0) continue;
            any = true;
            _list.Children.Add(new TextBlock { Text = g.Title, FontSize = 9.5, FontWeight = FontWeight.Bold, LetterSpacing = 1.3, Foreground = Res("C.Ink3"), Margin = new Thickness(2, 8, 0, 3) });
            foreach (var row in g.Rows)
            {
                bool current = row.Id == _selected;
                var dot = new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center, Background = Res(row.Mark == "r" ? "C.Bad" : row.Mark == "w" ? "C.Warn" : "C.Good") };
                var text = new StackPanel();
                var idLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                idLine.Children.Add(new TextBlock { Text = row.Id, FontSize = 12.5, FontWeight = FontWeight.SemiBold, Foreground = Res("C.Ink") });
                idLine.Children.Add(new TextBlock { Text = Today.MarkWord(row.Mark), FontSize = 11, Foreground = Res("C.Ink3"), VerticalAlignment = VerticalAlignment.Center });
                text.Children.Add(idLine);
                text.Children.Add(new TextBlock { Text = row.Line, FontSize = 11.5, Foreground = Res("C.Ink3"), TextWrapping = TextWrapping.Wrap });
                var line = new Grid { ColumnDefinitions = new ColumnDefinitions("18,*") };
                line.Children.Add(dot); Grid.SetColumn(text, 1); line.Children.Add(text);
                var b = new Button { Content = line, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 5), Background = Brushes.Transparent,
                    BorderThickness = new Thickness(current ? 2 : 1), BorderBrush = Res(current ? "C.Accent" : "C.Hair"), Tag = row.Id };
                // the mark is a color; its meaning is also in the name a screen reader reads
                string mark = Today.MarkWord(row.Mark);
                AutomationProperties.SetName(b, $"ANDA {row.Id}, {mark}. {row.Line}" + (current ? ". Shown on the right" : ""));
                string id = row.Id;
                b.Click += (_, _) => Choose(id);
                _list.Children.Add(b);
            }
            if (g.More > 0) _list.Children.Add(new TextBlock { Text = Today.More(g.More), FontSize = 11, Foreground = Res("C.Ink3"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 2, 0, 0) });
        }
        ShowAllButton.IsVisible = _hooks.ShowAll is not null && Groups()[0].Ids.Count > 0;
        if (!any) _list.Children.Add(new TextBlock { Text = Today.SaidNothing, FontSize = 12, Foreground = Res("C.Ink3"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 8, 0, 0) });
        PaintMain();
    }

    /// <summary>A row was pressed: show that ANDA, keep the keyboard on its row, and say so in one short sentence.</summary>
    private void Choose(string id)
    {
        _selected = id; Paint();
        _list.Children.OfType<Button>().FirstOrDefault(b => (string?)b.Tag == id && b.BorderThickness.Left > 1)?.Focus();
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
        headline.Children.Add(new TextBlock { Text = v.Figure, FontSize = v.Figure == "On" ? 22 : 30, FontWeight = FontWeight.Bold, Foreground = Res(v.Tone == "late" ? "C.Warn" : "C.Good"), VerticalAlignment = VerticalAlignment.Bottom });
        headline.Children.Add(new TextBlock { Text = v.Words, FontSize = 14, Foreground = Res("C.Ink"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4) });
        _main.Children.Add(headline);
        _main.Children.Add(new TextBlock { Text = v.Dates, FontSize = 11.5, Foreground = Res("C.Ink3"), TextWrapping = TextWrapping.Wrap });
        if (v.Attn is not null)
        {
            var attn = new StackPanel { Spacing = 2 };
            attn.Children.Add(new TextBlock { Text = "⚠ NEEDS YOU", FontSize = 9.5, FontWeight = FontWeight.Bold, LetterSpacing = 1.2, Foreground = Res("C.Warn") });
            attn.Children.Add(new TextBlock { Text = v.Attn, FontSize = 12.5, Foreground = Res("C.Ink"), TextWrapping = TextWrapping.Wrap });
            _main.Children.Add(new Border { BorderBrush = Res("C.Warn"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7), Child = attn });
        }
        if (v.Watch is not null) _main.Children.Add(new TextBlock { Text = v.Watch, FontSize = 11.5, Foreground = Res("C.Ink3"), TextWrapping = TextWrapping.Wrap });
        CopyButton = new Button { Content = "Copy status" }; OpenButton = new Button { Content = "Open the card" };
        var status = new TextBlock { FontSize = 11.5, Foreground = Res("C.Ink2"), TextWrapping = TextWrapping.Wrap, IsVisible = false };
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        string id = look.Record.Number, line = v.Status;
        CopyButton.Click += async (_, _) =>
        {
            bool ok;
            try { if (_hooks.CopyText is not null) ok = await _hooks.CopyText(line); else if (Clipboard is null) ok = false; else { await Clipboard.SetTextAsync(line); ok = true; } }
            catch (Exception) { ok = false; }
            status.Text = ok ? "Copied: the status line." : "Windows did not allow copying, so here is the status line: " + line; status.IsVisible = true;
        };
        OpenButton.Click += (_, _) => _hooks.OpenCard(id);
        var acts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        acts.Children.Add(CopyButton); acts.Children.Add(OpenButton);
        _main.Children.Add(acts); _main.Children.Add(status);
    }
}
