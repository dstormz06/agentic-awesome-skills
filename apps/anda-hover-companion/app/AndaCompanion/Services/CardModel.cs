using System.Text.Json;
using AndaCompanion.Models;

namespace AndaCompanion.Services;

public sealed record CardWatchFlag(string Code, string Severity, string Text, string Why, string Source);
public sealed record CardAttention(string Level, string Text);
public sealed record CardUrgency(bool IsUrgent, string? Reason);
public sealed record CardReference(string? Psg, string? Rld);
public sealed record CardBrief(string Tag, string Text, bool Generated);

/// <summary>
/// Everything a card shows, already decided: every string, every flag. A surface that is not the Windows app (the Edge
/// extension, in "together" mode) only draws this, so it can never say something different from the app. The same shape is
/// produced by the extension's own JavaScript in standalone mode, and a committed fixture keeps the two identical.
/// </summary>
public sealed record CardModel(
    string Number, string Kind, string Drug, string Meta, string? Freshness, string? Source, bool Demo,
    string GoalDate, string GoalDays, int? DaysToGoal, int? Progress,
    string ForecastDate, string ForecastDelta, bool ForecastLate, bool HasForecast, int? ForecastDeltaDays, string ForecastConfidence,
    CardAttention? Attention, string Latest, string? Ahead,
    IReadOnlyList<CardWatchFlag> Watch, int WatchMore, CardReference? Reference,
    IReadOnlyList<PendingItem> Pending, IReadOnlyList<NoteItem> Notes, string ForecastDetail, IReadOnlyList<SisterRef> Sisters,
    CardBrief Brief, string StatusLine, CardUrgency Urgency);

public sealed record CardContext(
    DateOnly Today, BusinessCalendar Calendar, int HolidayLeadDays = 3, int UrgentLateDays = 7,
    Func<AndaRecord, ReferenceInfo?>? Reference = null);

public static class CardModels
{
    /// <summary>camelCase, nulls kept, so a missing field is visible rather than silently absent.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
        WriteIndented = false,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Severity(WatchSeverity s) => s switch { WatchSeverity.Bad => "bad", WatchSeverity.Warn => "warn", _ => "info" };

    public static CardModel Build(AndaRecord r, CardContext ctx)
    {
        var flags = WatchRules.Evaluate(r, ctx.Today, ctx.Calendar, new WatchSettings(HolidayLeadBusinessDays: ctx.HolidayLeadDays));
        var info = ctx.Reference?.Invoke(r);
        CardReference? reference = info is not null && (info.PsgText is not null || info.RldText is not null) ? new(info.PsgText, info.RldText) : null;
        bool urgent = UrgencyPolicy.IsUrgent(r, ctx.Today, ctx.Calendar, ctx.UrgentLateDays);
        return new CardModel(
            r.Number, r.Kind, r.Drug, r.Meta, r.DataAsOf, r.SourceLabel, r.Demo,
            r.GoalDate, r.GoalDays, r.DaysToGoal, r.HasProgress ? r.ProgressPct : null,
            r.ForecastDate, r.ForecastDelta, r.ForecastLate, r.HasForecast, r.ForecastDeltaDays, r.ForecastConfidence,
            r.Attention is { } a ? new CardAttention(a.Level == AttentionLevel.Bad ? "bad" : "warn", a.Text) : null,
            r.Latest, r.Ahead,
            flags.Select(f => new CardWatchFlag(f.Code, Severity(f.Severity), f.Text, f.Why, f.Source)).ToList(),
            Math.Max(0, flags.Count - 1), reference,
            r.Pending, r.Notes, r.ForecastDetail, r.Sisters,
            new CardBrief("OFFLINE BRIEF", OfflineBriefService.Compose(r), false),
            StatusLine.Build(r),
            new CardUrgency(urgent, urgent ? UrgencyPolicy.Reason(r, ctx.Today, ctx.Calendar, ctx.UrgentLateDays) : null));
    }
}
