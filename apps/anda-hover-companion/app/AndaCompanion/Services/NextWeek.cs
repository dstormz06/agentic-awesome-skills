using AndaCompanion.Models;

namespace AndaCompanion.Services;

/// <summary>Round 36: next week, in Today, from Thursday noon to Sunday: the goal and forecast dates from Monday to Friday of the coming week, and its federal holidays.</summary>
public static class NextWeek
{
    public sealed record Row(string Id, string Text);
    public sealed record Result(string Title, IReadOnlyList<Row> Rows, IReadOnlyList<string> Holidays, string? Empty, IReadOnlyList<TaskRow>? Tasks = null, int TasksMore = 0);
    public const string SaidEmpty = "Nothing of yours falls due next week.";
    /// <summary>Round 39 review: with the to-do items switched off, the week does not claim that nothing of the person's falls due.</summary>
    public const string SaidEmptyTodosOff = "No ANDA dates of yours fall due next week. Your to-do items are not listed here; that is switched off in Settings.";

    /// <summary>Round 38: one of the person's unfinished to-do items with a due date (yyyy-MM-dd), from "ANDA to-do list" or "My to-do list".</summary>
    public sealed record WeekTask(string List, string Title, string? Due);
    public sealed record TaskRow(string List, string Text);
    public const int TaskMax = 20;
    public static string SaidTasksMore(int n) => $"{n} more to-do item{(n == 1 ? "" : "s")} due next week; the to-do lists show them all.";

    // round 39: the switch beside "Since you last looked" (the simulation's words, mockup/round39-golden.json)
    public const string SwitchLabel = "Also list my to-do items due next week";
    public const string SwitchHint = "From Thursday noon to Sunday, Today lists your unfinished to-do items due next week, from both lists, by title. They are read from this PC each time Today shows next week and only shown on the screen; nothing is kept or sent for them.";
    /// <summary>Round 39: the lists are read for Today only while the switch is on; off, nothing of them reaches Today.</summary>
    public static List<WeekTask> TasksIfOn(Settings settings, Func<IEnumerable<TodoItem>> anda, Func<IEnumerable<MyTodoItem>> mine) =>
        settings.WeekTodos ? TasksOf(anda(), mine()) : new List<WeekTask>();

    /// <summary>The unfinished items with a due date of both to-do lists, as Today's next week reads them. Read when Today opens; nothing is kept.</summary>
    public static List<WeekTask> TasksOf(IEnumerable<TodoItem> anda, IEnumerable<MyTodoItem> mine) =>
        anda.Where(i => !i.Done && !string.IsNullOrEmpty(i.Due)).Select(i => new WeekTask("ANDA to-do list", i.Title, i.Due))
            .Concat(mine.Where(i => !i.Done && !string.IsNullOrEmpty(i.Due)).Select(i => new WeekTask("My to-do list", i.Title, i.Due))).ToList();

    /// <summary>Round 38 review: the simulation's trim() (JavaScript's white space and line ends), so both programs drop and trim the same titles.</summary>
    private static readonly char[] JsSpace = "\t\n\v\f\r \u00A0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF".ToCharArray();
    /// <summary>A to Z as a to z, every other character as it is: the simulation's order of titles, whatever the language.</summary>
    private static string AsciiLower(string s) => string.Create(s.Length, s, (span, src) => { for (int i = 0; i < src.Length; i++) span[i] = src[i] is >= 'A' and <= 'Z' ? (char)(src[i] + 32) : src[i]; });

    public static Result? Of(DateOnly today, int hour, IEnumerable<AndaRecord> records, BusinessCalendar cal, IEnumerable<WeekTask>? tasks = null, bool todosOff = false)
    {
        var dow = today.DayOfWeek;
        if (!(dow is DayOfWeek.Friday or DayOfWeek.Saturday or DayOfWeek.Sunday || (dow == DayOfWeek.Thursday && hour >= 12))) return null;
        int ahead = (8 - (int)dow) % 7; if (ahead == 0) ahead = 7;
        var mon = today.AddDays(ahead); var fri = mon.AddDays(4);
        var holidays = new List<string>();
        for (var d = mon; d <= fri; d = d.AddDays(1)) if (cal.HolidayName(d) is { } name) holidays.Add($"{WatchRules.Short(d)}: {name} (federal holiday)");
        var found = new List<(DateOnly When, string Id, string Text)>();
        foreach (var r in records.OrderBy(r => r.Number, StringComparer.Ordinal))
            foreach (var (what, when) in new[] { ("Goal date", r.GoalDateValue), ("Forecast", r.HasForecast ? r.ForecastDateValue : null) })
                if (when is { } t && t >= mon && t <= fri) found.Add((t, r.Number, $"{what} {WatchRules.Short(t)}"));
        var rows = found.OrderBy(x => x.When).ThenBy(x => x.Id, StringComparer.Ordinal).Select(x => new Row(x.Id, x.Text)).ToList();
        // round 38: the person's to-do items due that week, by date, then list, then title; a date that cannot be read or a blank title is left out
        var due = (tasks ?? Array.Empty<WeekTask>())
            .Select(k => (k, t: DateOnly.TryParseExact(k.Due ?? "", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : (DateOnly?)null))
            .Where(x => x.t is { } d && d >= mon && d <= fri && (x.k.Title ?? "").Trim(JsSpace).Length > 0)
            .OrderBy(x => x.t).ThenBy(x => x.k.List, StringComparer.Ordinal).ThenBy(x => AsciiLower(x.k.Title), StringComparer.Ordinal).ToList();
        var shown = due.Take(TaskMax).Select(x => new TaskRow(x.k.List, $"{WatchRules.Short(x.t!.Value)} · {x.k.List}: {x.k.Title.Trim(JsSpace)}")).ToList();
        return new Result($"NEXT WEEK · {WatchRules.Short(mon)} TO {WatchRules.Short(fri)}".ToUpperInvariant(), rows, holidays, rows.Count > 0 || due.Count > 0 ? null : todosOff ? SaidEmptyTodosOff : SaidEmpty, shown, due.Count - shown.Count);
    }
}
