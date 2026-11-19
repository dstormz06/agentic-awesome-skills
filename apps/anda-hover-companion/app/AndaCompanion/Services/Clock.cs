using System.Globalization;

namespace AndaCompanion.Services;

/// <summary>
/// Time, as the tool writes it. Every time is stored in UTC and written in the person's time zone: the computer's own unless another is
/// chosen in Settings, with the zone's abbreviation and the right daylight time. Also: how long ago something was, in whole units, and the
/// one line that says where a piece of data is from, as of when, and that it is STALE once it is older than the days the person chose.
/// The rules are the simulation's (mockup/round31-golden.json).
/// </summary>
public static class Clock
{
    /// <summary>The zones offered, by IANA name, with the name shown. "" is the computer's own zone.</summary>
    public static readonly IReadOnlyList<(string Id, string Name)> ZoneChoices = new[]
    {
        ("", "Automatic: this computer's time zone"), ("America/New_York", "Eastern"), ("America/Chicago", "Central"), ("America/Denver", "Mountain"),
        ("America/Phoenix", "Arizona (no daylight time)"), ("America/Los_Angeles", "Pacific"), ("America/Anchorage", "Alaska"), ("Pacific/Honolulu", "Hawaii"),
        ("America/Puerto_Rico", "Atlantic (Puerto Rico)"), ("UTC", "UTC"),
    };

    /// <summary>
    /// The abbreviations the tool knows: for each zone its winter and summer abbreviation and how far its standard time is from UTC, in
    /// minutes. The zones offered, and the other zones of the United States a computer set to "Automatic" may be in.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string Standard, string Daylight, int Offset)> Abbreviations = Table();
    private static Dictionary<string, (string, string, int)> Table()
    {
        var t = new Dictionary<string, (string, string, int)>(StringComparer.Ordinal);
        void Add(string std, string dst, int offset, params string[] ids) { foreach (string id in ids) t[id] = (std, dst, offset); }
        Add("EST", "EDT", -300, "America/New_York", "America/Detroit", "America/Indiana/Indianapolis", "America/Indiana/Marengo", "America/Indiana/Petersburg", "America/Indiana/Vevay", "America/Indiana/Vincennes", "America/Indiana/Winamac", "America/Kentucky/Louisville", "America/Kentucky/Monticello");
        Add("CST", "CDT", -360, "America/Chicago", "America/Indiana/Knox", "America/Indiana/Tell_City", "America/Menominee", "America/North_Dakota/Center", "America/North_Dakota/New_Salem", "America/North_Dakota/Beulah");
        Add("MST", "MDT", -420, "America/Denver", "America/Boise"); Add("MST", "MST", -420, "America/Phoenix");
        Add("PST", "PDT", -480, "America/Los_Angeles");
        Add("AKST", "AKDT", -540, "America/Anchorage", "America/Juneau", "America/Sitka", "America/Yakutat", "America/Nome");
        Add("HST", "HST", -600, "Pacific/Honolulu"); Add("HST", "HDT", -600, "America/Adak");
        Add("AST", "AST", -240, "America/Puerto_Rico", "America/St_Thomas");
        Add("ChST", "ChST", 600, "Pacific/Guam", "Pacific/Saipan"); Add("SST", "SST", -660, "Pacific/Pago_Pago");
        Add("UTC", "UTC", 0, "UTC", "Etc/UTC");
        return t;
    }

    public static readonly IReadOnlyList<int> StaleChoices = new[] { 1, 3, 7, 14, 30 };
    public const int StaleDefault = 7;

    /// <summary>A moment the tool can write: from the year 1900 to the year 9999 (a day inside the far end, so that any zone's wall clock is too). Anything else is "time not known".</summary>
    public const long TimeMin = -2208988800000, TimeMax = 253402214399999;
    public static bool Known(long? ms) => ms is >= TimeMin and <= TimeMax;

    /// <summary>Abbreviations are written for moments in 2007 or later: the years these zones have kept the hours they keep now.</summary>
    private const long AbbrFrom = 1167609600000;
    private static readonly string[] Months = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ---------------------------------------------------------------- the zone in use

    private static string _zoneId = "";
    private static long _localAsked = long.MinValue;

    /// <summary>The zone chosen in Settings, by IANA name; "" for the computer's own. Anything that is not one of the choices is read as "".</summary>
    public static string ZoneId
    {
        get => _zoneId;
        set => _zoneId = ZoneChoices.Any(c => c.Id == value) ? value : "";
    }

    /// <summary>
    /// The zone the tool writes times in now: the one chosen, or this computer's own. A chosen zone this computer has no data for is never
    /// used under its name: the computer's own is used, and Settings says so (<see cref="ZoneSaid(string, long)"/>).
    /// </summary>
    public static TimeZoneInfo Zone => _zoneId.Length > 0 && TryFind(_zoneId, out var chosen) ? chosen : Local;

    /// <summary>
    /// This computer's zone. .NET remembers it for as long as the program runs, so it is asked afresh at most once a minute: a tray app
    /// stays open for days, and the person may travel or change the zone in Windows meanwhile.
    /// </summary>
    public static TimeZoneInfo Local
    {
        get
        {
            long tick = Environment.TickCount64;
            if (tick - Interlocked.Read(ref _localAsked) >= 60000 || tick < Interlocked.Read(ref _localAsked)) { Interlocked.Exchange(ref _localAsked, tick); TimeZoneInfo.ClearCachedData(); }
            return TimeZoneInfo.Local;
        }
    }

    /// <summary>Now, in UTC. A test may set it.</summary>
    public static Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    /// <summary>How a zone is looked up by name. A test may set it, to stand in for a computer that has no data for a zone.</summary>
    public static Func<string, TimeZoneInfo?> Find { get; set; } = FindOnThisComputer;
    public static TimeZoneInfo? FindOnThisComputer(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException or System.Security.SecurityException) { return null; }
    }
    public static bool TryFind(string? id, out TimeZoneInfo zone)
    {
        var found = string.IsNullOrEmpty(id) ? null : Find(id);
        zone = found ?? TimeZoneInfo.Utc;
        return found is not null;
    }

    /// <summary>A zone by its name. A zone that is not known is read as UTC, and is then written as UTC, never under the name asked for.</summary>
    public static TimeZoneInfo Resolve(string? id) => TryFind(id, out var zone) ? zone : TimeZoneInfo.Utc;

    /// <summary>The IANA name of a zone ("America/New_York"), also where Windows names it otherwise ("Eastern Standard Time"); the zone's own name when there is none.</summary>
    public static string IanaId(TimeZoneInfo zone)
    {
        if (zone.HasIanaId) return zone.Id;
        return TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out string? iana) && iana is not null ? iana : zone.Id;
    }

    /// <summary>A moment in UTC milliseconds. A time that is not marked as UTC is read as this computer's local time, as .NET reads it.</summary>
    public static long Ms(DateTime when) => (long)Math.Floor((when.ToUniversalTime() - DateTime.UnixEpoch).TotalMilliseconds);

    /// <summary>The wall clock of the zone in use at that moment.</summary>
    public static DateTime Wall(DateTime when) => TimeZoneInfo.ConvertTimeFromUtc(when.ToUniversalTime(), Zone);
    private static DateTime FromMs(long ms) => DateTime.UnixEpoch.AddMilliseconds(Math.Clamp(ms, TimeMin, TimeMax));

    // ---------------------------------------------------------------- the rules

    /// <summary>How far the zone is from UTC at that moment, in minutes.</summary>
    public static int OffsetMinutes(long utcMs, TimeZoneInfo zone) => (int)Math.Round(zone.GetUtcOffset(FromMs(utcMs)).TotalMinutes);

    /// <summary>
    /// "EST" or "EDT" where the tool knows the zone, the moment is in 2007 or later, and the zone is then at its standard time or an hour
    /// ahead of it. In every other case (a zone the tool has no abbreviation for, an earlier year, a zone this computer does not know) the
    /// difference from UTC ("UTC+5:30"), which is always right.
    /// </summary>
    public static string Abbreviation(long utcMs, TimeZoneInfo zone, string? ianaId = null)
    {
        int offset = OffsetMinutes(utcMs, zone);
        if (utcMs >= AbbrFrom && Abbreviations.TryGetValue(ianaId ?? IanaId(zone), out var pair))
        {
            if (offset == pair.Offset) return pair.Standard;
            if (offset == pair.Offset + 60 && pair.Daylight != pair.Standard) return pair.Daylight;
        }
        int m = Math.Abs(offset);
        return offset == 0 ? "UTC" : "UTC" + (offset < 0 ? "-" : "+") + (m / 60).ToString(Inv) + (m % 60 != 0 ? ":" + (m % 60).ToString("00", Inv) : "");
    }

    /// <summary>A moment as it is written everywhere in the tool: "Nov 18, 2026, 3:12 PM EST".</summary>
    public static string Label(long? utcMs, TimeZoneInfo zone, bool seconds, string? ianaId = null)
    {
        if (!Known(utcMs)) return "time not known";
        long ms = utcMs!.Value; var t = TimeZoneInfo.ConvertTimeFromUtc(FromMs(ms), zone);
        return $"{Months[t.Month - 1]} {t.Day.ToString(Inv)}, {t.Year.ToString(Inv)}, {((t.Hour + 11) % 12 + 1).ToString(Inv)}:{t.Minute.ToString("00", Inv)}{(seconds ? ":" + t.Second.ToString("00", Inv) : "")} {(t.Hour >= 12 ? "PM" : "AM")} {Abbreviation(ms, zone, ianaId)}";
    }

    /// <summary>The same by the zone's name, as the shared cases give it. A name that is not known is read as UTC.</summary>
    public static string Label(long? utcMs, string? zoneId, bool seconds) => TryFind(zoneId, out var zone) ? Label(utcMs, zone, seconds, zoneId) : Label(utcMs, TimeZoneInfo.Utc, seconds, "UTC");

    /// <summary>A moment in the zone in use, to the second: for the event log and the backup copies.</summary>
    public static string Label(DateTime utc, bool seconds = true) => Label(Ms(DateTime.SpecifyKind(utc, DateTimeKind.Utc)), Zone, seconds);

    /// <summary>A time of day alone, for a moment of today: "3:12 PM EST".</summary>
    public static string Time(long? utcMs, TimeZoneInfo zone, bool seconds, string? ianaId = null)
    {
        if (!Known(utcMs)) return "time not known";
        long ms = utcMs!.Value; var t = TimeZoneInfo.ConvertTimeFromUtc(FromMs(ms), zone);
        return $"{((t.Hour + 11) % 12 + 1).ToString(Inv)}:{t.Minute.ToString("00", Inv)}{(seconds ? ":" + t.Second.ToString("00", Inv) : "")} {(t.Hour >= 12 ? "PM" : "AM")} {Abbreviation(ms, zone, ianaId)}";
    }
    public static string Time(long? utcMs, string? zoneId, bool seconds) => TryFind(zoneId, out var zone) ? Time(utcMs, zone, seconds, zoneId) : Time(utcMs, TimeZoneInfo.Utc, seconds, "UTC");

    /// <summary>A time of day in the zone in use. A local time of this computer is first read as the moment it was.</summary>
    public static string Time(DateTime when, bool seconds = false) => Time(Ms(when), Zone, seconds);
    public static string Time(DateTimeOffset when, bool seconds = false) => Time(when.ToUnixTimeMilliseconds(), Zone, seconds);

    /// <summary>How long ago, in whole units, never rounded up: 59 seconds is "just now", 119 minutes is "1 hour ago".</summary>
    public static string Relative(long? thenMs, long? nowMs)
    {
        if (!Known(thenMs) || !Known(nowMs)) return "time not known";
        long then = thenMs!.Value, now = nowMs!.Value;
        long d = now - then;
        static string N(long count, string unit) => $"{count.ToString(Inv)} {unit}{(count == 1 ? "" : "s")} ago";
        if (d < -60000) return "later than now: please check this computer's clock";
        if (d < 60000) return "just now";
        long minutes = d / 60000; if (minutes < 60) return N(minutes, "minute");
        long hours = minutes / 60; if (hours < 24) return N(hours, "hour");
        return N(hours / 24, "day");
    }

    /// <summary>One line for a piece of data: where it is from, as of when, how long ago, and STALE once it is older than the days the person chose.</summary>
    public static (string Text, bool Stale) Fresh(string source, long? asOfMs, long nowMs, int staleDays, TimeZoneInfo zone, string? ianaId = null)
    {
        int days = StaleChoices.Contains(staleDays) ? staleDays : StaleDefault;
        if (!Known(asOfMs) || !Known(nowMs)) return ($"{source} · time not known", false);
        long asOf = asOfMs!.Value;
        bool stale = nowMs - asOf > days * 86400000L;
        return ($"{source} · as of {Label(asOf, zone, false, ianaId)} ({Relative(asOf, nowMs)})" + (stale ? $" · STALE: older than {days.ToString(Inv)} {(days == 1 ? "day" : "days")}" : ""), stale);
    }
    public static (string Text, bool Stale) Fresh(string source, long? asOfMs, long nowMs, int staleDays, string? zoneId) =>
        TryFind(zoneId, out var zone) ? Fresh(source, asOfMs, nowMs, staleDays, zone, zoneId) : Fresh(source, asOfMs, nowMs, staleDays, TimeZoneInfo.Utc, "UTC");

    public static readonly IReadOnlyList<int> RefStaleChoices = new[] { 14, 30, 45, 60, 90 };
    public const int RefStaleDefault = 45;

    /// <summary>
    /// A reference file (Drugs@FDA) changes about monthly, so it has its own number of days: when it was saved, how long ago, and STALE once
    /// it is older than those days. The day is the day in the zone given.
    /// </summary>
    public static (string Text, bool Stale) RefSaved(long? savedMs, long nowMs, int days, TimeZoneInfo zone)
    {
        int n = RefStaleChoices.Contains(days) ? days : RefStaleDefault;
        if (!Known(savedMs) || !Known(nowMs)) return ("saved: time not known", false);
        long saved = savedMs!.Value; var t = TimeZoneInfo.ConvertTimeFromUtc(FromMs(saved), zone);
        bool stale = nowMs - saved > n * 86400000L;
        return ($"saved {Months[t.Month - 1]} {t.Day.ToString(Inv)}, {t.Year.ToString(Inv)} ({Relative(saved, nowMs)})" + (stale ? $" · STALE: older than {n.ToString(Inv)} days" : ""), stale);
    }
    public static (string Text, bool Stale) RefSaved(long? savedMs, long nowMs, int days, string? zoneId) => RefSaved(savedMs, nowMs, days, Resolve(zoneId));
    public static string RefStaleSaid(int days) => $"A Drugs@FDA file older than {days.ToString(Inv)} days is now marked STALE.";

    /// <summary>
    /// The line for one record. A record of the person's own says how old it is and is marked STALE in time. The sample records never change,
    /// so they say so instead of counting days. A record that carries no moment keeps the words its source gave it.
    /// </summary>
    public static (string Text, bool Stale) DataLine(Models.AndaRecord r, long nowMs, int staleDays, TimeZoneInfo zone)
    {
        if (r.DataSource is not { Length: > 0 } source || r.AsOfUtc is not { } asOf) return (r.DataAsOf ?? "", false);
        return r.Demo ? ($"{source} · as of {Label(Ms(asOf), zone, false)} (sample data, which does not change)", false) : Fresh(source, Ms(asOf), nowMs, staleDays, zone);
    }

    /// <summary>What Settings says after the zone was chosen. A zone this computer has no data for is said plainly: its own is used instead.</summary>
    /// <remarks>This overload: for a moment, with the computer's zone given by name, as the shared cases give it.</remarks>
    public static string ZoneSaid(string chosen, string computerId, long nowMs)
    {
        _ = TryFind(computerId, out var computer);
        return Said(chosen, computerId, Abbreviation(nowMs, computer, computerId), nowMs);
    }

    /// <summary>The same for this computer: its zone is named as the computer names it (on Windows, "Eastern Standard Time").</summary>
    public static string ZoneSaid(string chosen, long nowMs) { var local = Local; return Said(chosen, local.Id, Abbreviation(nowMs, local), nowMs); }

    private static string Said(string chosen, string computerName, string computerAbbreviation, long nowMs)
    {
        string own = $"this computer's time zone ({computerName.Replace('_', ' ')}, {computerAbbreviation})";
        var choice = ZoneChoices.FirstOrDefault(c => c.Id == chosen && c.Id.Length > 0);
        if (string.IsNullOrEmpty(choice.Id)) return $"Times are now written in {own}. No stored time was changed.";
        if (!TryFind(choice.Id, out var zone)) return $"This computer has no data for {choice.Name} time, so times are written in {own}. No stored time was changed.";
        return $"Times are now written in {choice.Name} time ({Abbreviation(nowMs, zone, choice.Id)}). No stored time was changed.";
    }
    public static string StaleSaid(int days) => $"Data older than {days.ToString(Inv)} {(days == 1 ? "day" : "days")} is now marked STALE.";

    /// <summary>"Now: Thursday, Jul 16, 2026, 10:42 AM EDT": the clock in Settings.</summary>
    public static string NowLine(long nowMs, TimeZoneInfo zone) =>
        $"Now: {TimeZoneInfo.ConvertTimeFromUtc(FromMs(nowMs), zone).DayOfWeek}, {Label(nowMs, zone, false)}";
}
