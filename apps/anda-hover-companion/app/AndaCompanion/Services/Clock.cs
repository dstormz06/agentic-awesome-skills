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
        ("America/Phoenix", "Arizona (Mountain, no daylight time)"), ("America/Los_Angeles", "Pacific"), ("America/Anchorage", "Alaska"), ("Pacific/Honolulu", "Hawaii"),
        ("America/Puerto_Rico", "Atlantic (Puerto Rico)"), ("UTC", "UTC"),
    };

    /// <summary>The abbreviations of the zones offered, in winter and in summer.</summary>
    public static readonly IReadOnlyDictionary<string, (string Standard, string Daylight)> Abbreviations = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
    {
        ["America/New_York"] = ("EST", "EDT"), ["America/Chicago"] = ("CST", "CDT"), ["America/Denver"] = ("MST", "MDT"), ["America/Phoenix"] = ("MST", "MST"),
        ["America/Los_Angeles"] = ("PST", "PDT"), ["America/Anchorage"] = ("AKST", "AKDT"), ["Pacific/Honolulu"] = ("HST", "HST"), ["America/Puerto_Rico"] = ("AST", "AST"),
        ["UTC"] = ("UTC", "UTC"), ["Etc/UTC"] = ("UTC", "UTC"),
    };

    public static readonly IReadOnlyList<int> StaleChoices = new[] { 1, 3, 7, 14, 30 };
    public const int StaleDefault = 7;
    private static readonly string[] Months = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ---------------------------------------------------------------- the zone in use

    private static string _zoneId = "";

    /// <summary>The zone chosen in Settings, by IANA name; "" for the computer's own. Anything that is not one of the choices is read as "".</summary>
    public static string ZoneId
    {
        get => _zoneId;
        set => _zoneId = ZoneChoices.Any(c => c.Id == value) ? value : "";
    }

    /// <summary>The zone the tool writes times in now.</summary>
    public static TimeZoneInfo Zone => _zoneId.Length == 0 ? TimeZoneInfo.Local : Resolve(_zoneId);

    /// <summary>Now, in UTC. A test may set it.</summary>
    public static Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    /// <summary>A zone by its name. A zone that is not known is read as UTC.</summary>
    public static TimeZoneInfo Resolve(string? id)
    {
        try { return string.IsNullOrEmpty(id) ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { return TimeZoneInfo.Utc; }
    }

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
    private static DateTime FromMs(long ms) => DateTime.UnixEpoch.AddMilliseconds(ms);

    // ---------------------------------------------------------------- the rules

    /// <summary>How far the zone is from UTC at that moment, in minutes.</summary>
    public static int OffsetMinutes(long utcMs, TimeZoneInfo zone) => (int)Math.Round(zone.GetUtcOffset(FromMs(utcMs)).TotalMinutes);

    /// <summary>"EST" or "EDT" for the zones offered; for any other zone the difference from UTC ("UTC+5:30"), which is always right.</summary>
    public static string Abbreviation(long utcMs, TimeZoneInfo zone, string? ianaId = null)
    {
        int offset = OffsetMinutes(utcMs, zone);
        if (Abbreviations.TryGetValue(ianaId ?? IanaId(zone), out var pair))
        {
            int year = TimeZoneInfo.ConvertTimeFromUtc(FromMs(utcMs), zone).Year;
            int january = OffsetMinutes(Ms(new DateTime(year, 1, 1, 12, 0, 0, DateTimeKind.Utc)), zone);      // summer time is whatever differs from January
            return offset != january ? pair.Daylight : pair.Standard;
        }
        int m = Math.Abs(offset);
        return offset == 0 ? "UTC" : "UTC" + (offset < 0 ? "-" : "+") + (m / 60).ToString(Inv) + (m % 60 != 0 ? ":" + (m % 60).ToString("00", Inv) : "");
    }

    /// <summary>A moment as it is written everywhere in the tool: "Nov 18, 2026, 3:12 PM EST".</summary>
    public static string Label(long? utcMs, TimeZoneInfo zone, bool seconds, string? ianaId = null)
    {
        if (utcMs is not { } ms) return "time not known";
        var t = TimeZoneInfo.ConvertTimeFromUtc(FromMs(ms), zone);
        return $"{Months[t.Month - 1]} {t.Day.ToString(Inv)}, {t.Year.ToString(Inv)}, {((t.Hour + 11) % 12 + 1).ToString(Inv)}:{t.Minute.ToString("00", Inv)}{(seconds ? ":" + t.Second.ToString("00", Inv) : "")} {(t.Hour >= 12 ? "PM" : "AM")} {Abbreviation(ms, zone, ianaId)}";
    }

    /// <summary>The same by the zone's name, as the shared cases give it. A name that is not known is read as UTC.</summary>
    public static string Label(long? utcMs, string? zoneId, bool seconds) => Label(utcMs, Resolve(zoneId), seconds, zoneId ?? "");

    /// <summary>A moment in the zone in use, to the second: for the event log and the backup copies.</summary>
    public static string Label(DateTime utc, bool seconds = true) => Label(Ms(DateTime.SpecifyKind(utc, DateTimeKind.Utc)), Zone, seconds);

    /// <summary>A time of day alone, for a moment of today: "3:12 PM EST".</summary>
    public static string Time(long? utcMs, TimeZoneInfo zone, bool seconds, string? ianaId = null)
    {
        if (utcMs is not { } ms) return "time not known";
        var t = TimeZoneInfo.ConvertTimeFromUtc(FromMs(ms), zone);
        return $"{((t.Hour + 11) % 12 + 1).ToString(Inv)}:{t.Minute.ToString("00", Inv)}{(seconds ? ":" + t.Second.ToString("00", Inv) : "")} {(t.Hour >= 12 ? "PM" : "AM")} {Abbreviation(ms, zone, ianaId)}";
    }
    public static string Time(long? utcMs, string? zoneId, bool seconds) => Time(utcMs, Resolve(zoneId), seconds, zoneId ?? "");

    /// <summary>A time of day in the zone in use. A local time of this computer is first read as the moment it was.</summary>
    public static string Time(DateTime when, bool seconds = false) => Time(Ms(when), Zone, seconds);
    public static string Time(DateTimeOffset when, bool seconds = false) => Time(when.ToUnixTimeMilliseconds(), Zone, seconds);

    /// <summary>How long ago, in whole units, never rounded up: 59 seconds is "just now", 119 minutes is "1 hour ago".</summary>
    public static string Relative(long? thenMs, long? nowMs)
    {
        if (thenMs is not { } then || nowMs is not { } now) return "time not known";
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
        if (asOfMs is not { } asOf) return ($"{source} · time not known", false);
        bool stale = nowMs - asOf > days * 86400000L;
        return ($"{source} · as of {Label(asOf, zone, false, ianaId)} ({Relative(asOf, nowMs)})" + (stale ? $" · STALE: older than {days.ToString(Inv)} {(days == 1 ? "day" : "days")}" : ""), stale);
    }
    public static (string Text, bool Stale) Fresh(string source, long? asOfMs, long nowMs, int staleDays, string? zoneId) => Fresh(source, asOfMs, nowMs, staleDays, Resolve(zoneId), zoneId ?? "");

    /// <summary>
    /// The line for one record. A record of the person's own says how old it is and is marked STALE in time. The sample records never change,
    /// so they say so instead of counting days. A record that carries no moment keeps the words its source gave it.
    /// </summary>
    public static (string Text, bool Stale) DataLine(Models.AndaRecord r, long nowMs, int staleDays, TimeZoneInfo zone)
    {
        if (r.DataSource is not { Length: > 0 } source || r.AsOfUtc is not { } asOf) return (r.DataAsOf ?? "", false);
        return r.Demo ? ($"{source} · as of {Label(Ms(asOf), zone, false)} (sample data, which does not change)", false) : Fresh(source, Ms(asOf), nowMs, staleDays, zone);
    }

    /// <summary>What Settings says after the zone was chosen.</summary>
    public static string ZoneSaid(string zoneId, long nowMs)
    {
        var choice = ZoneChoices.FirstOrDefault(c => c.Id == zoneId);
        if (!string.IsNullOrEmpty(choice.Id)) return $"Times are now written in {choice.Name} time ({Abbreviation(nowMs, Resolve(choice.Id), choice.Id)}). No stored time was changed.";
        var local = TimeZoneInfo.Local;
        return $"Times are now written in this computer's time zone ({IanaId(local).Replace('_', ' ')}, {Abbreviation(nowMs, local)}). No stored time was changed.";
    }
    public static string StaleSaid(int days) => $"Data older than {days.ToString(Inv)} {(days == 1 ? "day" : "days")} is now marked STALE.";

    /// <summary>"Now: Thursday, Jul 16, 2026, 10:42 AM EDT": the clock in Settings.</summary>
    public static string NowLine(long nowMs, TimeZoneInfo zone) =>
        $"Now: {TimeZoneInfo.ConvertTimeFromUtc(FromMs(nowMs), zone).DayOfWeek}, {Label(nowMs, zone, false)}";
}
