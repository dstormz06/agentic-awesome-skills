using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

/// <summary>
/// The calendar ribbon of look U: the weeks around the goal date and the forecast date, weekends shaded, a federal holiday marked, and
/// the gap between the two drawn. It is the same picture as the browser simulation's. The sentence from <see cref="Describe"/> says the
/// same in words and is shown under it, so the picture is never the only carrier of the fact.
/// </summary>
public sealed class CalendarRibbon : Control
{
    private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

    public DateOnly? Goal { get; private set; }
    public DateOnly? Forecast { get; private set; }
    public BusinessCalendar Calendar { get; private set; } = new();

    public void Set(DateOnly? goal, DateOnly? forecast, BusinessCalendar calendar)
    {
        Goal = goal; Forecast = forecast; Calendar = calendar;
        IsVisible = Window(goal, forecast) is not null;
        InvalidateVisual();
    }

    /// <summary>The first day drawn (a Monday, at least a week before the earlier date) and the number of weeks; null when a date is
    /// missing or the two are more than fourteen weeks apart, in which case the sentence stands alone.</summary>
    public static (DateOnly Start, int Weeks)? Window(DateOnly? goal, DateOnly? forecast)
    {
        if (goal is not { } g || forecast is not { } f) return null;
        DateOnly lo = g < f ? g : f, hi = g < f ? f : g;
        if (hi.DayNumber - lo.DayNumber > 98) return null;
        var start = lo.AddDays(-7);
        start = start.AddDays(-(((int)start.DayOfWeek + 6) % 7));
        int weeks = Math.Clamp((int)Math.Ceiling((hi.DayNumber + 7 - start.DayNumber) / 7.0), 4, 16);
        return (start, weeks);
    }

    /// <summary>"Goal Sat Oct 3 (a weekend day); forecast Sat Oct 17 (a weekend day); 14 days apart."</summary>
    public static string Describe(DateOnly goal, DateOnly forecast, BusinessCalendar calendar)
    {
        string Word(DateOnly d) => calendar.KindOf(d) switch
        {
            DayKind.Weekend => " (a weekend day)",
            DayKind.Holiday => $" ({calendar.HolidayName(d)})",
            _ => "",
        };
        int gap = Math.Abs(forecast.DayNumber - goal.DayNumber);
        return $"Goal {goal.ToString("ddd MMM d", Inv)}{Word(goal)}; forecast {forecast.ToString("ddd MMM d", Inv)}{Word(forecast)}; {gap} day{(gap == 1 ? "" : "s")} apart.";
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 300 : availableSize.Width, 80);

    private static FormattedText Label(string text, IBrush brush) =>
        new(text, Inv, FlowDirection.LeftToRight, new Typeface((FontFamily)Application.Current!.Resources["C.BodyFont"]!, weight: FontWeight.Medium), 9.5, brush);

    public override void Render(DrawingContext ctx)
    {
        if (Window(Goal, Forecast) is not { } win || Goal is not { } goal || Forecast is not { } fc) return;
        double w = Bounds.Width; int n = win.Weeks * 7; double dw = w / n;
        var ink3 = (IBrush)Application.Current!.Resources["C.Ink3"]!;
        bool late = fc > goal;
        IBrush tone = new SolidColorBrush(Color.Parse(late ? "#C98A14" : "#2F8A5B")), dark = new SolidColorBrush(Color.Parse(late ? "#8A5800" : "#1A6B43"));
        ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#EEF0F2")), null, new RoundedRect(new Rect(0, 30, w, 14), 3));
        int holidayLabels = 0;
        for (int i = 0; i < n; i++)
        {
            var day = win.Start.AddDays(i);
            switch (Calendar.KindOf(day))
            {
                case DayKind.Weekend: ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#D3D8DE")), null, new Rect(i * dw, 30, dw, 14)); break;
                case DayKind.Holiday:
                    ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#C4B4E0")), null, new Rect(i * dw, 30, dw, 14));
                    if (holidayLabels++ < 2 && Calendar.HolidayName(day) is { } name)
                    {
                        var t = Label(name, ink3);
                        ctx.DrawText(t, new Point(Math.Clamp((i + .5) * dw - t.Width / 2, 0, Math.Max(0, w - t.Width)), 57));
                    }
                    break;
            }
        }
        double X(DateOnly d) => (d.DayNumber - win.Start.DayNumber + .5) * dw;
        double gx = X(goal), fx = X(fc);
        ctx.DrawRectangle(tone, null, new RoundedRect(new Rect(Math.Min(gx, fx), 35, Math.Abs(fx - gx), 4), 2));
        var diamond = new StreamGeometry();
        using (var c = diamond.Open())
        {
            c.BeginFigure(new Point(gx, 25), true);
            c.LineTo(new Point(gx + 7.5, 36.5)); c.LineTo(new Point(gx, 48)); c.LineTo(new Point(gx - 7.5, 36.5));
            c.EndFigure(true);
        }
        ctx.DrawGeometry(Brushes.White, new Pen(new SolidColorBrush(Color.Parse("#1D2329")), 2), diamond);
        ctx.DrawEllipse(dark, null, new Point(fx, 36.5), 7, 7);
        // the two names sit over their marks; when the marks are close, each name leans away from the other
        bool near = Math.Abs(gx - fx) < 78 * w / 300;
        void Name(string text, double x, bool leanLeft)
        {
            var t = Label(text, ink3);
            double left = near ? (leanLeft ? x - t.Width : x) : x - t.Width / 2;
            ctx.DrawText(t, new Point(Math.Clamp(left, 0, Math.Max(0, w - t.Width)), 6));
        }
        Name("Goal", gx, gx <= fx); Name("Forecast", fx, gx > fx);
        var first = Label(win.Start.ToString("MMM d", Inv), ink3);
        var last = Label(win.Start.AddDays(n - 1).ToString("MMM d", Inv), ink3);
        ctx.DrawText(first, new Point(0, 68));
        ctx.DrawText(last, new Point(w - last.Width, 68));
    }
}
