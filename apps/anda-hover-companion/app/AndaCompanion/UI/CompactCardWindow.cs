using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AndaCompanion.Models;
using AndaCompanion.Theming;

namespace AndaCompanion.UI;

/// <summary>Short forms of the card's own display strings for the small boxes. Pure, so it is unit-tested.</summary>
public static class CompactText
{
    /// <summary>"▲ +14 d vs goal" → "▲ +14 d"; "▼ 9 d ahead of goal" → "▼ 9 d". Anything unrecognised is kept whole.</summary>
    public static string ShortDelta(string delta)
    {
        foreach (var separator in new[] { " vs ", " ahead" })
        {
            int i = delta.IndexOf(separator, StringComparison.Ordinal);
            if (i > 0) return delta[..i];
        }
        return delta;
    }

    /// <summary>Drops the year only when both dates carry the same one ("Oct 3, 2026" + "Oct 17, 2026" → "Oct 3", "Oct 17").
    /// If the years differ the full dates are kept, so a compact box can never hide a year change.</summary>
    public static (string Goal, string Forecast) Pair(string goal, string forecast)
    {
        string? Year(string d) { int i = d.LastIndexOf(", ", StringComparison.Ordinal); return i > 0 ? d[(i + 2)..] : null; }
        string Day(string d) { int i = d.LastIndexOf(", ", StringComparison.Ordinal); return i > 0 ? d[..i] : d; }
        var (gy, fy) = (Year(goal), Year(forecast));
        return gy is not null && gy == fy ? (Day(goal), Day(forecast)) : (goal, forecast);
    }
}

/// <summary>A dashed vertical rule that takes the height of its row (a stretched Line would force a height of its own).</summary>
internal sealed class Perforation : Control
{
    protected override Size MeasureOverride(Size availableSize) => new(1, 0);

    public override void Render(DrawingContext context)
    {
        var pen = new Pen((IBrush)Application.Current!.Resources["C.Ink3"]!, 1) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) };
        context.DrawLine(pen, new Point(0.5, 0), new Point(0.5, Bounds.Height));
    }
}

/// <summary>
/// The compact appearances (L Strip, M Tile, N Ticket, O Badge): a small box that stays out of the way of an open
/// email. It shows only facts already on the record, makes NO AI call, and a click (or Enter / E) opens the sibling
/// look's full card. It NEVER takes keyboard focus: someone typing an email keeps typing in the email, and a stray
/// Enter or Space cannot expand it. The keyboard way in is to press the same hotkey again on the same number (the app
/// handles that); the mouse way is a click. Esc / Enter / E still work if the box has been focused (for example by
/// a click), and it auto-hides like the full card.
/// </summary>
public sealed class CompactCardWindow : Window
{
    private readonly AndaRecord _record;
    private readonly Action _expand;
    private readonly Border _root;
    private readonly AutoHideController _autoHide;
    private PixelPoint _anchor;

    /// <summary>Hide after this many idle seconds (0 = never). Off by default so tests and the self-test never race a timer.</summary>
    public double AutoHideSeconds { get; set; }

    /// <summary>Normal use: closes when the user clicks elsewhere. The self-test and screenshots turn this off.</summary>
    public bool CloseWhenDeactivated { get; set; } = true;

    internal string PlacementTrace { get; private set; } = "not placed yet";

    public CompactCardWindow(AndaRecord record, Action expand)
    {
        _record = record;
        _expand = expand;
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;          // never steal focus from the email being written or read
        Title = "ANDA compact";

        var look = ThemeManager.Current;
        _root = new Border
        {
            Background = Res("C.Bg"),
            BorderBrush = Res("C.Line"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(look.Form == CardForm.Badge ? 99 : Math.Min(look.Radius, 12)),
            BoxShadow = BoxShadows.Parse("0 8 22 -8 #59000000"),
            ClipToBounds = true,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = look.Form switch
            {
                CardForm.Tile => BuildTile(),
                CardForm.Ticket => BuildTicket(),
                CardForm.Badge => BuildBadge(),
                _ => BuildStrip(),
            },
        };
        Content = new Panel { Margin = new Thickness(14), Children = { _root } };

        string status = record.ForecastLate ? $"forecast {CompactText.ShortDelta(record.ForecastDelta)} late" : "on track";
        AutomationProperties.SetName(this, $"{record.Kind} {record.Number}, {status}. Press Enter to open the full card.");
        AutomationProperties.SetName(_root, $"{record.Kind} {record.Number} compact status. Click to open the full card.");

        _root.PointerPressed += (_, e) => { if (!e.Handled) { e.Handled = true; _expand(); } };
        KeyDown += (_, e) =>
        {
            _autoHide?.Touch();
            switch (e.Key)
            {
                case Key.Escape: Close(); break;
                case Key.Enter or Key.E or Key.Space: e.Handled = true; _expand(); break;
            }
        };
        Deactivated += (_, _) => { if (CloseWhenDeactivated) Close(); };

        _autoHide = new AutoHideController(() => false, () => false, () => Close());
        _root.PointerEntered += (_, _) => _autoHide.PointerInside = true;
        _root.PointerExited += (_, _) => _autoHide.PointerInside = false;
        _root.PointerMoved += (_, _) => _autoHide.Touch();
        Closed += (_, _) => _autoHide.Stop();

        _root.Opacity = 0;
        _root.Transitions = new Transitions
        {
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(140), Easing = new CubicEaseOut() },
        };
        Opened += (_, _) =>
        {
            PlaceNear(_anchor);
            _root.Opacity = 1;
            _autoHide.Start(AutoHideSeconds);
        };
    }

    public void ShowNear(PixelPoint near)
    {
        _anchor = near;
        Position = near;                 // provisional; Opened re-places it from the real, laid-out size
        Show();                          // deliberately not activated or focused
    }

    public string Number => _record.Number;

    private void PlaceNear(PixelPoint near)
    {
        var screen = Screens.ScreenFromPoint(near) ?? Screens.Primary;
        if (screen is null) { Position = near; return; }
        double s = screen.Scaling;
        int w = (int)Math.Ceiling(Bounds.Width * s), h = (int)Math.Ceiling(Bounds.Height * s);
        Position = Placement.Near(near, w, h, screen.WorkingArea);
        PlacementTrace = $"anchor {near.X},{near.Y}; size {w}x{h}px; area {screen.WorkingArea} → {Position.X},{Position.Y}";
    }

    // ---------- shared pieces ----------

    private static IBrush Res(string key) => (IBrush)Application.Current!.Resources[key]!;
    private static FontFamily Font(string key = "C.Font") => (FontFamily)Application.Current!.Resources[key]!;

    private IBrush ToneBrush =>
        Res(_record.Attention?.Level == AttentionLevel.Bad ? "C.Bad" : _record.ForecastLate ? "C.Warn" : "C.Good");

    private IBrush DeltaBrush => Res(_record.ForecastLate ? "C.Warn" : "C.Good");

    private static TextBlock Text(string text, double size, FontWeight weight = FontWeight.Normal,
        string ink = "C.Ink", string font = "C.Font", bool trim = false) => new()
    {
        Text = text, FontSize = size, FontWeight = weight, Foreground = Res(ink), FontFamily = Font(font),
        TextTrimming = trim ? TextTrimming.CharacterEllipsis : TextTrimming.None,
        TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBlock Label(string text) => new()
    {
        Text = text, FontSize = 9, FontWeight = FontWeight.Bold, LetterSpacing = 1.1,
        Foreground = Res("C.Ink3"), FontFamily = Font("C.LabelFont"), VerticalAlignment = VerticalAlignment.Center,
    };

    private Button CloseButton()
    {
        var b = new Button
        {
            Width = 22, Height = 22, Padding = new Thickness(0), Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(6),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Arrow),
            Content = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse("M3.5 3.5l7 7M10.5 3.5l-7 7"), Stroke = Res("C.Ink3"), StrokeThickness = 1.5,
                Width = 14, Height = 14,
            },
        };
        AutomationProperties.SetName(b, "Close");
        ToolTip.SetTip(b, "Close (Esc)");
        b.PointerPressed += (_, e) => e.Handled = true;      // a click on × must not also expand the card
        b.Click += (_, _) => Close();
        return b;
    }

    /// <summary>A thin bar for the progress, drawn as two proportional cells so it needs no custom control.</summary>
    private Grid ProgressBar(double height)
    {
        int pct = Math.Clamp(_record.ProgressPct, 0, 100);
        var g = new Grid { Height = height };
        g.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(pct, 0.001), GridUnitType.Star));
        g.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(100 - pct, 0.001), GridUnitType.Star));
        g.Children.Add(new Border { Background = Res("C.Fill") });
        var rest = new Border { Background = Res("C.Track") };
        Grid.SetColumn(rest, 1);
        g.Children.Add(rest);
        return g;
    }

    private Control? AttentionLine(double size)
    {
        if (_record.Attention is not { } a) return null;
        var tone = Res(a.Level == AttentionLevel.Bad ? "C.Bad" : "C.Warn");
        var t = Text("⚠ " + a.Text, size, FontWeight.SemiBold, trim: true);
        t.Foreground = tone;
        ToolTip.SetTip(t, a.Text);
        return t;
    }

    // ---------- L · Strip ----------

    private Control BuildStrip()
    {
        var (goal, fc) = CompactText.Pair(_record.GoalDate, _record.ForecastDate);
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        top.Children.Add(Text($"{_record.Kind} {_record.Number}", 13, FontWeight.Bold, font: "C.NumFont"));
        var drug = Text(_record.Drug, 12, ink: "C.Ink2", trim: true);
        drug.Margin = new Thickness(8, 0, 6, 0);
        Grid.SetColumn(drug, 1);
        top.Children.Add(drug);
        var close = CloseButton();
        Grid.SetColumn(close, 2);
        top.Children.Add(close);

        var dates = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(0, 1, 0, 0) };
        dates.Children.Add(Label("GOAL"));
        dates.Children.Add(Text(goal, 12.5, FontWeight.SemiBold));
        dates.Children.Add(Text("→", 12.5, ink: "C.Ink3"));
        dates.Children.Add(Label("FORECAST"));
        dates.Children.Add(Text(fc, 12.5, FontWeight.SemiBold));
        var delta = Text(CompactText.ShortDelta(_record.ForecastDelta), 12, FontWeight.Bold);
        delta.Foreground = DeltaBrush;
        delta.Margin = new Thickness(3, 0, 0, 0);
        dates.Children.Add(delta);

        var body = new StackPanel { Margin = new Thickness(12, 8, 6, 8), Spacing = 2 };
        body.Children.Add(top);
        body.Children.Add(dates);
        if (AttentionLine(11.5) is { } attn) body.Children.Add(attn);

        var inner = new StackPanel { Width = 340 - 4 - 2 };     // spine 4 + border 2 → 340 overall
        inner.Children.Add(body);
        var bar = ProgressBar(3);
        inner.Children.Add(bar);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("4,*") };
        grid.Children.Add(new Border { Background = ToneBrush });      // status edge
        Grid.SetColumn(inner, 1);
        grid.Children.Add(inner);
        return grid;
    }

    // ---------- M · Tile ----------

    private Control BuildTile()
    {
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var id = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        id.Children.Add(Text(_record.Number, 15, FontWeight.Bold, font: "C.NumFont"));
        id.Children.Add(Label(_record.Kind));
        head.Children.Add(id);
        var close = CloseButton();
        Grid.SetColumn(close, 1);
        head.Children.Add(close);

        var delta = Text(CompactText.ShortDelta(_record.ForecastDelta), 24, FontWeight.Bold);
        delta.Foreground = DeltaBrush;
        delta.Margin = new Thickness(0, 4, 0, 0);

        var panel = new StackPanel { Width = 196 - 2 - 18, Margin = new Thickness(12, 8, 6, 10), Spacing = 1 };   // → 196 overall
        panel.Children.Add(head);
        panel.Children.Add(Text(_record.Drug, 11.5, ink: "C.Ink2", trim: true));
        panel.Children.Add(delta);
        panel.Children.Add(Text($"forecast {CompactText.Pair(_record.GoalDate, _record.ForecastDate).Forecast} · goal {CompactText.Pair(_record.GoalDate, _record.ForecastDate).Goal}",
            10.5, ink: "C.Ink3", trim: true));
        var bar = ProgressBar(4);
        bar.Margin = new Thickness(0, 7, 6, 0);
        panel.Children.Add(bar);
        if (AttentionLine(11) is { } attn) { attn.Margin = new Thickness(0, 6, 6, 0); panel.Children.Add(attn); }
        return panel;
    }

    // ---------- N · Ticket ----------

    private Control BuildTicket()
    {
        var stub = new StackPanel { Width = 80, Margin = new Thickness(10, 8, 6, 8), VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        stub.Children.Add(Label(_record.Kind));
        stub.Children.Add(Text(_record.Number, 14, FontWeight.Bold, font: "C.NumFont"));
        if (_record.Attention is not null)
        {
            var warn = Text("⚠ needs you", 10.5, FontWeight.SemiBold);
            warn.Foreground = Res(_record.Attention.Level == AttentionLevel.Bad ? "C.Bad" : "C.Warn");
            stub.Children.Add(warn);
        }

        var perforation = new Perforation { Margin = new Thickness(0, 6) };

        var close = CloseButton();
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;

        var main = new StackPanel { Width = 163, Margin = new Thickness(10, 9, 28, 8), Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        main.Children.Add(Text(_record.Drug, 12, FontWeight.SemiBold, trim: true));
        var fc = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        fc.Children.Add(Label("FORECAST"));
        fc.Children.Add(Text(CompactText.Pair(_record.GoalDate, _record.ForecastDate).Forecast, 12.5, FontWeight.SemiBold));
        var delta = Text(CompactText.ShortDelta(_record.ForecastDelta), 12, FontWeight.Bold);
        delta.Foreground = DeltaBrush;
        fc.Children.Add(delta);
        main.Children.Add(fc);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,1,Auto") };
        row.Children.Add(stub);
        Grid.SetColumn(perforation, 1);
        row.Children.Add(perforation);
        var mainHost = new Panel { Children = { main, close } };
        Grid.SetColumn(mainHost, 2);
        row.Children.Add(mainHost);

        var outer = new StackPanel();
        outer.Children.Add(row);
        outer.Children.Add(ProgressBar(3));
        return outer;
    }

    // ---------- O · Badge ----------

    private Control BuildBadge()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Margin = new Thickness(12, 7, 6, 7) };
        row.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = ToneBrush, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(Text(_record.Number, 12.5, FontWeight.Bold, font: "C.NumFont"));
        var delta = Text(CompactText.ShortDelta(_record.ForecastDelta), 12, FontWeight.Bold);
        delta.Foreground = DeltaBrush;
        row.Children.Add(delta);
        if (_record.Attention is not null)
        {
            var warn = Text("⚠", 12, FontWeight.Bold);
            warn.Foreground = Res(_record.Attention.Level == AttentionLevel.Bad ? "C.Bad" : "C.Warn");
            row.Children.Add(warn);
        }
        row.Children.Add(CloseButton());
        ToolTip.SetTip(row, $"{_record.Drug}\n{_record.Attention?.Text ?? "Nothing needs you right now."}\nClick to open the full card.");
        return row;
    }
}
