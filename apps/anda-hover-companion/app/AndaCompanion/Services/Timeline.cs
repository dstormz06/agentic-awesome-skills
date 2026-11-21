using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AndaCompanion.Services;

/// <summary>
/// Round 42: the limits of the timeline checks, each one of its choices. Part of the settings and of a backup; nothing here is sent.
/// The simulation holds the same defaults, choices and labels (mockup/round42-golden.json).
/// </summary>
public sealed class TimelineSettings
{
    public int GoalClose { get; set; } = 10;
    public int NoForecast { get; set; } = 30;
    public int ThinMargin { get; set; } = 5;
    public int CrunchCount { get; set; } = 3;
    public int CrunchHorizon { get; set; } = 90;
    public int StatusDays { get; set; } = 30;
    public int TodoSoon { get; set; } = 2;

    public static readonly IReadOnlyList<(string Key, int[] Choices, string Label)> Limits = new (string, int[], string)[]
    {
        ("goalClose", new[] { 5, 10, 15 }, "Goal date is close: within this many business days"),
        ("noForecast", new[] { 14, 30, 60 }, "No forecast: when the goal date is within this many days"),
        ("thinMargin", new[] { 3, 5, 10 }, "Thin margin: forecast within this many business days of the goal"),
        ("crunchCount", new[] { 2, 3, 4, 5 }, "Crunch week: this many goal dates in one week"),
        ("crunchHorizon", new[] { 30, 60, 90 }, "Crunch weeks: look this many days ahead"),
        ("statusDays", new[] { 14, 30, 60 }, "Status unchanged: after this many days"),
        ("todoSoon", new[] { 1, 2, 5 }, "To-do item due: within this many business days"),
    };

    public int Get(string key) => key switch
    {
        "goalClose" => GoalClose, "noForecast" => NoForecast, "thinMargin" => ThinMargin, "crunchCount" => CrunchCount,
        "crunchHorizon" => CrunchHorizon, "statusDays" => StatusDays, "todoSoon" => TodoSoon, _ => throw new ArgumentException(key),
    };

    public void Set(string key, int value)
    {
        switch (key)
        {
            case "goalClose": GoalClose = value; break; case "noForecast": NoForecast = value; break; case "thinMargin": ThinMargin = value; break;
            case "crunchCount": CrunchCount = value; break; case "crunchHorizon": CrunchHorizon = value; break; case "statusDays": StatusDays = value; break;
            case "todoSoon": TodoSoon = value; break; default: throw new ArgumentException(key);
        }
    }

    /// <summary>Each limit kept only when it is one of its choices; otherwise its usual value.</summary>
    public static TimelineSettings Normalize(TimelineSettings? s)
    {
        var fresh = new TimelineSettings();
        if (s is null) return fresh;
        foreach (var (key, choices, _) in Limits) if (choices.Contains(s.Get(key))) fresh.Set(key, s.Get(key));
        return fresh;
    }
}

/// <summary>One ANDA as the timeline checks read it: dates written YYYY-MM-DD, as the simulation's.</summary>
public sealed record TimelineRecord(string Number, string? Drug, string? Goal, string? Forecast, string? Status = null);

/// <summary>One to-do item of the ANDA to-do list, as the checks read it.</summary>
public sealed record TimelineTodo(string? Id, string? Number, string? Title, string? Due, bool Done);

/// <summary>Everything the checks look at: today, the records, the look kept when Today last closed, the to-do items, and when each status was first seen.</summary>
public sealed record TimelineInput(string Today, IReadOnlyList<TimelineRecord> Records, IReadOnlyDictionary<string, (string? Goal, string? Forecast)>? Look,
    IReadOnlyList<TimelineTodo> Todos, IReadOnlyDictionary<string, string> StatusSince, TimelineSettings? Settings);

/// <summary>What a check found. <see cref="Horizon"/> is "now" (immediate) or "later" (longer term); <see cref="Sev"/> is bad, warn or info.</summary>
public sealed record TimelineAlert(string Key, string? Number, string Code, string Horizon, string Sev, string? Date, string Text);

/// <summary>
/// Round 42: the timeline checks. Fixed rules over the person's own sheet, to-do list and last look, with no AI and nothing sent. They say
/// what the data shows and never decide. Same answers as the simulation's timelineAlerts (mockup/round42-golden.json).
/// </summary>
public static class Timeline
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public const int CalendarDays = 180, NewsDays = 14, NewsMax = 240, TodayMax = 8;

    /// <summary>The codes of an immediate alert; every other code is longer term.</summary>
    public static readonly IReadOnlySet<string> Now = new HashSet<string>(StringComparer.Ordinal)
    {
        "goal-passed", "goal-close", "forecast-after-goal", "no-forecast", "todo-overdue", "todo-soon", "goal-earlier",
        "sheet-flag", "goal-date-calendar", "forecast-date-calendar", "attention",
    };

    public static string Horizon(string code) => Now.Contains(code) ? "now" : "later";

    // ---------- the sentences, the simulation's TL_SAID ----------
    public const string TodayTitle = "NEEDS A LOOK · MOST URGENT FIRST";
    public const string TodayNone = "Nothing in your data meets a timeline check today. Silence does not mean all is well.";
    public static string TodayMore(int n) => $"{n} more ANDA{(n == 1 ? "" : "s")} in News and alerts.";
    public static string SaidCopied(int now, int later) => $"Copied the summary: {now} immediate, {later} longer term. Please read it before you send it.";
    public static string SaidCalendar(int n) => $"{n} goal date{(n == 1 ? "" : "s")} in the next 180 days. In the Windows app, Save writes this as a calendar file (.ics) where you choose, and Outlook can open it; it holds each ANDA number and its goal date, nothing else. This page cannot save files, so the text is below.";
    public const string Hint = "The timeline checks run on this PC over your own sheet and to-do list, by fixed rules, with no AI. They say what the data shows and never decide. They appear in Today and in News and alerts, and near the clock when the notices are on.";

    /// <summary>A date written YYYY-MM-DD that exists, or null (2026-02-30 is refused).</summary>
    public static DateOnly? Iso(string? s) =>
        s is { Length: 10 } && DateOnly.TryParseExact(s, "yyyy-MM-dd", Inv, DateTimeStyles.None, out var d) ? d : null;

    public static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", Inv);

    private static string S(DateOnly d) => WatchRules.Short(d);
    private static string Plural(int n, string w) => $"{n} {w}{(n == 1 ? "" : "s")}";

    /// <summary>Business days after a, up to and including b (0 when b is a, or when none lies between and b is closed).</summary>
    public static int BizUntil(BusinessCalendar cal, DateOnly a, DateOnly b) => b <= a ? 0 : cal.BusinessDaysBetween(a, b) + (cal.IsBusinessDay(b) ? 1 : 0);

    /// <summary>Six digits 0-9, nothing else (an ANDA number).</summary>
    public static bool IsNumber(string? s) => s is { Length: 6 } && s.All(c => c is >= '0' and <= '9');

    public static IReadOnlyList<TimelineAlert> Alerts(TimelineInput input, BusinessCalendar cal)
    {
        var set = TimelineSettings.Normalize(input.Settings);
        var today = Iso(input.Today) ?? throw new ArgumentException("today must be YYYY-MM-DD", nameof(input));
        var outList = new List<TimelineAlert>();
        void Add(string? number, string code, string sev, DateOnly? date, string text) =>
            outList.Add(new($"{number ?? "-"}|{code}|{(date is { } d ? Iso(d) : "-")}", number, code, Horizon(code), sev, date is { } x ? Iso(x) : null, text));

        var records = input.Records.Where(r => r is not null && IsNumber(r.Number)).ToList();
        foreach (var r in records)
        {
            DateOnly? goal = Iso(r.Goal), fc = Iso(r.Forecast);
            bool close = false;
            if (goal is { } g)
            {
                int days = g.DayNumber - today.DayNumber;
                if (days < 0) Add(r.Number, "goal-passed", "bad", g, $"The goal date ({S(g)}) passed {Plural(-days, "day")} ago.");
                else
                {
                    int n = BizUntil(cal, today, g);
                    if (days == 0) { close = true; Add(r.Number, "goal-close", "warn", g, $"The goal date is today ({S(g)})."); }
                    else if (n <= set.GoalClose) { close = true; Add(r.Number, "goal-close", "warn", g, $"The goal date ({S(g)}) is in {Plural(n, "business day")}."); }
                    if (fc is null && days <= set.NoForecast)
                        Add(r.Number, "no-forecast", "warn", g, days == 0 ? $"There is no forecast date, and the goal date is today ({S(g)})."
                            : $"There is no forecast date, and the goal date ({S(g)}) is in {Plural(days, "day")}.");
                    if (fc is { } f && f > g)
                    {
                        int late = BizUntil(cal, g, f);
                        Add(r.Number, "forecast-after-goal", "bad", f, late == 0 ? $"The forecast ({S(f)}) is after the goal date ({S(g)}), with no business day between them."
                            : $"The forecast ({S(f)}) is {Plural(late, "business day")} after the goal date ({S(g)}).");
                    }
                    if (fc is { } f2 && f2 <= g && !close)
                    {
                        int m = BizUntil(cal, f2, g);
                        if (m <= set.ThinMargin)
                            Add(r.Number, "thin-margin", "warn", g, m == 0 ? $"The forecast is the goal date itself ({S(g)}), with no margin."
                                : $"The forecast ({S(f2)}) is only {Plural(m, "business day")} before the goal date ({S(g)}).");
                    }
                }
            }
            if (input.Look is { } look && look.TryGetValue(r.Number, out var was))
            {
                DateOnly? g0 = Iso(was.Goal), f0 = Iso(was.Forecast);
                if (g0 is { } a && goal is { } b && b < a)
                    Add(r.Number, "goal-earlier", "bad", b, $"The goal date moved {Plural(a.DayNumber - b.DayNumber, "day")} earlier since you last looked: {S(a)} to {S(b)}.");
                if (f0 is { } c && fc is { } e && e > c)
                    Add(r.Number, "forecast-slipped", "warn", e, $"The forecast slipped {Plural(e.DayNumber - c.DayNumber, "day")} since you last looked: {S(c)} to {S(e)}.");
            }
            if (input.StatusSince.TryGetValue(r.Number, out var sinceText) && Iso(sinceText) is { } since && since <= today)
            {
                int d = today.DayNumber - since.DayNumber;
                if (d >= set.StatusDays) Add(r.Number, "status-unchanged", "warn", since, $"The status has not changed in {d} days (since {S(since)}).");
            }
        }
        foreach (var t in input.Todos)
        {
            if (t is null || t.Done || Iso(t.Due) is not { } due) continue;
            string? number = IsNumber(t.Number) ? t.Number : null;
            string title = Cut(WithoutFormat(t.Title ?? ""), 80);
            void K(string code, string sev, string text) =>
                outList.Add(new($"{number ?? "-"}|{code}|{Hash((t.Id ?? "") + "|" + title)}|{Iso(due)}", number, code, "now", sev, Iso(due), text));
            if (due < today) K("todo-overdue", "bad", $"To-do item overdue since {S(due)}: {title}");
            else if (due == today) K("todo-soon", "warn", $"To-do item due today: {title}");
            else if (BizUntil(cal, today, due) <= set.TodoSoon) K("todo-soon", "warn", $"To-do item due {S(due)}: {title}");
        }
        // a crunch week: goal dates from today to the horizon, grouped by the Monday of their week
        var weeks = new SortedDictionary<DateOnly, List<string>>();
        foreach (var r in records)
        {
            if (Iso(r.Goal) is not { } g || g < today || g > today.AddDays(set.CrunchHorizon)) continue;
            var monday = g.AddDays(-(((int)g.DayOfWeek + 6) % 7));
            if (!weeks.TryGetValue(monday, out var list)) weeks[monday] = list = new();
            list.Add(r.Number);
        }
        foreach (var (monday, nums) in weeks)
        {
            if (nums.Count < set.CrunchCount) continue;
            var list = nums.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            outList.Add(new($"-|crunch-week|{Iso(monday)}|{list.Count}", null, "crunch-week", "later", "warn", Iso(monday),
                $"Week of {S(monday)}: {list.Count} goal dates (ANDA {string.Join(", ", list)})."));
        }
        return Sort(outList);
    }

    /// <summary>The card's codes that the timeline lists too: a mark in the sheet, a date on a closed day, and old data.</summary>
    public static readonly IReadOnlyList<string> FromCardCodes = new[] { "sheet-flag", "goal-date-calendar", "forecast-date-calendar", "stale-data" };

    /// <summary>
    /// The card's own facts that are not about the timeline itself, as alerts beside the timeline checks: those codes (not when only for
    /// information), and the record's open item. The simulation's allAlerts does the same.
    /// </summary>
    public static IEnumerable<TimelineAlert> FromCard(Models.AndaRecord r, IEnumerable<WatchFlag> flags)
    {
        foreach (var f in flags.Where(f => FromCardCodes.Contains(f.Code) && f.Severity != WatchSeverity.Info))
            yield return new($"{r.Number}|{f.Code}|-", r.Number, f.Code, Horizon(f.Code), f.Severity == WatchSeverity.Bad ? "bad" : "warn", null, f.Text);
        if (r.Attention is { Text: { Length: > 0 } t })
            yield return new($"{r.Number}|attention|{Hash(t)}", r.Number, "attention", "now", "warn", null, $"Needs action: {t}");
    }

    private static int SevRank(string s) => s switch { "bad" => 2, "warn" => 1, _ => 0 };

    /// <summary>Immediate first, then the most serious, the soonest date (none last), the number (none last), the code and the key; plain character order.</summary>
    public static List<TimelineAlert> Sort(IEnumerable<TimelineAlert> list)
    {
        var l = list.ToList();
        l.Sort((a, b) =>
        {
            int c = a.Horizon == b.Horizon ? 0 : a.Horizon == "now" ? -1 : 1;
            if (c == 0) c = SevRank(b.Sev) - SevRank(a.Sev);
            if (c == 0) c = (a.Date is null ? 1 : 0) - (b.Date is null ? 1 : 0);
            if (c == 0) c = string.CompareOrdinal(a.Date ?? "", b.Date ?? "");
            if (c == 0) c = (a.Number is null ? 1 : 0) - (b.Number is null ? 1 : 0);
            if (c == 0) c = string.CompareOrdinal(a.Number ?? "", b.Number ?? "");
            if (c == 0) c = string.CompareOrdinal(a.Code, b.Code);
            if (c == 0) c = string.CompareOrdinal(a.Key, b.Key);
            return Math.Sign(c);
        });
        return l;
    }

    /// <summary>The line an alert is shown with: its ANDA first, when it has one.</summary>
    public static string Line(TimelineAlert a) => a.Number is { } n ? $"ANDA {n}: {a.Text}" : a.Text;

    /// <summary>A summary to copy for a team lead: plain text, by the same rules; drug names from the person's own sheet.</summary>
    public static string Summary(IReadOnlyList<TimelineAlert> alerts, string today, Func<string, string?>? drugOf)
    {
        var t = Iso(today) ?? throw new ArgumentException("today must be YYYY-MM-DD", nameof(today));
        string Item(TimelineAlert a) => "- " + (a.Number is { } n ? $"ANDA {n}{(drugOf?.Invoke(n) is { Length: > 0 } d ? $" ({Cut(d, 60)})" : "")}: {a.Text}" : a.Text);
        var now = alerts.Where(a => a.Horizon == "now").ToList(); var later = alerts.Where(a => a.Horizon == "later").ToList();
        var parts = new List<string> { $"ANDA timeline summary as of {S(t)}, {t.Year.ToString(Inv)}, from my portfolio sheet, checked by fixed rules (no AI).", "" };
        if (alerts.Count == 0) parts.Add("No ANDA in my portfolio sheet meets a timeline rule today.");
        if (now.Count > 0) { parts.Add("Immediate"); parts.AddRange(now.Select(Item)); }
        if (now.Count > 0 && later.Count > 0) parts.Add("");
        if (later.Count > 0) { parts.Add("Longer term"); parts.AddRange(later.Select(Item)); }
        parts.Add(""); parts.Add("Please check each record before acting on this summary.");
        return string.Join("\n", parts);
    }

    /// <summary>The goal dates as a calendar file (iCalendar): one all-day event each, the ANDA number only, from today to 180 days ahead.</summary>
    public static (string Text, int Count) Calendar(IEnumerable<TimelineRecord> records, string today)
    {
        var t = Iso(today) ?? throw new ArgumentException("today must be YYYY-MM-DD", nameof(today));
        static string C(DateOnly d) => d.ToString("yyyyMMdd", Inv);
        var events = records.Where(r => IsNumber(r.Number)).Select(r => (N: r.Number, G: Iso(r.Goal)))
            .Where(e => e.G is { } g && g >= t && g <= t.AddDays(CalendarDays)).Select(e => (e.N, G: e.G!.Value))
            .OrderBy(e => e.G).ThenBy(e => e.N, StringComparer.Ordinal).ToList();
        var lines = new List<string> { "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//ANDA Companion (Notula)//Goal dates//EN", "CALSCALE:GREGORIAN" };
        foreach (var e in events)
            lines.AddRange(new[] { "BEGIN:VEVENT", $"UID:anda-{e.N}-goal-{C(e.G)}@anda-companion.invalid", $"DTSTAMP:{C(t)}T000000Z", $"DTSTART;VALUE=DATE:{C(e.G)}",
                $"DTEND;VALUE=DATE:{C(e.G.AddDays(1))}", $"SUMMARY:Goal date: ANDA {e.N}", "DESCRIPTION:From ANDA Companion (Notula)\\, read from my portfolio sheet. Please check the record before acting.",
                "TRANSP:TRANSPARENT", "END:VEVENT" });
        lines.Add("END:VCALENDAR");
        return (string.Join("\r\n", lines) + "\r\n", events.Count);
    }

    // ---------- text, exactly as the simulation's ----------

    /// <summary>The characters JavaScript's \s and trim() count as white space (not U+0085, which .NET would add).</summary>
    private const string JsWhite = "\t\n\v\f\r \u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000\ufeff";
    internal const string JsSpace = @"[\t\n\v\f\r \u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000\ufeff]";
    private static readonly Regex Runs = new(JsSpace + "+", RegexOptions.CultureInvariant);

    /// <summary>The simulation's cut(): white space folded to one space and trimmed, then at most n characters, the last an ellipsis.</summary>
    public static string Cut(string t, int n)
    {
        t = Runs.Replace(t, " ").Trim(JsWhite.ToCharArray());
        return t.Length > n ? t[..(n - 1)].TrimEnd(JsWhite.ToCharArray()) + "…" : t;
    }

    /// <summary>Removes every character of the Unicode category Format (Cf), as the simulation's /\p{Cf}/gu; a lone surrogate is kept, as there.</summary>
    public static string WithoutFormat(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(char.ConvertToUtf32(s[i], s[i + 1])) != UnicodeCategory.Format) sb.Append(s[i]).Append(s[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(s[i]) || CharUnicodeInfo.GetUnicodeCategory(s[i]) != UnicodeCategory.Format) sb.Append(s[i]);
        }
        return sb.ToString();
    }

    /// <summary>The simulation's stableHash: FNV-1a over the first UTF-16 unit of each code point of the trimmed, lower-cased text.</summary>
    public static string Hash(string s)
    {
        uint h = 2166136261;
        string t = s.Trim(JsWhite.ToCharArray()).ToLowerInvariant();
        for (int i = 0; i < t.Length; i++)
        {
            h ^= t[i]; h *= 16777619;
            if (char.IsHighSurrogate(t[i]) && i + 1 < t.Length && char.IsLowSurrogate(t[i + 1])) i++;
        }
        return h.ToString("x8", Inv);
    }
}
