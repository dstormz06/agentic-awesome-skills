using AndaCompanion.Data;

namespace AndaCompanion.Services;

public sealed record WatchListItem(string Number, string Drug, string GoalDate, string Severity, string? TopFlag, int FlagCount, bool Urgent, string? UrgentReason);

public sealed record WatchListResult(IReadOnlyList<WatchListItem> Items, int Total, int Shown);

/// <summary>
/// The "watch your back" view across a whole portfolio: every ANDA that has something worth a second look, worst first. It is
/// the same rules the card uses, applied to every record, so the list can never disagree with a card. Computed on demand when the
/// person opens it; nothing runs in the background and nothing is sent anywhere.
/// </summary>
public static class WatchList
{
    private static int Rank(string severity) => severity switch { "bad" => 2, "warn" => 1, _ => 0 };

    public static WatchListResult Build(IAndaRepository repo, CardContext ctx, int limit = 500)
    {
        var items = new List<(WatchListItem Item, DateOnly? Goal)>();
        int total = 0;
        foreach (var number in repo.Numbers)
        {
            if (repo.Find(number) is not { } r) continue;
            total++;
            var flags = WatchRules.Evaluate(r, ctx.Today, ctx.Calendar, new WatchSettings(HolidayLeadBusinessDays: ctx.HolidayLeadDays));
            bool urgent = UrgencyPolicy.IsUrgent(r, ctx.Today, ctx.Calendar, ctx.UrgentLateDays);
            if (flags.Count == 0 && !urgent) continue;
            string sev = flags.Count == 0 ? "info" : CardModels.Severity(flags.Max(f => f.Severity));
            items.Add((new WatchListItem(number, r.Drug, r.GoalDate, sev, flags.Count > 0 ? flags[0].Text : null, flags.Count, urgent,
                urgent ? UrgencyPolicy.Reason(r, ctx.Today, ctx.Calendar, ctx.UrgentLateDays) : null), r.GoalDateValue));
        }
        var ordered = items
            .OrderByDescending(x => Rank(x.Item.Severity))
            .ThenByDescending(x => x.Item.Urgent)
            .ThenBy(x => x.Goal is null ? 1 : 0)
            .ThenBy(x => x.Goal ?? DateOnly.MaxValue)
            .ThenBy(x => x.Item.Number, StringComparer.Ordinal)
            .Select(x => x.Item).ToList();
        return new WatchListResult(ordered.Take(limit).ToList(), total, ordered.Count);
    }
}
