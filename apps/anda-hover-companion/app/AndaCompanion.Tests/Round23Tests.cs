using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AndaCompanion.Data;
using AndaCompanion.Models;
using AndaCompanion.Services;
using AndaCompanion.Theming;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// Round 23: eight looks are offered (W, V and U are new to the app, and W is the default); the tray icon shows the number when two or
/// more things need the person; the person chooses when the tray icon is counted again; the Loupe carries no amber at any size.
/// </summary>
[Collection("Global state")]
public class Round23Tests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public Round23Tests(HeadlessFixture fx) => _fx = fx;
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static AndaRecord Late => new MockAndaRepository().Find("216482")!;
    private static AndaRecord Early => new MockAndaRepository().Find("214990")!;

    private static string Folder(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "anda-hover-companion") dir = dir.Parent;
        return Path.Combine(new[] { dir?.FullName ?? throw new DirectoryNotFoundException("anda-hover-companion folder not found") }.Concat(parts).ToArray());
    }

    private async Task<T> Inspect<T>(AndaRecord record, bool mini, AppearanceId id, Func<CardWindow, T> read, double opacity = 1.0)
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

    // ---------------------------------------------------------------- the eight looks

    [Fact]
    public void The_app_offers_the_same_eight_looks_in_the_same_order_as_the_simulation()
    {
        string page = File.ReadAllText(Folder("mockup", "windows-simulation.html"));
        var m = System.Text.RegularExpressions.Regex.Match(page, @"const OFFERED = \[([^\]]+)\];");
        Assert.True(m.Success, "the simulation's list of offered looks was not found");
        var ids = m.Groups[1].Value.Split(',').Select(x => x.Trim().Trim('"').ToUpperInvariant()).ToArray();
        Assert.Equal(ids, Appearances.Offered.Select(a => a.Id.ToString()));
        foreach (var a in Appearances.Offered) Assert.Contains($"\"{a.Name}\"", page);          // and under the same names
        Assert.Equal(6, Appearances.Offered.Count(a => !a.IsCompact));
        Assert.Equal(2, Appearances.Offered.Count(a => a.IsCompact));
        Assert.All(Appearances.Offered.Where(a => a.IsCompact), a => Assert.True(Appearances.All.Any(x => x.Id == a.ExpandsAs)));   // a compact box still opens its sibling's card
    }

    [Fact]
    public void A_saved_look_that_is_no_longer_offered_falls_back_to_the_default_and_an_offered_one_is_kept()
    {
        string path = Path.Combine(Temp.NewDir(), "settings.json");
        foreach (var a in Appearances.All)
        {
            File.WriteAllText(path, $"{{ \"Appearance\": {(int)a.Id} }}");
            Assert.Equal(Appearances.IsOffered(a.Id) ? a.Id : AppearanceId.W, SettingsService.Load(path).Appearance);
        }
    }

    [Fact]
    public void W_V_and_U_use_the_answer_first_layout_and_differ_as_the_simulation_says()
    {
        foreach (var a in new[] { Appearances.W, Appearances.V, Appearances.U }) { Assert.Equal(CardLayout.Pocket, a.Look.Layout); Assert.False(a.IsCompact); }
        Assert.True(Appearances.W.Look.HeroGauge && Appearances.W.Look.Glass && !Appearances.W.Look.Ribbon);
        Assert.True(Appearances.V.Look.Glass && !Appearances.V.Look.HeroGauge && !Appearances.V.Look.Ribbon);
        Assert.True(Appearances.U.Look.Ribbon && !Appearances.U.Look.HeroGauge && !Appearances.U.Look.Glass);
        Assert.False(Appearances.T.Look.Ribbon || Appearances.T.Look.HeroGauge || Appearances.T.Look.Glass);       // T is unchanged
        Assert.Equal("#56626E", Appearances.W.Palette.Accent);                                                     // the graphite of K
        Assert.Equal(Appearances.K.Palette.Ink, Appearances.W.Palette.Ink);
        Assert.Equal(ProgressVariant.Gauge, Appearances.W.Progress);
    }

    [Fact]
    public async Task The_default_card_shows_the_gauge_beside_the_answer_and_says_it_in_words()
    {
        var r = await Inspect(Late, false, AppearanceId.W, w =>
        {
            var box = w.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "HeroGauge");
            var dial = box.Children.OfType<ProgressControl>().Single();
            var hero = w.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "Hero");
            var number = Texts(w).First(t => t == "14");
            return (visible: box.IsEffectivelyVisible, value: dial.Value, name: Avalonia.Automation.AutomationProperties.GetName(dial), texts: Texts(w),
                inside: box.Bounds.Right <= hero.Bounds.Width + 0.5, width: dial.Bounds.Width, number);
        });
        Assert.True(r.visible);
        Assert.Equal(62, r.value);
        Assert.Equal("62 percent of the review clock has run", r.name);
        Assert.Contains("OF THE CLOCK", r.texts);
        Assert.Contains(r.texts, t => t.StartsWith("days later than the goal"));
        Assert.Contains(r.texts, t => t.StartsWith("Goal Oct 3, 2026 · Forecast Oct 17, 2026"));
        Assert.True(r.inside);
        Assert.Equal(96, r.width);
        var early = await Inspect(Early, false, AppearanceId.W, w => w.GetVisualDescendants().OfType<ProgressControl>().First(p => p.Parent is StackPanel { Name: "HeroGauge" }).Value);
        Assert.Equal(88, early);
        // a record with no percentage shows no dial: never an estimate
        var none = await Inspect(Late with { ProgressPct = -1 }, false, AppearanceId.W, w => w.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "HeroGauge").IsVisible);
        Assert.False(none);
        // the other answer-first looks have no dial
        Assert.False(await Inspect(Late, false, AppearanceId.T, w => w.GetVisualDescendants().OfType<StackPanel>().Any(p => p.Name == "HeroGauge")));
    }

    [Fact]
    public async Task The_calendar_look_draws_the_weeks_and_says_the_same_in_a_sentence()
    {
        var r = await Inspect(Late, false, AppearanceId.U, w =>
        {
            var slot = w.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "Ribbon");
            var ribbon = slot.Children.OfType<CalendarRibbon>().Single();
            return (visible: slot.IsEffectivelyVisible, texts: Texts(w), name: Avalonia.Automation.AutomationProperties.GetName(ribbon), h: ribbon.Bounds.Height, wide: ribbon.Bounds.Width);
        });
        Assert.True(r.visible);
        Assert.Contains("GOAL AND FORECAST ON THE CALENDAR", r.texts);
        Assert.Contains("Goal Sat Oct 3 (a weekend day); forecast Sat Oct 17 (a weekend day); 14 days apart.", r.texts);
        Assert.Equal("Calendar ribbon. Goal Sat Oct 3 (a weekend day); forecast Sat Oct 17 (a weekend day); 14 days apart.", r.name);
        Assert.Equal(80, r.h);
        Assert.InRange(r.wide, 250, 400);
        // the small card leaves the calendar out, as in the simulation
        Assert.False(await Inspect(Late, true, AppearanceId.U, w => w.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "Ribbon").IsVisible));
        // without a forecast there is no gap to draw
        Assert.False(await Inspect(Late with { ForecastDeltaDays = null }, false, AppearanceId.U, w => w.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "Ribbon").IsVisible));
    }

    [Fact]
    public void The_ribbon_window_starts_on_a_Monday_and_stands_down_when_the_dates_are_far_apart_or_missing()
    {
        var cal = new BusinessCalendar();
        var (start, weeks) = CalendarRibbon.Window(new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 17))!.Value;
        Assert.Equal(new DateOnly(2026, 9, 21), start);                 // the same first day as the simulation draws
        Assert.Equal(DayOfWeek.Monday, start.DayOfWeek);
        Assert.Equal(5, weeks);
        Assert.Equal((new DateOnly(2026, 7, 27), 4), CalendarRibbon.Window(new DateOnly(2026, 8, 15), new DateOnly(2026, 8, 6))!.Value);   // forecast before goal
        Assert.Null(CalendarRibbon.Window(new DateOnly(2026, 1, 5), new DateOnly(2026, 4, 14)));        // 99 days apart: the sentence stands alone
        Assert.NotNull(CalendarRibbon.Window(new DateOnly(2026, 1, 5), new DateOnly(2026, 4, 13)));     // 98 days: still drawn
        Assert.Null(CalendarRibbon.Window(null, new DateOnly(2026, 4, 14)));
        Assert.Null(CalendarRibbon.Window(new DateOnly(2026, 4, 14), null));
        Assert.Equal("Goal Mon Oct 12 (Columbus Day); forecast Tue Oct 13; 1 day apart.", CalendarRibbon.Describe(new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 13), cal));
        Assert.Equal("Goal Fri Aug 14; forecast Fri Aug 14; 0 days apart.", CalendarRibbon.Describe(new DateOnly(2026, 8, 14), new DateOnly(2026, 8, 14), cal));
    }

    // ---------------------------------------------------------------- the tray icon: a dot for one, the number for more

    [Theory]
    [InlineData(-3, "", "tray-32.png")]
    [InlineData(0, "", "tray-32.png")]
    [InlineData(1, "dot", "tray-32-alert.png")]
    [InlineData(2, "2", "tray-32-2.png")]
    [InlineData(3, "3", "tray-32-3.png")]
    [InlineData(9, "9", "tray-32-9.png")]
    [InlineData(10, "9+", "tray-32-9plus.png")]
    [InlineData(250, "9+", "tray-32-9plus.png")]
    public void The_tray_picture_follows_the_count(int alerts, string badge, string picture)
    {
        Assert.Equal(badge, TrayState.Badge(alerts));
        Assert.Equal(picture, TrayState.Picture(alerts));
        Assert.True(File.Exists(Folder("app", "AndaCompanion", "Assets", picture)), picture + " is missing from Assets");
    }

    [Fact]
    public void Every_tray_picture_exists_and_a_number_is_larger_than_the_dot()
    {
        string assets = Folder("app", "AndaCompanion", "Assets");
        int dot = Round19Tests.AmberPixels(Path.Combine(assets, "tray-32-alert.png"));
        Assert.Equal(0, Round19Tests.AmberPixels(Path.Combine(assets, "tray-32.png")));
        foreach (int n in new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10 })
        {
            int amber = Round19Tests.AmberPixels(Path.Combine(assets, TrayState.Picture(n)));
            Assert.True(amber > dot && amber < 400, $"{TrayState.Picture(n)}: {amber} amber pixels (the dot has {dot})");
        }
        // the pictures are embedded by one pattern, so a new one cannot be forgotten
        Assert.Contains("Assets\\tray-32*.png", File.ReadAllText(Folder("app", "AndaCompanion", "AndaCompanion.csproj")));
    }

    [Fact]
    public void The_Loupe_carries_no_amber_at_any_size()
    {
        string assets = Folder("app", "AndaCompanion", "Assets");
        Assert.DoesNotContain("f2a93b", File.ReadAllText(Path.Combine(assets, "loupe.svg")), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("f2a93b", File.ReadAllText(Path.Combine(assets, "loupe-small.svg")), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, Round19Tests.AmberPixels(Folder("extension", "icons", "icon32.png")));       // the extension's toolbar icon is drawn from the same files
    }

    // ---------------------------------------------------------------- when the tray icon is counted again

    [Fact]
    public void The_scheduled_count_is_never_once_after_midnight_or_every_hour()
    {
        var at = new DateTime(2026, 11, 12, 23, 59, 30, DateTimeKind.Local);
        Assert.Null(TrayRecount.Next(at, TrayRecount.OnUse));
        Assert.Equal(TimeSpan.FromSeconds(90), TrayRecount.Next(at, TrayRecount.Daily));                        // one minute past midnight
        Assert.Equal(TimeSpan.FromSeconds(30), TrayRecount.Next(new DateTime(2026, 11, 13, 0, 0, 30), TrayRecount.Daily));
        Assert.Equal(TimeSpan.FromHours(24), TrayRecount.Next(new DateTime(2026, 11, 13, 0, 1, 0), TrayRecount.Daily));     // just counted: the next is tomorrow
        Assert.Equal(TimeSpan.FromHours(14) + TimeSpan.FromMinutes(1), TrayRecount.Next(new DateTime(2026, 11, 12, 10, 0, 0), TrayRecount.Daily));
        Assert.Equal(TimeSpan.FromMinutes(2), TrayRecount.Next(new DateTime(2026, 11, 12, 10, 59, 0), TrayRecount.Hourly));
        Assert.Equal(TimeSpan.FromHours(1), TrayRecount.Next(new DateTime(2026, 11, 12, 10, 1, 0), TrayRecount.Hourly));
        Assert.Equal(TimeSpan.FromSeconds(90), TrayRecount.Next(at, TrayRecount.Hourly));                       // the hourly count covers midnight too
        Assert.Equal(TimeSpan.FromSeconds(90), TrayRecount.Next(new DateTime(2026, 12, 31, 23, 59, 30), TrayRecount.Daily));   // across a new year
        foreach (string mode in new[] { TrayRecount.Daily, TrayRecount.Hourly })
            for (int minute = 0; minute < 24 * 60; minute += 7)
            {
                var wait = TrayRecount.Next(new DateTime(2026, 3, 8, 0, 0, 0).AddMinutes(minute), mode)!.Value;   // a day on which US clocks change
                Assert.InRange(wait, TimeSpan.FromSeconds(1), TimeSpan.FromHours(24));
            }
    }

    [Fact]
    public void The_count_setting_has_three_choices_starts_at_daily_and_survives_a_bad_file()
    {
        Assert.Equal(new[] { "use", "daily", "hourly" }, TrayRecount.Choices.Select(c => c.Id));
        Assert.Equal(new[] { "Only when I use the app", "Also once a day, just after midnight", "Also every hour" }, TrayRecount.Choices.Select(c => c.Label));
        Assert.Equal("daily", new Settings().TrayRecount);
        foreach (string? junk in new[] { null, "", "DAILY", "always", "<script>", new string('x', 5000) }) Assert.Equal("daily", TrayRecount.Normalize(junk));
        string path = Path.Combine(Temp.NewDir(), "settings.json");
        File.WriteAllText(path, "{ \"TrayRecount\": \"every second\" }");
        Assert.Equal("daily", SettingsService.Load(path).TrayRecount);
        var s = new Settings { TrayRecount = TrayRecount.Hourly };
        SettingsService.Save(s, path);
        Assert.Equal("hourly", SettingsService.Load(path).TrayRecount);
        // the same three choices, in the same words, as the simulation
        string page = File.ReadAllText(Folder("mockup", "windows-simulation.html"));
        foreach (var (id, label) in TrayRecount.Choices) Assert.Contains($"name=\"trec\" value=\"{id}\"{(id == "daily" ? " checked" : "")}> {label}</label>", page);
    }

    [Fact]
    public async Task Settings_offers_the_three_choices_and_saving_one_applies_it()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            var settings = new Settings(); int applied = 0;
            var w = new SettingsWindow(settings, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => applied++);
            try
            {
                w.Show(); Settle();
                var panel = w.FindControl<WrapPanel>("TrayRecountPanel")!;
                var radios = panel.Children.OfType<RadioButton>().ToList();
                var labels = radios.Select(b => (string)b.Content!).ToList();
                var chosen = radios.Where(b => b.IsChecked == true).Select(b => (string)b.Content!).ToList();
                radios[2].IsChecked = true; Settle();
                return (labels, chosen, after: settings.TrayRecount, applied);
            }
            finally { w.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.Equal(new[] { "Only when I use the app", "Also once a day, just after midnight", "Also every hour" }, r.labels);
        Assert.Equal(new[] { "Also once a day, just after midnight" }, r.chosen);
        Assert.Equal("hourly", r.after);
        Assert.True(r.applied >= 1);
    }

    // ---------------------------------------------------------------- pictures for a person to look at (only with ANDA_SHOTS_DIR)

    [ShotFact]
    public async Task Shots_of_the_three_looks_new_to_the_app()
    {
        string dir = Environment.GetEnvironmentVariable("ANDA_SHOTS_DIR")!;
        Directory.CreateDirectory(dir);
        foreach (var (name, id, rec, mini, op) in new[]
        {
            ("r23-card-w-late", AppearanceId.W, Late, false, 1.0), ("r23-card-w-early", AppearanceId.W, Early, false, 1.0), ("r23-card-w-peek", AppearanceId.W, Late, true, 1.0),
            ("r23-card-w-late-90", AppearanceId.W, Late, false, Settings.DefaultCardOpacity),
            ("r23-card-v-late", AppearanceId.V, Late, false, 1.0), ("r23-card-u-late", AppearanceId.U, Late, false, 1.0), ("r23-card-u-early", AppearanceId.U, Early, false, 1.0),
        })
            await Inspect(rec, mini, id, w => { for (int i = 0; i < 30; i++) { Settle(); Thread.Sleep(12); } w.CaptureRenderedFrame()?.Save(Path.Combine(dir, name + ".png")); return 0; }, op);   // past the entrance animation
    }
}
