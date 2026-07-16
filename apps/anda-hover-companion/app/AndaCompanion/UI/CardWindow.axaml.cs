using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AndaCompanion.Models;
using AndaCompanion.Services;
using AndaCompanion.Theming;

namespace AndaCompanion.UI;

public partial class CardWindow : Window
{
    private readonly ICompanionBriefService _briefs;
    private readonly Func<string, AndaRecord?> _lookup;
    private AndaRecord? _record;
    private bool _pinned;
    private bool _mini;
    private CancellationTokenSource? _briefCts;

    public CardWindow(ICompanionBriefService briefs, Func<string, AndaRecord?> lookup)
    {
        _briefs = briefs;
        _lookup = lookup;
        InitializeComponent();

        CloseBtn.Click += (_, _) => Close();
        PinBtn.Click += (_, _) => TogglePin();
        ExpBtn.Click += (_, _) => ToggleExpand();
        MoreBtn.Click += (_, _) => ExpandFromMini();
        SisterChip.Click += (_, _) => OpenFirstSister();
        Deactivated += (_, _) => { if (!_pinned) Close(); };
        KeyDown += OnKey;
        ApplyAppearanceStructure();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.E: ToggleExpand(); break;
            case Key.Space: TogglePin(); e.Handled = true; break;
            case Key.C when _record is not null && BriefText.Text is { Length: > 0 } text:
                Clipboard?.SetTextAsync(text); break;
        }
    }

    private void TogglePin()
    {
        _pinned = !_pinned;
        Root.BorderBrush = _pinned
            ? (IBrush)Application.Current!.Resources["C.Accent"]!
            : (IBrush)Application.Current!.Resources["C.Line"]!;
        Root.BorderThickness = new Thickness(_pinned ? 2 : 1);
    }

    private void ToggleExpand()
    {
        Drawer.IsVisible = !Drawer.IsVisible;
        Root.Width = Drawer.IsVisible ? 492 : 372;
    }

    private void ExpandFromMini()
    {
        _mini = false;
        SetMiniVisibility();
        if (_record is not null) _ = LoadBriefAsync(_record);
    }

    private void OpenFirstSister()
    {
        if (_record?.Sisters.FirstOrDefault() is { } sister && _lookup(sister.Number) is { } rec)
            Show(rec, Position, _mini);
    }

    /// <summary>Structural signature bits that palettes alone can't express (stamp seal).</summary>
    private void ApplyAppearanceStructure()
    {
        var a = ThemeManager.Current;
        if (a.StampSeal)
        {
            Seal.Background = Brushes.Transparent;
            Seal.BorderBrush = (IBrush)Application.Current!.Resources["C.Good"]!;
            Seal.BorderThickness = new Thickness(1.5);
            Seal.CornerRadius = new CornerRadius(4);
            Seal.RenderTransform = a.Id == AppearanceId.B ? new RotateTransform(-2.5) : null;
        }
    }

    private void SetMiniVisibility()
    {
        AttentionSlot.IsVisible = !_mini && _record?.Attention is not null;
        LatestSlot.IsVisible = !_mini;
        AheadSlot.IsVisible = !_mini && _record?.Ahead is not null;
        BriefSlot.IsVisible = !_mini;
        ChipsSlot.IsVisible = !_mini;
        MoreBtn.IsVisible = _mini;
        if (_mini) { Drawer.IsVisible = false; Root.Width = 372; }
    }

    public void Show(AndaRecord record, PixelPoint near, bool mini)
    {
        _record = record;
        _mini = mini;

        NumText.Text = "ANDA " + record.Number;
        DrugText.Text = record.Drug;
        MetaText.Text = record.Meta;
        GoalText.Text = record.GoalDate;
        GoalDaysText.Text = record.GoalDays;
        PctText.Text = ThemeManager.Current.Progress == ProgressVariant.Ring ? "" : record.ProgressPct + "%";
        Progress.Value = record.ProgressPct;
        ForecastText.Text = record.ForecastDate;
        ForecastDeltaText.Text = record.ForecastDelta;
        ForecastDeltaText.Foreground = (IBrush)Application.Current!.Resources[record.ForecastLate ? "C.Warn" : "C.Good"]!;
        ForecastConfText.Text = record.ForecastConfidence;

        if (record.Attention is { } attn)
        {
            string tone = attn.Level == AttentionLevel.Bad ? "Bad" : "Warn";
            AttentionBox.Background = (IBrush)Application.Current.Resources[$"C.{tone}Bg"]!;
            AttentionKind.Foreground = (IBrush)Application.Current.Resources[$"C.{tone}"]!;
            AttentionText.Foreground = AttentionKind.Foreground;
            AttentionText.Text = attn.Text;
        }
        LatestText.Text = record.Latest;
        AheadText.Text = record.Ahead ?? "";

        // expanded drawer content
        PendingHeader.Text = $"Pending & urgent items · {(record.Pending.Count == 0 ? "none" : record.Pending.Count.ToString())}";
        PendingPanel.Children.Clear();
        foreach (var p in record.Pending)
        {
            var row = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(0, 3) };
            row.Children.Add(new TextBlock { Text = p.Title, FontSize = 12.5, Foreground = Ink2(), TextWrapping = TextWrapping.Wrap });
            var due = new TextBlock
            {
                Text = p.Due, FontSize = 11.5,
                Foreground = p.Urgent ? (IBrush)Application.Current.Resources["C.Warn"]! : Ink3(),
                FontWeight = p.Urgent ? FontWeight.SemiBold : FontWeight.Normal,
            };
            Grid.SetColumn(due, 1);
            row.Children.Add(due);
            PendingPanel.Children.Add(row);
        }
        if (record.Pending.Count == 0)
            PendingPanel.Children.Add(new TextBlock { Text = "No open items.", FontSize = 12.5, Foreground = Ink2() });

        NotesPanel.Children.Clear();
        foreach (var n in record.Notes)
        {
            NotesPanel.Children.Add(new TextBlock { Text = n.Who, FontSize = 11, Foreground = Ink3(), Margin = new Thickness(0, 5, 0, 1) });
            NotesPanel.Children.Add(new TextBlock { Text = n.Text, FontSize = 12.5, Foreground = Ink2(), TextWrapping = TextWrapping.Wrap });
        }
        ForecastDetailText.Text = record.ForecastDetail;

        SistersPanel.Children.Clear();
        foreach (var s in record.Sisters)
            SistersPanel.Children.Add(new TextBlock
            {
                Text = $"ANDA {s.Number} — {s.Description}", FontSize = 12.5,
                Foreground = (IBrush)Application.Current.Resources["C.Accent"]!, TextWrapping = TextWrapping.Wrap,
            });

        SisterChip.Content = $"Sister ANDA · {record.Sisters.Count}";
        SisterChip.IsVisible = record.Sisters.Count > 0;
        NotesChip.Content = $"Notes · {record.Notes.Count}";

        SetMiniVisibility();
        if (!IsVisible)
        {
            PositionNear(near);   // position once; sister-hops keep the card where it is
            Show();
        }
        Activate();
        Focus();
        if (!_mini) _ = LoadBriefAsync(record);
    }

    private async Task LoadBriefAsync(AndaRecord record)
    {
        _briefCts?.Cancel();
        var cts = _briefCts = new CancellationTokenSource();
        BriefText.Foreground = Ink3();
        BriefText.Text = "Reading the record…";
        var brief = await _briefs.GetBriefAsync(record, cts.Token);
        if (cts.IsCancellationRequested) return;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            BriefTag.Text = brief.Generated ? "COMPANION" : "OFFLINE BRIEF";
            BriefText.Foreground = Ink2();
            BriefText.Text = brief.Text;
        });
    }

    private void PositionNear(PixelPoint p)
    {
        var screen = Screens.ScreenFromPoint(p) ?? Screens.Primary;
        int x = p.X + 10, y = p.Y + 14;
        if (screen is not null)
        {
            var area = screen.WorkingArea;
            x = Math.Min(x, area.Right - 430);
            y = Math.Min(y, area.Bottom - 420);
            x = Math.Max(x, area.X);
            y = Math.Max(y, area.Y);
        }
        Position = new PixelPoint(x, y);
    }

    private IBrush Ink2() => (IBrush)Application.Current!.Resources["C.Ink2"]!;
    private IBrush Ink3() => (IBrush)Application.Current!.Resources["C.Ink3"]!;
}
