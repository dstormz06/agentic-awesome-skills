using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AndaCompanion.UI;

/// <summary>
/// Larger text for this app's windows (round 33). A window's content is drawn 1.15 or 1.3 times as large, so that every letter grows by that
/// factor; a window with a fixed size grows with it, never beyond the screen it is on. The simulation enlarges only the letters and lets
/// the lines wrap; here the whole window grows in proportion. Other programs are not touched.
/// </summary>
public static class DisplayScale
{
    private sealed class State
    {
        public LayoutTransformControl Host = null!;
        public double Width, Height, MinWidth, MinHeight, MaxWidth, MaxHeight;
        public double Applied = 1;
    }

    private static readonly ConditionalWeakTable<Window, State> States = new();
    private static readonly List<WeakReference<Window>> Open = new();

    /// <summary>The factor in use for windows that open from now on.</summary>
    public static double Current { get; private set; } = 1;

    /// <summary>The factor a window is drawn at now (1 when it was never scaled).</summary>
    public static double Of(Window w) => States.TryGetValue(w, out var s) ? s.Applied : 1;

    /// <summary>Draws a window at the factor. Safe to call again, with the same factor or another one.</summary>
    public static void Apply(Window w, double scale)
    {
        if (!(scale >= 1 && scale <= 2)) scale = 1;
        if (!States.TryGetValue(w, out var s))
        {
            if (scale == 1) return;                                      // standard text: the window is left exactly as it was built
            if (w.Content is not Control content) return;
            w.Content = null;
            s = new State { Host = new LayoutTransformControl { Child = content }, Width = w.Width, Height = w.Height, MinWidth = w.MinWidth, MinHeight = w.MinHeight, MaxWidth = w.MaxWidth, MaxHeight = w.MaxHeight };
            w.Content = s.Host;
            States.Add(w, s);
            Open.Add(new WeakReference<Window>(w));
            w.Closed += (_, _) => Open.RemoveAll(r => !r.TryGetTarget(out var t) || ReferenceEquals(t, w));
        }
        s.Applied = scale;
        s.Host.LayoutTransform = scale == 1 ? null : new ScaleTransform(scale, scale);
        var room = Room(w);
        double Fit(double v, double most) => double.IsNaN(v) || double.IsInfinity(v) ? v : Math.Min(v * scale, most);
        if (!double.IsNaN(s.MinWidth) && s.MinWidth > 0) w.MinWidth = Fit(s.MinWidth, room.Width);
        if (!double.IsNaN(s.MinHeight) && s.MinHeight > 0) w.MinHeight = Fit(s.MinHeight, room.Height);
        if (!double.IsInfinity(s.MaxWidth)) w.MaxWidth = Fit(s.MaxWidth, room.Width);
        if (!double.IsInfinity(s.MaxHeight)) w.MaxHeight = Fit(s.MaxHeight, room.Height);
        if (!double.IsNaN(s.Width)) w.Width = Fit(s.Width, room.Width);
        if (!double.IsNaN(s.Height)) w.Height = Fit(s.Height, room.Height);
    }

    /// <summary>The space a window may take on the screen it is on, in the window's own units. Without a screen (a test), no limit.</summary>
    private static Size Room(Window w)
    {
        try
        {
            var screen = w.Screens?.ScreenFromWindow(w) ?? w.Screens?.Primary;
            if (screen is null) return new Size(double.PositiveInfinity, double.PositiveInfinity);
            return new Size(screen.WorkingArea.Width / screen.Scaling, screen.WorkingArea.Height / screen.Scaling);
        }
        catch (Exception) { return new Size(double.PositiveInfinity, double.PositiveInfinity); }
    }

    /// <summary>The person changed the text size: windows that are open are drawn again at the new factor, and new ones open at it.</summary>
    public static void Set(double scale)
    {
        Current = scale >= 1 && scale <= 2 ? scale : 1;
        foreach (var r in Open.ToList()) if (r.TryGetTarget(out var w)) Apply(w, Current);
    }

    /// <summary>Every window of this app that opens is drawn at the factor in use. Called once, when the app starts.</summary>
    public static void Watch() => Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => Apply(w, Current));

    /// <summary>
    /// Compact spacing for the card: the space above and below each of the given parts is 0.72 of what it was. The first call remembers what
    /// it was, so calling again (comfortable, then compact, then comfortable) never adds up. The sides are left as they are.
    /// </summary>
    public static void Space(IEnumerable<Control> parts, double spacing, IDictionary<Control, (Thickness Margin, Thickness Padding)> remembered)
    {
        if (!(spacing > 0 && spacing <= 1)) spacing = 1;
        Thickness Squeeze(Thickness t) => new(t.Left, t.Top * spacing, t.Right, t.Bottom * spacing);
        foreach (var c in parts)
        {
            if (!remembered.TryGetValue(c, out var was)) { was = (c.Margin, c is Decorator d0 ? d0.Padding : default); remembered[c] = was; }
            c.Margin = Squeeze(was.Margin);
            if (c is Decorator d) d.Padding = Squeeze(was.Padding);
        }
    }
}
