using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AndaCompanion.Models;

namespace AndaCompanion.Services;

/// <summary>
/// "Review a draft", the part that needs no AI. It runs the draft check (numbers, goal and forecast dates and on-track or late wording against the
/// person's own data) and then checks the draft against itself and the calendar: a weekday that does not belong to its date, a deadline that falls on a
/// weekend, a federal holiday or the past, two different goal dates for the same ANDA, a placeholder left in, a word typed twice, an ANDA number with the
/// wrong number of digits. Every check is a fixed rule: the same draft on the same day always gives the same answer, nothing is sent anywhere, and the
/// result says what was checked and what was not. A clean result means "none of these things were found", never "correct".
/// </summary>
public static class DraftReview
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(250);
    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private const string MonthWords = "Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|June?|July?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?";
    private const string WeekdayWords = "(?:Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday|(?-i:Mon|Tues?|Wed|Thu(?:rs?)?|Fri|Sat|Sun))";
    private static readonly string[] Months = { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };
    private static readonly string[] MonthNames = { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };

    // A weekday written with a date: "Friday, November 7, 2026", "Fri Nov 7", "Friday 11/7/2026".
    private static readonly Regex WeekdayDate = new(
        @"(?<![A-Za-z])(?<wd>" + WeekdayWords + @")\.?,?\s+(?:the\s+)?(?:(?<mon>" + MonthWords + @")\.?\s+(?<d1>[0-9]{1,2})(?:st|nd|rd|th)?|(?<nm>[0-9]{1,2})/(?<nd>[0-9]{1,2}))(?:,?\s+|/)?(?<y>[0-9]{4})?(?![0-9])",
        Opt, Limit);

    // A date after a word that makes it a deadline: "by Nov 8", "no later than 11/14/2026", "due Friday, Nov 6".
    private static readonly Regex DeadlineDate = new(
        @"\b(?<cue>by|before|due|no later than|until|deadline(?: is| of)?|on or before)\s+(?:" + WeekdayWords + @"\.?,?\s+)?(?:(?<mon>" + MonthWords + @")\.?\s+(?<d1>[0-9]{1,2})(?:st|nd|rd|th)?|(?<nm>[0-9]{1,2})/(?<nd>[0-9]{1,2}))(?:,?\s+|/)?(?<y>[0-9]{4})?(?![0-9])",
        Opt, Limit);

    private static readonly Regex Placeholder = new(
        @"\[(?:TBD|TBC|TODO|XX+|INSERT[^\]\r\n]{0,40}|NAME|DATE|PLACEHOLDER)\]|(?-i:\bTBD\b|\bTBC\b|\bXXX+\b)|\?\?\?+|<<[^>\r\n]{1,40}>>|\{\{[^}\r\n]{1,40}\}\}|lorem ipsum",
        Opt, Limit);

    private static readonly Regex Repeated = new(@"(?<![A-Za-z'])(?<w>[A-Za-z]{2,})\s+\k<w>(?![A-Za-z'])", Opt, Limit);
    private static readonly HashSet<string> RepeatOk = new(StringComparer.OrdinalIgnoreCase) { "that", "had" };

    private static readonly Regex ShortAnda = new(@"\bANDA\s*(?:No\.?|#|number)?\s*(?<n>[0-9]{4,5}|[0-9]{7,})(?![0-9])", Opt, Limit);

    public static DraftCheckResult Check(string? draft, Func<string, AndaRecord?> lookup, DateOnly today, BusinessCalendar calendar)
    {
        var baseResult = DraftCheck.Check(draft, lookup);
        if (string.IsNullOrWhiteSpace(draft)) return baseResult;

        var diffs = new List<DraftFinding>();
        var notes = new List<DraftFinding>();
        int weekdaysChecked = 0, deadlinesChecked = 0;
        try
        {
            weekdaysChecked = CheckWeekdays(draft, today, diffs, notes);
            deadlinesChecked = CheckDeadlines(draft, today, calendar, diffs, notes);
            CheckContradictions(draft, diffs);
            CheckPlaceholders(draft, diffs);
            CheckRepeats(draft, notes);
            CheckShortNumbers(draft, notes);
        }
        catch (Exception ex) when (ex is RegexMatchTimeoutException or FormatException or ArgumentException or OverflowException) { /* what was found so far is still shown */ }

        var findings = new List<DraftFinding>();
        findings.AddRange(baseResult.Findings.Where(f => f.Kind is DraftFindingKind.Difference or DraftFindingKind.UnknownAnda));
        findings.AddRange(diffs);
        findings.AddRange(notes);
        findings.AddRange(baseResult.Findings.Where(f => f.Kind == DraftFindingKind.Note && f.Text != DraftCheck.StandardNote));
        bool anyDifference = findings.Any(f => f.Kind is DraftFindingKind.Difference or DraftFindingKind.UnknownAnda);
        int dates = baseResult.DatesChecked + weekdaysChecked + deadlinesChecked;
        if (!anyDifference)
        {
            var parts = new List<string>();
            if (baseResult.NumbersChecked > 0) parts.Add($"{baseResult.NumbersChecked} ANDA number{Plural(baseResult.NumbersChecked)}");
            if (baseResult.DatesChecked > 0) parts.Add($"{baseResult.DatesChecked} goal or forecast date{Plural(baseResult.DatesChecked)} against the record");
            if (baseResult.StatusChecked > 0) parts.Add($"{baseResult.StatusChecked} on-track or late statement{Plural(baseResult.StatusChecked)}");
            if (weekdaysChecked > 0) parts.Add($"{weekdaysChecked} weekday{Plural(weekdaysChecked)} against {(weekdaysChecked == 1 ? "its date" : "their dates")}");
            if (deadlinesChecked > 0) parts.Add($"{deadlinesChecked} deadline{Plural(deadlinesChecked)} against the calendar");
            parts.Add("the draft for leftover placeholders, repeated words and short ANDA numbers");
            string verdict = notes.Count == 0 ? "nothing found that needs a second look"
                : $"no differences, but please read the note{Plural(notes.Count)} below";
            findings.Insert(0, new DraftFinding(DraftFindingKind.Ok, "Checked " + JoinList(parts) + ": " + verdict + "."));
        }
        findings.Add(new DraftFinding(DraftFindingKind.Note, ReceiptNote));
        return new DraftCheckResult(findings, baseResult.NumbersChecked, dates, baseResult.StatusChecked);
    }

    /// <summary>The closing line: what a clean result does not mean.</summary>
    public const string ReceiptNote =
        "Not checked: spelling, grammar and other wording (these need an approved AI connection), facts that are not in your data, and anything the draft only implies. A clean result means none of the checks above found a problem, not that the draft is correct. This is not the system of record.";

    private static string Plural(int n) => n == 1 ? "" : "s";

    private static string JoinList(IReadOnlyList<string> parts) =>
        parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];

    // ---------- weekdays ----------

    private static int CheckWeekdays(string draft, DateOnly today, List<DraftFinding> diffs, List<DraftFinding> notes)
    {
        int checkedCount = 0; var seen = new HashSet<string>();
        foreach (Match m in WeekdayDate.Matches(draft))
        {
            if (!TryDate(m, out int month, out int day, out int? year)) continue;
            var said = WeekdayOf(m.Groups["wd"].Value);
            string shown = Squash(m.Value);
            if (!seen.Add(shown.ToLowerInvariant())) continue;
            if (year is { } y)
            {
                if (y < 1900 || y > 2200) continue;
                if (day > DateTime.DaysInMonth(y, month)) { diffs.Add(new(DraftFindingKind.Difference, $"“{shown}”: {MonthNames[month - 1]} {day}, {y} is not a real date.")); continue; }
                checkedCount++;
                var actual = new DateOnly(y, month, day).DayOfWeek;
                if (actual != said)
                    diffs.Add(new(DraftFindingKind.Difference, $"The draft says “{shown}”, but {MonthNames[month - 1]} {day}, {y} is a {actual}."));
            }
            else
            {
                // No year written: it is judged only against the three years around today, and only softly, because the person may mean another year.
                var candidates = new List<DateOnly>();
                foreach (int cy in new[] { today.Year - 1, today.Year, today.Year + 1 })
                    if (day <= DateTime.DaysInMonth(cy, month)) candidates.Add(new DateOnly(cy, month, day));
                if (day > DateTime.DaysInMonth(2000, month)) { diffs.Add(new(DraftFindingKind.Difference, $"“{shown}”: {MonthNames[month - 1]} {day} is not a real date.")); continue; }
                if (candidates.Count == 0) continue;                       // February 29 in years that have none: it cannot be judged
                checkedCount++;
                if (candidates.Any(c => c.DayOfWeek == said)) continue;
                var nearest = candidates.OrderBy(c => Math.Abs(c.DayNumber - today.DayNumber)).First();
                notes.Add(new(DraftFindingKind.Note, $"“{shown}”: in {nearest.Year}, {MonthNames[month - 1]} {day} is a {nearest.DayOfWeek}, not a {said}. Please check the day or the date."));
            }
        }
        return checkedCount;
    }

    // ---------- deadlines ----------

    private static int CheckDeadlines(string draft, DateOnly today, BusinessCalendar calendar, List<DraftFinding> diffs, List<DraftFinding> notes)
    {
        int checkedCount = 0; var seen = new HashSet<string>();
        foreach (Match m in DeadlineDate.Matches(draft))
        {
            if (!TryDate(m, out int month, out int day, out int? year)) continue;
            DateOnly date;
            string cue = Squash(m.Value);
            if (year is { } y)
            {
                if (y < 1900 || y > 2200) continue;
                if (day > DateTime.DaysInMonth(y, month)) { if (seen.Add(cue.ToLowerInvariant())) diffs.Add(new(DraftFindingKind.Difference, $"“{cue}”: {MonthNames[month - 1]} {day}, {y} is not a real date.")); continue; }
                date = new DateOnly(y, month, day);
            }
            else
            {
                // The same reading as the notes reader: this year, unless that is more than a month past, then the next.
                if (day > DateTime.DaysInMonth(2000, month)) { if (seen.Add(cue.ToLowerInvariant())) diffs.Add(new(DraftFindingKind.Difference, $"“{cue}”: {MonthNames[month - 1]} {day} is not a real date.")); continue; }
                if (day > DateTime.DaysInMonth(today.Year, month)) continue;      // February 29 in a year that has none: it cannot be judged
                date = new DateOnly(today.Year, month, day);
                if (today.DayNumber - date.DayNumber > 30 && day <= DateTime.DaysInMonth(today.Year + 1, month)) date = new DateOnly(today.Year + 1, month, day);
            }
            string shown = Squash(m.Value);
            if (!seen.Add(shown.ToLowerInvariant())) continue;
            checkedCount++;
            string when = date.ToString("MMM d, yyyy", Inv);
            if (calendar.HolidayName(date) is { } holiday)
                notes.Add(new(DraftFindingKind.Note, $"“{shown}” is {holiday} ({when}), a federal holiday."));
            else if (calendar.KindOf(date) == DayKind.Weekend)
                notes.Add(new(DraftFindingKind.Note, $"“{shown}” is a {date.DayOfWeek} ({when}), not a business day."));
            else if (date < today)
                notes.Add(new(DraftFindingKind.Note, $"“{shown}” is in the past ({when}; today is {today.ToString("MMM d, yyyy", Inv)})."));
        }
        return checkedCount;
    }

    // ---------- the draft against itself ----------

    private static void CheckContradictions(string draft, List<DraftFinding> diffs)
    {
        foreach (var group in DraftCheck.DateClaims(draft).GroupBy(c => (c.Subject, c.Which)))
        {
            var distinct = new List<DraftCheck.Mention>();
            foreach (var claim in group)
                if (!distinct.Any(d => SameDay(d, claim.Date))) distinct.Add(claim.Date);
            if (distinct.Count >= 2)
                diffs.Add(new(DraftFindingKind.Difference,
                    $"The draft gives {distinct.Count} different {group.Key.Which} dates for ANDA {group.Key.Subject}: {string.Join(" and ", distinct.Take(3).Select(DraftCheck.Show))}."));
        }
    }

    private static bool SameDay(DraftCheck.Mention a, DraftCheck.Mention b) =>
        a.Month == b.Month && a.Day == b.Day && (a.Year is null || b.Year is null || a.Year == b.Year);

    private static void CheckPlaceholders(string draft, List<DraftFinding> diffs)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Placeholder.Matches(draft))
            if (seen.Add(m.Value) && seen.Count <= 5)
                diffs.Add(new(DraftFindingKind.Difference, $"A placeholder is still in the draft: “{Squash(m.Value)}”."));
    }

    private static void CheckRepeats(string draft, List<DraftFinding> notes)
    {
        int reported = 0; var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Repeated.Matches(draft))
        {
            string word = m.Groups["w"].Value;
            if (RepeatOk.Contains(word) || !seen.Add(word)) continue;
            if (++reported > 3) break;
            notes.Add(new(DraftFindingKind.Note, $"The word “{word.ToLowerInvariant()}” appears twice in a row: “{Squash(m.Value)}”."));
        }
    }

    private static void CheckShortNumbers(string draft, List<DraftFinding> notes)
    {
        var seen = new HashSet<string>();
        foreach (Match m in ShortAnda.Matches(draft))
        {
            string n = m.Groups["n"].Value;
            if (seen.Add(n) && seen.Count <= 3)
                notes.Add(new(DraftFindingKind.Note, $"“{Squash(m.Value)}” has {n.Length} digits; an ANDA number has 6. Please check it."));
        }
    }

    // ---------- helpers ----------

    private static bool TryDate(Match m, out int month, out int day, out int? year)
    {
        month = day = 0; year = null;
        try
        {
            if (m.Groups["mon"].Success)
            {
                month = Array.IndexOf(Months, m.Groups["mon"].Value[..3].ToLowerInvariant()) + 1;
                day = int.Parse(m.Groups["d1"].Value, Inv);
            }
            else if (m.Groups["nm"].Success)
            {
                month = int.Parse(m.Groups["nm"].Value, Inv);
                day = int.Parse(m.Groups["nd"].Value, Inv);
            }
            else return false;
            if (m.Groups["y"].Success) year = int.Parse(m.Groups["y"].Value, Inv);
            return month is >= 1 and <= 12 && day is >= 1 and <= 31;
        }
        catch (FormatException) { return false; }
    }

    private static DayOfWeek WeekdayOf(string word) => word.ToLowerInvariant()[..3] switch
    {
        "mon" => DayOfWeek.Monday, "tue" => DayOfWeek.Tuesday, "wed" => DayOfWeek.Wednesday, "thu" => DayOfWeek.Thursday,
        "fri" => DayOfWeek.Friday, "sat" => DayOfWeek.Saturday, _ => DayOfWeek.Sunday,
    };

    private static string Squash(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s.Trim()) sb.Append(char.IsWhiteSpace(c) ? ' ' : c);
        string t = Regex.Replace(sb.ToString(), " {2,}", " ");
        return t.Length <= 60 ? t : t[..57] + "...";
    }
}
