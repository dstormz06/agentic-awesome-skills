using System.Globalization;
using AndaCompanion.Models;

namespace AndaCompanion.Services;

public enum WatchSeverity { Info, Warn, Bad }

/// <summary>One thing worth a second look, with the evidence behind it. Never an instruction, never a conclusion.</summary>
public sealed record WatchFlag(string Code, WatchSeverity Severity, string Text, string Why, string Source);

public sealed record WatchSettings(int HolidayLeadBusinessDays = 3, int StaleDataDays = 7, int GoalSoonDays = 5);

/// <summary>
/// The "watch your back" layer: deterministic checks over a record, each one carrying its evidence and source.
/// It states what the data shows and where a person should look; it never decides what a goal-date rule means.
/// The same record always gives the same flags for the same day, so it is fully testable.
/// </summary>
public static class WatchRules
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Short(DateOnly d) => d.ToString("ddd MMM d", Inv);

    public static IReadOnlyList<WatchFlag> Evaluate(AndaRecord r, DateOnly realToday, BusinessCalendar cal, WatchSettings? settings = null)
    {
        settings ??= new WatchSettings();
        var flags = new List<WatchFlag>();
        // Fictional records are frozen at their extract date; judging them against the real calendar would call them "overdue".
        DateOnly today = r.Demo && r.AsOfDate is { } asOf ? asOf : realToday;
        string source = r.SourceLabel ?? "the record";

        void DateRule(string code, string what, DateOnly? date)
        {
            if (date is not { } d) return;
            var f = cal.Evaluate(d, settings.HolidayLeadBusinessDays);
            if (f.Kind == DayKind.Weekend)
                flags.Add(new WatchFlag(code, WatchSeverity.Warn,
                    $"{what} date falls on a weekend: {Short(d)}. Nearest business days: {Short(f.PreviousBusinessDay)} and {Short(f.NextBusinessDay)}.",
                    "A date on a weekend may be handled differently from a weekday. Check how it applies to this action; this tool does not decide.",
                    "U.S. federal calendar (OPM)"));
            else if (f.Kind == DayKind.Holiday)
                flags.Add(new WatchFlag(code, WatchSeverity.Warn,
                    $"{what} date falls on a federal holiday: {Short(d)} ({f.HolidayName}). Nearest business days: {Short(f.PreviousBusinessDay)} and {Short(f.NextBusinessDay)}.",
                    "A date on a holiday may be handled differently from a workday. Check how it applies to this action; this tool does not decide.",
                    "U.S. federal calendar (OPM)"));
            else if (f.BusinessDaysFromHoliday is { } n)
                flags.Add(new WatchFlag(code, WatchSeverity.Info,
                    $"{what} date {Short(d)} is {n} business day{(n == 1 ? "" : "s")} {(f.Before ? "before" : "after")} {f.HolidayName}.",
                    "Staffing and turnaround around a federal holiday can differ. Worth a look if the date is tight.",
                    "U.S. federal calendar (OPM)"));
        }

        DateRule("goal-date-calendar", "Goal", r.GoalDateValue);
        DateRule("forecast-date-calendar", "Forecast", r.HasForecast ? r.ForecastDateValue : null);

        if (r.GoalDateValue is { } goal)
        {
            int days = goal.DayNumber - today.DayNumber;
            if (days < 0)
                flags.Add(new WatchFlag("goal-passed", WatchSeverity.Bad,
                    $"The goal date ({Short(goal)}) passed {-days} day{(days == -1 ? "" : "s")} ago.",
                    "The record still lists this goal date. Confirm whether it moved or the data is out of date.", source));
            else if (days <= settings.GoalSoonDays)
                flags.Add(new WatchFlag("goal-soon", WatchSeverity.Warn,
                    days == 0 ? $"The goal date is today ({Short(goal)})." : $"The goal date ({Short(goal)}) is in {days} day{(days == 1 ? "" : "s")}.",
                    "A near goal date is easy to miss in a long list.", source));
        }
        else
        {
            flags.Add(new WatchFlag("no-goal-date", WatchSeverity.Info, "No goal date in the data for this ANDA.",
                "Without a goal date nothing can be checked against the calendar.", source));
        }

        if (!r.Demo && r.AsOfDate is { } taken)
        {
            int age = today.DayNumber - taken.DayNumber;
            if (age > settings.StaleDataDays)
                flags.Add(new WatchFlag("stale-data", WatchSeverity.Warn,
                    $"This data is {age} days old (as of {taken.ToString("MMM d", Inv)}).",
                    "A stale fact shown confidently is the riskiest kind. Refresh your source before relying on it.", source));
        }

        if (r.SubmittedDate is { } submitted && r.PsgDateFromSheet is { } psg && psg > submitted)
            flags.Add(new WatchFlag("psg-after-submission", WatchSeverity.Warn,
                $"A PSG date in your sheet ({psg.ToString("MMM d, yyyy", Inv)}) is after this ANDA was submitted ({submitted.ToString("MMM d, yyyy", Inv)}).",
                "A product-specific guidance change after submission may matter. Possible impact: check; this tool does not judge it.", "Your sheet"));

        return flags
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => Rank(f.Code))
            .ThenBy(f => f.Code, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Within one severity, the order a person would want them in: the goal first, then the forecast, then the data itself.</summary>
    public static int Rank(string code) => code switch
    {
        "goal-passed" => 0, "goal-soon" => 1, "goal-date-calendar" => 2, "forecast-date-calendar" => 3,
        "psg-after-submission" => 4, "stale-data" => 5, "no-goal-date" => 6, _ => 9,
    };
}
