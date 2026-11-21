using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

/// <summary>
/// Round 42 (round 19 in the simulation): one short notice near the clock, bottom right of the screen. It never takes the keyboard (it is
/// shown without being activated), hides by itself after twelve seconds (held while the pointer is over it), and shows the ANDA number and a
/// short reason, never the person's notes. Open goes to the card or to News and alerts; "Remind me tomorrow" brings a notice from the data
/// back the next day; Close lets the next one come.
/// </summary>
public sealed class NoticeWindow : Window
{
    internal NoticeItem Item { get; }
    internal TextBlock KindText { get; } = new() { FontSize = 11, FontWeight = FontWeight.Bold, LetterSpacing = 1.2 };
    internal TextBlock Body { get; } = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    internal Button OpenButton { get; } = new() { FontSize = 12 };
    internal Button SnoozeButton { get; } = new() { Content = "Remind me tomorrow", FontSize = 12 };
    internal Button CloseButton { get; } = new() { Content = "Close", FontSize = 12 };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(Notices.ShowSeconds) };

    /// <param name="open">Opens the card (or News and alerts); <paramref name="snooze"/> keeps it for tomorrow; <paramref name="done"/> is told when it hides.</param>
    public NoticeWindow(NoticeItem item, Action<NoticeItem> open, Action<NoticeItem> snooze)
    {
        Item = item;
        Title = "ANDA Companion (Notula) — notice"; Width = 360; SizeToContent = SizeToContent.Height; CanResize = false;
        SystemDecorations = SystemDecorations.None; Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        IBrush Res(string key) => (IBrush)Application.Current!.Resources[key]!;
        Background = Res("C.Bg");
        KindText.Text = item.KindLabel; KindText.Foreground = Res("C.Ink3");
        Body.Text = item.Text; Body.Foreground = Res("C.Ink");
        OpenButton.Content = item.OpenLabel;
        SnoozeButton.IsVisible = item.Kind == "data";
        AutomationProperties.SetName(this, "Notice near the clock");
        AutomationProperties.SetLiveSetting(Body, AutomationLiveSetting.Polite);
        OpenButton.Click += (_, _) => { Close(); open(item); };
        SnoozeButton.Click += (_, _) => { snooze(item); Close(); };
        CloseButton.Click += (_, _) => Close();
        _timer.Tick += (_, _) => Close();
        PointerEntered += (_, _) => _timer.Stop();
        PointerExited += (_, _) => { _timer.Interval = TimeSpan.FromSeconds(4); _timer.Start(); };
        Opened += (_, _) => { Place(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();

        var buttons = new WrapPanel { ItemSpacing = 6, LineSpacing = 4 };
        foreach (var b in new[] { OpenButton, SnoozeButton, CloseButton }) buttons.Children.Add(b);
        var body = new StackPanel { Spacing = 6, Margin = new Thickness(14, 12) };
        body.Children.Add(KindText); body.Children.Add(Body); body.Children.Add(buttons);
        Content = new Border { BorderBrush = Res("C.Hair"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = body };
    }

    /// <summary>Bottom right of the screen's working area, above the taskbar, with a small margin.</summary>
    private void Place()
    {
        if (Screens.Primary is not { } screen) return;
        var area = screen.WorkingArea; double scale = screen.Scaling;
        // its own measured size, not Bounds, which may still be empty when it opens (round 42 review)
        Measure(new Size(Width, double.PositiveInfinity));
        var size = DesiredSize.Height > 0 ? DesiredSize : Bounds.Size;
        int w = (int)Math.Ceiling(Width * scale), h = (int)Math.Ceiling(size.Height * scale), margin = (int)(12 * scale);
        Position = new PixelPoint(Math.Max(area.X, area.Right - w - margin), Math.Max(area.Y, area.Bottom - h - margin));
    }
}
