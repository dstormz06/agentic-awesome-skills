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

    /// <summary>The ANDA an invite is for; null for a notice.</summary>
    public string? Number { get; private set; }
    private Action? _accept;

    /// <summary>
    /// An invite never takes the keyboard, so it cannot be reached with Tab. The keyboard way to accept it is to press the same shortcut
    /// again on the same number (the app calls <see cref="Accept"/>), and the invite says so to a screen reader.
    /// </summary>
    public void ShowInvite(string label, PixelPoint near, Action onAccept, string? number = null)
    {
        ChipText.Text = label; Number = number; _accept = onAccept;
        GoText.IsVisible = true;
        Avalonia.Automation.AutomationProperties.SetName(this, label + ". Press the shortcut again to open it, or click.");
        Pill.PointerPressed += (_, _) => Accept();
        Open(near, TimeSpan.FromSeconds(6));
    }

    /// <summary>Opens what the invite offers. True when there was an invite to accept.</summary>
    public bool Accept()
    {
        if (_accept is not { } go) return false;
        _accept = null; Close(); go();
        return true;
    }

    public void ShowNotice(string label, PixelPoint near)
    {
        Avalonia.Automation.AutomationProperties.SetName(this, label);      // a notice is read out, not only shown
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
