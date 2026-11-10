using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using AndaCompanion.Data;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

/// <summary>What the to-do window needs from the running app. Everything is a callback, so the window can be tested on its own.</summary>
public sealed record TodoHooks(
    Func<TodoView> Build, TodoService Store, Func<int> RefreshSeconds, Func<DateOnly> Today, Func<BusinessCalendar> Calendar,
    Action<string, PixelPoint> OpenCard, Func<string> SourceNote);

/// <summary>
/// The person's own to-do list plus the items that come from their data, worst first. It refreshes itself only while it is open:
/// every few seconds the person chose, and at once when the list changes. When it is closed nothing runs. Nothing here is sent anywhere.
/// </summary>
public partial class TodoWindow : Window
{
    private readonly TodoHooks _hooks;
    private readonly DispatcherTimer _timer = new();
    private TodoView _view = new(Array.Empty<TodoRow>(), 0, 0, 0, 0, 0, DateTime.MinValue);
    private string _signature = "";
    private readonly Action _onStoreChanged;

    private static readonly (string Label, int Days)[] DueChoices =
        { ("No date", -1), ("Today", 0), ("Tomorrow", 1), ("In 3 days", 3), ("In a week", 7) };

    public TodoWindow(TodoHooks hooks)
    {
        _hooks = hooks;
        InitializeComponent();
        DueBox.ItemsSource = DueChoices.Select(c => c.Label).ToList();
        DueBox.SelectedIndex = 0;

        CloseBtn.Click += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        RefreshBtn.Click += (_, _) => Reload(force: true);
        ClearBtn.Click += (_, _) => { int n = _hooks.Store.ClearCompleted(); AddStatus.Text = n == 0 ? "Nothing finished to clear." : $"Cleared {n} finished {(n == 1 ? "item" : "items")}."; };
        AddBtn.Click += (_, _) => AddTyped();
        AddBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { AddTyped(); e.Handled = true; } };

        // Any change to the list (here, from a shortcut, from a card) redraws it at once.
        _onStoreChanged = () => Dispatcher.UIThread.Post(() => Reload());
        _hooks.Store.Changed += _onStoreChanged;
        Closed += (_, _) => { _timer.Stop(); _hooks.Store.Changed -= _onStoreChanged; };

        _timer.Tick += (_, _) => Reload();
        ApplyTimer();
        Opened += (_, _) => AddBox.Focus();
        Reload(force: true);
    }

    /// <summary>Starts, changes or stops the timer to match Settings. Called when the window opens and whenever Settings change.</summary>
    public void ApplyTimer()
    {
        int seconds = _hooks.RefreshSeconds();
        _timer.Stop();
        if (seconds > 0) { _timer.Interval = TimeSpan.FromSeconds(seconds); _timer.Start(); }
        LiveLine.Text = seconds > 0
            ? $"Refreshes itself every {(seconds >= 60 ? $"{seconds / 60} min" : $"{seconds} s")} while this window is open, and at once when the list changes. A changed data file is picked up at the next refresh. Nothing runs when it is closed."
            : "Automatic refresh is off. Press Refresh now to update. Nothing runs when this window is closed.";
    }

    public TodoView View => _view;
    public bool TimerRunning => _timer.IsEnabled;

    private void AddTyped()
    {
        string text = AddBox.Text ?? "";
        int days = DueChoices[Math.Max(0, DueBox.SelectedIndex)].Days;
        var today = _hooks.Today();
        DateOnly? due = days < 0 ? null : days <= 1 ? AddCalendar(today, days) : today.AddDays(days);
        string? number = AndaValidator.Extract(text);
        var result = _hooks.Store.Add(text, number, null, due, "You", today);
        if (result.Reason is { } reason) { AddStatus.Text = reason; return; }
        AddStatus.Text = result.Added ? "Added." : "That is already on your list.";
        if (result.Added) { AddBox.Text = ""; DueBox.SelectedIndex = 0; }
        AddBox.Focus();
    }

    private DateOnly AddCalendar(DateOnly today, int days) => days == 0 ? today : _hooks.Calendar().NextBusinessDay(today);

    /// <summary>Rebuilds the list. The rows are redrawn only if something changed, so a refresh never steals the scroll position or the typing.</summary>
    public void Reload(bool force = false)
    {
        _view = _hooks.Build();
        string signature = string.Join("\n", _view.Rows.Select(r => $"{r.Key}|{r.Done}|{r.Priority}|{r.Title}|{r.SnoozedUntil}")) + $"#{_view.Open}";
        Summary.Text = _view.Rows.Count == 0
            ? "Nothing on your list, and nothing in your data needs a look."
            : $"{_view.Open} open · {_view.Now} {(_view.Now == 1 ? "needs" : "need")} a look now" + (_view.Snoozed > 0 ? $" · {_view.Snoozed} snoozed" : "") + (_view.Auto > 0 ? $" · {_view.Auto} from your data" : "");
        Footer.Text = $"Updated {_view.BuiltAt:h:mm:ss tt}. {_hooks.SourceNote()} Items from your data follow the same rules as the card and the watch list; they say what the data shows and decide nothing. Click a number to open its card.";
        if (!force && signature == _signature) return;
        _signature = signature;
        Draw();
    }

    private IBrush Res(string key) => (IBrush)Application.Current!.Resources[key]!;

    private static readonly (int Priority, string Heading)[] Groups =
    {
        (0, "NEEDS A LOOK NOW"), (1, "SOON"), (2, "LATER"), (3, "DONE IN THE LAST 7 DAYS"), (4, "SNOOZED"),
    };

    private void Draw()
    {
        Rows.Children.Clear();
        if (_view.Rows.Count == 0)
        {
            Rows.Children.Add(new TextBlock { Text = "Nothing to do. Add an item above, or give one of your actions a shortcut in Settings.", Foreground = Res("C.Ink2"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8) });
            return;
        }
        foreach (var (priority, heading) in Groups)
        {
            var group = _view.Rows.Where(r => r.Priority == priority).ToList();
            if (group.Count == 0) continue;
            Rows.Children.Add(new TextBlock { Text = $"{heading} · {group.Count}", FontSize = 9.5, FontWeight = FontWeight.Bold, Foreground = Res("C.Ink3"), Margin = new Thickness(2, 10, 0, 2) });
            foreach (var row in group) Rows.Children.Add(MakeRow(row));
        }
    }

    private Control MakeRow(TodoRow row)
    {
        string tone = row.Severity switch { "bad" => "C.Bad", "warn" => "C.Warn", _ => "C.Ink2" };
        var grid = new Grid { ColumnDefinitions = new("30,52,*,Auto"), Margin = new Thickness(2, 3) };
        var done = new CheckBox { IsChecked = row.Done, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, MinWidth = 24 };
        AutomationProperties.SetName(done, $"Mark finished: {row.Title}");
        done.IsCheckedChanged += (_, _) =>
        {
            bool on = done.IsChecked == true;
            if (on == row.Done) return;
            var today = _hooks.Today();
            if (row.Auto) { if (on) _hooks.Store.MarkAutoDone(row.Key, today); else _hooks.Store.UnmarkAutoDone(row.Key); }
            else if (row.ItemId is { } id) _hooks.Store.SetDone(id, on, today);
        };
        grid.Children.Add(done);
        var tag = new TextBlock
        {
            Text = row.Auto ? (row.Severity == "bad" ? "ACT" : row.Severity == "warn" ? "CHECK" : "NOTE") : "YOURS", FontSize = 9.5, FontWeight = FontWeight.Bold,
            Foreground = Res(row.Auto ? tone : "C.Accent"), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 0),
        };
        Grid.SetColumn(tag, 1); grid.Children.Add(tag);

        var text = new StackPanel { Spacing = 1 };
        var title = new TextBlock { Text = row.Title, TextWrapping = TextWrapping.Wrap, Foreground = row.Done ? Res("C.Ink3") : Res("C.Ink"), TextDecorations = row.Done ? TextDecorations.Strikethrough : null };
        text.Children.Add(title);
        var meta = new List<string>();
        if (row.Due is { } due) meta.Add((row.Done || row.Priority >= 3 ? "due " : due < _hooks.Today() ? "overdue since " : "due ") + due.ToString("ddd MMM d"));
        if (row.SnoozedUntil is { } sn) meta.Add("snoozed until " + sn.ToString("ddd MMM d"));
        if (!row.Auto) meta.Add("added by " + row.Source);
        if (meta.Count > 0) text.Children.Add(new TextBlock { Text = string.Join(" · ", meta), FontSize = 11, Foreground = Res(row.Severity == "bad" && !row.Done ? "C.Bad" : "C.Ink3") });
        if (!string.IsNullOrWhiteSpace(row.Detail) && !row.Done) text.Children.Add(new TextBlock { Text = row.Detail, FontSize = 11, Foreground = Res("C.Ink3"), TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(text, 2); grid.Children.Add(text);

        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        if (row.Number is { } number)
        {
            var open = new Button { Content = number, Padding = new Thickness(8, 2), FontSize = 11.5 };
            AutomationProperties.SetName(open, $"Open the card for ANDA {number}");
            open.Click += (_, _) => _hooks.OpenCard(number, new PixelPoint(Position.X + 80, Position.Y + 80));
            buttons.Children.Add(open);
        }
        if (!row.Done)
        {
            bool asleep = row.SnoozedUntil is not null;
            var snooze = new Button { Content = asleep ? "Wake" : "Snooze", Padding = new Thickness(8, 2), FontSize = 11.5 };
            AutomationProperties.SetName(snooze, (asleep ? "Wake: " : "Snooze until the next business day: ") + row.Title);
            snooze.Click += (_, _) =>
            {
                if (asleep) { if (row.Auto) _hooks.Store.WakeAuto(row.Key); else if (row.ItemId is { } id) _hooks.Store.Wake(id); }
                else
                {
                    var until = _hooks.Calendar().NextBusinessDay(_hooks.Today());
                    if (row.Auto) _hooks.Store.SnoozeAuto(row.Key, until); else if (row.ItemId is { } id) _hooks.Store.Snooze(id, until);
                }
            };
            buttons.Children.Add(snooze);
        }
        if (!row.Auto && row.ItemId is { } itemId)
        {
            var del = new Button { Content = "Delete", Padding = new Thickness(8, 2), FontSize = 11.5 };
            AutomationProperties.SetName(del, "Delete: " + row.Title);
            del.Click += (_, _) => _hooks.Store.Delete(itemId);
            buttons.Children.Add(del);
        }
        Grid.SetColumn(buttons, 3); grid.Children.Add(buttons);
        return new Border { Child = grid, BorderBrush = Res("C.Hair"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 2) };
    }
}
