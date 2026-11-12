using System.Text.Json;
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

/// <summary>Round 24: X · Graphite Calendar, the owner's choice of three proposals, is the default. Beside the answer it shows the days to
/// the goal date in a style that follows the distance; under the answer it draws the calendar.</summary>
[Collection("Global state")]
public class Round24Tests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public Round24Tests(HeadlessFixture fx) => _fx = fx;
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static AndaRecord Late => new MockAndaRepository().Find("216482")!;
    private static AndaRecord Early => new MockAndaRepository().Find("214990")!;

    private static string Folder(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "anda-hover-companion") dir = dir.Parent;
        return Path.Combine(new[] { dir?.FullName ?? throw new DirectoryNotFoundException("anda-hover-companion folder not found") }.Concat(parts).ToArray());
    }

    private async Task<T> Inspect<T>(AndaRecord record, bool mini, Func<CardWindow, T> read, AppearanceId id = AppearanceId.X, double opacity = 1.0)
    {
        return await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(id, opacity);
            var w = new CardWindow(new OfflineBriefService(), new MockAndaRepository().Find) { CloseWhenDeactivated = false, Today = () => new DateOnly(2026, 7, 16) };
            try { w.Show(record, new PixelPoint(40, 40), mini); Settle(); return read(w); }
            finally { w.Close(); Settle(); ThemeManager.Apply(Appearances.Default); }
        }, CancellationToken.None);
    }

    private static List<string> Texts(CardWindow w) => w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToList();

    [Fact]
    public void X_is_the_default_takes_Ws_place_in_the_eight_and_W_is_still_defined()
    {
        Assert.Equal(AppearanceId.X, Appearances.Default);
        Assert.Equal("X · Graphite Calendar", Appearances.X.Name);
        Assert.Equal(new[] { "X", "V", "U", "T", "K", "P", "L", "O" }, Appearances.Offered.Select(a => a.Id.ToString()));
        Assert.False(Appearances.IsOffered(AppearanceId.W));
        Assert.Contains(Appearances.All, a => a.Id == AppearanceId.W);
        Assert.True(Appearances.X.Look.HeroCount && Appearances.X.Look.Ribbon && Appearances.X.Look.Glass && !Appearances.X.Look.HeroGauge);
        Assert.Equal(CardLayout.Pocket, Appearances.X.Look.Layout);
        Assert.Equal(Appearances.W.Palette, Appearances.X.Palette);          // the same graphite sheet
        // someone who chose W in round 23 gets the new default
        string path = Path.Combine(Temp.NewDir(), "settings.json");
        File.WriteAllText(path, $"{{ \"Appearance\": {(int)AppearanceId.W} }}");
        Assert.Equal(AppearanceId.X, SettingsService.Load(path).Appearance);
    }

    [Fact]
    public void The_days_to_goal_rule_answers_the_shared_cases_exactly_as_the_simulation_does()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Folder("mockup", "countdown-golden.json")));
        int n = 0;
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            int days = c.GetProperty("days").GetInt32();
            Assert.Equal(c.GetProperty("level").GetString(), GoalCountdown.LevelOf(days).ToString().ToLowerInvariant());
            Assert.Equal(c.GetProperty("words").GetString(), GoalCountdown.Words(days));
            Assert.Equal(c.GetProperty("figure").GetString(), GoalCountdown.Figure(days));
            Assert.Equal(c.GetProperty("caption").GetString(), GoalCountdown.Caption(days));
            n++;
        }
        Assert.Equal(20, n);
        // the edges, and numbers no sheet should hold
        Assert.Equal(new[] { GoalCountdown.Level.Calm, GoalCountdown.Level.Near, GoalCountdown.Level.Near, GoalCountdown.Level.Soon, GoalCountdown.Level.Soon, GoalCountdown.Level.Today, GoalCountdown.Level.Passed },
            new[] { 31, 30, 8, 7, 1, 0, -1 }.Select(GoalCountdown.LevelOf));
        Assert.Equal("2147483647", GoalCountdown.Figure(int.MaxValue));
        Assert.Equal("2147483648", GoalCountdown.Figure(int.MinValue));
        Assert.Equal("The goal date passed 2147483648 days ago", GoalCountdown.Words(int.MinValue));
    }

    private static double Luminance(Color c)
    {
        static double Ch(byte v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }
    private static double Contrast(Color a, Color b) { double la = Luminance(a), lb = Luminance(b); return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05); }

    [Fact]
    public void Every_level_keeps_its_text_readable_on_its_ground()
    {
        var sheet = Color.Parse(Appearances.X.Palette.Bg);
        foreach (GoalCountdown.Level level in Enum.GetValues<GoalCountdown.Level>())
        {
            var (ink, fill, edge, _) = GoalCountdown.Style(level, Appearances.X.Palette.Ink, Appearances.X.Palette.Line);
            var ground = fill is null ? sheet : Color.Parse(fill);
            Assert.True(Contrast(Color.Parse(ink), ground) >= 4.5, $"{level}: text {ink} on {ground} is {Contrast(Color.Parse(ink), ground):0.00}:1");
            if (level != GoalCountdown.Level.Calm) Assert.True(Contrast(Color.Parse(edge), sheet) >= 1.9, $"{level}: the edge {edge} must be visible on the sheet");
        }
        // the levels differ in more than color: the two solid ones and "soon" are heavier, and the words always say which
        Assert.True(GoalCountdown.Style(GoalCountdown.Level.Soon, "#000", "#000").Heavy);
        Assert.False(GoalCountdown.Style(GoalCountdown.Level.Near, "#000", "#000").Heavy);
    }

    [Fact]
    public async Task The_default_card_shows_the_days_to_the_goal_date_beside_the_answer_and_the_calendar_under_it()
    {
        var r = await Inspect(Late, false, w =>
        {
            var box = w.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "HeroCount");
            var hero = w.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "Hero");
            var ribbon = w.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "Ribbon");
            return (visible: box.IsEffectivelyVisible, name: Avalonia.Automation.AutomationProperties.GetName(box), texts: Texts(w), width: box.Bounds.Width,
                inside: box.Bounds.Right <= hero.Bounds.Width + 0.5, ribbon: ribbon.IsEffectivelyVisible, ribbonBelow: ribbon.Bounds.Top >= hero.Bounds.Bottom - 0.5,
                fill: (box.Background as ISolidColorBrush)?.Color.A ?? 0, gauge: w.GetVisualDescendants().OfType<StackPanel>().Any(p => p.Name == "HeroGauge"));
        });
        Assert.True(r.visible);
        Assert.Equal("79 days to the goal date", r.name);
        Assert.Contains("79", r.texts);
        Assert.Contains("DAYS TO GOAL", r.texts);
        Assert.Contains(r.texts, t => t.StartsWith("days later than the goal"));           // the answer is still the first thing said
        Assert.Contains("Goal Sat Oct 3 (a weekend day); forecast Sat Oct 17 (a weekend day); 14 days apart.", r.texts);
        Assert.Equal(96, r.width);
        Assert.True(r.inside && r.ribbon && r.ribbonBelow);
        Assert.Equal(0, r.fill);                                                            // 79 days: quiet, no fill
        Assert.False(r.gauge);                                                              // the dial belongs to W
        // 30 days: amber, still no fill
        var near = await Inspect(Early, false, w => { var b = w.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "HeroCount"); return (name: Avalonia.Automation.AutomationProperties.GetName(b), edge: ((ISolidColorBrush)b.BorderBrush!).Color.ToString(), fill: (b.Background as ISolidColorBrush)?.Color.A ?? 0); });
        Assert.Equal(("30 days to the goal date", "#ffc9a45c", (byte)0), near);
    }

    [Theory]
    [InlineData(5, "5 days to the goal date", "5", "DAYS TO GOAL", "#fffaf1dc")]
    [InlineData(1, "1 day to the goal date", "1", "DAY TO GOAL", "#fffaf1dc")]
    [InlineData(0, "The goal date is today", "Today", "IS THE GOAL DATE", "#ff8a5800")]
    [InlineData(-3, "The goal date passed 3 days ago", "3", "DAYS PAST GOAL", "#ffa33636")]
    public async Task The_count_changes_its_words_and_its_ground_as_the_date_comes_and_goes(int days, string words, string figure, string caption, string ground)
    {
        var r = await Inspect(Late with { DaysToGoal = days }, false, w =>
        {
            var b = w.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "HeroCount");
            var t = b.GetVisualDescendants().OfType<TextBlock>().Select(x => x.Text).ToList();
            return (name: Avalonia.Automation.AutomationProperties.GetName(b), t, fill: ((ISolidColorBrush)b.Background!).Color.ToString());
        });
        Assert.Equal(words, r.name);
        Assert.Equal(new[] { figure, caption }, r.t);
        Assert.Equal(ground, r.fill);
    }

    [Fact]
    public async Task Without_a_goal_date_there_is_no_count_and_the_small_card_keeps_the_count_but_not_the_calendar()
    {
        Assert.False(await Inspect(Late with { DaysToGoal = null }, false, w => w.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "HeroCount").IsVisible));
        var mini = await Inspect(Late, true, w => (count: w.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "HeroCount").IsEffectivelyVisible, ribbon: w.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "Ribbon").IsVisible));
        Assert.Equal((true, false), mini);
    }

    [ShotFact]
    public async Task Shots_of_the_default_card()
    {
        string dir = Environment.GetEnvironmentVariable("ANDA_SHOTS_DIR")!;
        Directory.CreateDirectory(dir);
        foreach (var (name, rec, mini, op) in new[]
        {
            ("r24-card-x-late", Late, false, 1.0), ("r24-card-x-early", Early, false, 1.0), ("r24-card-x-peek", Late, true, 1.0), ("r24-card-x-late-90", Late, false, Settings.DefaultCardOpacity),
            ("r24-card-x-5-days", Late with { DaysToGoal = 5 }, false, 1.0), ("r24-card-x-today", Late with { DaysToGoal = 0 }, false, 1.0), ("r24-card-x-passed", Late with { DaysToGoal = -3 }, false, 1.0),
        })
            await Inspect(rec, mini, w => { for (int i = 0; i < 30; i++) { Settle(); Thread.Sleep(12); } w.CaptureRenderedFrame()?.Save(Path.Combine(dir, name + ".png")); return 0; }, AppearanceId.X, op);   // past the entrance animation
    }
}
