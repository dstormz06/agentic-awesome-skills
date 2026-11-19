using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AndaCompanion.Models;
using AndaCompanion.Services;
using AndaCompanion.Theming;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// Round 31 in the Windows app's windows: the card's data line (source, as of, how long ago, STALE) brought up to date while the card is
/// open, and the Time part of Settings (a clock, the zone, the days before "stale"). The rules are pinned to the simulation in
/// <see cref="Round31Tests"/>. Every test here names its zone and moves a clock of its own.
/// </summary>
[Collection("Global state")]
public class Round31UiTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public Round31UiTests(HeadlessFixture fx) => _fx = fx;
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static readonly DateTime AsOf = new(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc);

    private static AndaRecord Mine() => new()
    {
        Number = "123456", Drug = "Testazole tablets", Meta = "x", GoalDate = "Mar 3, 2027", GoalDays = "", ProgressPct = -1,
        ForecastDate = AndaRecord.Unknown, ForecastDelta = "", ForecastLate = false, ForecastConfidence = "", Latest = "",
        Pending = Array.Empty<PendingItem>(), Notes = Array.Empty<NoteItem>(), ForecastDetail = "", Sisters = Array.Empty<SisterRef>(),
        GoalDateValue = new DateOnly(2027, 3, 3), AsOfDate = new DateOnly(2026, 11, 11), AsOfUtc = AsOf, DataSource = "Your portfolio file · P.xlsx",
        DataAsOf = "your portfolio file, saved Nov 11 · P.xlsx", Demo = false, SourceLabel = "Your portfolio: P.xlsx",
    };

    [Fact]
    public async Task The_cards_data_line_says_source_as_of_and_age_and_turns_stale_by_the_minute_with_the_watch_line()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            using var zone = new ZoneScope("America/New_York");
            ThemeManager.Apply(Appearances.Default);
            var now = AsOf.AddMinutes(59); int days = 7; var record = Mine();
            var w = new CardWindow(new OfflineBriefService(), _ => record) { CloseWhenDeactivated = false, Today = () => DateOnly.FromDateTime(now), UtcNow = () => now, StaleDays = () => days };
            try
            {
                w.Show(record, new PixelPoint(40, 40), false); Settle();
                var line = w.FindControl<TextBlock>("FreshnessText")!; var watch = w.FindControl<TextBlock>("WatchText")!;
                var seen = new List<(string Line, bool Bold, bool Warn, string Watch)>();
                void Look() { Settle(); seen.Add((line.Text ?? "", line.FontWeight == FontWeight.SemiBold, ReferenceEquals(line.Foreground, Application.Current!.Resources["C.Warn"]), watch.IsEffectivelyVisible ? watch.Text ?? "" : "")); }
                Look();                                                                  // 0: fresh
                now = AsOf.AddMinutes(60); w.MinutePassed(); Look();                      // 1: a minute later
                now = AsOf.AddDays(7); w.MinutePassed(); Look();                          // 2: exactly seven days is not older than seven days
                now = AsOf.AddDays(7).AddMinutes(1); w.MinutePassed(); Look();            // 3: the minute it becomes true
                days = 14; w.MinutePassed(); Look();                                      // 4: the person's choice of days takes effect
                days = 1; w.MinutePassed(); Look();                                       // 5
                Clock.ZoneId = "UTC"; w.MinutePassed(); Look();                           // 6: the zone chosen in Settings is followed
                Clock.ZoneId = "America/New_York";
                now = AsOf.AddHours(-2); days = 7; w.MinutePassed(); Look();              // 7: a file from a clock that was ahead
                return seen;
            }
            finally { w.Close(); Settle(); }
        }, CancellationToken.None);
        const string head = "Your portfolio file · P.xlsx · as of Nov 11, 2026, 3:00 PM EST";
        Assert.Equal(($"{head} (59 minutes ago)", false, false), (r[0].Line, r[0].Bold, r[0].Warn));
        Assert.DoesNotContain("days old", r[0].Watch);
        Assert.Equal($"{head} (1 hour ago)", r[1].Line);
        Assert.Equal(($"{head} (7 days ago)", false, false), (r[2].Line, r[2].Bold, r[2].Warn));
        Assert.DoesNotContain("days old", r[2].Watch);
        Assert.Equal(($"{head} (7 days ago) · STALE: older than 7 days", true, true), (r[3].Line, r[3].Bold, r[3].Warn));      // in words, and by its look
        Assert.StartsWith("This data is 7 days old (as of Nov 11).", r[3].Watch);                                          // the watch line says so at the same minute
        Assert.Equal(($"{head} (7 days ago)", false, false), (r[4].Line, r[4].Bold, r[4].Warn));
        Assert.DoesNotContain("days old", r[4].Watch);
        Assert.Equal(($"{head} (7 days ago) · STALE: older than 1 day", true), (r[5].Line, r[5].Bold));
        Assert.Equal("Your portfolio file · P.xlsx · as of Nov 11, 2026, 8:00 PM UTC (7 days ago) · STALE: older than 1 day", r[6].Line);
        Assert.Equal(($"{head} (later than now: please check this computer's clock)", false), (r[7].Line, r[7].Bold));
    }

    [Fact]
    public async Task The_cards_own_timer_brings_the_line_up_to_date_while_it_is_open_and_stops_when_it_closes()
    {
        var r = await _fx.Session.Dispatch(async () =>
        {
            using var zone = new ZoneScope("America/New_York");
            ThemeManager.Apply(Appearances.Default);
            var now = AsOf.AddMinutes(5); int asked = 0; var record = Mine();
            var w = new CardWindow(new OfflineBriefService(), _ => record) { CloseWhenDeactivated = false, Today = () => DateOnly.FromDateTime(now), UtcNow = () => { asked++; return now; }, StaleDays = () => 7, MinuteEvery = TimeSpan.FromMilliseconds(60) };
            w.Show(record, new PixelPoint(40, 40), false); Settle();
            var line = w.FindControl<TextBlock>("FreshnessText")!;
            string first = line.Text ?? "";
            now = AsOf.AddMinutes(6);                                                    // nobody tells the card: only its timer can find out
            for (int i = 0; i < 40 && line.Text == first; i++) { await Task.Delay(50); Dispatcher.UIThread.RunJobs(); }
            string second = line.Text ?? "";
            w.Close(); Settle();
            await Task.Delay(200); Dispatcher.UIThread.RunJobs(); int atClose = asked;
            await Task.Delay(300); Dispatcher.UIThread.RunJobs();
            return (first, second, quiet: asked == atClose);
        }, CancellationToken.None);
        Assert.EndsWith("(5 minutes ago)", r.first);
        Assert.EndsWith("(6 minutes ago)", r.second);
        Assert.True(r.quiet, "the card's timer went on after the card was closed");
    }

    [Fact]
    public async Task The_sample_records_say_they_are_sample_data_and_are_never_called_stale()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            using var zone = new ZoneScope("America/Chicago");
            ThemeManager.Apply(Appearances.Default);
            var repo = new Data.MockAndaRepository(); var now = new DateTime(2027, 12, 1, 15, 0, 0, DateTimeKind.Utc);
            var w = new CardWindow(new OfflineBriefService(), repo.Find) { CloseWhenDeactivated = false, UtcNow = () => now, StaleDays = () => 1 };
            try
            {
                w.Show(repo.Find("216482")!, new PixelPoint(40, 40), false); Settle();
                var line = w.FindControl<TextBlock>("FreshnessText")!;
                return (line.Text, line.FontWeight == FontWeight.SemiBold, line.IsVisible);
            }
            finally { w.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.Equal(("Fictional demo data · extract #412 · as of Jul 16, 2026, 6:12 AM CDT (sample data, which does not change)", false, true), r);
    }

    [Fact]
    public async Task Settings_has_a_clock_a_zone_and_the_days_before_stale_each_with_a_name_and_each_taking_effect_at_once()
    {
        string home = Temp.NewDir();
        var r = await _fx.Session.Dispatch(async () =>
        {
            using var env = new EnvScope(AppPaths.HomeEnvVar, home);
            using var zone = new ZoneScope("");
            ThemeManager.Apply(Appearances.Default);
            ConnectionStatus.Reset();
            var now = new DateTime(2026, 7, 16, 14, 42, 0, DateTimeKind.Utc); int changed = 0;
            var s = new Settings();
            var w = new SettingsWindow(s, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => { changed++; Clock.ZoneId = s.TimeZone; }, null, new QuickReplyService(Path.Combine(Temp.NewDir(), "r.json")))
                { UtcNow = () => now, MinuteEvery = TimeSpan.FromMilliseconds(60) };
            try
            {
                w.Show(); Settle();
                w.FindControl<Expander>("SecLook")!.IsExpanded = true; Settle();
                var clock = w.FindControl<TextBlock>("TimeNow")!; var zoneBox = w.FindControl<ComboBox>("TimeZoneBox")!; var staleBox = w.FindControl<ComboBox>("StaleBox")!; var hint = w.FindControl<TextBlock>("TimeHint")!;
                var names = (AutomationProperties.GetName(zoneBox), AutomationProperties.GetName(staleBox), AutomationProperties.GetLiveSetting(hint));
                var choices = (zoneBox.Items.Cast<string>().ToArray(), staleBox.Items.Cast<string>().ToArray(), zoneBox.SelectedIndex, staleBox.SelectedIndex, hint.IsVisible);
                bool onScreen = clock.IsEffectivelyVisible && zoneBox.IsEffectivelyVisible && staleBox.IsEffectivelyVisible;
                string atOpen = clock.Text ?? "";

                zoneBox.SelectedIndex = 2; Settle();                                       // Central
                var central = (clock.Text, hint.Text, hint.IsVisible, s.TimeZone, Clock.ZoneId, changed, File.ReadAllText(Path.Combine(home, "settings.json")).Contains("\"TimeZone\": \"America/Chicago\""));
                zoneBox.SelectedIndex = 7; Settle();                                       // Hawaii: no daylight time
                var hawaii = (clock.Text, hint.Text);
                zoneBox.SelectedIndex = 9; Settle();
                var utc = (clock.Text, hint.Text);
                staleBox.SelectedIndex = 0; Settle();
                var one = (hint.Text, s.StaleAfterDays);
                staleBox.SelectedIndex = 4; Settle();
                var thirty = (hint.Text, s.StaleAfterDays, File.ReadAllText(Path.Combine(home, "settings.json")).Contains("\"StaleAfterDays\": 30"));

                zoneBox.SelectedIndex = 1; Settle();                                       // Eastern
                string before = clock.Text ?? "";
                now = now.AddMinutes(1);                                                   // nobody tells the window: only its timer can find out
                for (int i = 0; i < 40 && clock.Text == before; i++) { await Task.Delay(50); Dispatcher.UIThread.RunJobs(); }
                string after = clock.Text ?? "";
                zoneBox.SelectedIndex = 0; Settle();                                       // back to this computer's zone
                var auto = (s.TimeZone, hint.Text ?? "");
                return (names, choices, onScreen, atOpen, central, hawaii, utc, one, thirty, before, after, auto);
            }
            finally { w.Close(); Settle(); }
        }, CancellationToken.None);

        Assert.Equal(("Show times in", "Mark my data as stale when it is older than", AutomationLiveSetting.Polite), r.names);
        Assert.Equal(new[] { "Automatic: this computer's time zone", "Eastern (America/New York)", "Central (America/Chicago)", "Mountain (America/Denver)", "Arizona (Mountain, no daylight time) (America/Phoenix)",
            "Pacific (America/Los Angeles)", "Alaska (America/Anchorage)", "Hawaii (Pacific/Honolulu)", "Atlantic (Puerto Rico) (America/Puerto Rico)", "UTC (UTC)" }, r.choices.Item1);
        Assert.Equal(new[] { "1 day", "3 days", "7 days", "14 days", "30 days" }, r.choices.Item2);
        Assert.Equal((0, 2, false), (r.choices.Item3, r.choices.Item4, r.choices.Item5));
        Assert.True(r.onScreen);
        Assert.StartsWith("Now: Thursday, Jul 16, 2026, ", r.atOpen.Length > 0 && TimeZoneInfo.Local.BaseUtcOffset.TotalHours is > -10 and < 9 ? r.atOpen : "Now: Thursday, Jul 16, 2026, ");
        Assert.Equal(("Now: Thursday, Jul 16, 2026, 9:42 AM CDT", "Times are now written in Central time (CDT). No stored time was changed.", true, "America/Chicago", "America/Chicago", 1, true), r.central);
        Assert.Equal(("Now: Thursday, Jul 16, 2026, 4:42 AM HST", "Times are now written in Hawaii time (HST). No stored time was changed."), r.hawaii);
        Assert.Equal(("Now: Thursday, Jul 16, 2026, 2:42 PM UTC", "Times are now written in UTC time (UTC). No stored time was changed."), r.utc);
        Assert.Equal(("Data older than 1 day is now marked STALE.", 1), r.one);
        Assert.Equal(("Data older than 30 days is now marked STALE.", 30, true), r.thirty);
        Assert.Equal("Now: Thursday, Jul 16, 2026, 10:42 AM EDT", r.before);
        Assert.Equal("Now: Thursday, Jul 16, 2026, 10:43 AM EDT", r.after);
        Assert.Equal("", r.auto.Item1); Assert.StartsWith("Times are now written in this computer's time zone (", r.auto.Item2);
    }
}
