using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
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
    private Task _briefReady = Task.CompletedTask;
    private readonly AutoHideController _autoHide;
    private DateTime _shownAt = DateTime.Now;

    /// <summary>Hide after this many idle seconds (0 = never). The app sets it from Settings; tests and the
    /// self-test leave it off so a render is never raced by a timer.</summary>
    public double AutoHideSeconds { get; set; }

    /// <summary>The federal calendar and thresholds behind the watch flags; the app sets them from Settings.</summary>
    public BusinessCalendar Calendar { get; set; } = new();
    public WatchSettings Watch { get; set; } = new();

    /// <summary>Today's date for the watch rules. Overridable so a test is never at the mercy of the real clock.</summary>
    public Func<DateOnly> Today { get; set; } = () => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>The PSG and RLD-labeling lines, if the person has set up the sources. Null or empty parts are hidden.</summary>
    public Func<AndaRecord, ReferenceInfo?>? Reference { get; set; }

    /// <summary>What the last placement decided and from which numbers. The self-test reports it.</summary>
    internal string PlacementTrace { get; private set; } = "not placed yet";
    private PixelPoint _anchor;

    /// <summary>Normal use: the card closes when the user clicks elsewhere. The self-test and
    /// screenshot paths turn this off so a stolen focus cannot close a card mid-render.</summary>
    public bool CloseWhenDeactivated { get; set; } = true;

    public CardWindow(ICompanionBriefService briefs, Func<string, AndaRecord?> lookup)
    {
        _briefs = briefs;
        _lookup = lookup;
        InitializeComponent();

        CloseBtn.Click += (_, _) => Close();
        CopyChip.Click += (_, _) => CopyStatusLine();
        PinBtn.Click += (_, _) => TogglePin();
        ExpBtn.Click += (_, _) => ToggleExpand();
        MoreBtn.Click += (_, _) => ExpandFromMini();
        SisterChip.Click += (_, _) => OpenFirstSister();
        Deactivated += (_, _) => { if (!_pinned && CloseWhenDeactivated) Close(); };
        KeyDown += OnKey;
        ApplyAppearanceStructure();

        _autoHide = new AutoHideController(() => _pinned, () => Drawer.IsVisible, () => Close());
        Root.PointerEntered += (_, _) => _autoHide.PointerInside = true;
        Root.PointerExited += (_, _) => _autoHide.PointerInside = false;
        Root.PointerMoved += (_, _) => _autoHide.Touch();
        Root.PointerPressed += (_, _) => _autoHide.Touch();
        Closed += (_, _) => _autoHide.Stop();

        // Apple-style entrance: quick fade + settle from 97% scale (no bounce, no delay).
        var scale = new ScaleTransform(0.97, 0.97)
        {
            Transitions = new Transitions
            {
                new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromMilliseconds(180), Easing = new CubicEaseOut() },
                new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = TimeSpan.FromMilliseconds(180), Easing = new CubicEaseOut() },
            },
        };
        Root.RenderTransform = scale;
        Root.RenderTransformOrigin = new RelativePoint(0.5, 0.3, RelativeUnit.Relative);
        Root.Transitions = new Transitions
        {
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(160), Easing = new CubicEaseOut() },
        };
        Root.Opacity = 0;
        Opened += async (_, _) =>
        {
            // The card is invisible (opacity 0) until now. Give an instant brief (offline mode, a cached one)
            // a moment to land so the card is placed from its final height rather than the placeholder's.
            await Task.WhenAny(_briefReady, Task.Delay(150));
            PlaceNear(_anchor);
            Root.Opacity = 1; scale.ScaleX = 1; scale.ScaleY = 1;
            _autoHide.Start(AutoHideSeconds);
        };
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        _autoHide.Touch();
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.E: ToggleExpand(); break;
            case Key.Space: TogglePin(); e.Handled = true; break;
            case Key.C when _record is not null && BriefText.Text is { Length: > 0 } text:
                Clipboard?.SetTextAsync(text); break;
            case Key.S: CopyStatusLine(); break;
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
        _autoHide.Touch();
        Root.Width = Drawer.IsVisible ? 492 : 372;
        Dispatcher.UIThread.Post(ClampToScreen, DispatcherPriority.Loaded);   // it just grew: keep it on-screen
    }

    private void ExpandFromMini()
    {
        _mini = false;
        SetMiniVisibility();
        if (_record is not null) _ = LoadBriefAsync(_record);
        Dispatcher.UIThread.Post(ClampToScreen, DispatcherPriority.Loaded);
    }

    private void CopyStatusLine()
    {
        if (_record is null) return;
        Clipboard?.SetTextAsync(Services.StatusLine.Build(_record));
        UsageLog.Record("copy_status", (DateTime.Now - _shownAt).TotalSeconds);
        CopyChip.Content = "Copied ✓";
        Avalonia.Threading.DispatcherTimer.RunOnce(
            () => CopyChip.Content = "Copy status", TimeSpan.FromSeconds(1.2));
    }

    private void OpenFirstSister()
    {
        if (_record?.Sisters.FirstOrDefault() is { } sister && _lookup(sister.Number) is { } rec)
            Show(rec, Position, _mini);
    }

    private static IBrush Br(string hex) => new SolidColorBrush(Color.Parse(hex));

    /// <summary>
    /// Structural signature bits that palettes alone can't express: seal style, tinted header, status spine,
    /// top rule, chip form, spacing. Everything comes from <see cref="Appearance.Look"/>; the anatomy and slot
    /// order are identical in every look.
    /// </summary>
    private void ApplyAppearanceStructure()
    {
        var a = ThemeManager.Current;
        var look = a.Look;
        var res = Application.Current!.Resources;

        // Verified seal
        switch (look.Seal)
        {
            case SealStyle.Quiet:
                Seal.Background = Brushes.Transparent;
                Seal.BorderBrush = (IBrush)res["C.Line"]!;
                Seal.BorderThickness = new Thickness(1);
                SealText.Foreground = (IBrush)res["C.Ink2"]!;
                break;
            case SealStyle.Tinted:
                Seal.Background = Br(look.SealFill!);
                SealText.Foreground = Br(look.SealInk!);
                break;
            default:
                if (a.StampSeal)
                {
                    Seal.Background = Brushes.Transparent;
                    Seal.BorderBrush = (IBrush)res["C.Good"]!;
                    Seal.BorderThickness = new Thickness(1.5);
                    Seal.CornerRadius = new CornerRadius(4);
                    Seal.RenderTransform = a.Basis == AppearanceId.B ? new RotateTransform(-2.5) : null;
                }
                break;
        }

        // Header band, spacing
        HeaderBand.Background = look.HeaderTint is null ? null : Br(look.HeaderTint);
        double v = look.SlotPadV, h = look.SlotPadH;
        HeaderBand.Padding = new Thickness(h, v + 4, h, v + 2);
        ClockPanel.Margin = new Thickness(h, v + 1, h, v + 2);
        AttentionBox.Margin = new Thickness(h, v, h, v);
        LatestGrid.Margin = new Thickness(h, v, h, v);
        AheadGrid.Margin = new Thickness(h, v, h, v);
        BriefPanel.Margin = new Thickness(h, v, h, v + 2);
        ChipsWrap.Margin = new Thickness(h, v, h, v + 2);

        // Spine (left, coloured per record in Show) and top rule
        Spine.IsVisible = look.StatusSpine;
        double top = 0;
        if (look.TopRule is { } rule)
        {
            TopRule.IsVisible = true;
            TopRule.Height = look.TopRuleHeight;
            if (look.TopRuleDouble)
            {
                TopRule.BorderBrush = Br(rule);
                TopRule.BorderThickness = new Thickness(0, 1.5, 0, 1.5);
            }
            else TopRule.Background = Br(rule);
            top = look.TopRuleHeight;
        }
        Body.Margin = new Thickness(look.StatusSpine ? 5 : 0, top, 0, 0);

        // Chips
        foreach (var chip in new[] { CopyChip, SisterChip, NotesChip, OpenChip })
        {
            switch (look.Chips)
            {
                case ChipStyle.Tint:
                    chip.Background = Br(look.ChipFill!);
                    chip.BorderBrush = Br(look.ChipLine!);
                    chip.Foreground = Br(look.ChipInk!);
                    break;
                case ChipStyle.Underline:
                    chip.BorderThickness = new Thickness(0, 0, 0, 1);
                    chip.CornerRadius = new CornerRadius(0);
                    chip.Padding = new Thickness(2, 6);
                    chip.MinHeight = 30;
                    break;
            }
        }
        if (look.LinkFill is { } fill)
        {
            OpenChip.Classes.Add("filled");
            OpenChip.Background = Br(fill);
            OpenChip.BorderBrush = Br(fill);
            OpenChip.Foreground = Brushes.White;
        }
    }

    private void SetMiniVisibility()
    {
        AttentionSlot.IsVisible = !_mini && _record?.Attention is not null;
        WatchSlot.IsVisible = !_mini && _watchFlags.Count > 0;
        RefSlot.IsVisible = !_mini && _refShown;
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

        NumText.Text = $"{record.Kind} {record.Number}";
        DrugText.Text = record.Drug;
        MetaText.Text = record.Meta;
        GoalText.Text = record.GoalDate;
        GoalDaysText.Text = record.GoalDays;
        bool ring = ThemeManager.Current.Progress == ProgressVariant.Ring;
        bool timeline = ThemeManager.Current.Progress == ProgressVariant.Timeline && record.DaysToGoal is not null && record.ForecastDeltaDays is not null;
        // A sheet that has no progress or forecast shows nothing there, never an estimate.
        Progress.IsVisible = record.HasProgress || timeline;
        PctText.Text = ring || !record.HasProgress ? "" : record.ProgressPct + "%";
        Progress.HorizontalAlignment = ring
            ? Avalonia.Layout.HorizontalAlignment.Right
            : Avalonia.Layout.HorizontalAlignment.Stretch;
        Progress.Value = Math.Max(0, record.ProgressPct);
        Progress.DaysToGoal = record.DaysToGoal;
        Progress.DeltaDays = record.ForecastDeltaDays;
        ForecastText.Text = record.ForecastDate;
        ForecastDeltaText.Text = record.ForecastDelta;
        ForecastDeltaText.Foreground = (IBrush)Application.Current!.Resources[
            !record.HasForecast ? "C.Ink3" : record.ForecastLate ? "C.Warn" : "C.Good"]!;
        ForecastConfText.Text = record.ForecastConfidence;
        ForecastConfText.IsVisible = record.ForecastConfidence.Length > 0;

        if (record.Attention is { } attn)
        {
            string tone = attn.Level == AttentionLevel.Bad ? "Bad" : "Warn";
            AttentionKind.Foreground = (IBrush)Application.Current.Resources[$"C.{tone}"]!;
            switch (ThemeManager.Current.Look.Attention)
            {
                case AttentionShape.Outline:
                    AttentionBox.Background = Brushes.Transparent;
                    AttentionBox.BorderBrush = AttentionKind.Foreground;
                    AttentionBox.CornerRadius = new CornerRadius(6);
                    break;
                case AttentionShape.Pill:
                    AttentionBox.Background = (IBrush)Application.Current.Resources[$"C.{tone}Bg"]!;
                    AttentionBox.CornerRadius = new CornerRadius(18);
                    AttentionBox.Padding = new Thickness(14, 8);
                    break;
                default:
                    AttentionBox.Background = (IBrush)Application.Current.Resources[$"C.{tone}Bg"]!;
                    break;
            }
            AttentionText.Foreground = AttentionKind.Foreground;
            AttentionText.Text = attn.Text;
        }
        // Status spine (G): bad attention reads red, a late forecast amber, otherwise green.
        Spine.Background = (IBrush)Application.Current.Resources[
            record.Attention?.Level == AttentionLevel.Bad ? "C.Bad" : record.ForecastLate ? "C.Warn" : "C.Good"]!;
        LatestText.Text = record.Latest;
        AheadText.Text = record.Ahead ?? "";
        ShowWatch(record);
        ShowReference(record);
        FreshnessText.Text = record.DataAsOf ?? "";
        FreshnessText.IsVisible = record.DataAsOf is not null;
        AutomationProperties.SetName(this, $"{record.Kind} {record.Number} quick brief");

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
                Text = $"{record.Kind} {s.Number} — {s.Description}", FontSize = 12.5,
                Foreground = (IBrush)Application.Current.Resources["C.Accent"]!, TextWrapping = TextWrapping.Wrap,
            });

        SisterChip.Content = $"Sister ANDA · {record.Sisters.Count}";
        SisterChip.IsVisible = record.Sisters.Count > 0;
        AutomationProperties.SetName(SisterChip, $"Open sister application, {record.Sisters.Count} related");
        NotesChip.Content = $"Notes · {record.Notes.Count}";
        AutomationProperties.SetName(NotesChip, $"Recent project notes, {record.Notes.Count} entries");

        SetMiniVisibility();
        if (!IsVisible)
        {
            _anchor = near;
            Position = near;      // provisional; Opened re-places it using the real, laid-out size
            LimitHeightTo(near);
            _briefReady = _mini ? Task.CompletedTask : LoadBriefAsync(record);
            Show();
        }
        else
        {
            // Sister-hop on an open card: new record, new brief, possibly a different height.
            _briefReady = _mini ? Task.CompletedTask : LoadBriefAsync(record);
            Dispatcher.UIThread.Post(ClampToScreen, DispatcherPriority.Loaded);
        }
        _shownAt = DateTime.Now;
        UsageLog.Record("card_open");
        Activate();
        Focus();
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
            BriefVerify.Text = brief.Generated
                ? "generated — verify before acting"
                : "assembled from the record";     // offline text is deterministic, not generated
            BriefText.Foreground = Ink2();
            BriefText.Text = brief.Text;
            // The brief is taller than the "Reading the record…" placeholder the card was placed with.
            Dispatcher.UIThread.Post(ClampToScreen, DispatcherPriority.Loaded);
        });
    }

    /// <summary>
    /// A card taller than the work area (expanded, on a 768 px laptop) must scroll rather than run off the
    /// screen. The body's max height is the work area minus the shadow margin (2 x 26) and the border.
    /// </summary>
    private void LimitHeightTo(PixelPoint at)
    {
        var screen = Screens.ScreenFromPoint(at) ?? Screens.Primary;
        if (screen is null) return;
        Body.MaxHeight = Math.Max(240, screen.WorkingArea.Height / screen.Scaling - 52 - 2 - Body.Margin.Top);
    }

    private IReadOnlyList<WatchFlag> _watchFlags = Array.Empty<WatchFlag>();
    private bool _refShown;

    /// <summary>The watch layer: the most important flag on the card, the full list with evidence in the drawer.</summary>
    private void ShowWatch(AndaRecord record)
    {
        _watchFlags = WatchRules.Evaluate(record, Today(), Calendar, Watch);
        WatchPanel.Children.Clear();
        WatchExpander.IsVisible = _watchFlags.Count > 0;
        WatchHeader.Text = $"Watch list · {_watchFlags.Count}";
        if (_watchFlags.Count == 0) { WatchSlot.IsVisible = false; return; }

        var top = _watchFlags[0];
        string tone = top.Severity switch { WatchSeverity.Bad => "C.Bad", WatchSeverity.Warn => "C.Warn", _ => "C.Ink2" };
        WatchText.Text = top.Text + (_watchFlags.Count > 1 ? $"  (+{_watchFlags.Count - 1} more in the drawer)" : "");
        WatchText.Foreground = (IBrush)Application.Current!.Resources[tone]!;
        ToolTip.SetTip(WatchText, top.Why);
        UsageLog.Record("watch_flag_shown");

        foreach (var f in _watchFlags)
        {
            var color = (IBrush)Application.Current!.Resources[f.Severity switch { WatchSeverity.Bad => "C.Bad", WatchSeverity.Warn => "C.Warn", _ => "C.Ink2" }]!;
            WatchPanel.Children.Add(new TextBlock { Text = f.Text, FontSize = 12.5, FontWeight = FontWeight.SemiBold, Foreground = color, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 1) });
            WatchPanel.Children.Add(new TextBlock { Text = f.Why, FontSize = 11.5, Foreground = Ink2(), TextWrapping = TextWrapping.Wrap });
            WatchPanel.Children.Add(new TextBlock { Text = "Source: " + f.Source, FontSize = 10.5, Foreground = Ink3() });
        }
    }

    private void ShowReference(AndaRecord record)
    {
        var info = Reference?.Invoke(record);
        _refShown = info is not null && (info.PsgText is not null || info.RldText is not null);
        PsgLabel.IsVisible = PsgText.IsVisible = info?.PsgText is not null;
        RldLabel.IsVisible = RldText.IsVisible = info?.RldText is not null;
        PsgText.Text = info?.PsgText ?? "";
        RldText.Text = info?.RldText ?? "";
        RefSlot.IsVisible = !_mini && _refShown;
    }

    private (PixelRect Area, int Width, int Height)? Geometry(PixelPoint at)
    {
        var screen = Screens.ScreenFromPoint(at) ?? Screens.Primary;
        if (screen is null) return null;
        double scale = screen.Scaling;
        return (screen.WorkingArea, (int)Math.Ceiling(Bounds.Width * scale), (int)Math.Ceiling(Bounds.Height * scale));
    }

    private void PlaceNear(PixelPoint near)
    {
        LimitHeightTo(near);
        if (Geometry(near) is not { } g) { Position = near; return; }
        Position = Placement.Near(near, g.Width, g.Height, g.Area);
        PlacementTrace = $"anchor {near.X},{near.Y}; bounds {Bounds.Width:0.#}x{Bounds.Height:0.#} dip; size {g.Width}x{g.Height}px; area {g.Area} → {Position.X},{Position.Y}";
    }

    private void ClampToScreen()
    {
        if (Geometry(Position) is not { } g) return;
        Position = Placement.Clamp(Position, g.Width, g.Height, g.Area);
    }

    private IBrush Ink2() => (IBrush)Application.Current!.Resources["C.Ink2"]!;
    private IBrush Ink3() => (IBrush)Application.Current!.Resources["C.Ink3"]!;
}
