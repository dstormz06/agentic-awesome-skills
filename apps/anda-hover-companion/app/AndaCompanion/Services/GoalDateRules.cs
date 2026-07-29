namespace AndaCompanion.Services;

public enum AmendmentClass { Minor, Major }

/// <summary>
/// Versioned GDUFA goal-date rules — the regulatory logic behind the forecast.
///
/// Why versioned: the goal-date rulebook is negotiated per GDUFA cycle. GDUFA III
/// (FY2023–2027) rules come from the GDUFA III commitment letter and the final
/// guidance "ANDA Submissions — Amendments to ANDAs Under GDUFA" (rev. Sept 2024).
/// GDUFA IV (FY2028–2032) is in FDA–industry negotiation as of 2026 — industry has
/// proposed guardrails on goal-date extensions — so the rule set WILL change.
/// Encoding rules behind this interface means GDUFA IV is a new implementation,
/// not a rewrite.
///
/// Accuracy policy: only rules confirmed against a citable source are encoded.
/// Unencoded combinations return null — the forecast layer must surface "rule not
/// encoded" rather than guess. Never fill this table from memory.
/// </summary>
public interface IGoalDateRules
{
    /// <summary>Rule-set version shown in the forecast drivers panel, e.g. "GDUFA III".</summary>
    string Version { get; }

    /// <summary>
    /// Assessment goal, in months from amendment submission, for an amendment of the
    /// given class; null when the combination is not yet encoded from a citable source.
    /// </summary>
    int? AmendmentAssessmentMonths(AmendmentClass amendmentClass, bool requiresPreapprovalInspection);

    /// <summary>Standard original ANDA assessment goal in months (the 90% cohort goal).</summary>
    int StandardOriginalAndaMonths { get; }

    string Citation { get; }
}

/// <summary>GDUFA III (FY2023–2027) — the rule set in force today.</summary>
public sealed class Gdufa3Rules : IGoalDateRules
{
    public string Version => "GDUFA III";

    public string Citation =>
        "GDUFA III commitment letter (fda.gov/media/153631) and 'ANDA Submissions — Amendments " +
        "to ANDAs Under GDUFA', final guidance rev. Sept 2024 (fda.gov/media/89258).";

    // 90% of standard original ANDAs assessed within 10 months of submission.
    public int StandardOriginalAndaMonths => 10;

    public int? AmendmentAssessmentMonths(AmendmentClass amendmentClass, bool requiresPreapprovalInspection) =>
        (amendmentClass, requiresPreapprovalInspection) switch
        {
            // Major amendment, no preapproval inspection required: 8-month goal (standard).
            (AmendmentClass.Major, false) => 8,
            // Remaining tiers (minor amendments; inspection-required combinations) must be
            // transcribed from the commitment letter's goal tables before production use —
            // returning null here forces the UI to say "rule not encoded" instead of guessing.
            _ => null,
        };
}

/// <summary>
/// GDUFA IV (FY2028–2032) — placeholder. The commitment letter is still in
/// FDA–industry negotiation (sessions held Jan 28 and Mar 27, 2026); every lookup
/// intentionally reports "not yet published" until it finalizes.
/// </summary>
public sealed class Gdufa4Rules : IGoalDateRules
{
    public string Version => "GDUFA IV (pending)";
    public string Citation => "GDUFA IV commitment letter — not yet published (negotiations ongoing, 2026).";
    public int StandardOriginalAndaMonths => throw new InvalidOperationException(Citation);
    public int? AmendmentAssessmentMonths(AmendmentClass a, bool i) => null;
}
