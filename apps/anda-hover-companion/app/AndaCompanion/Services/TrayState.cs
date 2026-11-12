using AndaCompanion.Data;

namespace AndaCompanion.Services;

/// <summary>
/// What the tray icon says. Until round 22 the amber dot followed the card's "urgent" rule (a forecast well after the goal, a serious action
/// item or a passed goal date), so with most real portfolios, and always with the sample data, it was on all the time and told nobody anything.
/// It now shows only when something needs the person today: an action item that says it is due today or overdue, or a goal date that is
/// today or has passed. The sample records never raise it. A message marked urgent that has waited two days, and a column of the sheet that
/// marks a safety or customer service issue, are in the simulation and not in the app yet (the app does not read mail in the background).
/// It is recounted when the app reads data (at start, when a card opens, when Settings are applied) and, since round 23, on the schedule the
/// person chooses in Settings (never, once just after midnight, or every hour as well): see <see cref="TrayRecount"/>.
/// One thing shows a dot; two to nine show the number; more show "9+".
/// </summary>
public static class TrayState
{
    private static readonly System.Text.RegularExpressions.Regex DueNow = new(@"\b(due today|overdue|past due|was due)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    /// <summary>Whether this record needs the person today. A sample record never does.</summary>
    public static bool NeedsAttentionToday(AndaCompanion.Models.AndaRecord r, DateOnly today)
    {
        if (r.Demo) return false;
        if (r.GoalDateValue is { } goal && goal <= today) return true;
        try { return r.Attention is { } a && DueNow.IsMatch(a.Text); }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { return false; }
    }

    public static int AlertCount(IAndaRepository repo, DateOnly today, int cap = 500)
    {
        int n = 0, seen = 0;
        foreach (string number in repo.Numbers)
        {
            if (++seen > cap) break;
            if (repo.Find(number) is { } r && NeedsAttentionToday(r, today)) n++;
        }
        return n;
    }

    /// <summary>What the icon carries for this many things: nothing, a dot, the number, or "9+".</summary>
    public static string Badge(int alerts) => alerts <= 0 ? "" : alerts == 1 ? "dot" : alerts > 9 ? "9+" : alerts.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The embedded picture for that badge (see Assets/make-icons.mjs, which draws them).</summary>
    public static string Picture(int alerts) => Badge(alerts) switch
    {
        "" => "tray-32.png",
        "dot" => "tray-32-alert.png",
        "9+" => "tray-32-9plus.png",
        var n => $"tray-32-{n}.png",
    };

    /// <summary>The tooltip says how many, never which: a tooltip can be read over a shoulder.</summary>
    public static string Tip(string connectionLabel, int alerts) =>
        $"ANDA Companion (Notula) · {connectionLabel}" + (alerts > 0 ? $" · {alerts} {(alerts == 1 ? "ANDA needs" : "ANDAs need")} your attention today" : "");
}
