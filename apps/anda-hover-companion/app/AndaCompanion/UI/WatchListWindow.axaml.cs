using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

/// <summary>Every ANDA with something worth a second look, worst first. Computed when it opens; never in the background.</summary>
public partial class WatchListWindow : Window
{
    private readonly Func<WatchListResult> _compute;
    private readonly Action<string, PixelPoint> _open;
    private WatchListResult _result = new(Array.Empty<WatchListItem>(), 0, 0);

    public WatchListWindow(Func<WatchListResult> compute, Action<string, PixelPoint> open)
    {
        _compute = compute;
        _open = open;
        InitializeComponent();
        CloseBtn.Click += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        NotesCheck.IsCheckedChanged += (_, _) => Draw();
        Reload();
    }

    public void Reload() { _result = _compute(); Draw(); }

    private IBrush Res(string key) => (IBrush)Application.Current!.Resources[key]!;

    private void Draw()
    {
        bool notes = NotesCheck.IsChecked == true;
        var shown = _result.Items.Where(i => notes || i.Severity != "info").ToList();
        Summary.Text = $"{_result.Shown} of {_result.Total} ANDAs have something worth a look" + (notes ? "." : "; notes are hidden.");
        Rows.Children.Clear();
        if (shown.Count == 0)
        {
            Rows.Children.Add(new TextBlock { Text = "Nothing needs a second look in your data.", Foreground = Res("C.Ink2"), Margin = new Thickness(0, 8) });
            return;
        }
        foreach (var item in shown)
        {
            string tone = item.Severity switch { "bad" => "C.Bad", "warn" => "C.Warn", _ => "C.Ink2" };
            string tag = item.Severity switch { "bad" => "ACT", "warn" => "CHECK", _ => "NOTE" };
            var grid = new Grid { ColumnDefinitions = new("56,70,*"), Margin = new Thickness(2, 4) };
            grid.Children.Add(new TextBlock { Text = tag, FontSize = 9.5, FontWeight = FontWeight.Bold, Foreground = Res(tone), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            var num = new TextBlock { Text = item.Number, FontWeight = FontWeight.Bold, Foreground = Res("C.Ink") };
            Grid.SetColumn(num, 1);
            grid.Children.Add(num);
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = item.Drug, Foreground = Res("C.Ink"), TextWrapping = TextWrapping.Wrap });
            if (item.TopFlag is not null) text.Children.Add(new TextBlock { Text = item.TopFlag, FontSize = 11.5, Foreground = Res(tone), TextWrapping = TextWrapping.Wrap });
            else if (item.UrgentReason is not null) text.Children.Add(new TextBlock { Text = item.UrgentReason, FontSize = 11.5, Foreground = Res(tone), TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(text, 2);
            grid.Children.Add(text);
            string number = item.Number;
            var btn = new Button { Content = grid, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, Background = Brushes.Transparent, Padding = new Thickness(4) };
            AutomationProperties.SetName(btn, $"ANDA {item.Number}, {item.Drug}");
            btn.Click += (_, _) => _open(number, new PixelPoint(Position.X + 60, Position.Y + 80));
            Rows.Children.Add(btn);
        }
    }
}
