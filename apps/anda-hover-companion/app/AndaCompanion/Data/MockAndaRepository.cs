using AndaCompanion.Models;

namespace AndaCompanion.Data;

public interface IAndaRepository
{
    /// <summary>Null when the number is not a recognized ANDA.</summary>
    AndaRecord? Find(string number);
}

/// <summary>
/// Fictional demo data. The production implementation reads a locally cached
/// extract of the sanctioned system-of-record exports (see DESIGN.md §6).
/// </summary>
public sealed class MockAndaRepository : IAndaRepository
{
    private readonly Dictionary<string, AndaRecord> _records;

    public MockAndaRepository()
    {
        var a216482 = new AndaRecord
        {
            Number = "216482",
            Drug = "Metformin HCl ER Tablets, 500 mg",
            Meta = "Meridian Pharmaceuticals (fictional) · Cycle 2",
            GoalDate = "Oct 3, 2026", GoalDays = "· 80 days", ProgressPct = 62,
            ForecastDate = "Oct 17, 2026", ForecastDelta = "▲ +14 d vs goal",
            ForecastLate = true, ForecastConfidence = "moderate confidence",
            Attention = new AttentionItem(AttentionLevel.Warn, "IR response due in 6 days — Biopharmaceutics"),
            Latest = "Latest: Amendment (minor) received Jun 30 — under review",
            Ahead = "Thu huddle — status requested on this ANDA + sister 214990 (memo, Jul 14)",
            Pending = new[]
            {
                new PendingItem("IR response — Biopharmaceutics", "due Jul 21", true),
                new PendingItem("Labeling carve-out review", "in queue", false),
            },
            Notes = new[]
            {
                new NoteItem("You · Jul 8", "Applicant confirmed receipt of IR; response expected on time."),
                new NoteItem("You · Jun 30", "Minor amendment logged; routed to OPQ same day."),
                new NoteItem("Team lead · Jun 12", "Watch facility linkage with 214990 — same site."),
            },
            ForecastDetail = "Baseline from comparable cycle-2 reviews: Oct 10. Adjusted +7 days for this " +
                             "applicant's IR-response history; band ±5 days. Assumptions: first-pass IR response; " +
                             "no new facility inspection. Tool calibration, trailing 6 months: median error ±8 days.",
            Sisters = new[] { new SisterRef("214990", "Metformin HCl ER Tablets, 750 mg — same applicant, same facility") },
        };

        var a214990 = new AndaRecord
        {
            Number = "214990",
            Drug = "Metformin HCl ER Tablets, 750 mg",
            Meta = "Meridian Pharmaceuticals (fictional) · Cycle 3",
            GoalDate = "Aug 15, 2026", GoalDays = "· 31 days", ProgressPct = 88,
            ForecastDate = "Aug 6, 2026", ForecastDelta = "▼ 9 d ahead of goal",
            ForecastLate = false, ForecastConfidence = "high confidence",
            Attention = null,
            Latest = "Latest: Facility IR resolved — closed Jul 2",
            Ahead = "Aug 1 — labeling final-format confirmation due from applicant",
            Pending = Array.Empty<PendingItem>(),
            Notes = new[]
            {
                new NoteItem("You · Jul 2", "Facility IR closed; memo filed."),
                new NoteItem("You · Jun 20", "Labeling agreed; final format pending."),
            },
            ForecastDetail = "Baseline from comparable cycle-3 reviews: Aug 8. Adjusted −2 days: all disciplines " +
                             "complete or scheduled; band ±3 days. Assumption: no late labeling revisions. " +
                             "Tool calibration, trailing 6 months: median error ±8 days.",
            Sisters = new[] { new SisterRef("216482", "Metformin HCl ER Tablets, 500 mg — same applicant, same facility") },
        };

        _records = new() { [a216482.Number] = a216482, [a214990.Number] = a214990 };
    }

    public AndaRecord? Find(string number) => _records.GetValueOrDefault(number);
}

public static class AndaValidator
{
    private static readonly System.Text.RegularExpressions.Regex Pattern =
        new(@"(?:ANDA[-\s#]?)?(?<!\d)(\d{6})(?!\d)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Extracts the first 6-digit ANDA-shaped number from arbitrary captured text.</summary>
    public static string? Extract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = Pattern.Match(text);
        return m.Success ? m.Groups[1].Value : null;
    }
}
