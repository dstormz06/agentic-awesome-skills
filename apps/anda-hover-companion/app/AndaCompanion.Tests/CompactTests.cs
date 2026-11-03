using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AndaCompanion.Data;
using AndaCompanion.Services;
using AndaCompanion.Theming;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

public class CompactTextTests
{
    [Theory]
    [InlineData("▲ +14 d vs goal", "▲ +14 d")]
    [InlineData("▼ 9 d ahead of goal", "▼ 9 d")]
    [InlineData("on goal", "on goal")]
    [InlineData("", "")]
    public void ShortDelta_keeps_the_arrow_and_the_number(string input, string expected) =>
        Assert.Equal(expected, CompactText.ShortDelta(input));

    [Fact]
    public void Years_are_dropped_only_when_both_dates_share_one()
    {
        Assert.Equal(("Oct 3", "Oct 17"), CompactText.Pair("Oct 3, 2026", "Oct 17, 2026"));
        // a forecast that slips into the next year must never lose its year
        Assert.Equal(("Dec 20, 2026", "Jan 9, 2027"), CompactText.Pair("Dec 20, 2026", "Jan 9, 2027"));
        Assert.Equal(("TBD", "Oct 17, 2026"), CompactText.Pair("TBD", "Oct 17, 2026"));
    }

    [Fact]
    public void Timeline_positions_are_fractions_ordered_by_the_forecast_delta()
    {
        var (goal, fc) = ProgressControl.TimelinePositions(80, 14);
        Assert.InRange(goal, 0, 1); Assert.InRange(fc, 0, 1);
        Assert.True(fc > goal, "a late forecast sits to the right of the goal");
        Assert.True(fc <= 1 / 1.08 + 1e-9, "the later of the two leaves a 8% margin");

        var (g2, f2) = ProgressControl.TimelinePositions(31, -9);
        Assert.True(f2 < g2, "an early forecast sits to the left of the goal");
    }

    [Fact]
    public void Timeline_never_leaves_the_track_even_for_an_overdue_goal()
    {
        var (g, f) = ProgressControl.TimelinePositions(-5, 3);      // goal date already passed
        Assert.InRange(g, 0, 1); Assert.InRange(f, 0, 1);
        var (g0, f0) = ProgressControl.TimelinePositions(0, 0);
        Assert.InRange(g0, 0, 1); Assert.InRange(f0, 0, 1);
    }
}

/// <summary>The compact boxes and the structural signatures of the new full looks, rendered for real in the headless host.</summary>
[Collection("Global state")]
public class CompactRenderTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    private static readonly MockAndaRepository Repo = new();

    public CompactRenderTests(HeadlessFixture fx) => _fx = fx;

    public static IEnumerable<object[]> CompactIds() =>
        Appearances.All.Where(a => a.IsCompact).Select(a => new object[] { a.Id });

    [Theory, MemberData(nameof(CompactIds))]
    public async Task A_compact_box_is_a_fraction_of_the_full_card_and_shows_the_facts(AppearanceId id)
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.K.Id);
            var full = new CardWindow(new OfflineBriefService(), Repo.Find) { CloseWhenDeactivated = false };
            full.Show(Repo.Find("216482")!, new PixelPoint(40, 40), false);
            Dispatcher.UIThread.RunJobs();
            double fullArea = full.FindControl<Border>("Root")!.Bounds.Width * full.FindControl<Border>("Root")!.Bounds.Height;
            full.Close();

            ThemeManager.Apply(id);
            var box = new CompactCardWindow(Repo.Find("216482")!, () => { }) { CloseWhenDeactivated = false };
            box.ShowNear(new PixelPoint(40, 40));
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var frame = box.CaptureRenderedFrame();
            var texts = box.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
            var content = ((Panel)box.Content!).Children[0];
            var size = (content.Bounds.Width, content.Bounds.Height);
            box.Close();
            return (frame, texts, size, fullArea);
        }, CancellationToken.None);

        Assert.NotNull(r.frame);
        double area = r.size.Width * r.size.Height;
        Assert.True(area < r.fullArea * 0.35, $"{id}: {r.size.Width:0}x{r.size.Height:0} is not much smaller than the full card");
        Assert.True(r.size.Width <= 340 && r.size.Height <= 160, $"{id}: {r.size.Width:0}x{r.size.Height:0} is bigger than a compact box should be");
        Assert.Contains(r.texts, t => t.Contains("216482"));
        Assert.Contains(r.texts, t => t.Contains("+14"));                          // the forecast gap is always visible
    }

    [Fact]
    public async Task The_compact_boxes_have_the_sizes_the_tagline_promises()
    {
        var sizes = await _fx.Session.Dispatch(() =>
        {
            var result = new Dictionary<AppearanceId, (double W, double H)>();
            foreach (var id in new[] { AppearanceId.L, AppearanceId.M, AppearanceId.N, AppearanceId.O })
            {
                ThemeManager.Apply(id);
                var box = new CompactCardWindow(Repo.Find("216482")!, () => { }) { CloseWhenDeactivated = false };
                box.ShowNear(new PixelPoint(40, 40));
                Dispatcher.UIThread.RunJobs();
                var c = ((Panel)box.Content!).Children[0];
                result[id] = (c.Bounds.Width, c.Bounds.Height);
                box.Close();
            }
            return result;
        }, CancellationToken.None);

        // The Appearance taglines quote these sizes; if a layout changes, this test says the tagline is stale.
        Assert.InRange(sizes[AppearanceId.L].W, 330, 345); Assert.InRange(sizes[AppearanceId.L].H, 60, 90);
        Assert.InRange(sizes[AppearanceId.M].W, 185, 205); Assert.InRange(sizes[AppearanceId.M].H, 115, 160);
        Assert.InRange(sizes[AppearanceId.N].W, 285, 310); Assert.InRange(sizes[AppearanceId.N].H, 50, 80);
        Assert.InRange(sizes[AppearanceId.O].W, 140, 190); Assert.InRange(sizes[AppearanceId.O].H, 28, 44);
    }

    [Fact]
    public async Task Clicking_the_box_expands_it_once_but_clicking_the_close_button_only_closes()
    {
        var (expandedByClick, expandedByClose, closed) = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.L);
            int byClick = 0, byClose = 0;
            var box = new CompactCardWindow(Repo.Find("216482")!, () => byClick++) { CloseWhenDeactivated = false };
            box.ShowNear(new PixelPoint(40, 40));
            Dispatcher.UIThread.RunJobs();
            var root = ((Panel)box.Content!).Children[0];
            var p = root.TranslatePoint(new Point(root.Bounds.Width / 2, root.Bounds.Height - 6), box)!.Value;
            box.MouseDown(p, MouseButton.Left); box.MouseUp(p, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            box.Close();

            var box2 = new CompactCardWindow(Repo.Find("216482")!, () => byClose++) { CloseWhenDeactivated = false };
            box2.ShowNear(new PixelPoint(40, 40));
            Dispatcher.UIThread.RunJobs();
            var x = box2.GetVisualDescendants().OfType<Button>().Single();
            var q = x.TranslatePoint(new Point(x.Bounds.Width / 2, x.Bounds.Height / 2), box2)!.Value;
            box2.MouseDown(q, MouseButton.Left); box2.MouseUp(q, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            bool closedNow = !box2.IsVisible;
            if (box2.IsVisible) box2.Close();
            return (byClick, byClose, closedNow);
        }, CancellationToken.None);

        Assert.Equal(1, expandedByClick);
        Assert.Equal(0, expandedByClose);
        Assert.True(closed);
    }

    [Fact]
    public async Task A_compact_box_never_takes_focus_from_the_email_being_written()
    {
        var (showActivated, activeAfterShow) = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.L);
            var box = new CompactCardWindow(Repo.Find("216482")!, () => { }) { CloseWhenDeactivated = false };
            box.ShowNear(new PixelPoint(40, 40));
            Dispatcher.UIThread.RunJobs();
            var r = (box.ShowActivated, box.IsActive);
            box.Close();
            return r;
        }, CancellationToken.None);
        Assert.False(showActivated);
        Assert.False(activeAfterShow);
    }

    [Fact]
    public async Task Enter_expands_and_Escape_closes()
    {
        var (enter, escape) = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.O);
            int n = 0;
            var box = new CompactCardWindow(Repo.Find("214990")!, () => n++) { CloseWhenDeactivated = false };
            box.ShowNear(new PixelPoint(40, 40));
            Dispatcher.UIThread.RunJobs();
            box.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
            box.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();
            return (n, !box.IsVisible);
        }, CancellationToken.None);
        Assert.Equal(1, enter);
        Assert.True(escape);
    }

    [Fact]
    public async Task An_idle_compact_box_hides_itself_when_auto_hide_is_on()
    {
        bool gone = await _fx.Session.Dispatch(async () =>
        {
            ThemeManager.Apply(AppearanceId.M);
            var box = new CompactCardWindow(Repo.Find("216482")!, () => { }) { CloseWhenDeactivated = false, AutoHideSeconds = 0.3 };
            box.ShowNear(new PixelPoint(40, 40));
            for (int i = 0; i < 40 && box.IsVisible; i++) { await Task.Delay(100); Dispatcher.UIThread.RunJobs(); }
            return !box.IsVisible;
        }, CancellationToken.None);
        Assert.True(gone);
    }

    [Fact]
    public async Task Auto_hide_closes_an_idle_full_card_and_leaves_a_pinned_one_alone()
    {
        var (idleGone, pinnedKept) = await _fx.Session.Dispatch(async () =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var idle = new CardWindow(new OfflineBriefService(), Repo.Find) { CloseWhenDeactivated = false, AutoHideSeconds = 0.3 };
            idle.Show(Repo.Find("216482")!, new PixelPoint(40, 40), false);
            var pinned = new CardWindow(new OfflineBriefService(), Repo.Find) { CloseWhenDeactivated = false, AutoHideSeconds = 0.3 };
            pinned.Show(Repo.Find("216482")!, new PixelPoint(40, 40), false);
            pinned.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");        // pin
            for (int i = 0; i < 40 && idle.IsVisible; i++) { await Task.Delay(100); Dispatcher.UIThread.RunJobs(); }
            bool a = !idle.IsVisible, b = pinned.IsVisible;
            if (pinned.IsVisible) pinned.Close();
            if (idle.IsVisible) idle.Close();
            return (a, b);
        }, CancellationToken.None);
        Assert.True(idleGone);
        Assert.True(pinnedKept);
    }

    // ---------- structural signatures of the new full looks ----------

    private async Task<T> Inspect<T>(AppearanceId id, string number, Func<CardWindow, T> read) =>
        await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(id);
            var w = new CardWindow(new OfflineBriefService(), Repo.Find) { CloseWhenDeactivated = false };
            w.Show(Repo.Find(number)!, new PixelPoint(40, 40), false);
            Dispatcher.UIThread.RunJobs();
            var value = read(w);
            w.Close();
            return value;
        }, CancellationToken.None);

    private static Color Colour(IBrush? b) => ((ISolidColorBrush)b!).Color;

    [Fact]
    public async Task G_shows_a_status_spine_that_follows_the_record()
    {
        var late = await Inspect(AppearanceId.G, "216482", w => (w.FindControl<Border>("Spine")!.IsVisible, Colour(w.FindControl<Border>("Spine")!.Background)));
        var ok = await Inspect(AppearanceId.G, "214990", w => Colour(w.FindControl<Border>("Spine")!.Background));
        Assert.True(late.Item1);
        Assert.Equal(Color.Parse(Appearances.G.Palette.Warn), late.Item2);
        Assert.Equal(Color.Parse(Appearances.G.Palette.Good), ok);
        Assert.False(await Inspect(AppearanceId.K, "216482", w => w.FindControl<Border>("Spine")!.IsVisible));
    }

    [Fact]
    public async Task I_and_J_carry_their_top_rules_and_others_carry_none()
    {
        var i = await Inspect(AppearanceId.I, "216482", w => (w.FindControl<Border>("TopRule")!.IsVisible, w.FindControl<Border>("TopRule")!.Height));
        var j = await Inspect(AppearanceId.J, "216482", w => (w.FindControl<Border>("TopRule")!.IsVisible, Colour(w.FindControl<Border>("TopRule")!.Background)));
        Assert.True(i.Item1); Assert.Equal(5, i.Item2);
        Assert.True(j.Item1); Assert.Equal(Color.Parse("#C99A1D"), j.Item2);
        Assert.False(await Inspect(AppearanceId.K, "216482", w => w.FindControl<Border>("TopRule")!.IsVisible));
    }

    [Fact]
    public async Task F_fills_the_open_chip_under_a_bronze_band_with_a_plain_header_and_H_tints_its_band()
    {
        var f = await Inspect(AppearanceId.F, "216482", w => (w.FindControl<Button>("OpenChip")!.Classes.Contains("filled"), Colour(w.FindControl<Button>("OpenChip")!.Background),
            w.FindControl<Border>("HeaderBand")!.Background is null, w.FindControl<Border>("TopRule")!.IsVisible, w.FindControl<Border>("TopRule")!.Height, Colour(w.FindControl<Border>("TopRule")!.Background)));
        var h = await Inspect(AppearanceId.H, "216482", w => Colour(w.FindControl<Border>("HeaderBand")!.Background));
        Assert.True(f.Item1);
        Assert.Equal(Color.Parse("#7A5A2E"), f.Item2);
        Assert.True(f.Item3);                                           // no tint behind the header
        Assert.True(f.Item4);
        Assert.Equal(5, f.Item5);                                       // a solid 5 px bronze band across the top
        Assert.Equal(Color.Parse("#7A5A2E"), f.Item6);
        Assert.Equal(Color.Parse("#EEF4F0"), h);
    }

    [Fact]
    public async Task K_is_monochrome_with_an_outlined_attention_underlined_chips_and_larger_dates()
    {
        var k = await Inspect(AppearanceId.K, "216482", w =>
        {
            var box = w.FindControl<Border>("AttentionBox")!;
            var chip = w.FindControl<Button>("CopyChip")!;
            return (Colour(box.Background).A, Colour(box.BorderBrush), chip.BorderThickness, w.FindControl<TextBlock>("GoalText")!.FontSize);
        });
        Assert.Equal(0, k.A);                                           // transparent: outline, not tint
        Assert.Equal(Color.Parse(Appearances.K.Palette.Warn), k.Item2);
        Assert.Equal(new Thickness(0, 0, 0, 1), k.Item3);
        Assert.Equal(15, k.Item4);
    }

    [Fact]
    public async Task J_draws_a_timeline_taller_than_a_bar_and_falls_back_to_a_bar_without_numbers()
    {
        var (withNumbers, without) = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.J);
            var a = new ProgressControl { Value = 62, DaysToGoal = 80, DeltaDays = 14 };
            a.Measure(new Size(300, 100));
            var b = new ProgressControl { Value = 62 };
            b.Measure(new Size(300, 100));
            return (a.DesiredSize.Height, b.DesiredSize.Height);
        }, CancellationToken.None);
        Assert.True(withNumbers >= 40);
        Assert.Equal(4, without);
    }

    [Theory]
    [InlineData(AppearanceId.H, 9)]
    [InlineData(AppearanceId.I, 14)]
    [InlineData(AppearanceId.K, 10)]
    public async Task The_new_progress_forms_have_their_heights(AppearanceId id, double height)
    {
        double h = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(id);
            var p = new ProgressControl { Value = 62 };
            p.Measure(new Size(300, 100));
            return p.DesiredSize.Height;
        }, CancellationToken.None);
        Assert.Equal(height, h);
    }
}
