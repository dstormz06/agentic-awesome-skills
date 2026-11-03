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

/// <summary>Every full look has to survive the awkward records a real portfolio produces: nothing known, very long text, peek mode and an open drawer.</summary>
[Collection("Global state")]
public class LookRobustnessTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public LookRobustnessTests(HeadlessFixture fx) => _fx = fx;
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }

    public static IEnumerable<AppearanceId> FullLooks => Appearances.All.Where(a => !a.IsCompact).Select(a => a.Id);
    public static IEnumerable<object[]> Cases() => from id in FullLooks from v in new[] { "sparse", "long", "peek", "drawer", "healthy" } select new object[] { id, v };

    private static AndaRecord Sparse() => new()
    {
        Number = "123456", Drug = "Unknown product", Meta = AndaRecord.Unknown, GoalDate = AndaRecord.Unknown, GoalDays = "", ProgressPct = -1,
        ForecastDate = AndaRecord.Unknown, ForecastDelta = AndaRecord.Unknown, ForecastLate = false, ForecastConfidence = "", Latest = AndaRecord.Unknown,
        Pending = Array.Empty<PendingItem>(), Notes = Array.Empty<NoteItem>(), ForecastDetail = "", Sisters = Array.Empty<SisterRef>(),
    };

    private static AndaRecord Long() => Demo with
    {
        Drug = "Metformin Hydrochloride Extended-Release Tablets, 500 mg and 750 mg, film-coated, in blister packs of thirty and one hundred",
        Meta = "A very long applicant name that goes on (fictional) Pharmaceuticals and Distribution Partners International · Cycle 2",
        Attention = new AttentionItem(AttentionLevel.Bad, "An information request response is overdue by eleven days and the facility inspection is still unresolved, so please look today"),
        Latest = "An amendment with a very long description was received on Jun 30 and is still under review by three different disciplines",
        Ahead = "A long anticipation line that keeps going: the Thursday huddle, a leadership request for status and two sister applications to compare",
        ForecastConfidence = "moderate confidence, based on only two earlier cycles",
    };

    private static AndaRecord Demo => new MockAndaRepository().Find("216482")!;

    [Theory, MemberData(nameof(Cases))]
    public async Task The_card_renders_inside_its_frame_with_its_facts(AppearanceId id, string variant)
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(id);
            var repo = new MockAndaRepository();
            var rec = variant switch { "sparse" => Sparse(), "long" => Long(), "healthy" => repo.Find("214990")!, _ => Demo };
            var w = new CardWindow(new OfflineBriefService(), repo.Find) { CloseWhenDeactivated = false, Today = () => new DateOnly(2026, 11, 2) };
            w.Show(rec, new PixelPoint(40, 40), variant == "peek"); Settle();
            if (variant == "drawer") { w.FindControl<Button>("ExpBtn")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(); }
            var root = w.FindControl<Border>("Root")!;
            var frame = w.CaptureRenderedFrame();
            double rootW = root.Bounds.Width;
            var outside = w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible && !string.IsNullOrEmpty(t.Text) && t.IsEffectivelyVisible)
                .Select(t => (t.Text, Right: t.TranslatePoint(new Point(t.Bounds.Width, 0), root)?.X ?? 0)).Where(x => x.Right > rootW + 1).Select(x => x.Text).ToList();
            var texts = w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToList();
            string? dir = Environment.GetEnvironmentVariable("ANDA_SHOT_DIR");
            if (dir is not null && frame is not null) { Directory.CreateDirectory(dir); frame.Save(Path.Combine(dir, $"{id}-{variant}.png")); }
            w.Close(); Settle();
            ThemeManager.Apply(Appearances.Default);
            return (frame, rootW, outside, texts);
        }, CancellationToken.None);

        Assert.NotNull(r.frame);
        Assert.InRange(r.rootW, 360, 520);
        Assert.True(r.outside.Count == 0, $"{id}/{variant}: text sticks out of the card: {string.Join(" | ", r.outside.Take(3))}");
        string all = string.Join("\n", r.texts);
        Assert.Contains(variant == "sparse" ? "123456" : variant == "healthy" ? "214990" : "216482", all);
        if (variant != "peek") { Assert.Contains("PSG", all); Assert.Contains("RLD LABEL", all); }   // both reference lines are on every full card (a peek is the short form)
        if (variant == "sparse") Assert.DoesNotContain("NaN", all);
        if (variant is "peek") Assert.DoesNotContain("OFFLINE BRIEF", all);                   // a peek makes no brief
    }
}
