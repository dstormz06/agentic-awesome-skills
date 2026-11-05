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

    /// <summary>The person's quick replies, filled from the open card when one is chosen. Null or empty hides the button.</summary>
    public Func<IReadOnlyList<QuickReply>>? QuickReplies { get; set; }

    /// <summary>The optional Outlook connection. Null or off hides the mail drawer; nothing is searched until the drawer is opened.</summary>
    public OutlookService? Outlook { get; set; }
    private string? _mailFor;

    /// <summary>The person's registered agents and the service that runs them. Null, or none switched on, hides the drawer section.</summary>
    public AgentService? Agents { get; set; }

    /// <summary>Whether the urgency rules call this ANDA at risk. Decides which agents the person set to run only then.</summary>
    public Func<AndaRecord, bool>? AtRisk { get; set; }

    /// <summary>Adds suggested lines from an agent to the to-do list (lines, the ANDA, the agent's name). Returns how many were new.</summary>
    public Func<IReadOnlyList<string>, AndaRecord, string, int>? AddTodos { get; set; }
    private bool _agentsWired;

    /// <summary>The PSG and RLD-labeling lines. Both are always shown: a date and its source, or a brief reason there is none.</summary>
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
        RepliesChip.Click += (_, _) => OpenReplies();
        PaintConnection();
        void OnConn() => Dispatcher.UIThread.Post(PaintConnection);
        ConnectionStatus.Changed += OnConn;
        Closed += (_, _) => ConnectionStatus.Changed -= OnConn;
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
            case Key.R: OpenReplies(); break;
        }
    }

    /// <summary>The connection pill: a filled dot only when a request has really succeeded; otherwise a hollow one and the plain state.</summary>
    private void PaintConnection()
    {
        var c = ConnectionStatus.Current;
        string brush = c.State switch { ConnectionState.Connected => "C.Good", ConnectionState.Failed => "C.Bad", ConnectionState.SetUp => "C.Warn", _ => "C.Ink3" };
        string back = c.State switch { ConnectionState.Connected => "C.GoodBg", ConnectionState.Failed => "C.BadBg", ConnectionState.SetUp => "C.WarnBg", _ => "C.Mark" };
        var fore = (IBrush)Application.Current!.Resources[brush]!;
        ConnText.Text = c.Label; ConnText.Foreground = fore;
        ConnDot.Stroke = fore; ConnDot.Fill = c.State == ConnectionState.Connected ? fore : Brushes.Transparent;
        ConnPill.Background = (IBrush)Application.Current!.Resources[back]!;
    }

    private void OpenReplies()
    {
        if (_record is null || QuickReplies?.Invoke() is not { Count: > 0 } replies) return;
        var menu = new ContextMenu();
        var items = new List<object>();
        foreach (var reply in replies)
        {
            var item = new MenuItem { Header = reply.Name };
            item.Click += (_, _) => CopyReply(reply);
            items.Add(item);
        }
        menu.ItemsSource = items;
        menu.Open(RepliesChip);
    }

    private void CopyReply(QuickReply reply)
    {
        if (_record is null) return;
        var info = Reference?.Invoke(_record);
        Clipboard?.SetTextAsync(QuickReplyFill.Fill(reply.Text, _record, Today(), info));
        UsageLog.Record("copy_reply", (DateTime.Now - _shownAt).TotalSeconds);
        RepliesChip.Content = "Copied ✓";
        Avalonia.Threading.DispatcherTimer.RunOnce(() => RepliesChip.Content = "Quick replies ▾", TimeSpan.FromSeconds(1.2));
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
        PrepareAgents();                    // the full card is stage two: the agents drawer appears now, and agents set to run by themselves are asked
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
        ApplyLayout(a, look);

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

    // ---------------- card layouts (P–S): the same blocks, arranged differently ----------------

    private TextBlock? _railDays, _railCaption;
    private string _pctSuffix = "";

    private static void Detach(Control c) { if (c.Parent is Panel p) p.Children.Remove(c); }

    private TextBlock Cap(string text) => new() { Classes = { "lbl" }, Text = text };

    private void ApplyLayout(Appearance a, Structure look)
    {
        var res = Application.Current!.Resources;
        double h = look.SlotPadH, v = look.SlotPadV;
        switch (look.Layout)
        {
            case CardLayout.Folder:
            {
                // The number sits on a bronze tab in the top-left corner; the corner itself is square.
                double r = a.Radius;
                Root.CornerRadius = new CornerRadius(0, r, r, r);
                NumTab.Background = (IBrush)res["C.Accent"]!;
                NumTab.Padding = new Thickness(14, 6);
                NumTab.CornerRadius = new CornerRadius(0, 0, 12, 0);
                NumText.Foreground = Brushes.White;
                HeaderBand.Padding = new Thickness(0, 0, h, v + 2);
                foreach (var t in new[] { DrugText, MetaText, FreshnessText })
                    t.Margin = new Thickness(h, t.Margin.Top == 0 ? 0 : t.Margin.Top + (t == DrugText ? 4 : 0), 0, 0);
                ToolsPanel.Margin = new Thickness(0, 4, 0, 0);
                break;
            }
            case CardLayout.Rail:
            {
                // A tinted column on the left carries the days to goal; goal, forecast and the attention line read down the right.
                int at = Slots.Children.IndexOf(ClockPanel);
                Slots.Children.Remove(ClockPanel);
                Slots.Children.Remove(AttentionSlot);
                GoalDaysText.IsVisible = false;
                Detach(ForecastDeltaText); Detach(ForecastConfText);
                ForecastDeltaText.TextWrapping = TextWrapping.Wrap; ForecastConfText.TextWrapping = TextWrapping.Wrap;
                ForecastConfText.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                ForecastConfText.Margin = new Thickness(0);
                _railCaption = Cap("DAYS TO GOAL"); _railCaption.TextWrapping = TextWrapping.Wrap;
                _railDays = new TextBlock { FontSize = 38, FontWeight = FontWeight.Bold, Foreground = (IBrush)res["C.Accent"]!, FontFamily = (FontFamily)res["C.NumFont"]!, LineHeight = 42 };
                var left = new StackPanel { Spacing = 5, Margin = new Thickness(12, 14, 8, 12) };
                left.Children.Add(_railDays); left.Children.Add(_railCaption); left.Children.Add(ForecastDeltaText); left.Children.Add(ForecastConfText);
                var wash = new Border { Background = Br(look.RailWash!), BorderBrush = (IBrush)res["C.Line"]!, BorderThickness = new Thickness(0, 0, 1, 0), Child = left };
                var right = new StackPanel();
                right.Children.Add(ClockPanel); right.Children.Add(AttentionSlot);
                var split = new Grid { ColumnDefinitions = new ColumnDefinitions("114,*") };
                split.Children.Add(wash); Grid.SetColumn(right, 1); split.Children.Add(right);
                Slots.Children.Insert(at, split);
                break;
            }
            case CardLayout.Tag:
            {
                // An ID tag: a small slot with the tools beside it, a centred head, goal and forecast in two columns.
                var slot = new Border { Width = 54, Height = 11, CornerRadius = new CornerRadius(99), BorderThickness = new Thickness(1.5), BorderBrush = (IBrush)res["C.Accent"]!, Background = (IBrush)res["C.Mark"]!, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), Margin = new Thickness(0, -6, 0, 2) };
                Detach(ToolsPanel);
                ToolsPanel.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
                Grid.SetColumn(slot, 1); top.Children.Add(slot);
                Grid.SetColumn(ToolsPanel, 2); top.Children.Add(ToolsPanel);
                HeaderPanel.Children.Insert(0, top);
                HeaderGrid.ColumnDefinitions = new ColumnDefinitions("*,Auto,8,Auto,*");
                Grid.SetColumn(NumTab, 1); Grid.SetColumn(Seal, 3);
                foreach (var t in new[] { DrugText, MetaText, FreshnessText })
                { t.TextAlignment = TextAlignment.Center; t.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center; }
                NumText.FontSize = 18;

                Detach(GoalText); Detach(GoalDaysText); Detach(ForecastText); Detach(ForecastDeltaText); Detach(PctText); Detach(ForecastConfText); Detach(Progress);
                ClockPanel.Children.Clear();
                TextBlock C(TextBlock t) { t.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center; t.TextAlignment = TextAlignment.Center; return t; }
                var gl = Cap("GOAL"); gl.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
                var fl = Cap("FORECAST"); fl.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
                var gs = new StackPanel { Spacing = 2 }; gs.Children.Add(gl); gs.Children.Add(C(GoalText)); gs.Children.Add(C(GoalDaysText));
                var fs = new StackPanel { Spacing = 2 }; fs.Children.Add(fl); fs.Children.Add(C(ForecastText)); fs.Children.Add(C(ForecastDeltaText));
                var two = new Grid { ColumnDefinitions = new ColumnDefinitions("*,1,*") };
                var rule = new Border { Background = (IBrush)res["C.Line"]!, Margin = new Thickness(0, 4) };
                Grid.SetColumn(rule, 1); Grid.SetColumn(fs, 2);
                two.Children.Add(gs); two.Children.Add(rule); two.Children.Add(fs);
                Progress.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch; Progress.Margin = new Thickness(0, 12, 0, 4);
                var foot = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                ForecastConfText.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right; ForecastConfText.Margin = new Thickness(0);
                Grid.SetColumn(PctText, 0); Grid.SetColumn(ForecastConfText, 1); foot.Children.Add(PctText); foot.Children.Add(ForecastConfText);
                ClockPanel.Children.Add(two); ClockPanel.Children.Add(Progress); ClockPanel.Children.Add(foot);
                _pctSuffix = " through review";
                ChipsWrap.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
                break;
            }
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
        bool ring = ThemeManager.Current.Progress is ProgressVariant.Ring or ProgressVariant.Gauge;   // the value sits inside the shape
        bool timeline = ThemeManager.Current.Progress == ProgressVariant.Timeline && record.DaysToGoal is not null && record.ForecastDeltaDays is not null;
        // A sheet that has no progress or forecast shows nothing there, never an estimate.
        Progress.IsVisible = record.HasProgress || timeline;
        PctText.Text = ring || !record.HasProgress ? "" : record.ProgressPct + "%" + _pctSuffix;
        if (_railDays is not null)
        {
            int? days = record.DaysToGoal;
            _railDays.Text = days is null ? "—" : Math.Abs(days.Value).ToString();
            _railCaption!.Text = days is < 0 ? "DAYS PAST GOAL" : "DAYS TO GOAL";
        }
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
        PrepareMail();
        PrepareAgents();
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

    // ---------------- agent findings (the person's own agents; nothing runs unless they switched it on) ----------------

    /// <summary>
    /// Shows the agent section for a full card when at least one agent is switched on. A peek and an invite never show it and never
    /// ask an agent. An agent the person set to run by itself is asked here, once, when the full card opens; the others wait for a click.
    /// </summary>
    private void PrepareAgents()
    {
        AgentsPanel.Children.Clear();
        AgentsExpander.IsExpanded = false;
        AgentsExpander.IsVisible = false;
        if (Agents is not { } service || _record is not { } record || _mini) return;
        if (!service.Agents.Any(a => a.Enabled)) return;
        AgentsExpander.IsVisible = true;
        if (!_agentsWired)
        {
            _agentsWired = true;
            void OnChanged() => Dispatcher.UIThread.Post(PaintAgents);
            service.Changed += OnChanged;
            Closed += (_, _) => service.Changed -= OnChanged;
        }
        PaintAgents();
        bool risk = AtRisk?.Invoke(record) ?? false;
        foreach (var agent in service.AutoFor(risk)) _ = AskAgentAsync(agent, record, force: false);
    }

    private async Task AskAgentAsync(AgentSettings agent, AndaRecord record, bool force)
    {
        if (Agents is not { } service) return;
        try { await service.RunAsync(agent, record, Today(), Reference?.Invoke(record), null, force); }
        catch (Exception) { /* the service reports its own failures as a finding */ }
    }

    private void PaintAgents()
    {
        if (Agents is not { } service || _record is not { } record || !AgentsExpander.IsVisible) return;
        AgentsPanel.Children.Clear();
        int answered = 0;
        foreach (var agent in service.Agents.Where(a => a.Enabled))
        {
            var last = service.Latest(agent.Id, record.Number);
            bool running = service.IsRunning(agent.Id, record.Number);
            string? problem = service.SetupProblem(agent);
            if (last is { State: AgentState.Done }) answered++;

            var head = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(0, 6, 0, 0) };
            head.Children.Add(new TextBlock { Text = agent.Name, FontSize = 12.5, FontWeight = FontWeight.SemiBold, Foreground = (IBrush)Application.Current!.Resources["C.Ink"]!, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            var run = new Button { Content = running ? "Asking…" : last is { State: AgentState.Done } ? "Ask again" : last is { State: AgentState.Failed } ? "Try again" : "Run", IsEnabled = !running && problem is null, Padding = new Thickness(10, 2), FontSize = 11.5 };
            AutomationProperties.SetName(run, $"{(last is null ? "Run" : "Ask again")} {agent.Name} for ANDA {record.Number}");
            var captured = agent;
            run.Click += (_, _) => _ = AskAgentAsync(captured, record, force: true);
            Grid.SetColumn(run, 1); head.Children.Add(run);
            AgentsPanel.Children.Add(head);

            string status = problem is not null ? $"Not set up. {problem} Please open Settings, then My agents."
                : running ? $"Asking {agent.Name}… only what its prompt names is sent."
                : last is null ? $"Not asked yet. Press Run to ask {agent.Name} about this ANDA, or set it to run by itself in Settings."
                : last.State == AgentState.Done ? last.Text
                : last.Message;
            AgentsPanel.Children.Add(MailLine(status, brush: last?.State == AgentState.Failed ? (IBrush)Application.Current!.Resources["C.Bad"]! : problem is not null || running || last is null ? Ink3() : Ink2()));

            if (last is { State: AgentState.Done } done && AddTodos is { } add)
                foreach (var item in done.Items.Take(AgentRules.MaxItems))
                {
                    var row = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(0, 2, 0, 0) };
                    row.Children.Add(new TextBlock { Text = "• " + item, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Ink2() });
                    var btn = new Button { Content = "Add to to-do", Padding = new Thickness(8, 1), FontSize = 11, Margin = new Thickness(8, 0, 0, 0) };
                    AutomationProperties.SetName(btn, "Add to my to-do list: " + item);
                    btn.Click += (_, _) => { int n = add(new[] { item }, record, agent.Name); btn.Content = n > 0 ? "Added ✓" : "On your list"; btn.IsEnabled = false; };
                    Grid.SetColumn(btn, 1); row.Children.Add(btn);
                    AgentsPanel.Children.Add(row);
                }
            if (last is { State: AgentState.Done } d2)
                AgentsPanel.Children.Add(MailLine($"{d2.Message} Asked at {d2.At:h:mm tt}.", 10.5, brush: Ink3(), margin: new Thickness(0, 4, 0, 0)));
        }
        AgentsPanel.Children.Add(MailLine("These are the agents' own words, shown as received, not facts from the record. They are never run or passed on, and nothing is added to your to-do list until you press its button.", 10.5, brush: Ink3(), margin: new Thickness(0, 8, 0, 0)));
        AgentsHeader.Text = answered > 0 ? $"Agent findings · {answered}" : "Agent findings";
        Dispatcher.UIThread.Post(ClampToScreen, DispatcherPriority.Loaded);
    }

    // ---------------- Outlook (optional, read-only) ----------------

    private bool _mailWired;

    /// <summary>Shows or hides the mail drawer for the card that is open now. Nothing is searched until the person opens the drawer.</summary>
    private void PrepareMail()
    {
        _mailFor = null;
        MailPanel.Children.Clear();
        MailExpander.IsExpanded = false;
        MailExpander.IsVisible = Outlook is { } o && o.Snapshot.State != OutlookState.Off;
        if (_mailWired) return;
        _mailWired = true;
        MailExpander.PropertyChanged += (_, e) =>
        {
            if (e.Property == Expander.IsExpandedProperty && MailExpander.IsExpanded && _record is { } r && _mailFor != r.Number) _ = LoadMailAsync(r);
        };
    }

    private TextBlock MailLine(string text, double size = 12.5, bool strong = false, IBrush? brush = null, Thickness? margin = null) => new()
    {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = brush ?? Ink2(),
        FontWeight = strong ? FontWeight.SemiBold : FontWeight.Normal, Margin = margin ?? new Thickness(0),
    };

    private async Task LoadMailAsync(AndaRecord record)
    {
        if (Outlook is not { } service) return;
        _mailFor = record.Number;
        MailPanel.Children.Clear();
        var snap = service.Snapshot;
        if (snap.State != OutlookState.SignedIn)
        {
            _mailFor = null;     // try again when the drawer is next opened, after the person has signed in
            MailPanel.Children.Add(MailLine(snap.State switch
            {
                OutlookState.SigningIn => snap.Message,
                OutlookState.Error => snap.Message + " Please open Settings, then Outlook.",
                _ => "Not signed in to Outlook. Please open Settings, then Outlook, to sign in.",
            }, brush: Ink3()));
            return;
        }
        MailPanel.Children.Add(MailLine("Looking in Outlook…", brush: Ink3()));
        MailResult result;
        try { result = await service.SearchAsync(record.Number); }
        catch (OperationCanceledException) { return; }
        if (_record?.Number != record.Number) return;      // the card moved on while Outlook answered
        MailPanel.Children.Clear();
        MailHeader.Text = result.Ok && result.Items.Count > 0 ? $"Recent Outlook mail · {result.Items.Count}" : "Recent Outlook mail";
        if (!result.Ok || result.Items.Count == 0)
        {
            _mailFor = result.Ok ? record.Number : null;
            MailPanel.Children.Add(MailLine(result.Message, brush: Ink3()));
            return;
        }
        foreach (var m in result.Items)
        {
            MailPanel.Children.Add(MailLine(m.Subject, strong: true, margin: new Thickness(0, 6, 0, 0)));
            MailPanel.Children.Add(MailLine(string.Join(" · ", new[] { m.From, m.Received?.LocalDateTime.ToString("MMM d, yyyy") ?? "" }.Where(x => x.Length > 0)), 11, brush: Ink3()));
            if (m.Preview.Length > 0) MailPanel.Children.Add(MailLine(m.Preview));
        }
        MailPanel.Children.Add(MailLine("Read from your Outlook for this ANDA number only. Not saved, not sent to any AI service.", 10.5, brush: Ink3(), margin: new Thickness(0, 8, 0, 0)));
        Dispatcher.UIThread.Post(ClampToScreen, DispatcherPriority.Loaded);
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
        // Always two lines: each date with its source, or a brief reason there is none.
        var info = Reference?.Invoke(record) ?? ReferenceResolver.For(record, null, "", null, loading: false);
        _refShown = true;
        PsgText.Text = info.PsgText ?? ReferenceResolver.NoPsg;
        RldText.Text = info.RldText ?? ReferenceResolver.NoRld;
        RefSlot.IsVisible = !_mini;
        RepliesChip.IsVisible = QuickReplies?.Invoke() is { Count: > 0 };
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
