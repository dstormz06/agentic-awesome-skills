using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

/// <summary>What the person entered for one to-do item, or why it cannot be used yet.</summary>
public sealed record ItemRead(string? Error, string Title, TodoCategory Category, DateOnly? Due, bool High, Repeat Repeat, int RepeatDay)
{
    public bool Ok => Error is null;
}

/// <summary>
/// The fields of one to-do item, used in two places so they look and behave the same: the editor under a row in "My to-do list", and
/// each suggested item in "Notes to my to-do list". The date is typed in words ("tomorrow", "Friday", "Nov 12", "in 3 days") and the line under
/// it says which day was understood, so nothing is guessed silently. A date it cannot read is refused, never dropped.
/// </summary>
public sealed class ItemFields
{
    private readonly Func<DateOnly> _today;
    public TextBox TitleBox { get; } = new() { MaxLength = MyTodoService.MaxTitleChars, TextWrapping = TextWrapping.Wrap };
    public ComboBox CategoryBox { get; } = new() { Width = 104 };
    public TextBox WhenBox { get; } = new() { Width = 138, MaxLength = 40, Watermark = "When? (Fri, Nov 12)" };
    public ComboBox RepeatBox { get; } = new() { Width = 154 };
    public CheckBox HighBox { get; } = new() { Content = "Urgent" };
    public TextBlock WhenNote { get; } = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap };

    public static readonly string[] CategoryLabels = { "Work", "Personal", "Other" };
    public static readonly string[] RepeatLabels = { "Does not repeat", "Every day", "Every weekday", "Every week", "Every month" };

    public ItemFields(Func<DateOnly> today, string nameSuffix = "")
    {
        _today = today;
        CategoryBox.ItemsSource = CategoryLabels; CategoryBox.SelectedIndex = 2;
        RepeatBox.ItemsSource = RepeatLabels; RepeatBox.SelectedIndex = 0;
        string suffix = nameSuffix.Length > 0 ? " for " + nameSuffix : "";
        AutomationProperties.SetName(TitleBox, "What needs doing" + suffix);
        AutomationProperties.SetName(CategoryBox, "Category" + suffix);
        AutomationProperties.SetName(WhenBox, "When it is due. Type a day such as tomorrow, Friday or Nov 12" + suffix);
        AutomationProperties.SetName(RepeatBox, "How often it repeats" + suffix);
        AutomationProperties.SetName(HighBox, "Urgent" + suffix);
        WhenBox.TextChanged += (_, _) => ShowWhen();
        RepeatBox.SelectionChanged += (_, _) => ShowWhen();
        ShowWhen();
    }

    private StackPanel? _panel;

    /// <summary>The fields laid out in two lines. The same panel is handed out each time, taken from its old place first, so a window can redraw without losing what was typed.</summary>
    public Control Panel
    {
        get
        {
            _panel ??= BuildPanel();
            switch (_panel.Parent)
            {
                case Panel p: p.Children.Remove(_panel); break;
                case Decorator d: d.Child = null; break;
                case ContentControl c: c.Content = null; break;
            }
            return _panel;
        }
    }

    private StackPanel BuildPanel()
    {
        var options = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (Control c in new Control[] { CategoryBox, WhenBox, RepeatBox, HighBox }) { c.Margin = new Avalonia.Thickness(0, 0, 8, 4); c.VerticalAlignment = VerticalAlignment.Center; options.Children.Add(c); }
        return new StackPanel { Spacing = 4, Children = { TitleBox, options, WhenNote } };
    }

    /// <summary>A day as the person reads it, the same in every language setting, so what is shown can always be read back.</summary>
    public static string Day(DateOnly d) => d.ToString("ddd MMM d", CultureInfo.InvariantCulture);

    private DateOnly? _loadedDue;
    private string _loadedText = "";

    public void Load(string title, TodoCategory category, DateOnly? due, bool high, Repeat repeat)
    {
        TitleBox.Text = title;
        CategoryBox.SelectedIndex = Math.Clamp((int)category, 0, 2);
        // A year is shown when it is not this one, and a day left as it was is kept exactly: "Sep 26" typed in November would otherwise mean next year.
        _loadedDue = due;
        _loadedText = due is { } d ? (d.Year == _today().Year ? Day(d) : Day(d) + ", " + d.Year) : "";
        WhenBox.Text = _loadedText;
        HighBox.IsChecked = high;
        RepeatBox.SelectedIndex = Math.Clamp((int)repeat, 0, 4);
        ShowWhen();
    }

    /// <summary>Reads a typed day. Empty or "none" means no date; null means the words were not understood.</summary>
    public static (bool Understood, DateOnly? Date) ReadWhen(string? text, DateOnly today)
    {
        string t = (text ?? "").Trim();
        if (t.Length == 0 || t.Equals("none", StringComparison.OrdinalIgnoreCase) || t.Equals("no date", StringComparison.OrdinalIgnoreCase) || t == "-") return (true, null);
        // "Fri" on its own is not a date to the note reader, which wants a hint such as "by Fri"; the same words with a hint are read the same way.
        var due = NoteParser.ReadDue(t, today) ?? NoteParser.ReadDue("by " + t, today);
        return due is null ? (false, null) : (true, due);
    }

    /// <summary>What the When box says now: the day it was loaded with if the text is unchanged, otherwise the typed words read afresh.</summary>
    private (bool Understood, DateOnly? Date) ReadBox(DateOnly today) =>
        _loadedDue is not null && (WhenBox.Text ?? "").Trim() == _loadedText ? (true, _loadedDue) : ReadWhen(WhenBox.Text, today);

    private Repeat CurrentRepeat => (Repeat)Math.Clamp(RepeatBox.SelectedIndex, 0, 4);

    private void ShowWhen()
    {
        var today = _today();
        var (ok, date) = ReadBox(today);
        var repeat = CurrentRepeat;
        if (!ok)
        {
            WhenNote.Text = "I could not read that date. Try: today, tomorrow, Friday, Nov 12, or in 3 days.";
            WhenNote.Foreground = Brush("C.Bad");
            return;
        }
        string text = date is { } d ? $"Due {Day(d)}" : "No due date";
        if (repeat != Repeat.None)
        {
            var (first, _) = Resolve(repeat, date, today);
            text = $"{RepeatLabels[(int)repeat]}, starting {Day(first)}";
        }
        WhenNote.Text = text;
        WhenNote.Foreground = Brush("C.Ink3");
    }

    private static IBrush Brush(string key) => (IBrush)Avalonia.Application.Current!.Resources[key]!;

    /// <summary>The first date a repeating item can fall on, on or after the typed day (or today), and the day of the week or month it repeats on.</summary>
    private static (DateOnly Date, int Day) Resolve(Repeat repeat, DateOnly? due, DateOnly today)
    {
        var basis = due ?? today;
        int day = repeat switch { Repeat.Weekly => (int)basis.DayOfWeek, Repeat.Monthly => basis.Day, _ => -1 };
        return (NoteParser.OnOrAfter(repeat, day, basis), day);
    }

    public ItemRead Read()
    {
        var today = _today();
        string title = (TitleBox.Text ?? "").Trim();
        var category = (TodoCategory)Math.Clamp(CategoryBox.SelectedIndex, 0, 2);
        var repeat = CurrentRepeat;
        bool high = HighBox.IsChecked == true;
        if (title.Length == 0) return new("Please type what needs doing.", title, category, null, high, repeat, -1);
        var (ok, date) = ReadBox(today);
        if (!ok) return new("I could not read that date. Try: today, tomorrow, Friday, Nov 12, or in 3 days. Or leave it empty.", title, category, null, high, repeat, -1);
        int day = -1;
        // A repeating item always has a date that fits its pattern, so it is shown in the right place and comes back on time.
        if (repeat != Repeat.None) { var r = Resolve(repeat, date, today); date = r.Date; day = r.Day; }
        return new(null, title, category, date, high, repeat, day);
    }
}

/// <summary>A line of text that takes no room while it is empty, so a window has no blank gaps between its parts.</summary>
public static class QuietText
{
    public static void HideWhenEmpty(params TextBlock[] blocks)
    {
        foreach (var block in blocks)
        {
            block.IsVisible = !string.IsNullOrEmpty(block.Text);
            block.PropertyChanged += (_, e) => { if (e.Property == TextBlock.TextProperty) block.IsVisible = !string.IsNullOrEmpty(block.Text); };
        }
    }
}
