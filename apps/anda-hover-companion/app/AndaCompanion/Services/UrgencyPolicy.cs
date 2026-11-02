using AndaCompanion.Models;

namespace AndaCompanion.Services;

public enum FirstStep { Invite, Peek, Full }

/// <summary>
/// When a whisper invite should open as a peek on its own. Still started by the user's selection and hotkey; the only thing
/// the rule decides is whether the second step is taken for them. Pure, so it is tested without a window.
/// </summary>
public static class UrgencyPolicy
{
    /// <summary>
    /// What a captured selection does first. Full opens the card, Peek opens the small one (no AI call), Invite shows the whisper
    /// chip. In whisper mode an urgent ANDA skips the invite and opens a peek, if the person left that on.
    /// </summary>
    public static FirstStep Decide(ConsentModel consent, bool escalateUrgent, bool urgent) => consent switch
    {
        ConsentModel.Whisper => escalateUrgent && urgent ? FirstStep.Peek : FirstStep.Invite,
        ConsentModel.PeekFirst => FirstStep.Peek,
        _ => FirstStep.Full,
    };

    public const int DefaultLateDays = 7;
    public static readonly IReadOnlyList<int> LateDayChoices = new[] { 3, 7, 14 };

    public static int Normalize(int days) => LateDayChoices.Contains(days) ? days : DefaultLateDays;

    public static bool IsUrgent(AndaRecord r, DateOnly realToday, BusinessCalendar cal, int lateDays = DefaultLateDays)
    {
        if (r.Attention is { Level: AttentionLevel.Bad }) return true;
        if (r.HasForecast && r.ForecastDeltaDays is { } delta && delta >= lateDays) return true;
        return WatchRules.Evaluate(r, realToday, cal).Any(f => f.Severity == WatchSeverity.Bad);
    }

    /// <summary>The words for why, so the peek can say what made it open.</summary>
    public static string? Reason(AndaRecord r, DateOnly realToday, BusinessCalendar cal, int lateDays = DefaultLateDays)
    {
        if (r.Attention is { Level: AttentionLevel.Bad } a) return a.Text;
        if (r.HasForecast && r.ForecastDeltaDays is { } delta && delta >= lateDays) return $"Forecast is {delta} days after the goal";
        return WatchRules.Evaluate(r, realToday, cal).FirstOrDefault(f => f.Severity == WatchSeverity.Bad)?.Text;
    }
}
