using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AndaCompanion.Theming;

namespace AndaCompanion.UI;

/// <summary>
/// Renders the active appearance's progress form. One control, nine forms —
/// thin bar (A), milestone ticks (B), ring (C), hatched rule (D), capsule (E),
/// line with a dot (K), ten dots (H), five-step stepper (I), goal-versus-forecast timeline (J).
/// Form changes with appearance; the value and its meaning never do.
/// </summary>
public sealed class ProgressControl : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ProgressControl, double>(nameof(Value));

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Timeline look only: days from the extract to the goal, and the forecast's days late (+) or early (−).
    /// When the record carries no numbers the timeline falls back to a plain bar — it never invents a position.</summary>
    public static readonly StyledProperty<int?> DaysToGoalProperty =
        AvaloniaProperty.Register<ProgressControl, int?>(nameof(DaysToGoal));
    public static readonly StyledProperty<int?> DeltaDaysProperty =
        AvaloniaProperty.Register<ProgressControl, int?>(nameof(DeltaDays));

    public int? DaysToGoal { get => GetValue(DaysToGoalProperty); set => SetValue(DaysToGoalProperty, value); }
    public int? DeltaDays { get => GetValue(DeltaDaysProperty); set => SetValue(DeltaDaysProperty, value); }

    static ProgressControl() => AffectsRender<ProgressControl>(ValueProperty, DaysToGoalProperty, DeltaDaysProperty);

    private bool HasTimeline => Variant == ProgressVariant.Timeline && DaysToGoal is not null && DeltaDays is not null;

    /// <summary>Pure geometry for the timeline, exposed for tests: positions as 0..1 fractions of the width.</summary>
    public static (double Goal, double Forecast) TimelinePositions(int daysToGoal, int deltaDays)
    {
        double goal = Math.Max(0, daysToGoal);
        double forecast = Math.Max(0, daysToGoal + deltaDays);
        double domain = Math.Max(1, Math.Max(goal, forecast) * 1.08);
        return (Math.Clamp(goal / domain, 0, 1), Math.Clamp(forecast / domain, 0, 1));
    }

    // Deliberately NOT animated: sweeping the value from 0 would display a wrong
    // percentage for ~half a second. Motion may never misrepresent a fact — the card's
    // entrance animation carries the polish instead.

    private static Color Res(string key) =>
        ((ISolidColorBrush)Application.Current!.Resources[key]!).Color;

    private ProgressVariant Variant => ThemeManager.Current.Progress;

    protected override Size MeasureOverride(Size availableSize)
    {
        double h = Variant switch
        {
            ProgressVariant.Ring => 52,
            ProgressVariant.Timeline => HasTimeline ? 46 : 4,
            ProgressVariant.Step => 14,
            ProgressVariant.Line => 10,
            ProgressVariant.Dots => 9,
            ProgressVariant.Capsule => 8,
            ProgressVariant.Hatch => 6,
            ProgressVariant.Ticks => 5,
            _ => 4,
        };
        double w = Variant == ProgressVariant.Ring ? 52
            : double.IsFinite(availableSize.Width) ? availableSize.Width : 240;
        return new Size(w, h);
    }

    public override void Render(DrawingContext ctx)
    {
        double pct = Math.Clamp(Value, 0, 100) / 100.0;
        double w = Bounds.Width, h = Bounds.Height;
        var track = new SolidColorBrush(Res("C.Track"));
        var fill = new SolidColorBrush(Res("C.Fill"));

        switch (Variant == ProgressVariant.Timeline && !HasTimeline ? ProgressVariant.Bar : Variant)
        {
            case ProgressVariant.Bar:
                ctx.DrawRectangle(track, null, new RoundedRect(new Rect(0, 0, w, 4), 2));
                if (pct > 0) ctx.DrawRectangle(fill, null, new RoundedRect(new Rect(0, 0, w * pct, 4), 2));
                break;

            case ProgressVariant.Ticks:
                int on = (int)Math.Round(pct * 5);
                double seg = (w - 4 * 4) / 5.0;
                for (int i = 0; i < 5; i++)
                {
                    var brush = i < on ? fill : track;
                    ctx.DrawRectangle(brush, null,
                        new RoundedRect(new Rect(i * (seg + 4), 0, seg, 5), 2));
                }
                break;

            case ProgressVariant.Hatch:
                var line = new Pen(new SolidColorBrush(Res("C.Line")), 1);
                ctx.DrawRectangle(track, line, new Rect(0.5, 0.5, w - 1, 5));
                double fw = w * pct;
                if (fw > 2)
                {
                    using (ctx.PushClip(new Rect(0, 0, fw, 6)))
                    {
                        var hatch = new Pen(fill, 2);
                        for (double x = -6; x < fw + 6; x += 6)
                            ctx.DrawLine(hatch, new Point(x, 7), new Point(x + 7, -1));
                    }
                    ctx.DrawRectangle(null, new Pen(fill, 1), new Rect(0.5, 0.5, fw - 1, 5));
                }
                break;

            case ProgressVariant.Capsule:
                ctx.DrawRectangle(track, null, new RoundedRect(new Rect(0, 0, w, 8), 4));
                if (pct > 0.02)
                    ctx.DrawRectangle(fill, null, new RoundedRect(new Rect(0, 0, w * pct, 8), 4));
                break;

            case ProgressVariant.Line:
                {
                    // K: a 2 px line with a dot at the value — colour-free, reads on any projector.
                    double y = h / 2;
                    ctx.DrawRectangle(track, null, new RoundedRect(new Rect(0, y - 1, w, 2), 1));
                    double x = Math.Clamp(w * pct, 0, w);
                    if (pct > 0) ctx.DrawRectangle(fill, null, new RoundedRect(new Rect(0, y - 1, x, 2), 1));
                    ctx.DrawEllipse(fill, null, new Point(Math.Clamp(x, 4.5, w - 4.5), y), 4.5, 4.5);
                    break;
                }

            case ProgressVariant.Dots:
                {
                    // H: ten round dots, filled to the nearest tenth.
                    int n = (int)Math.Round(pct * 10);
                    const double d = 8;
                    double gap = Math.Max(2, (w - 10 * d) / 9);
                    for (int i = 0; i < 10; i++)
                        ctx.DrawEllipse(i < n ? fill : track, null,
                            new Point(i * (d + gap) + d / 2, h / 2), d / 2, d / 2);
                    break;
                }

            case ProgressVariant.Step:
                {
                    // I: five steps joined by rules; steps reached are filled.
                    int n = (int)Math.Round(pct * 5);
                    const double d = 11;
                    double step = (w - d) / 4;
                    for (int i = 0; i < 4; i++)
                        ctx.DrawLine(new Pen(i < n - 1 ? fill : track, 2),
                            new Point(i * step + d, h / 2), new Point((i + 1) * step, h / 2));
                    for (int i = 0; i < 5; i++)
                    {
                        var c = new Point(i * step + d / 2, h / 2);
                        if (i < n) ctx.DrawEllipse(fill, null, c, d / 2, d / 2);
                        else ctx.DrawEllipse(Brushes.Transparent, new Pen(new SolidColorBrush(Res("C.Line")), 1.5), c, d / 2 - 0.75, d / 2 - 0.75);
                    }
                    break;
                }

            case ProgressVariant.Timeline:
                DrawTimeline(ctx, w, h, track, fill);
                break;

            case ProgressVariant.Ring:
                var center = new Point(26, 26);
                const double r = 21;
                ctx.DrawEllipse(null, new Pen(track, 4), center, r, r);
                if (pct > 0.01)
                {
                    double sweep = pct * 2 * Math.PI;
                    var geo = new StreamGeometry();
                    using (var g = geo.Open())
                    {
                        g.BeginFigure(new Point(26, 26 - r), isFilled: false);
                        g.ArcTo(
                            new Point(26 + r * Math.Sin(sweep), 26 - r * Math.Cos(sweep)),
                            new Size(r, r), 0, sweep > Math.PI, SweepDirection.Clockwise);
                        g.EndFigure(false);
                    }
                    ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Res("C.Accent")), 4)
                        { LineCap = PenLineCap.Round }, geo);
                }
                var tf = new Typeface((FontFamily)Application.Current!.Resources["C.NumFont"]!,
                                      weight: FontWeight.Bold);
                var text = new FormattedText($"{Value:0}%",
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, tf, 11.5, new SolidColorBrush(Res("C.Ink")));
                ctx.DrawText(text, new Point(26 - text.Width / 2, 26 - text.Height / 2));
                break;
        }
    }

    // J: one line from today to a little past the later of goal and forecast. The gap between the goal tick and
    // the forecast marker is the picture — amber when the forecast is later than the goal, green when earlier.
    private void DrawTimeline(DrawingContext ctx, double w, double h, IBrush track, IBrush fill)
    {
        var (goalAt, fcAt) = TimelinePositions(DaysToGoal!.Value, DeltaDays!.Value);
        bool late = DeltaDays!.Value > 0;
        var gapBrush = new SolidColorBrush(Res(late ? "C.Warn" : "C.Good"));
        var ink = new SolidColorBrush(Res("C.Ink"));
        double gx = goalAt * w, fx = fcAt * w, lo = Math.Min(gx, fx), hi = Math.Max(gx, fx);
        double y = 23;

        ctx.DrawRectangle(track, null, new RoundedRect(new Rect(0, y - 1.5, w, 3), 1.5));
        if (lo > 0) ctx.DrawRectangle(fill, null, new RoundedRect(new Rect(0, y - 1.5, lo, 3), 1.5));
        if (hi - lo > 0.5) ctx.DrawRectangle(gapBrush, null, new Rect(lo, y - 2.5, hi - lo, 5));
        ctx.DrawLine(new Pen(ink, 2), new Point(gx, y - 8), new Point(gx, y + 8));                  // goal tick
        var diamond = new StreamGeometry();                                                          // forecast marker: a hollow diamond
        using (var g = diamond.Open())
        {
            g.BeginFigure(new Point(fx, y - 6.5), isFilled: true);
            g.LineTo(new Point(fx + 6.5, y)); g.LineTo(new Point(fx, y + 6.5)); g.LineTo(new Point(fx - 6.5, y));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(new SolidColorBrush(Res("C.Bg")), new Pen(gapBrush, 2.5), diamond);
        ctx.DrawEllipse(fill, null, new Point(2.5, y), 2.5, 2.5);                                    // today

        var tf = new Typeface((FontFamily)Application.Current!.Resources["C.Font"]!, weight: FontWeight.Bold);
        var lab = new SolidColorBrush(Res("C.Ink3"));
        FormattedText Label(string t) => new(t, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, tf, 9, lab);

        var today = Label("TODAY");
        ctx.DrawText(today, new Point(0, 0));
        var goal = Label("GOAL");
        double goalX = Math.Clamp(gx - goal.Width / 2, today.Width + 6, Math.Max(today.Width + 6, w - goal.Width));
        ctx.DrawText(goal, new Point(goalX, 0));
        var fc = Label("FORECAST");
        ctx.DrawText(fc, new Point(Math.Clamp(fx - fc.Width / 2, 0, Math.Max(0, w - fc.Width)), h - fc.Height));
    }
}


/// <summary>Faint graph-grid surface behind the card — appearance D's signature.</summary>
public sealed class GridSurface : Control
{
    public override void Render(DrawingContext ctx)
    {
        var color = ((ISolidColorBrush)Application.Current!.Resources["C.Grid"]!).Color;
        if (color.A == 0) return;
        var pen = new Pen(new SolidColorBrush(color), 1);
        for (double x = 14; x < Bounds.Width; x += 14)
            ctx.DrawLine(pen, new Point(x, 0), new Point(x, Bounds.Height));
        for (double y = 14; y < Bounds.Height; y += 14)
            ctx.DrawLine(pen, new Point(0, y), new Point(Bounds.Width, y));
    }
}
