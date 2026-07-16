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
    public required string Drug { get; init; }
    public required string Meta { get; init; }
    public required string GoalDate { get; init; }
    public required string GoalDays { get; init; }
    public required int ProgressPct { get; init; }
    public required string ForecastDate { get; init; }
    public required string ForecastDelta { get; init; }   // e.g. "▲ +14 d vs goal"
    public required bool ForecastLate { get; init; }
    public required string ForecastConfidence { get; init; }
    public AttentionItem? Attention { get; init; }
    public required string Latest { get; init; }
    public string? Ahead { get; init; }
    public required IReadOnlyList<PendingItem> Pending { get; init; }
    public required IReadOnlyList<NoteItem> Notes { get; init; }
    public required string ForecastDetail { get; init; }
    public required IReadOnlyList<SisterRef> Sisters { get; init; }
}

public sealed record CompanionBrief(string Text, IReadOnlyList<string> SourceRefs, bool Generated);
