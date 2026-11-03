using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AndaCompanion.Data;
using AndaCompanion.Models;
using AndaCompanion.Services;
using AndaCompanion.Theming;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>Round 12: no look draws a pattern, and the four new Bronze looks rearrange the card without losing a fact.</summary>
[Collection("Global state")]
public class Round12Tests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public Round12Tests(HeadlessFixture fx) => _fx = fx;
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static AndaRecord Demo => new MockAndaRepository().Find("216482")!;

    [Fact]
    public void No_look_has_a_grid_hatch_or_texture_in_code()
    {
        Assert.DoesNotContain(Enum.GetNames<ProgressVariant>(), n => n.Contains("Hatch", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(Palette).GetProperties(), p => p.Name.Contains("Grid", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(CardWindow).Assembly.GetTypes(), t => t.Name.Contains("GridSurface"));
        Assert.Equal(ProgressVariant.Solid, Appearances.D.Progress);
        Assert.Null(Appearances.D.Look.HeaderTint);
    }

    [Fact]
    public void Agency_Bronze_has_a_solid_band_and_a_plain_header()
    {
        var f = Appearances.F.Look;
        Assert.Equal(Appearances.F.Palette.Accent, f.TopRule);
        Assert.Equal(5, f.TopRuleHeight);
        Assert.Null(f.HeaderTint);
    }

    private async Task<T> Inspect<T>(AppearanceId id, Func<CardWindow, T> read)
    {
        return await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(id);
            var w = new CardWindow(new OfflineBriefService(), new MockAndaRepository().Find) { CloseWhenDeactivated = false, Today = () => new DateOnly(2026, 11, 2) };
            w.Show(Demo, new PixelPoint(40, 40), false); Settle();
            var r = read(w);
            w.Close(); Settle();
            ThemeManager.Apply(Appearances.Default);
            return r;
        }, CancellationToken.None);
    }

    private static List<string> Texts(CardWindow w) => w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text ?? "").ToList();

    [Fact]
    public async Task Every_new_look_still_shows_every_fact_the_standard_card_shows()
    {
        foreach (var id in new[] { AppearanceId.P, AppearanceId.Q, AppearanceId.R, AppearanceId.S })
        {
            var t = await Inspect(id, Texts);
            string all = string.Join("\n", t);
            foreach (var must in new[] { "216482", "Metformin", "Oct 3, 2026", "Oct 17, 2026", "NEEDS YOU", "WATCH", "LATEST", "AHEAD", "PSG", "RLD LABEL", "OFFLINE", "Copy status" })
                Assert.True(all.Contains(must, StringComparison.OrdinalIgnoreCase), $"{id} is missing {must}");
            Assert.Contains(t, x => x.Contains("14 d"));            // the forecast gap, wherever the look puts it
            Assert.Contains(t, x => x.Contains("confidence"));
        }
    }

    [Fact]
    public async Task Gauge_puts_the_value_inside_a_half_dial()
    {
        var r = await Inspect(AppearanceId.P, w => (w.FindControl<ProgressControl>("Progress")!.Bounds.Height, w.FindControl<TextBlock>("PctText")!.Text));
        Assert.InRange(r.Height, 55, 70);
        Assert.Equal("", r.Text);                                    // no second percentage next to the dial
    }

    [Fact]
    public async Task Folder_has_a_bronze_tab_and_a_square_top_left_corner()
    {
        var r = await Inspect(AppearanceId.Q, w => (w.FindControl<Border>("NumTab")!.Background, w.FindControl<Border>("Root")!.CornerRadius));
        Assert.NotNull(r.Item1);
        Assert.Equal(0, r.Item2.TopLeft);
        Assert.True(r.Item2.TopRight > 0);
    }

    [Fact]
    public async Task Rail_carries_the_days_to_goal_in_a_tinted_column()
    {
        var t = await Inspect(AppearanceId.R, Texts);
        Assert.Contains("80", t);
        Assert.Contains("DAYS TO GOAL", t);
    }

    [Fact]
    public async Task Tag_centres_the_head_and_labels_its_progress_in_words()
    {
        var r = await Inspect(AppearanceId.S, w => (Texts(w), w.FindControl<TextBlock>("DrugText")!.HorizontalAlignment));
        Assert.Contains("62% through review", r.Item1);
        Assert.Contains("GOAL", r.Item1); Assert.Contains("FORECAST", r.Item1);
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Center, r.Item2);
    }
}
