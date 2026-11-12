using AndaCompanion.Data;

namespace AndaCompanion.Services;

/// <summary>
/// What the tray icon says: how many ANDAs in the data are urgent, by the same rule as the card (a forecast well after the goal, a serious
/// action item or a passed goal date). The icon gets a small amber dot when the count is above zero. It is recomputed only when the app
/// reads data (at start, when a card opens, when Settings are applied): there is no timer and nothing runs in the background.
/// </summary>
public static class TrayState
{
    public static int UrgentCount(IAndaRepository repo, DateOnly today, BusinessCalendar calendar, int lateDays, int cap = 500)
    {
        int n = 0, seen = 0;
        foreach (string number in repo.Numbers)
        {
            if (++seen > cap) break;
            if (repo.Find(number) is { } r && UrgencyPolicy.IsUrgent(r, today, calendar, lateDays)) n++;
        }
        return n;
    }

    /// <summary>The tooltip says how many, never which: a tooltip can be read over a shoulder.</summary>
    public static string Tip(string connectionLabel, int urgent) =>
        $"ANDA Companion (Notula) · {connectionLabel}" + (urgent > 0 ? $" · {urgent} ANDA{(urgent == 1 ? " is" : "s are")} urgent" : "");
}
