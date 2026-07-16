using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace AndaCompanion.UI;

/// <summary>
/// Consent stage one: a small transient invite (whisper model) or a one-line
/// notice (invalid number). Self-fades if ignored — a card never opens from it
/// unless the user clicks.
/// </summary>
public partial class ChipWindow : Window
{
    private readonly DispatcherTimer _fade = new();

    public ChipWindow()
    {
        InitializeComponent();
        _fade.Tick += (_, _) => Close();
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
        Open(near, TimeSpan.FromSeconds(1.8));
    }

    private void Open(PixelPoint near, TimeSpan lifetime)
    {
        Position = new PixelPoint(near.X + 8, near.Y + 12);
        _fade.Interval = lifetime;
        _fade.Start();
        Show();
    }
}
