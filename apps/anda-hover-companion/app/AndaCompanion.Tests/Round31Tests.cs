using System.Text.Json;
using AndaCompanion.Models;
using AndaCompanion.Services;
using AndaCompanion.Services.Portfolio;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// Round 31: time. Every time is written in the person's time zone (the computer's own, or one chosen in Settings) with the zone's
/// abbreviation and the right daylight time; how long ago is counted in whole units; and every piece of data says where it is from, as of
/// when, and that it is STALE after the days the person chose. The simulation wrote mockup/round31-golden.json (tools/round31-golden.cjs);
/// the app must give the same answers.
/// </summary>
[Collection("Global state")]
public class Round31Tests : IDisposable
{
    private readonly string _zoneBefore = Clock.ZoneId;
    private readonly Func<DateTime> _nowBefore = Clock.UtcNow;
    private readonly Func<string, TimeZoneInfo?> _findBefore = Clock.Find;
    public void Dispose() { Clock.ZoneId = _zoneBefore; Clock.UtcNow = _nowBefore; Clock.Find = _findBefore; }

    private static string Folder(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "anda-hover-companion") dir = dir.Parent;
        return Path.Combine(new[] { dir?.FullName ?? throw new DirectoryNotFoundException("anda-hover-companion folder not found") }.Concat(parts).ToArray());
    }
    private static JsonDocument Golden() => JsonDocument.Parse(File.ReadAllText(Folder("mockup", "round31-golden.json")));
    private static void Fail(List<string> bad) => Assert.True(bad.Count == 0, bad.Count + " differences from the simulation:\n" + string.Join("\n", bad.Take(8)));
    private static void Same(List<string> bad, string what, string expected, string actual)
    {
        if (expected != actual) bad.Add($"{what}\n  expected: {JsonSerializer.Serialize(expected)}\n  actual:   {JsonSerializer.Serialize(actual)}");
    }
    private static long Ms(string iso) => DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture).ToUnixTimeMilliseconds();
    private static DateTime Utc(string iso) => DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture).UtcDateTime;
    private const long Minute = 60000, Hour = 60 * Minute, Day = 24 * Hour;

    private static AndaRecord Record(DateTime? asOfUtc, bool demo = false, string? source = "Your portfolio file · P.xlsx", string? words = "your portfolio file, saved Nov 11 · P.xlsx") => new()
    {
        Number = "123456", Drug = "Test", Meta = "x", GoalDate = "Mar 3, 2027", GoalDays = "", ProgressPct = -1,
        ForecastDate = AndaRecord.Unknown, ForecastDelta = "", ForecastLate = false, ForecastConfidence = "", Latest = "",
        Pending = Array.Empty<PendingItem>(), Notes = Array.Empty<NoteItem>(), ForecastDetail = "", Sisters = Array.Empty<SisterRef>(),
        GoalDateValue = new DateOnly(2027, 3, 3), AsOfDate = asOfUtc is { } t ? DateOnly.FromDateTime(t) : null, AsOfUtc = asOfUtc, DataSource = source, DataAsOf = words,
        Demo = demo, SourceLabel = "Your portfolio: P.xlsx",
    };

    // ---------------------------------------------------------------- the rules, held to the simulation's answers

    [Fact]
    public void A_moment_is_written_in_each_zone_exactly_as_the_simulation_writes_it()
    {
        using var doc = Golden(); var bad = new List<string>(); var root = doc.RootElement; int n = 0;
        Same(bad, "zones offered", JsonSerializer.Serialize(root.GetProperty("zoneChoices")), JsonSerializer.Serialize(Clock.ZoneChoices.Select(c => new[] { c.Id, c.Name })));
        Same(bad, "abbreviations", JsonSerializer.Serialize(root.GetProperty("abbreviations")), JsonSerializer.Serialize(root.GetProperty("abbreviations").EnumerateObject().ToDictionary(p => p.Name, p => new object[] { Clock.Abbreviations[p.Name].Standard, Clock.Abbreviations[p.Name].Daylight, Clock.Abbreviations[p.Name].Offset })));
        Same(bad, "abbreviations: how many", root.GetProperty("abbreviations").EnumerateObject().Count().ToString(), Clock.Abbreviations.Count.ToString());
        Same(bad, "days", JsonSerializer.Serialize(new { c = root.GetProperty("staleChoices"), d = root.GetProperty("staleDefault") }), JsonSerializer.Serialize(new { c = Clock.StaleChoices, d = Clock.StaleDefault }));
        foreach (var c in root.GetProperty("labels").EnumerateArray())
        {
            n++; string zone = c.GetProperty("zone").GetString()!; long ms = c.GetProperty("ms").GetInt64(); string what = $"{zone} at {ms}";
            Same(bad, what + " offset", c.GetProperty("offset").GetInt32().ToString(), Clock.OffsetMinutes(ms, Clock.Resolve(zone)).ToString());
            Same(bad, what + " abbreviation", c.GetProperty("abbr").GetString()!, Clock.Abbreviation(ms, Clock.Resolve(zone), zone));
            Same(bad, what + " label", c.GetProperty("label").GetString()!, Clock.Label(ms, zone, false));
            Same(bad, what + " with seconds", c.GetProperty("withSeconds").GetString()!, Clock.Label(ms, zone, true));
            Same(bad, what + " time of day", c.GetProperty("time").GetString()!, Clock.Time(ms, zone, false));
            Same(bad, what + " time of day with seconds", c.GetProperty("timeWithSeconds").GetString()!, Clock.Time(ms, zone, true));
        }
        Assert.True(n >= 506, "cases are missing"); n = 0;
        foreach (var c in root.GetProperty("bounds").EnumerateArray())
        {
            n++; long? ms = c.GetProperty("ms").ValueKind == JsonValueKind.Null ? null : (long)c.GetProperty("ms").GetDouble() is var v && c.GetProperty("ms").GetDouble() is > -9.3e18 and < 9.3e18 ? v : null;
            if (ms is null && c.GetProperty("ms").ValueKind != JsonValueKind.Null) ms = c.GetProperty("ms").GetDouble() > 0 ? long.MaxValue : long.MinValue;      // a number too large for the app's own kind of number is still "not known"
            Same(bad, $"end {n} known", c.GetProperty("known").GetBoolean().ToString(), Clock.Known(ms).ToString());
            Same(bad, $"end {n} label", c.GetProperty("label").GetString()!, Clock.Label(ms, "America/New_York", true));
            Same(bad, $"end {n} time", c.GetProperty("time").GetString()!, Clock.Time(ms, "UTC", false));
            Same(bad, $"end {n} ago", c.GetProperty("ago").GetString()!, Clock.Relative(ms, 0));
            var fresh = ms is { } known ? Clock.Fresh("X", known, 0, 7, "UTC") : Clock.Fresh("X", null, 0, 7, "UTC");
            Same(bad, $"end {n} data line", c.GetProperty("fresh").GetProperty("text").GetString()! + "|" + c.GetProperty("fresh").GetProperty("stale").GetBoolean(), fresh.Text + "|" + fresh.Stale);
        }
        Assert.True(n >= 9, "cases are missing"); n = 0;
        foreach (var c in root.GetProperty("said").EnumerateArray())
            Same(bad, $"sentence {++n}", c.GetProperty("text").GetString()!, Clock.ZoneSaid(c.GetProperty("chosen").GetString()!, c.GetProperty("computer").GetString()!, c.GetProperty("ms").GetInt64()));
        Assert.True(n >= 12, "cases are missing"); n = 0;
        foreach (var c in root.GetProperty("pages").EnumerateArray())
        {
            n++;
            var entries = c.GetProperty("entries").EnumerateArray().Select(e => new HandoffEntry(e.GetProperty("status").GetString()!, e.GetProperty("watch").EnumerateArray().Select(x => x.GetString()!).ToList(),
                e.GetProperty("open").EnumerateArray().Select(x => x.GetString()!).ToList(), e.GetProperty("notes").EnumerateArray().Select(x => x.GetString()!).ToList())).ToList();
            Same(bad, $"page {n}", string.Join("\n", c.GetProperty("lines").EnumerateArray().Select(x => x.GetString())), string.Join("\n", Handoff.Text(c.GetProperty("title").GetString()!, entries, c.GetProperty("today").GetString()!, c.GetProperty("notes").GetBoolean(), c.GetProperty("line").GetString())));
        }
        Assert.True(n >= 3, "cases are missing");
        Same(bad, "a time that is not known", JsonSerializer.Serialize(root.GetProperty("unknown")),
            JsonSerializer.Serialize(new[] { Clock.Label(null, "UTC", false), Clock.Label(null, "UTC", true), Clock.Relative(null, 5), Clock.Relative(5, null), Clock.Time(null, "UTC", true) }));
        Fail(bad);
    }

    [Fact]
    public void How_long_ago_and_the_data_line_are_the_simulations_answers()
    {
        using var doc = Golden(); var bad = new List<string>(); var root = doc.RootElement; int n = 0;
        foreach (var c in root.GetProperty("relative").EnumerateArray())
            Same(bad, $"age {++n}", c.GetProperty("text").GetString()!, Clock.Relative(c.GetProperty("then").GetInt64(), c.GetProperty("now").GetInt64()));
        Assert.True(n >= 24, "cases are missing"); n = 0;
        foreach (var c in root.GetProperty("fresh").EnumerateArray())
        {
            n++;
            long? asOf = c.GetProperty("asOf").ValueKind == JsonValueKind.Null ? null : c.GetProperty("asOf").GetInt64();
            var (text, stale) = Clock.Fresh(c.GetProperty("source").GetString()!, asOf, c.GetProperty("now").GetInt64(), c.GetProperty("days").GetInt32(), c.GetProperty("zone").GetString());
            Same(bad, $"data line {n}", c.GetProperty("result").GetProperty("text").GetString()!, text);
            Same(bad, $"data line {n} stale", c.GetProperty("result").GetProperty("stale").GetBoolean().ToString(), stale.ToString());
        }
        Assert.True(n >= 88, "cases are missing"); Fail(bad);
    }

    // ---------------------------------------------------------------- daylight time

    [Fact]
    public void The_nights_the_clocks_change_are_written_rightly()
    {
        const string ny = "America/New_York";
        // Mar 8, 2026: 1:59:59 AM EST is followed by 3:00:00 AM EDT
        Assert.Equal("Mar 8, 2026, 1:59:59 AM EST", Clock.Label(Ms("2026-03-08T06:59:59Z"), ny, true));
        Assert.Equal("Mar 8, 2026, 3:00:00 AM EDT", Clock.Label(Ms("2026-03-08T07:00:00Z"), ny, true));
        // Nov 1, 2026: 1:30 AM comes twice, and only the abbreviation tells the two apart
        Assert.Equal("Nov 1, 2026, 1:30 AM EDT", Clock.Label(Ms("2026-11-01T05:30:00Z"), ny, false));
        Assert.Equal("Nov 1, 2026, 1:30 AM EST", Clock.Label(Ms("2026-11-01T06:30:00Z"), ny, false));
        // a zone that keeps no daylight time, the far side of the date line, and a half-hour zone
        Assert.Equal("Jul 16, 2026, 7:42 AM MST", Clock.Label(Ms("2026-07-16T14:42:00Z"), "America/Phoenix", false));
        Assert.Equal("Jul 16, 2026, 4:42 AM HST", Clock.Label(Ms("2026-07-16T14:42:00Z"), "Pacific/Honolulu", false));
        Assert.Equal("Jul 16, 2026, 8:12 PM UTC+5:30", Clock.Label(Ms("2026-07-16T14:42:00Z"), "Asia/Kolkata", false));
        Assert.Equal("Jul 16, 2026, 2:42 PM UTC", Clock.Label(Ms("2026-07-16T14:42:00Z"), "Not/AZone", false));       // a zone that is not known is read as UTC, and says so
        // "a day" is 24 hours of real time: the week the clocks went back is 7 days and an hour on the wall clock, and still 7 days
        Assert.Equal("7 days ago", Clock.Relative(Ms("2026-10-31T16:00:00Z"), Ms("2026-11-07T16:30:00Z")));
        var across = Clock.Fresh("My file", Ms("2026-10-31T16:00:00Z"), Ms("2026-11-07T16:30:00Z"), 7, ny);
        Assert.Equal(("My file · as of Oct 31, 2026, 12:00 PM EDT (7 days ago) · STALE: older than 7 days", true), across);
        // the name Windows gives the zone leads to the same abbreviations
        var windows = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        Assert.Equal("America/New_York", Clock.IanaId(windows));
        Assert.Equal("Jul 16, 2026, 10:42 AM EDT", Clock.Label(Ms("2026-07-16T14:42:00Z"), windows, false));
        Assert.Equal("Nov 18, 2026, 3:12 PM EST", Clock.Label(Ms("2026-11-18T20:12:00Z"), windows, false));
        Assert.Equal("Now: Thursday, Jul 16, 2026, 10:42 AM EDT", Clock.NowLine(Ms("2026-07-16T14:42:00Z"), windows));
        Assert.Equal("Now: Sunday, Nov 1, 2026, 1:30 AM EST", Clock.NowLine(Ms("2026-11-01T06:30:00Z"), windows));
    }

    // ---------------------------------------------------------------- the zone in use

    [Fact]
    public void The_zone_chosen_in_Settings_is_the_zone_every_time_is_written_in()
    {
        Clock.ZoneId = "America/Chicago";
        Assert.Equal("America/Chicago", Clock.IanaId(Clock.Zone));
        Assert.Equal("Jul 16, 2026, 8:05:07 AM CDT", EventLog.Local("2026-07-16T13:05:07.042Z"));                 // the event log
        Assert.Equal("Jul 16, 2026, 9:42:05 AM CDT", Clock.Label(Utc("2026-07-16T14:42:05Z")));                  // a backup copy
        Assert.Equal("9:42 AM CDT", Clock.Time(new DateTimeOffset(2026, 7, 16, 10, 42, 0, TimeSpan.FromHours(-4))));      // a moment is a moment, whatever offset it was noted with
        ConnectionStatus.Reset();
        try
        {
            ConnectionStatus.Success("Test provider", new DateTimeOffset(2026, 11, 18, 20, 12, 0, TimeSpan.Zero));
            Assert.Equal("CONNECTED · Test provider · checked 2:12 PM CST", ConnectionStatus.Current.Label);
            Assert.EndsWith("The last request succeeded at 2:12 PM CST.", ConnectionStatus.Current.Sentence);
        }
        finally { ConnectionStatus.Reset(); }

        Clock.ZoneId = "UTC";
        Assert.Equal("Jul 16, 2026, 1:05:07 PM UTC", EventLog.Local("2026-07-16T13:05:07.042Z"));
        Clock.ZoneId = "Mars/Olympus";                                                                            // not one of the choices: this computer's own zone
        Assert.Equal("", Clock.ZoneId); Assert.Equal(TimeZoneInfo.Local.Id, Clock.Zone.Id);
        Clock.ZoneId = null!; Assert.Equal("", Clock.ZoneId);
        Assert.Equal("not a time", EventLog.Local("not a time"));

        Assert.Equal("Times are now written in Central time (CST). No stored time was changed.", Clock.ZoneSaid("America/Chicago", Ms("2026-11-18T20:00:00Z")));
        Assert.Equal("Times are now written in Central time (CDT). No stored time was changed.", Clock.ZoneSaid("America/Chicago", Ms("2026-07-16T14:42:00Z")));
        Assert.StartsWith("Times are now written in this computer's time zone (", Clock.ZoneSaid("", Ms("2026-07-16T14:42:00Z")));
        Assert.Equal("Data older than 1 day is now marked STALE.", Clock.StaleSaid(1));
        Assert.Equal("Data older than 14 days is now marked STALE.", Clock.StaleSaid(14));
    }

    [Fact]
    public void A_zone_this_computer_has_no_data_for_is_never_used_under_its_name()
    {
        // The review's finding: where .NET cannot find the zone, the app wrote UTC times under "EST" and Settings said "Eastern time".
        Clock.Find = id => id == "America/New_York" ? null : Clock.FindOnThisComputer(id);
        long july = Ms("2026-07-16T14:42:00Z");
        Assert.Equal("Jul 16, 2026, 2:42 PM UTC", Clock.Label(july, "America/New_York", false));                 // read as UTC, and says UTC
        Assert.Equal("2:42 PM UTC", Clock.Time(july, "America/New_York", false));
        Assert.Equal("X · as of Jul 16, 2026, 2:42 PM UTC (just now)", Clock.Fresh("X", july, july, 7, "America/New_York").Text);
        Clock.ZoneId = "America/New_York";
        Assert.Equal(TimeZoneInfo.Local.Id, Clock.Zone.Id);                                                       // the computer's own zone is used instead
        string said = Clock.ZoneSaid("America/New_York", july);
        Assert.StartsWith("This computer has no data for Eastern time, so times are written in this computer's time zone (", said);
        Assert.EndsWith("). No stored time was changed.", said);
        Assert.Equal("This computer has no data for Eastern time, so times are written in this computer's time zone (America/Chicago, CDT). No stored time was changed.", Clock.ZoneSaid("America/New_York", "America/Chicago", july));
        Clock.ZoneId = "America/Chicago";                                                                         // a zone it does have is used as before
        Assert.Equal("Jul 16, 2026, 9:42 AM CDT", Clock.Label(Utc("2026-07-16T14:42:00Z"), seconds: false));

        // a number that is no date, and years a zone kept other hours than it keeps now
        Clock.Find = Clock.FindOnThisComputer;
        Assert.Equal("time not known", Clock.Label(long.MaxValue, "UTC", true));
        Assert.Equal("time not known", Clock.Label(Clock.TimeMin - 1, "UTC", true));
        Assert.Equal(("X · time not known", false), Clock.Fresh("X", long.MinValue, 0, 7, "UTC"));
        Assert.Equal("Jul 4, 1967, 12:00 PM UTC-6", Clock.Label(Ms("1967-07-04T18:00:00Z"), "America/Phoenix", false));          // Arizona kept daylight time that year: not "MST"
        Assert.Equal("Jan 15, 1980, 10:00 AM UTC-8", Clock.Label(Ms("1980-01-15T18:00:00Z"), "America/Juneau", false));          // Juneau was on Pacific time: not "AKDT"
        Assert.Equal("Jul 16, 2026, 10:42 AM EDT", Clock.Label(july, "America/Detroit", false));                                 // a computer in Michigan set to "Automatic"
        Assert.Equal("Jul 16, 2026, 10:42 AM UTC-4", Clock.Label(july, "America/La_Paz", false));                                // what Windows calls Puerto Rico's zone: right, without an abbreviation
    }

    [Fact]
    public void Backup_copies_are_counted_by_this_computers_days_whatever_zone_times_are_written_in()
    {
        Clock.ZoneId = "Pacific/Honolulu";
        var b = new Backups(Path.Combine(Temp.NewDir(), "backups"), () => 30);
        Assert.Equal(TimeZoneInfo.Local.Id, b.DayZone.Id);                                                        // so that changing the zone can never remove a copy
        Clock.ZoneId = "UTC";
        Assert.Equal(TimeZoneInfo.Local.Id, b.DayZone.Id);
    }

    [Fact]
    public void The_browser_is_told_what_the_app_shows_and_a_page_for_a_colleague_says_how_old_its_data_is()
    {
        string dir = Temp.NewDir(), path = Path.Combine(dir, "my sheet.csv");
        File.WriteAllText(path, "ANDA,Drug,Goal date\n123456,Testazole tablets,3/3/2027\n");
        var saved = new DateTime(2026, 11, 10, 12, 12, 0, DateTimeKind.Utc); File.SetLastWriteTimeUtc(path, saved);
        Clock.UtcNow = () => saved.AddDays(9);
        var repo = new PortfolioRepository(path, today: () => new DateOnly(2026, 11, 19));
        var r = repo.Find("123456")!; var cal = new BusinessCalendar();

        // the browser host: the chosen zone and the chosen days, as in the app's own card
        Clock.ZoneId = "Pacific/Honolulu";
        NativeHostContext Host(int days) => new() { Settings = new Settings { TimeZone = "Pacific/Honolulu", StaleAfterDays = days }, Repository = repo, Calendar = cal, Today = () => new DateOnly(2026, 11, 19), DataKind = "portfolio" };
        var host = Host(30);
        Assert.Equal("Your portfolio file · my sheet.csv · as of Nov 10, 2026, 2:12 AM HST (9 days ago)", host.Freshness(r));
        Assert.DoesNotContain(CardModels.Build(r, host.Card()).Watch, f => f.Code == "stale-data");
        var reply = System.Text.Json.Nodes.JsonNode.Parse(new NativeHostSession(host).Handle("{\"id\":1,\"type\":\"lookup\",\"number\":\"ANDA 123456\"}"))!;
        Assert.Equal("Your portfolio file · my sheet.csv · as of Nov 10, 2026, 2:12 AM HST (9 days ago)", reply["card"]!["freshness"]!.GetValue<string>());
        Assert.DoesNotContain("days old", reply.ToJsonString());
        host = Host(7);
        Assert.EndsWith("(9 days ago) · STALE: older than 7 days", host.Freshness(r));
        Assert.Contains(CardModels.Build(r, host.Card()).Watch, f => f.Code == "stale-data" && f.Text == "This data is 9 days old (as of Nov 10).");

        // the watch flag names the day the card's line names, also after the zone was changed; and the status line follows
        Clock.ZoneId = "America/New_York";
        File.SetLastWriteTimeUtc(path, new DateTime(2026, 11, 10, 3, 0, 0, DateTimeKind.Utc));
        repo = new PortfolioRepository(path, today: () => new DateOnly(2026, 11, 19)); r = repo.Find("123456")!;
        string Flag() => WatchRules.Evaluate(repo.Find("123456")!, new DateOnly(2026, 11, 19), cal, new WatchSettings(3, 7, NowMs: Clock.Ms(Clock.UtcNow()))).Single(f => f.Code == "stale-data").Text;
        Assert.Equal("This data is 9 days old (as of Nov 9).", Flag()); Assert.Contains("(file saved Nov 9, 2026, 10:00 PM EST)", repo.Status.Message);
        Assert.Contains("as of Nov 9, 2026, 10:00 PM EST", Clock.DataLine(r, Clock.Ms(Clock.UtcNow()), 7, Clock.Zone).Text);
        Clock.ZoneId = "UTC";
        Assert.Equal("This data is 9 days old (as of Nov 10).", Flag()); Assert.Contains("(file saved Nov 10, 2026, 3:00 AM UTC)", repo.Status.Message);
        Assert.Contains("as of Nov 10, 2026, 3:00 AM UTC", Clock.DataLine(r, Clock.Ms(Clock.UtcNow()), 7, Clock.Zone).Text);
        Assert.Equal("This data is 9 days old (as of Nov 10).", WatchRules.Evaluate(r, new DateOnly(2026, 11, 19), cal, new WatchSettings(3, 7, NowMs: Clock.Ms(Clock.UtcNow()))).Single(f => f.Code == "stale-data").Text);      // a record read before the change says the same

        // the hand-off page
        Clock.ZoneId = "America/New_York";
        var ctx = new CardContext(new DateOnly(2026, 11, 19), cal, 3, 7, null, 7, Clock.Ms(Clock.UtcNow()));
        string line = HandoffData.DataLine(new[] { "123456" }, repo, Clock.Ms(Clock.UtcNow()), 7, Clock.Zone);
        Assert.Equal("Your portfolio file · my sheet.csv · as of Nov 9, 2026, 10:00 PM EST (9 days ago) · STALE: older than 7 days", line);
        var page = Handoff.Text("Hand-off", HandoffData.Entries(new[] { "123456" }, repo, ctx, null, null, null), "Nov 19, 2026", false, line);
        Assert.Equal(new[] { "Hand-off: 1 ANDA, made on Nov 19, 2026", "From my own data and notes on this PC. It is not the system of record.", "Data: " + line }, page.Take(3));
        Assert.Contains(page, l => l.Contains("This data is 9 days old (as of Nov 9)."));
        Assert.Equal(line, HandoffData.DataLine(Array.Empty<string>(), repo, Clock.Ms(Clock.UtcNow()), 7, Clock.Zone));      // a page that lists no ANDA still says where the data is from
        Assert.DoesNotContain(Handoff.Text("Hand-off", Array.Empty<HandoffEntry>(), "Nov 19, 2026", false), l => l.StartsWith("Data:"));
    }

    // ---------------------------------------------------------------- settings: a new version of the file

    [Fact]
    public void Settings_written_by_an_earlier_version_are_moved_forward_and_a_value_that_is_not_a_choice_becomes_the_usual_one()
    {
        string dir = Temp.NewDir(), path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, "{\"SchemaVersion\":2,\"Tone\":\"Warm\",\"BackupCopies\":60,\"ArchiveAfterDays\":90}");      // written by round 30
        var s = SettingsService.Load(path);
        Assert.Equal((Settings.CurrentSchema, "", 7, "Warm", 60, 90), (s.SchemaVersion, s.TimeZone, s.StaleAfterDays, s.Tone, s.BackupCopies, s.ArchiveAfterDays));
        Assert.True(Settings.CurrentSchema >= 3);                                                                 // round 32 moved it to 4; see Round32Tests
        Assert.Null(SettingsService.LoadNote);                                                                    // nothing the person set was changed, so nothing is said
        SettingsService.Save(s, path);
        Assert.Contains($"\"SchemaVersion\": {Settings.CurrentSchema}", File.ReadAllText(path));

        File.WriteAllText(path, "{\"SchemaVersion\":1,\"Tone\":\"Warm\",\"Provider\":3}");                        // two versions back: each step is taken
        s = SettingsService.Load(path);
        Assert.Equal((Settings.CurrentSchema, "", 7, 30, 30, "Warm"), (s.SchemaVersion, s.TimeZone, s.StaleAfterDays, s.BackupCopies, s.ArchiveAfterDays, s.Tone));

        File.WriteAllText(path, "{\"SchemaVersion\":3,\"TimeZone\":\"America/Denver\",\"StaleAfterDays\":30}");
        s = SettingsService.Load(path);
        Assert.Equal(("America/Denver", 30), (s.TimeZone, s.StaleAfterDays));

        File.WriteAllText(path, "{\"SchemaVersion\":3,\"TimeZone\":\"Mars/Olympus\",\"StaleAfterDays\":5,\"Tone\":\"Warm\"}");
        s = SettingsService.Load(path);
        Assert.Equal(("", 7, "Warm"), (s.TimeZone, s.StaleAfterDays, s.Tone));

        File.WriteAllText(path, "{\"SchemaVersion\":3,\"TimeZone\":17,\"StaleAfterDays\":\"soon\",\"Tone\":\"Warm\"}");      // values that cannot be read are repaired alone
        s = SettingsService.Load(path);
        Assert.Equal(("", 7, "Warm"), (s.TimeZone, s.StaleAfterDays, s.Tone));
        Assert.Contains("could not be read", SettingsService.LoadNote ?? "");

        File.WriteAllText(path, "{\"SchemaVersion\":3,\"TimeZone\":null,\"StaleAfterDays\":14}");
        s = SettingsService.Load(path);
        Assert.Equal(("", 14), (s.TimeZone, s.StaleAfterDays));
    }

    [Fact]
    public void A_backup_of_the_settings_carries_the_zone_and_the_days_and_repairs_what_is_not_a_choice()
    {
        string dir = Temp.NewDir();
        var lenses = new PromptLibraryService(Path.Combine(dir, "p.json")); var replies = new QuickReplyService(Path.Combine(dir, "r.json"));
        var mine = new Settings { TimeZone = "America/Denver", StaleAfterDays = 30 };
        string backup = SettingsBackup.Export(mine, lenses, replies, new DateTime(2026, 11, 18, 20, 0, 0, DateTimeKind.Utc));
        var other = new Settings();
        var (ok, _) = SettingsBackup.Import(backup, other, lenses, replies);
        Assert.True(ok); Assert.Equal(("America/Denver", 30), (other.TimeZone, other.StaleAfterDays));

        string bad = backup.Replace("\"America/Denver\"", "\"Mars/Olympus\"").Replace("\"StaleAfterDays\": 30", "\"StaleAfterDays\": 5");
        Assert.NotEqual(backup, bad);
        var third = new Settings { TimeZone = "UTC", StaleAfterDays = 1 };
        (ok, _) = SettingsBackup.Import(bad, third, lenses, replies);
        Assert.True(ok); Assert.Equal(("", 7), (third.TimeZone, third.StaleAfterDays));

        SettingsBackup.Reset(third, lenses, replies);
        Assert.Equal(("", 7), (third.TimeZone, third.StaleAfterDays));
    }

    // ---------------------------------------------------------------- data: source, as of, stale

    [Fact]
    public void A_record_says_where_it_is_from_as_of_when_and_that_it_is_stale_by_one_rule_for_the_card_and_the_watch_list()
    {
        var ny = TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); var cal = new BusinessCalendar();
        var asOf = Utc("2026-11-11T20:00:00Z"); long t0 = Clock.Ms(asOf);
        var r = Record(asOf);
        bool Flag(long now, int days) => WatchRules.Evaluate(r, DateOnly.FromDateTime(DateTime.UnixEpoch.AddMilliseconds(now)), cal, new WatchSettings(3, days, NowMs: now)).Any(f => f.Code == "stale-data");

        Assert.Equal(("Your portfolio file · P.xlsx · as of Nov 11, 2026, 3:00 PM EST (just now)", false), Clock.DataLine(r, t0, 7, ny));
        Assert.Equal(("Your portfolio file · P.xlsx · as of Nov 11, 2026, 3:00 PM EST (59 minutes ago)", false), Clock.DataLine(r, t0 + 59 * Minute + 59000, 7, ny));
        Assert.Equal(("Your portfolio file · P.xlsx · as of Nov 11, 2026, 3:00 PM EST (7 days ago)", false), Clock.DataLine(r, t0 + 7 * Day, 7, ny));            // exactly seven days is not older than seven days
        Assert.Equal(("Your portfolio file · P.xlsx · as of Nov 11, 2026, 3:00 PM EST (7 days ago) · STALE: older than 7 days", true), Clock.DataLine(r, t0 + 7 * Day + 1, 7, ny));
        Assert.Equal(("Your portfolio file · P.xlsx · as of Nov 11, 2026, 3:00 PM EST (1 day ago) · STALE: older than 1 day", true), Clock.DataLine(r, t0 + Day + Hour, 1, ny));
        Assert.Equal(("Your portfolio file · P.xlsx · as of Nov 11, 2026, 3:00 PM EST (later than now: please check this computer's clock)", false), Clock.DataLine(r, t0 - 2 * Hour, 7, ny));
        Assert.Equal(("Your portfolio file · P.xlsx · as of Nov 11, 2026, 8:00 PM UTC (9 days ago) · STALE: older than 7 days", true), Clock.DataLine(r, t0 + 9 * Day, 7, TimeZoneInfo.Utc));

        // the watch rule says "stale" at the same minute as the line, for every choice of days
        foreach (int days in Clock.StaleChoices)
            foreach (long age in new[] { 0, Hour, days * Day - 1, days * Day, days * Day + 1, days * Day + 23 * Hour, (days + 1) * Day, 40 * Day })
                Assert.True(Clock.DataLine(r, t0 + age, days, ny).Stale == Flag(t0 + age, days), $"the card's line and the watch rule disagree at {age} ms with {days} days");
        var flag = WatchRules.Evaluate(r, new DateOnly(2026, 11, 18), cal, new WatchSettings(3, 7, NowMs: t0 + 7 * Day + Hour)).Single(f => f.Code == "stale-data");
        Assert.Equal("This data is 7 days old (as of Nov 11).", flag.Text);
        flag = WatchRules.Evaluate(r, new DateOnly(2026, 11, 13), cal, new WatchSettings(3, 1, NowMs: t0 + Day + Hour)).Single(f => f.Code == "stale-data");
        Assert.Equal("This data is 1 day old (as of Nov 11).", flag.Text);

        // without the moment, or without "now", the rule counts whole days as before
        Assert.Contains(WatchRules.Evaluate(Record(null) with { AsOfDate = new DateOnly(2026, 11, 1) }, new DateOnly(2026, 11, 18), cal), f => f.Code == "stale-data");
        Assert.DoesNotContain(WatchRules.Evaluate(Record(null) with { AsOfDate = new DateOnly(2026, 11, 11) }, new DateOnly(2026, 11, 18), cal), f => f.Code == "stale-data");
        Assert.Contains(WatchRules.Evaluate(r, new DateOnly(2026, 11, 19), cal), f => f.Code == "stale-data");
        Assert.Equal(new WatchSettings(3, 14, NowMs: 5), new CardContext(new DateOnly(2026, 11, 18), cal, 3, 7, null, 14, 5).Watch);        // the card, the watch list, the to-do list and a hand-off use this one
        Assert.Equal(new WatchSettings(), new CardContext(new DateOnly(2026, 11, 18), cal).Watch);

        // the sample records never change: they say so, and are never called stale
        var demo = Record(Utc("2026-07-16T11:12:00Z"), demo: true, source: "Fictional demo data · extract #412");
        Assert.Equal(("Fictional demo data · extract #412 · as of Jul 16, 2026, 7:12 AM EDT (sample data, which does not change)", false), Clock.DataLine(demo, t0 + 400 * Day, 1, ny));
        Assert.DoesNotContain(WatchRules.Evaluate(demo, new DateOnly(2027, 12, 1), cal, new WatchSettings(3, 1, NowMs: t0 + 400 * Day)), f => f.Code == "stale-data");
        var samples = new Data.MockAndaRepository(); Assert.NotEmpty(samples.Numbers);
        foreach (var sample in samples.Numbers.Select(n => samples.Find(n)!))
            Assert.Equal(("Fictional demo data · extract #412 · as of Jul 16, 2026, 7:12 AM EDT (sample data, which does not change)", false), Clock.DataLine(sample, t0, 7, ny));

        // a record that carries no moment keeps the words its source gave it
        Assert.Equal(("your portfolio file, saved Nov 11 · P.xlsx", false), Clock.DataLine(Record(null), t0, 7, ny));
        Assert.Equal(("your portfolio file, saved Nov 11 · P.xlsx", false), Clock.DataLine(Record(asOf, source: null), t0 + 40 * Day, 7, ny));
        Assert.Equal(("", false), Clock.DataLine(Record(null, words: null), t0, 7, ny));
    }

    [Fact]
    public void A_portfolio_file_gives_each_record_the_moment_it_was_saved()
    {
        string dir = Temp.NewDir(), path = Path.Combine(dir, "my sheet.csv");
        File.WriteAllText(path, "ANDA,Drug,Goal date\n123456,Testazole tablets,3/3/2027\n");
        var saved = new DateTime(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, saved);
        Clock.ZoneId = "America/New_York";
        var repo = new PortfolioRepository(path, today: () => new DateOnly(2026, 11, 18));
        var r = repo.Find("123456")!;
        Assert.Equal((saved, "Your portfolio file · my sheet.csv", false), (r.AsOfUtc, r.DataSource, r.Demo));
        Assert.Equal("Your portfolio file · my sheet.csv · as of Nov 11, 2026, 3:00 PM EST (9 days ago) · STALE: older than 7 days", Clock.DataLine(r, Clock.Ms(saved) + 9 * Day, 7, Clock.Zone).Text);
        Assert.Contains("(file saved Nov 11, 2026, 3:00 PM EST)", repo.Status.Message);
        Clock.ZoneId = "America/Los_Angeles";                                                                     // the same moment, written where the person now is
        Assert.EndsWith("as of Nov 11, 2026, 12:00 PM PST (2 days ago)", Clock.DataLine(r, Clock.Ms(saved) + 2 * Day, 7, Clock.Zone).Text);
    }

    // ---------------------------------------------------------------- stored in UTC

    [Fact]
    public void A_note_is_kept_as_its_moment_in_UTC_and_written_in_the_zone_chosen()
    {
        string dir = Temp.NewDir(), path = Path.Combine(dir, "notes.json");
        var at = new DateTime(2026, 11, 18, 20, 12, 9, DateTimeKind.Utc);
        var notes = new QuickNotesService(path, () => at.ToLocalTime());
        Assert.True(notes.Add("123456", "Called the US agent.").Ok);
        Assert.Contains("\"When\": \"2026-11-18T20:12:09Z\"", File.ReadAllText(path));      // UTC, whatever zone this computer is in
        var again = new QuickNotesService(path, () => at);
        var note = again.For("123456").Single();
        Assert.Equal(Clock.Ms(at), Clock.Ms(note.When));
        Clock.ZoneId = "America/New_York";
        Assert.Equal("Nov 18, 2026, 3:12 PM EST", Clock.Label(Clock.Ms(note.When), Clock.Zone, false));
        Clock.ZoneId = "Pacific/Honolulu";
        Assert.Equal("Nov 18, 2026, 10:12 AM HST", Clock.Label(Clock.Ms(note.When), Clock.Zone, false));

        // a note written by an earlier version, with its difference from UTC, is the same moment
        File.WriteAllText(path, "{\"Version\":1,\"Notes\":{\"123456\":[{\"Id\":\"q1\",\"Text\":\"An older note\",\"When\":\"2026-07-16T09:05:00-04:00\"}]}}");
        var old = new QuickNotesService(path, () => at).For("123456").Single();
        Assert.Equal(Ms("2026-07-16T13:05:00Z"), Clock.Ms(old.When));
        Clock.ZoneId = "America/Chicago";
        Assert.Equal("Jul 16, 2026, 8:05 AM CDT", Clock.Label(Clock.Ms(old.When), Clock.Zone, false));
    }
}
