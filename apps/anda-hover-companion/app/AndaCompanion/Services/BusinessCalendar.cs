namespace AndaCompanion.Services;

public enum DayKind { Business, Weekend, Holiday }

public sealed record Holiday(DateOnly Observed, DateOnly Actual, string Name, bool Regional = false);

/// <summary>What is notable about one date, for the weekend and holiday warning.</summary>
public sealed record DateFlag(
    DateOnly Date, DayKind Kind, string? HolidayName, int? BusinessDaysFromHoliday, bool Before,
    DateOnly PreviousBusinessDay, DateOnly NextBusinessDay)
{
    public bool IsNotable => Kind != DayKind.Business || BusinessDaysFromHoliday is not null;
}

/// <summary>
/// U.S. federal holidays and business days, computed from the statutory rules and checked against OPM's published list
/// in the tests. A holiday on a Saturday is observed on the Friday, on a Sunday on the Monday (OPM's rule for employees on
/// a Monday to Friday schedule). Not predictable, so not included unless added as an extra closure: closures declared by
/// executive order (for example the Dec 24 and 26, 2025 closing), and funding lapses. Inauguration Day is a holiday only in the
/// Washington, DC area and is marked Regional.
/// </summary>
public sealed class BusinessCalendar
{
    private readonly List<(DateOnly Date, string Name)> _extra;

    public BusinessCalendar(IEnumerable<(DateOnly Date, string Name)>? extraClosures = null) =>
        _extra = extraClosures?.ToList() ?? new();

    public static IReadOnlyList<Holiday> HolidaysOf(int year)
    {
        var list = new List<Holiday>();
        void Fixed(int month, int day, string name)
        {
            var actual = new DateOnly(year, month, day);
            var observed = actual.DayOfWeek switch
            {
                DayOfWeek.Saturday => actual.AddDays(-1),
                DayOfWeek.Sunday => actual.AddDays(1),
                _ => actual,
            };
            list.Add(new Holiday(observed, actual, name));
        }
        void Nth(int month, DayOfWeek dow, int n, string name)
        {
            var d = new DateOnly(year, month, 1);
            while (d.DayOfWeek != dow) d = d.AddDays(1);
            d = d.AddDays(7 * (n - 1));
            list.Add(new Holiday(d, d, name));
        }
        void Last(int month, DayOfWeek dow, string name)
        {
            var d = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
            while (d.DayOfWeek != dow) d = d.AddDays(-1);
            list.Add(new Holiday(d, d, name));
        }

        Fixed(1, 1, "New Year's Day");
        Nth(1, DayOfWeek.Monday, 3, "Birthday of Martin Luther King, Jr.");
        Nth(2, DayOfWeek.Monday, 3, "Washington's Birthday");
        Last(5, DayOfWeek.Monday, "Memorial Day");
        Fixed(6, 19, "Juneteenth National Independence Day");
        Fixed(7, 4, "Independence Day");
        Nth(9, DayOfWeek.Monday, 1, "Labor Day");
        Nth(10, DayOfWeek.Monday, 2, "Columbus Day");
        Fixed(11, 11, "Veterans Day");
        Nth(11, DayOfWeek.Thursday, 4, "Thanksgiving Day");
        Fixed(12, 25, "Christmas Day");
        if ((year - 1) % 4 == 0)   // 2029, 2033, ...: Inauguration Day (Jan 20, Monday if Sunday), Washington DC area only
        {
            var d = new DateOnly(year, 1, 20);
            if (d.DayOfWeek == DayOfWeek.Sunday) d = d.AddDays(1);
            list.Add(new Holiday(d, new DateOnly(year, 1, 20), "Inauguration Day (Washington, DC area)", Regional: true));
        }
        return list;
    }

    private Holiday? HolidayOn(DateOnly d)
    {
        foreach (var e in _extra) if (e.Date == d) return new Holiday(d, d, e.Name);
        // a New Year's Day on a Saturday is observed on Dec 31 of the previous year, so look one year ahead in December
        foreach (var h in HolidaysOf(d.Year).Concat(d.Month == 12 ? HolidaysOf(d.Year + 1) : Array.Empty<Holiday>()))
            if (h.Observed == d) return h;
        return null;
    }

    public DayKind KindOf(DateOnly d) =>
        d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? DayKind.Weekend
        : HolidayOn(d) is not null ? DayKind.Holiday : DayKind.Business;

    public string? HolidayName(DateOnly d) => HolidayOn(d)?.Name;

    public bool IsBusinessDay(DateOnly d) => KindOf(d) == DayKind.Business;

    public DateOnly NextBusinessDay(DateOnly d)
    {
        do d = d.AddDays(1); while (!IsBusinessDay(d));
        return d;
    }

    public DateOnly PreviousBusinessDay(DateOnly d)
    {
        do d = d.AddDays(-1); while (!IsBusinessDay(d));
        return d;
    }

    /// <summary>Business days strictly between two dates (neither end counted).</summary>
    public int BusinessDaysBetween(DateOnly a, DateOnly b)
    {
        if (a > b) (a, b) = (b, a);
        int n = 0;
        for (var d = a.AddDays(1); d < b; d = d.AddDays(1)) if (IsBusinessDay(d)) n++;
        return n;
    }

    /// <summary>
    /// Is this date on a weekend or holiday, or a business day within <paramref name="leadBusinessDays"/> business days
    /// before or after a federal holiday? 1 means "the business day right before or after".
    /// </summary>
    public DateFlag Evaluate(DateOnly date, int leadBusinessDays = 3)
    {
        var kind = KindOf(date);
        string? name = kind == DayKind.Holiday ? HolidayName(date) : null;
        int? near = null; bool before = false;
        if (kind == DayKind.Business && leadBusinessDays > 0)
        {
            // nearest holiday within a window wide enough to cover the lead plus weekends
            int window = leadBusinessDays * 2 + 4;
            for (int k = 1; k <= window && near is null; k++)
            {
                var ahead = date.AddDays(k);
                if (KindOf(ahead) == DayKind.Holiday)
                {
                    int n = BusinessDaysBetween(date, ahead) + 1;
                    if (n <= leadBusinessDays) { near = n; before = true; name = HolidayName(ahead); }
                }
                var behind = date.AddDays(-k);
                if (near is null && KindOf(behind) == DayKind.Holiday)
                {
                    int n = BusinessDaysBetween(behind, date) + 1;
                    if (n <= leadBusinessDays) { near = n; before = false; name = HolidayName(behind); }
                }
            }
        }
        return new DateFlag(date, kind, name, near, before, PreviousBusinessDay(date), NextBusinessDay(date));
    }

    /// <summary>Extra closures from a plain text file, one per line: <c>2025-12-24 Executive order closing</c>.</summary>
    public static BusinessCalendar FromFile(string? path)
    {
        var extra = new List<(DateOnly, string)>();
        try
        {
            if (path is not null && File.Exists(path))
                foreach (var line in File.ReadLines(path))
                {
                    var t = line.Trim();
                    if (t.Length < 10 || t.StartsWith('#')) continue;
                    if (DateOnly.TryParseExact(t[..10], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out var d))
                        extra.Add((d, t.Length > 10 ? t[10..].Trim() : "Closure"));
                }
        }
        catch { /* an unreadable closures file means no extra closures */ }
        return new BusinessCalendar(extra);
    }
}
