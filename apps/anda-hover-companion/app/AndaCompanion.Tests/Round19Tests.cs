using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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

/// <summary>Round 19: the default card of the simulation (T · Pocket Plain) in the Windows app. One answer first, one main action.</summary>
[Collection("Global state")]
public class Round19Tests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public Round19Tests(HeadlessFixture fx) => _fx = fx;
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static AndaRecord Late => new MockAndaRepository().Find("216482")!;
    private static AndaRecord Early => new MockAndaRepository().Find("214990")!;

    private async Task<T> Inspect<T>(AndaRecord record, bool mini, Func<CardWindow, T> read, AppearanceId id = AppearanceId.T)
    {
        return await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(id);
            var w = new CardWindow(new OfflineBriefService(), new MockAndaRepository().Find) { CloseWhenDeactivated = false, Today = () => new DateOnly(2026, 7, 16) };
            try
            {
                w.Show(record, new PixelPoint(40, 40), mini); Settle();
                return read(w);
            }
            finally { w.Close(); Settle(); ThemeManager.Apply(Appearances.Default); }
        }, CancellationToken.None);
    }

    private static List<string> Texts(CardWindow w) => w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToList();

    [Fact]
    public void T_is_appended_and_is_the_default_in_both_editions()
    {
        Assert.Equal(AppearanceId.T, Appearances.Default);
        Assert.Equal(CardLayout.Pocket, Appearances.T.Look.Layout);
        Assert.False(Appearances.T.IsCompact);
        Assert.Equal(AppearanceId.T, Edition.Appearance(new Settings()));                                              // focused edition: always the default look
        Assert.Equal(AppearanceId.K, Edition.Appearance(new Settings { ShowAllTools = true, Appearance = AppearanceId.K }));   // someone who chose K keeps K
    }

    [Fact]
    public async Task The_card_leads_with_the_gap_and_never_shows_the_figure_alone()
    {
        var late = await Inspect(Late, false, Texts);
        Assert.Contains("14", late);
        Assert.Contains("days later than the goal", late);
        Assert.Contains("Goal Oct 3, 2026 · Forecast Oct 17, 2026 · moderate confidence", late);
        Assert.DoesNotContain("GOAL", late);                 // the two-row clock is replaced by the answer, not shown twice

        var early = await Inspect(Early, false, Texts);
        Assert.Contains("9", early);
        Assert.Contains("days ahead of the goal", early);
        Assert.Contains("Goal Aug 15, 2026 · Forecast Aug 6, 2026 · high confidence", early);
    }

    [Fact]
    public async Task The_figure_is_amber_when_late_and_green_when_early()
    {
        static Color Ink(CardWindow w) => ((ISolidColorBrush)w.GetVisualDescendants().OfType<TextBlock>().First(t => t.FontSize == 30).Foreground!).Color;
        Assert.Equal(Color.Parse(Appearances.T.Palette.Warn), await Inspect(Late, false, Ink));
        Assert.Equal(Color.Parse(Appearances.T.Palette.Good), await Inspect(Early, false, Ink));
    }

    [Fact]
    public async Task A_record_without_a_forecast_shows_the_plain_goal_row_and_no_made_up_gap()
    {
        var bare = Late with { ForecastDate = "", ForecastDelta = "", ForecastDeltaDays = null, ForecastConfidence = "", ForecastLate = false };
        var t = await Inspect(bare, false, Texts);
        Assert.DoesNotContain("days later than the goal", t);
        Assert.Contains("GOAL", t);
        Assert.Contains("Oct 3, 2026", t);
    }

    [Fact]
    public async Task Copy_status_is_the_one_filled_action_outside_the_scrolling_part()
    {
        var r = await Inspect(Late, false, w =>
        {
            var copy = w.FindControl<Button>("CopyChip")!;
            var body = w.FindControl<ScrollViewer>("Body")!;
            bool insideScroll = copy.GetVisualAncestors().Contains(body);
            var others = new[] { "RepliesChip", "SisterChip", "NotesChip", "OpenChip" }.Select(n => w.FindControl<Button>(n)!).ToList();
            return (insideScroll, Fill: ((ISolidColorBrush)copy.Background!).Color, copy.MinHeight, copy.IsEffectivelyVisible,
                    PlainOthers: others.All(b => b.BorderThickness == new Thickness(0)), Label: string.Join(" ", copy.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)));
        });
        Assert.False(r.insideScroll);                                             // it stays put while the facts scroll
        Assert.Equal(Color.Parse(Appearances.T.Palette.Accent), r.Fill);
        Assert.True(r.MinHeight >= 44);                                           // a comfortable target
        Assert.True(r.IsEffectivelyVisible);
        Assert.True(r.PlainOthers);
        Assert.Equal("Copy status Enter", r.Label);
    }

    [Fact]
    public async Task Enter_copies_the_status_line_and_says_so()
    {
        var label = await Inspect(Late, false, w =>
        {
            w.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Settle();
            return string.Join(" ", w.FindControl<Button>("CopyChip")!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        });
        Assert.StartsWith("Copied ✓", label);
    }

    [Fact]
    public async Task A_peek_keeps_the_answer_and_hides_the_action_until_the_full_card()
    {
        var r = await Inspect(Late, true, w => (Texts: Texts(w), Copy: w.FindControl<Button>("CopyChip")!.IsEffectivelyVisible, More: w.FindControl<Button>("MoreBtn")!.IsEffectivelyVisible));
        Assert.Contains("days later than the goal", r.Texts);
        Assert.False(r.Copy);
        Assert.True(r.More);
    }

    [Fact]
    public async Task Every_fact_of_the_standard_card_is_still_on_the_default_card()
    {
        var t = string.Join("\n", await Inspect(Late, false, Texts));
        foreach (var fact in new[] { "ANDA 216482", "Metformin HCl ER Tablets, 500 mg", "IR response due in 5 days", "Amendment (minor) received Jun 30", "Oct 3, 2026", "Oct 17, 2026", "moderate confidence" })
            Assert.Contains(fact, t);
    }

    [Fact]
    public async Task The_verified_seal_is_quiet_so_the_answer_is_the_only_color()
    {
        var r = await Inspect(Late, false, w =>
        {
            var seal = w.FindControl<Border>("Seal")!;
            return (Alpha: seal.Background is ISolidColorBrush b ? b.Color.A : (byte)255, Ink: ((ISolidColorBrush)w.FindControl<TextBlock>("SealText")!.Foreground!).Color,
                    Info: $"background {seal.Background}; look {ThemeManager.Current.Id}; seal style {ThemeManager.Current.Look.Seal}; border {seal.BorderThickness}");
        });
        Assert.True(r.Alpha == 0, r.Info);
        Assert.Equal(Color.Parse(Appearances.T.Palette.Ink2), r.Ink);
    }

    [Fact]
    public async Task The_earlier_default_K_is_unchanged()
    {
        var t = await Inspect(Late, false, Texts, AppearanceId.K);
        Assert.Contains("GOAL", t);
        Assert.Contains("FORECAST", t);
        Assert.DoesNotContain("days later than the goal", t);
    }

    /// <summary>A picture for a person to look at. Not run unless ANDA_SHOTS_DIR is set.</summary>
    [ShotFact]
    public async Task Save_pictures_of_the_default_card()
    {
        string dir = Environment.GetEnvironmentVariable("ANDA_SHOTS_DIR")!;
        Directory.CreateDirectory(dir);
        foreach (var (name, rec, mini) in new[] { ("r19-card-t-late", Late, false), ("r19-card-t-early", Early, false), ("r19-card-t-peek", Late, true) })
            await Inspect(rec, mini, w => { Settle(); Settle(); w.CaptureRenderedFrame()?.Save(Path.Combine(dir, name + ".png")); return 0; });
    }
}
