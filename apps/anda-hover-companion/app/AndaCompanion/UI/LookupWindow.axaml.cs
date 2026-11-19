using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AndaCompanion.Data;
using AndaCompanion.Models;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

/// <summary>
/// What the lookup window offers besides a number and Go (round 33): the ANDAs kept in the person's list and the recent ones, and, as
/// One Bar, rows that open Review or Ask. Without it the window is the plain box it always was.
/// </summary>
/// <param name="Bar">One Bar: also offers Review and Ask.</param>
/// <param name="Listed">The kept and recent numbers the data knows, kept ones first, each with whether it is kept.</param>
/// <param name="Find">Looks a number up, for the row that names the ANDA.</param>
/// <param name="OpenDraft">Opens Review a draft with a text.</param>
/// <param name="OpenAsk">Opens Ask with a text.</param>
public sealed record LookupOptions(bool Bar, Func<IReadOnlyList<(string Id, bool Kept)>>? Listed = null, Func<string, AndaRecord?>? Find = null,
    Action<string>? OpenDraft = null, Action<string>? OpenAsk = null);

public partial class LookupWindow : Window
{
    private readonly Action<string> _onValid;
    private readonly LookupOptions? _options;
    private List<BarRow> _rows = new();
    private int _at;

    public LookupWindow(Action<string> onValid) : this(onValid, null) { }

    public LookupWindow(Action<string> onValid, LookupOptions? options)
    {
        _onValid = onValid; _options = options;
        InitializeComponent();
        GoBtn.Click += (_, _) => Submit();
        Input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; if (Bar && _rows.Count > 0) Run(_rows[_at]); else Submit(); }
            else if (Bar && e.Key == Key.Down) { e.Handled = true; Move(1); }
            else if (Bar && e.Key == Key.Up) { e.Handled = true; Move(-1); }
        };
        Input.TextChanged += (_, _) => { ErrorText.IsVisible = false; PaintRows(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Deactivated += (_, _) => Close();
        Opened += (_, _) => Input.Focus();
        if (Bar) { Caption.Text = "ONE BAR"; Input.Watermark = "An ANDA number, or text to review"; AutomationProperties.SetName(Input, "An ANDA number, or text to review as a draft"); }
        PaintRecent(); PaintRows();
    }

    private bool Bar => _options is { Bar: true };

    /// <summary>The rows shown now, for a test to read.</summary>
    public IReadOnlyList<BarRow> Rows => _rows;
    public int SelectedRow => _at;

    private static IBrush Res(string key) => (IBrush)Application.Current!.Resources[key]!;

    private void PaintRecent()
    {
        RecentPanel.Children.Clear();
        var ids = _options?.Listed?.Invoke() ?? Array.Empty<(string, bool)>();
        RecentPanel.IsVisible = ids.Count > 0;
        if (ids.Count == 0) return;
        RecentPanel.Children.Add(new TextBlock { Text = "RECENT", FontSize = 9.5, FontWeight = FontWeight.Bold, LetterSpacing = 1.4, Foreground = Res("C.Ink3"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 4) });
        foreach (var (id, kept) in ids)
        {
            var b = new Button { Content = id, Classes = { "chip" }, Margin = new Thickness(0, 0, 6, 4), FontWeight = kept ? FontWeight.SemiBold : FontWeight.Normal, Tag = id };
            AutomationProperties.SetName(b, Recents.Name(id, kept));
            if (kept) ToolTip.SetTip(b, "Kept in my list");
            b.Click += (_, _) => { Close(); _onValid(id); };
            RecentPanel.Children.Add(b);
        }
    }

    private void PaintRows()
    {
        RowsPanel.Children.Clear();
        RowsPanel.IsVisible = Bar;
        if (!Bar) { _rows = new(); return; }
        _rows = OneBar.Rows(Input.Text, "", _options!.Find ?? (_ => null));
        _at = Math.Clamp(_at, 0, _rows.Count - 1);
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i]; int index = i;
            var text = new StackPanel { Spacing = 1 };
            text.Children.Add(new TextBlock { Text = row.Title, FontSize = 12.5, FontWeight = FontWeight.SemiBold, Foreground = Res("C.Ink"), TextWrapping = TextWrapping.Wrap });
            text.Children.Add(new TextBlock { Text = row.Sub, FontSize = 11.5, Foreground = Res("C.Ink3"), TextWrapping = TextWrapping.Wrap });
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("22,*") };
            line.Children.Add(new TextBlock { Text = row.Icon, FontSize = 13, Foreground = Res("C.Accent"), VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(text, 1); line.Children.Add(text);
            var b = new Button { Content = line, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 6), Background = Brushes.Transparent,
                BorderThickness = new Thickness(i == _at ? 2 : 1), BorderBrush = Res(i == _at ? "C.Accent" : "C.Hair"), Tag = row.Act };
            AutomationProperties.SetName(b, $"{row.Title}. {row.Sub}" + (i == _at ? ". Selected: Enter opens it" : ""));
            b.Click += (_, _) => { _at = index; Run(row); };
            RowsPanel.Children.Add(b);
        }
    }

    private void Move(int by) { if (_rows.Count == 0) return; _at = (_at + by + _rows.Count) % _rows.Count; PaintRows(); }

    /// <summary>Presses one row: the card for the number typed, Review a draft with what was typed, or Ask.</summary>
    public void Run(BarRow row)
    {
        if (row.Act == "card") { Submit(); return; }
        Close();
        if (row.Act == "draft") _options?.OpenDraft?.Invoke(row.Text); else _options?.OpenAsk?.Invoke(row.Text);
    }

    private void Submit()
    {
        var number = AndaValidator.Extract(Input.Text);
        if (number is null)
        {
            ErrorText.Text = OneBar.LookupError(Input.Text);
            ErrorText.IsVisible = true;
            return;
        }
        Close();
        _onValid(number);
    }
}
