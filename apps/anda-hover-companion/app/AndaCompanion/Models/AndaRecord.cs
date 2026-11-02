namespace AndaCompanion.Models;

public enum AttentionLevel { Warn, Bad }

public sealed record AttentionItem(AttentionLevel Level, string Text);

public sealed record PendingItem(string Title, string Due, bool Urgent);

public sealed record NoteItem(string Who, string Text);

public sealed record SisterRef(string Number, string Description);

/// <summary>
/// One ANDA as the card renders it. Deterministic facts only — the AI never writes these.
/// The forecast fields come from the forecast model, not the LLM.
/// </summary>
public sealed record AndaRecord
{
    public required string Number { get; init; }

    /// <summary>Identifier type label, e.g. "ANDA". Lets one card serve any tracked
    /// submission type without touching the UI (see Data/EntityKinds.cs).</summary>
    public string Kind { get; init; } = "ANDA";
    public required string Drug { get; init; }
    public required string Meta { get; init; }
    public required string GoalDate { get; init; }
    public required string GoalDays { get; init; }

    /// <summary>Days from the data extract to the goal date, and the forecast's days late (+) or early (−).
    /// Optional: when absent, the timeline look falls back to a plain bar. Never parsed from display text.</summary>
    public int? DaysToGoal { get; init; }
    public int? ForecastDeltaDays { get; init; }
    public required int ProgressPct { get; init; }
    public required string ForecastDate { get; init; }
    public required string ForecastDelta { get; init; }   // e.g. "▲ +14 d vs goal"
    public required bool ForecastLate { get; init; }
    public required string ForecastConfidence { get; init; }
    public AttentionItem? Attention { get; init; }
    public required string Latest { get; init; }

    /// <summary>T4 — freshness: when the underlying extract was taken. The most
    /// dangerous state is not a wrong fact but a stale fact confidently shown.</summary>
    public string? DataAsOf { get; init; }
    public string? Ahead { get; init; }
    public required IReadOnlyList<PendingItem> Pending { get; init; }
    public required IReadOnlyList<NoteItem> Notes { get; init; }
    public required string ForecastDetail { get; init; }
    public required IReadOnlyList<SisterRef> Sisters { get; init; }

    // ---- machine-readable dates and provenance (optional) ----

    /// <summary>The text a display field carries when the source has no value. Never a guess.</summary>
    public const string Unknown = "—";

    public DateOnly? GoalDateValue { get; init; }
    public DateOnly? ForecastDateValue { get; init; }
    public DateOnly? SubmittedDate { get; init; }

    /// <summary>When the underlying data was taken (a file's modified date, or an extract date).</summary>
    public DateOnly? AsOfDate { get; init; }

    /// <summary>True for the bundled fictional records. Watch rules then treat <see cref="AsOfDate"/> as "today",
    /// so the demo never shows "stale" or "overdue" just because the calendar moved on.</summary>
    public bool Demo { get; init; }

    /// <summary>Where the record came from, e.g. "Your portfolio: Portfolio.xlsx".</summary>
    public string? SourceLabel { get; init; }

    /// <summary>Used only to match a public reference listing; never sent anywhere.</summary>
    public string? ActiveIngredient { get; init; }
    public string? DosageForm { get; init; }
    public string? Strength { get; init; }

    /// <summary>A PSG date the RPM keeps in their own sheet, shown as theirs. Not from an official source.</summary>
    public DateOnly? PsgDateFromSheet { get; init; }

    [System.Text.Json.Serialization.JsonIgnore] public bool HasProgress => ProgressPct >= 0;
    [System.Text.Json.Serialization.JsonIgnore] public bool HasForecast => ForecastDate != Unknown;
}

public sealed record CompanionBrief(string Text, IReadOnlyList<string> SourceRefs, bool Generated);
