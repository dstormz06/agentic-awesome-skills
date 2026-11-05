using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AndaCompanion.Data;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

/// <summary>What the to-do list window needs from the running app. Everything is a callback, so the window can be tested on its own.</summary>
public sealed record MyTodoHooks(
    MyTodoService Store, Func<DateOnly> Today, Func<string> Shortcut,
    Action<string?> OpenNotes, Action<string, PixelPoint> OpenCard);

/// <summary>
/// "My to-do list": the person's own workload list, for any task, ANDA related or not, kept apart from the ANDA to-do list. Items are grouped (overdue, today,
/// the next seven days, anytime), repeating items come back by themselves, and the list redraws at once when anything changes and when the
/// day changes. It only runs while it is open. Nothing here is sent anywhere.
/// </summary>
public partial class MyTodoWindow : Window
{
    private readonly MyTodoHooks _hooks;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly Action _onChanged;
    private MyTodoView _view = new(Array.Empty<MyTodoGroup>(), 0, 0, 0, 0, 0, 0, null);
    private string _signature = "";
    private DateOnly _day;
    private string? _editingId;
    private ItemFields? _editor;
    private string _editorError = "";
    private TextBlock? _errorBox;
    private MyTodoItem? _deleted;

    private static readonly string[] Filters = { "Everything", "Work", "Personal", "Other" };

    public MyTodoWindow(MyTodoHooks hooks)
    {
        _hooks = hooks;
        InitializeComponent();
        QuietText.HideWhenEmpty(AddPreview, AddStatus, FocusLine);
        FilterBox.ItemsSource = Filters;
        FilterBox.SelectedIndex = 0;
        FilterBox.SelectionChanged += (_, _) => Reload(force: true);

        CloseBtn.Click += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { if (_editingId is not null) CancelEdit(); else Close(); } };
        AddBtn.Click += (_, _) => AddTyped();
        AddBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { AddTyped(); e.Handled = true; } };
        AddBox.TextChanged += (_, _) => ShowPreview();
        NotesBtn.Click += (_, _) => _hooks.OpenNotes(null);
        RollBtn.Click += (_, _) => { int n = _hooks.Store.RollOverOverdue(_hooks.Today()); AddStatus.Text = n == 0 ? "Nothing is overdue." : $"Moved {n} overdue {(n == 1 ? "item" : "items")} to today."; };
        ClearBtn.Click += (_, _) => { int n = _hooks.Store.ClearFinished(); AddStatus.Text = n == 0 ? "Nothing finished to clear. Finished items that repeat stay: they come back by themselves." : $"Cleared {n} finished {(n == 1 ? "item" : "items")}."; };
        UndoBtn.Click += (_, _) => Undo();

        _onChanged = () => Dispatcher.UIThread.Post(() => Reload());
        _hooks.Store.Changed += _onChanged;
        // A new day brings repeating items back and moves a missed repeating item on, even if the window was left open overnight.
        _timer.Tick += (_, _) => { if (_hooks.Today() != _day) Reload(force: true); };
        _timer.Start();
        Closed += (_, _) => { _timer.Stop(); _hooks.Store.Changed -= _onChanged; };
        Opened += (_, _) => { FitToScreen(); AddBox.Focus(); };
        Reload(force: true);
    }

    private void FitToScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;
        Body.MaxHeight = Math.Max(200, Math.Min(380, screen.WorkingArea.Height / screen.Scaling - 420));
    }

    public MyTodoView View => _view;
    public bool TimerRunning => _timer.IsEnabled;
    public string? EditingId => _editingId;

    private TodoCategory? Filter => FilterBox.SelectedIndex is >= 1 and <= 3 ? (TodoCategory)(FilterBox.SelectedIndex - 1) : null;

    private IBrush Res(string key) => (IBrush)Application.Current!.Resources[key]!;

    /// <summary>How an item reads in a sentence: its title, category, day, repeat and urgency. Used for the preview and the confirmations.</summary>
    public static string Describe(string title, TodoCategory category, DateOnly? due, bool high, Repeat repeat)
    {
        var parts = new List<string> { ItemFields.CategoryLabels[(int)category] };
        if (due is { } d) parts.Add("due " + ItemFields.Day(d));
        if (repeat != Repeat.None) parts.Add("repeats " + RepeatWords(repeat));
        if (high) parts.Add("urgent");
        return $"{title} ({string.Join(", ", parts)})";
    }

    public static string RepeatWords(Repeat r) => r switch { Repeat.Daily => "every day", Repeat.Weekdays => "every weekday", Repeat.Weekly => "every week", Repeat.Monthly => "every month", _ => "never" };

    // ---------- adding ----------

    private void ShowPreview()
    {
        var item = NoteParser.ParseOne(AddBox.Text, _hooks.Today());
        AddPreview.Text = item is null ? "" : "Will add: " + Describe(item.Title, item.Category, item.Due, item.High, item.Repeat);
    }

    private void AddTyped()
    {
        var today = _hooks.Today();
        var item = NoteParser.ParseOne(AddBox.Text, today);
        if (item is null) { AddStatus.Text = "Please type what needs doing."; AddBox.Focus(); return; }
        var result = _hooks.Store.AddSuggested(item, "You", today);
        if (result.Reason is { } reason) { AddStatus.Text = reason; return; }
        AddStatus.Text = result.Added ? "Added: " + Describe(item.Title, item.Category, item.Due, item.High, item.Repeat) : "That is already on your list.";
        if (result.Added) { AddBox.Text = ""; AddPreview.Text = ""; }
        AddBox.Focus();
    }

    private void Undo()
    {
        if (_deleted is { } item && _hooks.Store.Restore(item)) AddStatus.Text = "Put back: " + item.Title;
        else AddStatus.Text = "It could not be put back.";
        _deleted = null; UndoBtn.IsVisible = false;
    }

    // ---------- drawing ----------

    /// <summary>Rebuilds the list. The rows are redrawn only if something changed, so a refresh never steals the scroll position or the typing.</summary>
    public void Reload(bool force = false)
    {
        var today = _hooks.Today();
        _day = today;
        _hooks.Store.Refresh(today);
        _view = MyTodoBuilder.Build(_hooks.Store.Items, today, _hooks.Store.FinishedInLast(MyTodoBuilder.RecentDoneDays, today), Filter);
        int total = _hooks.Store.Items.Count;
        string signature = string.Join("\n", _view.Groups.SelectMany(g => g.Items.Select(i => $"{g.Heading}|{i.Id}|{i.Title}|{i.Due}|{i.Done}|{i.High}|{i.Category}|{i.Repeat}"))) + $"#{_view.Open}|{_view.Finished7}|{_editingId}|{Filter}|{today}";
        ShowSummary(total);
        RollBtn.IsVisible = _view.Overdue > 0;
        Footer.Text = "Saved on this PC only (mytodo.json); nothing is sent anywhere. Items that repeat come back by themselves, and a repeating item you miss moves to its next day instead of piling up.";
        if (!force && signature == _signature) return;
        _signature = signature;
        Draw(total);
    }

    private void ShowSummary(int total)
    {
        if (total == 0)
        {
            Summary.Text = $"Your to-do list is empty. Type something above, or select your notes anywhere and press {_hooks.Shortcut()} to turn them into items.";
            FocusLine.Text = "";
            SetBar(0, 0);
            return;
        }
        var bits = new List<string> { $"{_view.Open} open" };
        if (_view.Urgent > 0) bits.Add($"{_view.Urgent} urgent");
        if (_view.Overdue > 0) bits.Add($"{_view.Overdue} overdue");
        if (_view.TodayTotal > 0) bits.Add($"{_view.DoneToday} of {_view.TodayTotal} done today");
        bits.Add($"{_view.Finished7} finished in the last 7 days");
        Summary.Text = string.Join(" · ", bits);
        FocusLine.Text = _view.Focus is { } f ? "Start with: " + f
            : _view.Open == 0 ? "All items are complete." : "Nothing is due today and nothing is overdue. The remaining items are listed below.";
        SetBar(_view.DoneToday, _view.TodayTotal);
    }

    private void SetBar(int done, int total)
    {
        Bar.ColumnDefinitions = total <= 0 ? new ColumnDefinitions("0*,1*") : new ColumnDefinitions($"{done}*,{Math.Max(0, total - done)}*");
        AutomationProperties.SetName(Bar, total <= 0 ? "Nothing is due today" : $"{done} of {total} done today");
    }

    private void Draw(int total)
    {
        Rows.Children.Clear();
        if (_view.Groups.Count == 0)
        {
            Rows.Children.Add(new TextBlock
            {
                Text = total == 0 ? "No items yet. Add your first item above, for example “Send the status update to my supervisor by Friday”." : "Nothing in this view.",
                Foreground = Res("C.Ink2"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8),
            });
            return;
        }
        foreach (var group in _view.Groups)
        {
            Rows.Children.Add(new TextBlock
            {
                Text = $"{group.Heading} · {group.Items.Count}", FontSize = 9.5, FontWeight = FontWeight.Bold,
                Foreground = Res(group.Heading == "OVERDUE" ? "C.Bad" : "C.Ink3"), Margin = new Thickness(2, 10, 0, 2),
            });
            foreach (var item in group.Items) Rows.Children.Add(MakeRow(item));
        }
    }

    private string DuePhrase(MyTodoItem i)
    {
        if (MyTodoService.ParseIso(i.Due) is not { } d) return "";
        var today = _hooks.Today();
        if (i.Done) return "";
        return d < today ? "overdue since " + ItemFields.Day(d) : d == today ? "due today" : d == today.AddDays(1) ? "due tomorrow" : "due " + ItemFields.Day(d);
    }

    private Control MakeRow(MyTodoItem item)
    {
        var grid = new Grid { ColumnDefinitions = new("30,*,Auto"), Margin = new Thickness(2, 3) };
        var done = new CheckBox { IsChecked = item.Done, VerticalAlignment = VerticalAlignment.Top, MinWidth = 24 };
        AutomationProperties.SetName(done, (item.Done ? "Mark not finished: " : "Mark finished: ") + item.Title);
        done.IsCheckedChanged += (_, _) =>
        {
            bool on = done.IsChecked == true;
            if (on != item.Done) _hooks.Store.SetDone(item.Id, on, _hooks.Today());
        };
        grid.Children.Add(done);

        var text = new StackPanel { Spacing = 1 };
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (item.High && !item.Done) line.Children.Add(new TextBlock { Text = "URGENT", FontSize = 9.5, FontWeight = FontWeight.Bold, Foreground = Res("C.Bad"), VerticalAlignment = VerticalAlignment.Center });
        text.Children.Add(new TextBlock
        {
            Text = item.Title, TextWrapping = TextWrapping.Wrap, Foreground = item.Done ? Res("C.Ink3") : Res("C.Ink"),
            TextDecorations = item.Done ? TextDecorations.Strikethrough : null,
        });
        if (line.Children.Count > 0) text.Children.Insert(0, line);
        var meta = new List<string>();
        string due = DuePhrase(item);
        if (due.Length > 0) meta.Add(due);
        meta.Add(ItemFields.CategoryLabels[(int)item.Category]);
        if (item.Repeat != Repeat.None) meta.Add("repeats " + RepeatWords(item.Repeat));
        if (item.Source == "Notes") meta.Add("from your notes");
        bool late = !item.Done && MyTodoService.ParseIso(item.Due) is { } dd && dd < _hooks.Today();
        text.Children.Add(new TextBlock { Text = string.Join(" · ", meta), FontSize = 11, Foreground = Res(late ? "C.Bad" : "C.Ink3") });
        Grid.SetColumn(text, 1); grid.Children.Add(text);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Top };
        if (item.Number is { } number)
        {
            var open = new Button { Content = number, Padding = new Thickness(8, 2), FontSize = 11.5 };
            AutomationProperties.SetName(open, $"Open the card for ANDA {number}");
            open.Click += (_, _) => _hooks.OpenCard(number, new PixelPoint(Position.X + 80, Position.Y + 80));
            buttons.Children.Add(open);
        }
        if (!item.Done)
        {
            var tomorrow = new Button { Content = "Tomorrow", Padding = new Thickness(8, 2), FontSize = 11.5 };
            AutomationProperties.SetName(tomorrow, "Move to tomorrow: " + item.Title);
            tomorrow.Click += (_, _) => _hooks.Store.MoveDue(item.Id, _hooks.Today().AddDays(1));
            buttons.Children.Add(tomorrow);
            var edit = new Button { Content = "Edit", Padding = new Thickness(8, 2), FontSize = 11.5 };
            AutomationProperties.SetName(edit, "Edit: " + item.Title);
            edit.Click += (_, _) => BeginEdit(item);
            buttons.Children.Add(edit);
        }
        var del = new Button { Content = "Delete", Padding = new Thickness(8, 2), FontSize = 11.5 };
        AutomationProperties.SetName(del, "Delete: " + item.Title);
        del.Click += (_, _) =>
        {
            _deleted = item;
            if (_editingId == item.Id) { _editingId = null; _editor = null; }
            _hooks.Store.Delete(item.Id);
            UndoBtn.IsVisible = true;
            AddStatus.Text = "Deleted: " + item.Title;
        };
        buttons.Children.Add(del);
        Grid.SetColumn(buttons, 2); grid.Children.Add(buttons);

        var stack = new StackPanel { Children = { grid } };
        if (_editingId == item.Id && _editor is { } editor) stack.Children.Add(EditorBox(editor));
        return new Border { Child = stack, BorderBrush = Res("C.Hair"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 2) };
    }

    // ---------- editing ----------

    private void BeginEdit(MyTodoItem item)
    {
        _editingId = item.Id;
        _editorError = "";
        _editor = new ItemFields(_hooks.Today, item.Title);
        _editor.Load(item.Title, item.Category, MyTodoService.ParseIso(item.Due), item.High, item.Repeat);
        _editor.TitleBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { SaveEdit(); e.Handled = true; } };
        Reload(force: true);
        _editor.TitleBox.Focus();
    }

    private Control EditorBox(ItemFields editor)
    {
        var error = _errorBox = new TextBlock { Text = _editorError, FontSize = 11.5, Foreground = Res("C.Bad"), TextWrapping = TextWrapping.Wrap };
        QuietText.HideWhenEmpty(error);
        var save = new Button { Content = "Save", Classes = { "primary" } };
        var cancel = new Button { Content = "Cancel" };
        save.Click += (_, _) => SaveEdit();
        cancel.Click += (_, _) => CancelEdit();
        return new Border
        {
            Margin = new Thickness(30, 4, 0, 6), Padding = new Thickness(10), CornerRadius = new CornerRadius(8),
            Background = Res("C.Mark"), BorderBrush = Res("C.Hair"), BorderThickness = new Thickness(1),
            Child = new StackPanel { Spacing = 6, Children = { editor.Panel, error, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, cancel } } } },
        };
    }

    /// <summary>Saves the open editor. A date that cannot be read, or an empty title, is explained under the fields and nothing is changed.</summary>
    public void SaveEdit()
    {
        if (_editingId is not { } id || _editor is not { } editor) return;
        var read = editor.Read();
        if (!read.Ok) { _editorError = read.Error!; if (_errorBox is not null) _errorBox.Text = _editorError; return; }
        string? refused = _hooks.Store.Edit(id, read.Title, read.Category, read.Due, read.High, read.Repeat, read.RepeatDay, _hooks.Today());
        if (refused is not null) { _editorError = refused; if (_errorBox is not null) _errorBox.Text = refused; return; }
        _editingId = null; _editor = null; _editorError = "";
        AddStatus.Text = "Saved.";
        Reload(force: true);
    }

    private void CancelEdit()
    {
        _editingId = null; _editor = null; _editorError = "";
        Reload(force: true);
    }
}
