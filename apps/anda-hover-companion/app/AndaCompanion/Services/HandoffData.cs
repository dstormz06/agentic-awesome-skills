using AndaCompanion.Data;
using AndaCompanion.Models;

namespace AndaCompanion.Services;

/// <summary>
/// Fills a hand-off page or a meeting page from the person's own data and notes: the status line the card copies, what the watch rules flag,
/// the open items and the latest notes. Nothing new is decided here, and nothing is sent. The page itself is written by <see cref="Handoff"/>.
/// </summary>
public static class HandoffData
{
    private static string Cut(string t, int n) => FileCompare.Show(string.Join(" ", t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)), n);

    /// <summary>The ANDAs of a page, in order: those that need a look first, worst first, then the rest by number.</summary>
    public static IReadOnlyList<string> Order(IAndaRepository repo, CardContext ctx, bool onlyThoseThatNeedALook)
    {
        var watch = WatchList.Build(repo, ctx).Items.Select(i => i.Number).ToList();
        if (onlyThoseThatNeedALook) return watch;
        return watch.Concat(repo.Numbers.Where(n => !watch.Contains(n) && repo.Find(n) is not null).OrderBy(n => n, StringComparer.Ordinal)).ToList();
    }

    public static HandoffEntry? Entry(string number, IAndaRepository repo, CardContext ctx, QuickNotesService? quick, IEnumerable<TodoItem>? andaTodo, IEnumerable<MyTodoItem>? myTodo)
    {
        if (repo.Find(number) is not { } r) return null;
        var watch = new List<string>();
        if (UrgencyPolicy.IsUrgent(r, ctx.Today, ctx.Calendar, ctx.UrgentLateDays)) watch.Add(Cut("at risk. " + UrgencyPolicy.Reason(r, ctx.Today, ctx.Calendar, ctx.UrgentLateDays), 140));
        foreach (var f in WatchRules.Evaluate(r, ctx.Today, ctx.Calendar, ctx.Watch).Where(f => f.Severity != WatchSeverity.Info))
        {
            string text = Cut(f.Text, 140);
            if (!watch.Any(w => w.Contains(text, StringComparison.Ordinal))) watch.Add(text);
        }
        var group = Recall.For("ANDA " + r.Number, repo.Find, rec => DraftNotes.For(rec, quick, ctx.Today), andaTodo, myTodo).FirstOrDefault();
        var open = (group?.Items ?? Array.Empty<RecallItem>()).Take(5).Select(i => i.Title + (i.Due.Length > 0 ? $" ({i.Due})" : "")).ToList();
        var notes = (group?.Notes ?? Array.Empty<RecallNote>()).Take(3).Select(n => $"{n.Note.Who}: {Cut(n.Note.Text, 160)}").ToList();
        return new HandoffEntry(StatusLine.Build(r), watch, open, notes);
    }

    /// <summary>
    /// Where the data of a page is from, as of when and whether it is STALE: the card's own line, taken from the first ANDA listed (one file
    /// feeds them all), or from any ANDA in the data when the page lists none.
    /// </summary>
    public static string DataLine(IEnumerable<string> numbers, IAndaRepository repo, long nowMs, int staleDays, TimeZoneInfo zone)
    {
        var record = numbers.Concat(repo.Numbers).Select(repo.Find).FirstOrDefault(r => r is not null);
        return record is null ? "" : Clock.DataLine(record, nowMs, staleDays, zone).Text;
    }

    public static IReadOnlyList<HandoffEntry> Entries(IEnumerable<string> numbers, IAndaRepository repo, CardContext ctx, QuickNotesService? quick, IEnumerable<TodoItem>? andaTodo, IEnumerable<MyTodoItem>? myTodo) =>
        numbers.Select(n => Entry(n, repo, ctx, quick, andaTodo?.ToList(), myTodo?.ToList())).Where(e => e is not null).Select(e => e!).ToList();
}
