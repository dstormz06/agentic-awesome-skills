using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AndaCompanion.Theming;

namespace AndaCompanion.UI;

/// <summary>
/// Renders the active appearance's progress form. One control, five forms —
/// thin bar (A), milestone ticks (B), ring (C), hatched rule (D), capsule (E).
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

    static ProgressControl() => AffectsRender<ProgressControl>(ValueProperty);

    private static Color Res(string key) =>
        ((ISolidColorBrush)Application.Current!.Resources[key]!).Color;

    private ProgressVariant Variant => ThemeManager.Current.Progress;

    protected override Size MeasureOverride(Size availableSize)
    {
        double h = Variant switch
        {
            ProgressVariant.Ring => 52,
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

        switch (Variant)
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
