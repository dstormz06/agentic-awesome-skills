using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

public sealed record NotesHooks(MyTodoService Store, Func<DateOnly> Today, Func<string> Shortcut, Action OpenMyTodo);

/// <summary>
/// "Notes to my to-do list": the person's highlighted or pasted notes are read by simple rules on this PC (no AI, nothing sent anywhere) and turned
/// into suggested to-do items. Each suggestion shows what was understood, can be changed or unticked, and nothing is added until the person
/// presses Add (unless they chose, in Settings, to add at once; then Undo takes the whole batch back). This is its own window, apart from
/// the ANDA lookup and the draft check.
/// </summary>
public partial class NotesWindow : Window
{
    private sealed class Row
    {
        public required CheckBox Include { get; init; }
        public required ItemFields Fields { get; init; }
        public required TextBlock Problem { get; init; }
        public required TextBlock AddedMark { get; init; }
        public required SuggestedItem Source { get; init; }
        public bool Added { get; set; }
    }

    private readonly NotesHooks _hooks;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly List<Row> _rows = new();
    private List<string> _lastAdded = new();

    public NotesWindow(NotesHooks hooks, string initialText, bool addAtOnce)
    {
        _hooks = hooks;
        InitializeComponent();

        string text = initialText ?? "";
        bool trimmed = text.Length > NoteParser.MaxInputChars;
        if (trimmed) text = NoteParser.FitToLimit(text);
        NotesBox.Text = text;
        Hint.Text = $"Select your notes anywhere and press {hooks.Shortcut()}, or paste them above. They are read on this PC by simple rules: no AI, nothing is sent anywhere. " +
                    "You can change any suggestion before it is added. Changing the notes above refreshes the suggestions." +
                    (trimmed ? $" Only the first {NoteParser.MaxInputChars:N0} characters were read." : "");

        QuietText.HideWhenEmpty(Status, FoundNote);
        CloseBtn.Click += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        AddBtn.Click += (_, _) => AddTicked();
        AllBtn.Click += (_, _) => { foreach (var r in _rows.Where(r => !r.Added)) r.Include.IsChecked = true; };
        NoneBtn.Click += (_, _) => { foreach (var r in _rows.Where(r => !r.Added)) r.Include.IsChecked = false; };
        UndoBtn.Click += (_, _) => Undo();
        OpenBtn.Click += (_, _) => _hooks.OpenMyTodo();

        _debounce.Tick += (_, _) => { _debounce.Stop(); Analyse(); };
        NotesBox.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        Closed += (_, _) => _debounce.Stop();
        Opened += (_, _) => { FitToScreen(); if (string.IsNullOrWhiteSpace(NotesBox.Text)) NotesBox.Focus(); };

        Analyse();
        if (addAtOnce && !string.IsNullOrWhiteSpace(text)) AddAtOnce();
    }

    private void FitToScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;
        Body.MaxHeight = Math.Max(240, screen.WorkingArea.Height / screen.Scaling - 52 - 40 - 34 - 14 - 20);
    }

    private IBrush Res(string key) => (IBrush)Application.Current!.Resources[key]!;

    public int SuggestionCount => _rows.Count;
    public int TickedCount => _rows.Count(r => !r.Added && r.Include.IsChecked == true);

    /// <summary>Reads the notes and redraws the suggestions. Public so a test (or a paste) can ask for it at once.</summary>
    public void Analyse()
    {
        _debounce.Stop();
        string text = NotesBox.Text ?? "";
        var today = _hooks.Today();
        var items = NoteParser.Parse(text, today);
        _rows.Clear();
        Found.Children.Clear();
        UndoBtn.IsVisible = false;
        foreach (var item in items) { var row = MakeRow(item, today); _rows.Add(row); Found.Children.Add(RowVisual(row)); }

        FoundLabel.Text = _rows.Count == 0 ? "SUGGESTED ITEMS" : $"SUGGESTED ITEMS · {_rows.Count}";
        FoundNote.Text = string.IsNullOrWhiteSpace(text) ? "Suggestions appear here as soon as there are notes."
            : _rows.Count == 0 ? "I did not find anything that looks like a task. You can type one by hand in My to-do list."
            : _rows.Count >= NoteParser.MaxItems ? $"Showing the first {NoteParser.MaxItems}. Split very long notes to see the rest."
            : "Tick what you want on your list. Change a title, day, repeat or category if the reading is not right.";
        UpdateAddButton();
    }

    private Row MakeRow(SuggestedItem item, DateOnly today)
    {
        var fields = new ItemFields(() => _hooks.Today(), item.Title);
        fields.Load(item.Title, item.Category, item.Due, item.High, item.Repeat);
        var include = new CheckBox { IsChecked = item.Actionable, VerticalAlignment = VerticalAlignment.Top, MinWidth = 24 };
        AutomationProperties.SetName(include, "Add to my to-do list: " + item.Title);
        include.IsCheckedChanged += (_, _) => UpdateAddButton();
        return new Row
        {
            Include = include, Fields = fields, Source = item,
            Problem = new TextBlock { FontSize = 11.5, Foreground = Res("C.Bad"), TextWrapping = TextWrapping.Wrap, IsVisible = false },
            AddedMark = new TextBlock { Text = "Added to your to-do list", FontSize = 11.5, FontWeight = FontWeight.SemiBold, Foreground = Res("C.Good"), IsVisible = false },
        };
    }

    private Control RowVisual(Row row)
    {
        var inner = new StackPanel { Spacing = 3, Children = { row.Fields.Panel } };
        if (!row.Source.Actionable)
            inner.Children.Add(new TextBlock { Text = "This reads like a note, not a task, so it is not ticked. Tick it if you want it on the list.", FontSize = 11, Foreground = Res("C.Ink3"), TextWrapping = TextWrapping.Wrap });
        inner.Children.Add(row.Problem);
        inner.Children.Add(row.AddedMark);
        var grid = new Grid { ColumnDefinitions = new("30,*") };
        grid.Children.Add(row.Include);
        Grid.SetColumn(inner, 1); grid.Children.Add(inner);
        return new Border { Child = grid, Padding = new Thickness(8), CornerRadius = new CornerRadius(8), Background = Res("C.Mark"), BorderBrush = Res("C.Hair"), BorderThickness = new Thickness(1) };
    }

    private void UpdateAddButton()
    {
        int n = TickedCount;
        AddBtn.Content = n == 0 ? "Add the ticked items" : $"Add the {n} ticked {(n == 1 ? "item" : "items")}";
        AddBtn.IsEnabled = n > 0;
        AllBtn.IsEnabled = NoneBtn.IsEnabled = _rows.Any(r => !r.Added);
    }

    // ---------- adding ----------

    /// <summary>Adds the ticked items. If any ticked item has a problem (an empty title, a date that cannot be read) nothing is added and the problem is shown beside it.</summary>
    public void AddTicked() => AddRows(_rows.Where(r => !r.Added && r.Include.IsChecked == true).ToList(), atOnce: false);

    private void AddAtOnce()
    {
        foreach (var r in _rows) r.Include.IsChecked = r.Source.Actionable;
        var chosen = _rows.Where(r => r.Include.IsChecked == true).ToList();
        if (chosen.Count == 0) { Status.Text = _rows.Count == 0 ? "I did not find anything to add." : "Nothing looked like a task, so nothing was added. You can tick items yourself."; return; }
        AddRows(chosen, atOnce: true);
    }

    private void AddRows(List<Row> chosen, bool atOnce)
    {
        if (chosen.Count == 0) { Status.Text = "Please tick at least one item."; return; }
        var reads = new List<(Row Row, ItemRead Read)>();
        int problems = 0;
        foreach (var row in chosen)
        {
            var read = row.Fields.Read();
            row.Problem.IsVisible = !read.Ok; row.Problem.Text = read.Error ?? "";
            if (!read.Ok) problems++;
            reads.Add((row, read));
        }
        if (problems > 0) { Status.Text = $"Nothing was added yet. Please fix {problems} {(problems == 1 ? "item" : "items")} marked in red, or untick {(problems == 1 ? "it" : "them")}."; return; }

        var today = _hooks.Today();
        var ids = new List<string>();
        int already = 0;
        string? full = null;
        foreach (var (row, read) in reads)
        {
            var result = _hooks.Store.Add(read.Title, read.Category, read.Due, read.High, read.Repeat, read.RepeatDay, row.Source.Number, "Notes", today);
            if (result.Reason is { } reason) { full = reason; break; }
            if (result.Added && result.Item is { } added) ids.Add(added.Id); else already++;
            row.Added = true; row.Include.IsChecked = false; row.Include.IsEnabled = false; row.AddedMark.IsVisible = true;
            row.AddedMark.Text = result.Added ? "Added to your to-do list" : "Already on your to-do list";
            row.Fields.TitleBox.IsEnabled = row.Fields.CategoryBox.IsEnabled = row.Fields.WhenBox.IsEnabled = row.Fields.RepeatBox.IsEnabled = row.Fields.HighBox.IsEnabled = false;
        }
        _lastAdded = ids;
        UndoBtn.IsVisible = ids.Count > 0;
        string made = ids.Count == 0 ? "Nothing new was added" : $"Added {ids.Count} {(ids.Count == 1 ? "item" : "items")} to your to-do list";
        Status.Text = made + (already > 0 ? $" ({already} already on the list)" : "") + (full is not null ? ". " + full : ".") +
                      (atOnce && ids.Count > 0 ? " This was added at once, as you chose in Settings. Press Undo to take it back." : "");
        UpdateAddButton();
    }

    private void Undo()
    {
        int removed = _lastAdded.Count(id => _hooks.Store.Delete(id));
        _lastAdded = new();
        UndoBtn.IsVisible = false;
        // Show the suggestions again, ready to change or add.
        string keep = $"Removed {removed} {(removed == 1 ? "item" : "items")} from your to-do list.";
        Analyse();
        Status.Text = keep;
    }
}
