using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace AndaCompanion.UI;

/// <summary>
/// Consent stage one: a small transient invite (whisper model) or a short notice
/// (invalid number, paused in terminals, hotkey conflict). Self-fades if ignored — a
/// card never opens from it unless the user clicks. A notice stays long enough to be
/// READ (scaled to its length) and a click dismisses it early.
/// </summary>
public partial class ChipWindow : Window
{
    private readonly DispatcherTimer _fade = new();
    private PixelPoint _anchor;

    public ChipWindow()
    {
        InitializeComponent();
        _fade.Tick += (_, _) => Close();
        Closed += (_, _) => _fade.Stop();
        Opened += (_, _) => PlaceNear();
    }

    public void ShowInvite(string label, PixelPoint near, Action onAccept)
    {
        ChipText.Text = label;
        GoText.IsVisible = true;
        Pill.PointerPressed += (_, _) => { Close(); onAccept(); };
        Open(near, TimeSpan.FromSeconds(3));
    }

    public void ShowNotice(string label, PixelPoint near)
    {
        ChipText.Text = label;
        GoText.IsVisible = false;
        Dot.IsVisible = false;
        Pill.PointerPressed += (_, _) => Close();
        Open(near, NoticeLifetime(label));
    }

    /// <summary>About 14 characters per second of reading time plus a beat to notice it; never under 2.5 s.</summary>
    public static TimeSpan NoticeLifetime(string text) =>
        TimeSpan.FromSeconds(Math.Clamp(1.2 + text.Length / 14.0, 2.5, 8.0));

    private void Open(PixelPoint near, TimeSpan lifetime)
    {
        _anchor = near;
        Position = new PixelPoint(near.X + 8, near.Y + 12);   // provisional; Opened re-places with the real size
        _fade.Interval = lifetime;
        _fade.Start();
        Show();
    }

    private void PlaceNear()
    {
        var screen = Screens.ScreenFromPoint(_anchor) ?? Screens.Primary;
        if (screen is null) return;
        double s = screen.Scaling;
        Position = Placement.Near(_anchor, (int)Math.Ceiling(Bounds.Width * s), (int)Math.Ceiling(Bounds.Height * s),
            screen.WorkingArea, dx: 8, dy: 12);
    }
}
