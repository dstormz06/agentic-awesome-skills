using AndaCompanion.Models;

namespace AndaCompanion.Services;

public sealed record TodayRow(string Id, string Mark, string Line);
public sealed record TodayGroup(string Title, IReadOnlyList<string> Ids, IReadOnlyList<TodayRow> Rows, int More = 0);
public sealed record TodayMain(string Meta, string Tone, string Figure, string Words, string Dates, string? Attn, string? Watch, string Status);

/// <summary>
/// Today (round 18 in the simulation, round 33 here): a small two-pane window. Left: what needs a look, the ANDAs kept in my list, recents.
/// Right: the selected one, with Copy status. The same rules as the card and the watch list, read when the window is opened; nothing runs
/// while it is closed.
/// </summary>
public static class Today
{
    /// <summary>Each mark in words, shown beside the number, so that its meaning is not carried by color alone.</summary>
    public static string MarkWord(string mark) => mark switch { "r" => "urgent", "w" => "worth a look", _ => "on track" };

    /// <summary>What is said when a row is pressed: short, so that the whole right-hand side is not read again.</summary>
    public static string Shown(string id, TodayMain v) => $"Shown: ANDA {id}, {(v.Figure == "On" ? "on the goal date" : v.Figure == AndaRecord.Unknown ? v.Words : v.Figure + " " + v.Words)}.";

    public const string SaidNothing = "Nothing needs a look. Type a number above to open an ANDA.", SaidChoose = "Choose an ANDA on the left.";
    /// <summary>How many rows "needs a look" shows. A large portfolio has more; the watch list holds them all.</summary>
    public const int NeedLookMax = 40;

    /// <summary>What Today needs to know about a record besides the record: whether it is urgent, and its watch flags, by the card's own rules.</summary>
    public sealed record Look(AndaRecord Record, bool Urgent, IReadOnlyList<WatchFlag> Flags);

    /// <summary>The row's mark: "r" when urgent; "w" when something is flagged, needs the person, or the forecast is later than the goal; "g" otherwise.</summary>
    public static string Mark(Look x) => x.Urgent ? "r" : x.Flags.Count > 0 || x.Record.Attention is not null || x.Record.ForecastDeltaDays > 0 ? "w" : "g";

    public static string Line(AndaRecord r)
    {
        if (r.ForecastDeltaDays is not { } d) return "No forecast in your data";      // the simulation has no record without a forecast
        return d > 0 ? $"{d} d later" + (r.Attention is { } a ? $" · {a.Text.Split(" — ")[0]}" : "") : d < 0 ? $"{-d} d ahead" : "On the goal date";
    }

    public static bool NeedsLook(Look x) => x.Urgent || x.Flags.Count > 0 || x.Record.Attention is not null;

    /// <summary>The three groups on the left. "Needs a look" is the most pressing first: red before amber, and within one mark the lower number first.</summary>
    public static List<TodayGroup> Groups(IEnumerable<string> pins, IEnumerable<string> recents, IEnumerable<Look> all, Func<string, Look?> find)
    {
        var p = pins.ToList();
        var need = all.Where(NeedsLook).OrderBy(x => "rwg".IndexOf(Mark(x), StringComparison.Ordinal)).ThenBy(x => x.Record.Number, StringComparer.Ordinal).ToList();
        TodayGroup Group(string title, IEnumerable<Look> looks, int more = 0)
        {
            var list = looks.ToList();
            return new TodayGroup(title, list.Select(x => x.Record.Number).ToList(), list.Select(x => new TodayRow(x.Record.Number, Mark(x), Line(x.Record))).ToList(), more);
        }
        IEnumerable<Look> Known(IEnumerable<string> ids) => ids.Select(find).Where(x => x is not null)!;
        return new List<TodayGroup>
        {
            Group("NEEDS A LOOK", need.Take(NeedLookMax), Math.Max(0, need.Count - NeedLookMax)),
            Group("KEPT IN MY LIST", Known(p)),
            Group("RECENT", Known(recents.Where(x => !p.Contains(x)))),
        };
    }

    /// <summary>What is said under "needs a look" when the portfolio has more than are shown.</summary>
    public static string More(int n) => $"{n} more {(n == 1 ? "is" : "are")} in the watch list.";

    /// <summary>What the right-hand side says about one ANDA.</summary>
    public static TodayMain Main(Look x)
    {
        var r = x.Record;
        string dates = $"Goal {r.GoalDate} · Forecast {r.ForecastDate}" + (r.ForecastConfidence.Length > 0 ? $" · {r.ForecastConfidence}" : "");
        string? watch = x.Flags.Count > 0 ? "Watch: " + x.Flags[0].Text : null;
        if (r.ForecastDeltaDays is not { } d)
            return new TodayMain($"ANDA {r.Number} · {r.Drug}", "good", AndaRecord.Unknown, "no forecast in your data", dates, r.Attention?.Text, watch, StatusLine.Build(r));
        int n = Math.Abs(d);
        return new TodayMain($"ANDA {r.Number} · {r.Drug}", d > 0 ? "late" : "good", d == 0 ? "On" : n.ToString(System.Globalization.CultureInfo.InvariantCulture),
            d == 0 ? "the goal date" : $"day{(n == 1 ? "" : "s")} {(d > 0 ? "later than" : "ahead of")} the goal", dates, r.Attention?.Text, watch, StatusLine.Build(r));
    }
}
