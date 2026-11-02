using System.Globalization;
using System.Text.RegularExpressions;
using AndaCompanion.Data;
using AndaCompanion.Models;

namespace AndaCompanion.Services;

public enum DraftFindingKind { Difference, UnknownAnda, Note, Ok }

public sealed record DraftFinding(DraftFindingKind Kind, string Text);

public sealed record DraftCheckResult(IReadOnlyList<DraftFinding> Findings, int NumbersChecked, int DatesChecked, int StatusChecked)
{
    public bool HasDifferences => Findings.Any(f => f.Kind is DraftFindingKind.Difference or DraftFindingKind.UnknownAnda);
}

/// <summary>
/// The draft check: compares what an email draft says about an ANDA (the number, a goal or forecast date, and an on-track or
/// late statement) with the record. Deterministic: no model, no network, the same draft always gives the same answer. It says
/// exactly what it checked and what it did not, and it only knows the data this tool has, which is not the system of record.
/// </summary>
public static class DraftCheck
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly Regex DateMention = new(
        @"(?<![\d/])(?:(?<mon>Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|June?|July?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?\s+(?<d1>\d{1,2})(?:st|nd|rd|th)?(?:,?\s+(?<y1>\d{4}))?(?!\d)" +
        @"|(?<nm>\d{1,2})/(?<nd>\d{1,2})(?:/(?<ny>\d{4}|\d{2}))?(?![\d/])" +
        @"|(?<iy>\d{4})-(?<im>\d{2})-(?<id>\d{2}))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex OnTrack = new(@"\b(on track|on schedule|ahead of (?:the )?(?:goal|schedule)|ahead of goal)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Late = new(@"\b(running late|behind schedule|behind|delayed|at risk|slipping|slipped|past (?:the )?goal|will miss|going to miss)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] Months = { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };

    private sealed record Mention(int Month, int Day, int? Year, int Index);

    public static DraftCheckResult Check(string? draft, Func<string, AndaRecord?> lookup)
    {
        var findings = new List<DraftFinding>();
        if (string.IsNullOrWhiteSpace(draft))
            return new(new[] { new DraftFinding(DraftFindingKind.Note, "Nothing to check yet. Paste or type a draft.") }, 0, 0, 0);

        int numbersChecked = 0, datesChecked = 0, statusChecked = 0, multi = 0;
        var seenNumbers = new HashSet<string>();
        var reportedUnknown = new HashSet<string>();
        var seenFinding = new HashSet<string>();
        void Add(DraftFindingKind kind, string text) { if (seenFinding.Add(kind + text)) findings.Add(new DraftFinding(kind, text)); }

        foreach (var paragraph in Regex.Split(draft, @"\r?\n\s*\r?\n"))
        {
            string? context = null;
            foreach (var sentence in Regex.Split(paragraph, @"(?<=[.!?])\s+|\r?\n").Where(s => s.Trim().Length > 0))
            {
                var numbers = EntityKinds.Anda.Compiled.Matches(sentence).Select(m => m.Groups[1].Value).Distinct().ToList();
                foreach (var n in numbers)
                {
                    if (seenNumbers.Add(n)) numbersChecked++;
                    if (lookup(n) is null && reportedUnknown.Add(n))
                        Add(DraftFindingKind.UnknownAnda, $"ANDA {n} is not in your data, so nothing about it could be checked. Check the number.");
                }

                string? subject;
                if (numbers.Count == 1) { subject = numbers[0]; context = subject; }
                else if (numbers.Count > 1)
                {
                    subject = null; context = null;
                    if (++multi <= 3) Add(DraftFindingKind.Note, $"One sentence names several ANDAs, so its dates and wording were not checked: \"{Trim(sentence)}\"");
                }
                else subject = context;

                if (subject is null || lookup(subject) is not { } record) continue;

                foreach (Match m in DateMention.Matches(sentence))
                {
                    var mention = ToMention(m);
                    if (mention is null) continue;
                    string? which = Keyword(sentence, m);
                    if (which is null) continue;
                    DateOnly? actual = which == "goal" ? record.GoalDateValue : record.HasForecast ? record.ForecastDateValue : null;
                    if (actual is null) continue;
                    datesChecked++;
                    if (!Matches(mention, actual.Value))
                        Add(DraftFindingKind.Difference,
                            $"ANDA {subject}: the draft gives the {which} date as {Show(mention)}, but the record says {actual.Value.ToString("MMM d, yyyy", Inv)}.");
                }

                if (record.HasForecast)
                {
                    bool claimsOnTrack = OnTrack.IsMatch(sentence), claimsLate = Late.IsMatch(sentence);
                    if (claimsOnTrack) statusChecked++;
                    if (claimsLate) statusChecked++;
                    if (claimsOnTrack && record.ForecastLate)
                        Add(DraftFindingKind.Difference, $"ANDA {subject}: the draft sounds on track, but the record's forecast is {record.ForecastDelta.Replace("▲", "").Replace("▼", "").Trim()}.");
                    if (claimsLate && !record.ForecastLate)
                        Add(DraftFindingKind.Difference, $"ANDA {subject}: the draft sounds late or at risk, but the record's forecast is {record.ForecastDelta.Replace("▲", "").Replace("▼", "").Trim()}.");
                }
            }
        }

        var ordered = findings.OrderBy(f => f.Kind == DraftFindingKind.Note ? 1 : 0).ToList();
        if (numbersChecked == 0)
            ordered.Insert(0, new DraftFinding(DraftFindingKind.Note, "No ANDA numbers found in the draft, so nothing was checked against a record."));
        else if (!ordered.Any(f => f.Kind is DraftFindingKind.Difference or DraftFindingKind.UnknownAnda))
            ordered.Insert(0, new DraftFinding(DraftFindingKind.Ok,
                $"Checked {numbersChecked} ANDA number{(numbersChecked == 1 ? "" : "s")}, {datesChecked} goal or forecast date{(datesChecked == 1 ? "" : "s")} and {statusChecked} on-track or late statement{(statusChecked == 1 ? "" : "s")}: no differences from the record."));
        ordered.Add(new DraftFinding(DraftFindingKind.Note,
            "This compares the draft only with the data this tool has, and only numbers, goal and forecast dates, and on-track or late wording. It does not check other wording and is not the system of record."));
        return new(ordered, numbersChecked, datesChecked, statusChecked);
    }

    private static string Trim(string s) { s = s.Trim(); return s.Length <= 90 ? s : s[..87] + "..."; }

    private static string? Keyword(string sentence, Match m)
    {
        int start = Math.Max(0, m.Index - 50);
        string before = sentence[start..m.Index].ToLowerInvariant();
        int g = before.LastIndexOf("goal", StringComparison.Ordinal);
        int f = Math.Max(before.LastIndexOf("forecast", StringComparison.Ordinal), before.LastIndexOf("projected", StringComparison.Ordinal));
        if (g >= 0 || f >= 0) return g > f ? "goal" : "forecast";
        string after = sentence[(m.Index + m.Length)..Math.Min(sentence.Length, m.Index + m.Length + 30)].ToLowerInvariant();
        int ag = after.IndexOf("goal", StringComparison.Ordinal), af = Math.Min(after.IndexOf("forecast", StringComparison.Ordinal) is var x && x >= 0 ? x : int.MaxValue, int.MaxValue);
        if (ag >= 0 && (af == int.MaxValue || ag < af)) return "goal";
        if (af != int.MaxValue) return "forecast";
        return null;
    }

    private static Mention? ToMention(Match m)
    {
        try
        {
            if (m.Groups["mon"].Success)
            {
                int mon = Array.IndexOf(Months, m.Groups["mon"].Value[..3].ToLowerInvariant()) + 1;
                int day = int.Parse(m.Groups["d1"].Value, Inv);
                int? year = m.Groups["y1"].Success ? int.Parse(m.Groups["y1"].Value, Inv) : null;
                return mon is >= 1 and <= 12 && day is >= 1 and <= 31 ? new Mention(mon, day, year, m.Index) : null;
            }
            if (m.Groups["nm"].Success)
            {
                int mon = int.Parse(m.Groups["nm"].Value, Inv), day = int.Parse(m.Groups["nd"].Value, Inv);
                int? year = null;
                if (m.Groups["ny"].Success) { year = int.Parse(m.Groups["ny"].Value, Inv); if (year < 100) year += 2000; }
                return mon is >= 1 and <= 12 && day is >= 1 and <= 31 ? new Mention(mon, day, year, m.Index) : null;
            }
            if (m.Groups["iy"].Success)
                return new Mention(int.Parse(m.Groups["im"].Value, Inv), int.Parse(m.Groups["id"].Value, Inv), int.Parse(m.Groups["iy"].Value, Inv), m.Index);
        }
        catch (FormatException) { }
        return null;
    }

    private static bool Matches(Mention m, DateOnly d) => m.Month == d.Month && m.Day == d.Day && (m.Year is null || m.Year == d.Year);

    private static string Show(Mention m) =>
        $"{CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(Math.Clamp(m.Month, 1, 12))} {m.Day}" + (m.Year is { } y ? $", {y}" : "");
}
