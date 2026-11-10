using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AndaCompanion.Data;
using AndaCompanion.Models;

namespace AndaCompanion.Services;

/// <summary>One item the person added to their own to-do list (by hand, by a custom action, or from an AI or agent suggestion they approved).</summary>
public sealed class TodoItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Number { get; set; }
    public string? Note { get; set; }

    /// <summary>yyyy-MM-dd, or null for no due date.</summary>
    public string? Due { get; set; }
    public bool Done { get; set; }
    public string? DoneOn { get; set; }
    public string? SnoozedUntil { get; set; }
    public string Created { get; set; } = "";

    /// <summary>Who added it: "You", "Action: name" or "Agent: name".</summary>
    public string Source { get; set; } = "You";
}

public sealed class TodoFile
{
    public int Version { get; set; } = 1;
    public List<TodoItem> Items { get; set; } = new();

    /// <summary>Items derived from the person's data that they ticked off, by stable key → the day they did.</summary>
    public Dictionary<string, string> AutoDone { get; set; } = new();
    public Dictionary<string, string> AutoSnoozed { get; set; } = new();
}

public sealed record TodoAddResult(TodoItem? Item, bool Added, string? Reason);

/// <summary>
/// The person's own to-do items, stored as plain JSON in the app's data folder (todo.json) that they can read. Written by this app only
/// when they act, never in the background. Nothing in it is sent anywhere. Reads are tolerant: a damaged file becomes an empty list.
/// </summary>
public sealed class TodoService
{
    public const int MaxItems = 500, MaxTitleChars = 200, MaxNoteChars = 500, KeepDoneDays = 30;
    public const string DateFormat = "yyyy-MM-dd";

    private readonly object _gate = new();
    private readonly string _path;
    private TodoFile _file;

    public event Action? Changed;

    public TodoService(string? path = null)
    {
        _path = path ?? AppPaths.TodoFile;
        _file = Load(_path);
    }

    public static string Iso(DateOnly d) => d.ToString(DateFormat, CultureInfo.InvariantCulture);
    public static DateOnly? ParseIso(string? s) =>
        DateOnly.TryParseExact(s, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static string OneLine(string? s, int max)
    {
        var sb = new StringBuilder();
        foreach (char c in s ?? "") sb.Append(char.IsControl(c) ? ' ' : c);
        string t = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        return t.Length > max ? t[..max].TrimEnd() : t;
    }

    private static TodoFile Load(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 4 * 1024 * 1024) return new();
            return Normalize(JsonSerializer.Deserialize<TodoFile>(File.ReadAllText(path)));
        }
        catch { return new(); }
    }

    /// <summary>Repairs a loaded file: limits, valid dates and numbers, unique ids. Used for the saved file.</summary>
    public static TodoFile Normalize(TodoFile? f)
    {
        var clean = new TodoFile();
        if (f is null) return clean;
        var ids = new HashSet<string>();
        foreach (var raw in (f.Items ?? new()).Where(i => i is not null).Take(MaxItems))
        {
            string title = OneLine(raw.Title, MaxTitleChars);
            if (title.Length == 0) continue;
            string id = OneLine(raw.Id, 24);
            if (id.Length == 0 || !ids.Add(id)) { id = NewId(); ids.Add(id); }
            clean.Items.Add(new TodoItem
            {
                Id = id, Title = title,
                Number = raw.Number is { } n && Regex.IsMatch(n, "^[0-9]{6}$") ? n : null,
                Note = string.IsNullOrWhiteSpace(raw.Note) ? null : OneLine(raw.Note, MaxNoteChars),
                Due = ParseIso(raw.Due) is { } d ? Iso(d) : null,
                Done = raw.Done, DoneOn = ParseIso(raw.DoneOn) is { } dn ? Iso(dn) : null,
                SnoozedUntil = ParseIso(raw.SnoozedUntil) is { } su ? Iso(su) : null,
                Created = OneLine(raw.Created, 32), Source = OneLine(raw.Source, 60) is { Length: > 0 } s ? s : "You",
            });
        }
        foreach (var (k, v) in (f.AutoDone ?? new()).Take(2000)) if (k.Length is > 0 and <= 200 && ParseIso(v) is { } d) clean.AutoDone[k] = Iso(d);
        foreach (var (k, v) in (f.AutoSnoozed ?? new()).Take(2000)) if (k.Length is > 0 and <= 200 && ParseIso(v) is { } d) clean.AutoSnoozed[k] = Iso(d);
        return clean;
    }

    private static string NewId() => Guid.NewGuid().ToString("N")[..12];

    public IReadOnlyList<TodoItem> Items { get { lock (_gate) return _file.Items.Select(Copy).ToList(); } }
    public IReadOnlyDictionary<string, string> AutoDone { get { lock (_gate) return new Dictionary<string, string>(_file.AutoDone); } }
    public IReadOnlyDictionary<string, string> AutoSnoozed { get { lock (_gate) return new Dictionary<string, string>(_file.AutoSnoozed); } }

    private static TodoItem Copy(TodoItem i) => new()
    {
        Id = i.Id, Title = i.Title, Number = i.Number, Note = i.Note, Due = i.Due, Done = i.Done, DoneOn = i.DoneOn,
        SnoozedUntil = i.SnoozedUntil, Created = i.Created, Source = i.Source,
    };

    /// <summary>Adds an item. The same open title for the same ANDA is not added twice. Returns a plain reason if it was refused.</summary>
    public TodoAddResult Add(string? title, string? number, string? note, DateOnly? due, string source, DateOnly today)
    {
        string t = OneLine(title, MaxTitleChars);
        if (t.Length == 0) return new(null, false, "Please type what needs doing.");
        string? n = number is not null && Regex.IsMatch(number, "^[0-9]{6}$") ? number : null;
        TodoItem item;
        lock (_gate)
        {
            var same = _file.Items.FirstOrDefault(i => !i.Done && i.Number == n && i.Title.Equals(t, StringComparison.OrdinalIgnoreCase));
            if (same is not null) return new(Copy(same), false, null);
            if (_file.Items.Count >= MaxItems)
            {
                var oldestDone = _file.Items.Where(i => i.Done).OrderBy(i => i.DoneOn ?? "").FirstOrDefault();
                if (oldestDone is null) return new(null, false, $"The list is full ({MaxItems} items). Please finish or delete some.");
                _file.Items.Remove(oldestDone);
            }
            item = new TodoItem
            {
                Id = NewId(), Title = t, Number = n, Note = string.IsNullOrWhiteSpace(note) ? null : OneLine(note, MaxNoteChars),
                Due = due is { } d ? Iso(d) : null, Created = Iso(today), Source = OneLine(source, 60) is { Length: > 0 } s ? s : "You",
            };
            _file.Items.Add(item);
            SaveLocked();
        }
        Changed?.Invoke();
        return new(Copy(item), true, null);
    }

    private bool Mutate(string id, Action<TodoItem> change)
    {
        lock (_gate)
        {
            var item = _file.Items.FirstOrDefault(i => i.Id == id);
            if (item is null) return false;
            change(item);
            SaveLocked();
        }
        Changed?.Invoke();
        return true;
    }

    public bool SetDone(string id, bool done, DateOnly today) => Mutate(id, i => { i.Done = done; i.DoneOn = done ? Iso(today) : null; if (done) i.SnoozedUntil = null; });
    public bool Snooze(string id, DateOnly until) => Mutate(id, i => i.SnoozedUntil = Iso(until));
    public bool Wake(string id) => Mutate(id, i => i.SnoozedUntil = null);

    public bool Delete(string id)
    {
        lock (_gate)
        {
            if (_file.Items.RemoveAll(i => i.Id == id) == 0) return false;
            SaveLocked();
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>Removes completed items the person added. Items from their data that were ticked off stay marked so they do not come back.</summary>
    public int ClearCompleted()
    {
        int n;
        lock (_gate) { n = _file.Items.RemoveAll(i => i.Done); if (n > 0) SaveLocked(); }
        if (n > 0) Changed?.Invoke();
        return n;
    }

    public void MarkAutoDone(string key, DateOnly today) { lock (_gate) { _file.AutoDone[key] = Iso(today); _file.AutoSnoozed.Remove(key); SaveLocked(); } Changed?.Invoke(); }
    public void UnmarkAutoDone(string key) { lock (_gate) { _file.AutoDone.Remove(key); SaveLocked(); } Changed?.Invoke(); }
    public void SnoozeAuto(string key, DateOnly until) { lock (_gate) { _file.AutoSnoozed[key] = Iso(until); SaveLocked(); } Changed?.Invoke(); }
    public void WakeAuto(string key) { lock (_gate) { _file.AutoSnoozed.Remove(key); SaveLocked(); } Changed?.Invoke(); }

    /// <summary>
    /// Forgets marks for items from the data that no longer exist (the condition ended, or the date moved, so a new one starts fresh),
    /// snoozes that have ended, and completed items older than <see cref="KeepDoneDays"/> days. Does not raise Changed.
    /// </summary>
    public void Prune(ISet<string> liveAutoKeys, DateOnly today)
    {
        lock (_gate)
        {
            bool changed = false;
            foreach (var k in _file.AutoDone.Keys.Where(k => !liveAutoKeys.Contains(k)).ToList()) { _file.AutoDone.Remove(k); changed = true; }
            foreach (var (k, v) in _file.AutoSnoozed.ToList())
                if (!liveAutoKeys.Contains(k) || ParseIso(v) is not { } until || until <= today) { _file.AutoSnoozed.Remove(k); changed = true; }
            changed |= _file.Items.RemoveAll(i => i.Done && ParseIso(i.DoneOn) is { } d && today.DayNumber - d.DayNumber > KeepDoneDays) > 0;
            foreach (var i in _file.Items.Where(i => i.SnoozedUntil is not null && (ParseIso(i.SnoozedUntil) is not { } u || u <= today))) { i.SnoozedUntil = null; changed = true; }
            if (changed) SaveLocked();
        }
    }

    private void SaveLocked()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_file, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, _path, overwrite: true);       // a crash mid-write can never leave a half-written list
        }
        catch { /* best effort: the list still works for this session */ }
    }
}

/// <summary>One line of the to-do list: either the person's own item or one derived from their data.</summary>
public sealed record TodoRow(string Key, bool Auto, string Title, string? Detail, string? Number, DateOnly? Due, string Severity,
    int Priority, bool Done, DateOnly? SnoozedUntil, string Source, string? ItemId);

public sealed record TodoView(IReadOnlyList<TodoRow> Rows, int Open, int Now, int Done, int Snoozed, int Auto, DateTime BuiltAt);

/// <summary>
/// Builds the live list: the person's own items plus, optionally, one item for everything in their data that needs a look, using the same
/// rules as the card and the watch list so the three can never disagree. Pure apart from reading the repository and the store, and
/// computed only when asked (while the list is open), never in the background. It decides nothing: it says what the data shows.
/// </summary>
public static class TodoBuilder
{
    public const int AutoCap = 400, ShowDoneDays = 7;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static TodoView Build(IAndaRepository repo, CardContext ctx, TodoService store, bool includeAuto, DateTime now)
    {
        var today = ctx.Today;
        var rows = new List<TodoRow>();
        var autoDone = store.AutoDone; var autoSnoozed = store.AutoSnoozed;
        var liveKeys = new HashSet<string>();

        if (includeAuto)
        {
            var auto = new List<TodoRow>();
            foreach (var number in repo.Numbers)
                if (repo.Find(number) is { } r) auto.AddRange(AutoRowsFor(r, ctx));
            foreach (var a in auto) liveKeys.Add(a.Key);
            foreach (var a in auto.Take(AutoCap * 4))
            {
                bool done = autoDone.TryGetValue(a.Key, out var doneOn);
                DateOnly? snooze = autoSnoozed.TryGetValue(a.Key, out var sn) && TodoService.ParseIso(sn) is { } su && su > today ? su : null;
                if (done && (TodoService.ParseIso(doneOn) is not { } dd || today.DayNumber - dd.DayNumber > ShowDoneDays)) continue;
                rows.Add(a with { Done = done, SnoozedUntil = done ? null : snooze, Priority = done ? 3 : snooze is not null ? 4 : a.Priority });
            }
        }

        foreach (var i in store.Items)
        {
            var due = TodoService.ParseIso(i.Due);
            string sev = due is { } d ? (d < today ? "bad" : d == today ? "warn" : "info") : "info";
            if (i.Done && (TodoService.ParseIso(i.DoneOn) is not { } dd || today.DayNumber - dd.DayNumber > ShowDoneDays)) continue;
            var snooze = !i.Done && TodoService.ParseIso(i.SnoozedUntil) is { } su && su > today ? su : (DateOnly?)null;
            rows.Add(new TodoRow("m|" + i.Id, false, i.Title, i.Note, i.Number, due, sev, i.Done ? 3 : snooze is not null ? 4 : PriorityOf(sev, due, today),
                i.Done, snooze, i.Source, i.Id));
        }

        store.Prune(liveKeys, today);

        var ordered = rows
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Due is null ? 1 : 0).ThenBy(r => r.Due ?? DateOnly.MaxValue)
            .ThenByDescending(r => Rank(r.Severity))
            .ThenBy(r => r.Number ?? "~", StringComparer.Ordinal)
            .ThenBy(r => r.Title, StringComparer.Ordinal)
            .ToList();
        int autoShown = 0;
        var capped = ordered.Where(r => !r.Auto || ++autoShown <= AutoCap).ToList();
        return new TodoView(capped, capped.Count(r => r.Priority <= 2), capped.Count(r => r.Priority == 0), capped.Count(r => r.Priority == 3),
            capped.Count(r => r.Priority == 4), capped.Count(r => r.Auto), now);
    }

    private static int Rank(string sev) => sev switch { "bad" => 2, "warn" => 1, _ => 0 };

    /// <summary>0 = needs a look now (serious, or due today or earlier), 1 = soon (a caution, or due within a week), 2 = later.</summary>
    public static int PriorityOf(string severity, DateOnly? due, DateOnly today) =>
        severity == "bad" || (due is { } d && d <= today) ? 0
        : severity == "warn" || (due is { } d2 && d2 <= today.AddDays(7)) ? 1 : 2;

    /// <summary>A short stable code for a piece of text, so a key does not carry the whole text.</summary>
    public static string StableHash(string s)
    {
        uint h = 2166136261;
        foreach (char c in s.Trim().ToLowerInvariant()) { h ^= c; h *= 16777619; }
        return h.ToString("x8", Inv);
    }

    private static string D(DateOnly? d) => d is { } v ? TodoService.Iso(v) : "-";

    public static IEnumerable<TodoRow> AutoRowsFor(AndaRecord r, CardContext ctx)
    {
        string n = r.Number;
        var today = ctx.Today;
        // The bundled fictional records are frozen at their extract date; judging them against the real calendar would call them overdue.
        // The watch rules make the same choice, so the list and the card cannot disagree.
        var effective = r.Demo && r.AsOfDate is { } asOf ? asOf : today;
        TodoRow Make(string key, string title, string? detail, DateOnly? due, string sev, string source) =>
            new(key, true, title, detail, n, due, sev, PriorityOf(sev, due, effective), false, null, source, null);

        string? urgentReason = UrgencyPolicy.IsUrgent(r, today, ctx.Calendar, ctx.UrgentLateDays) ? UrgencyPolicy.Reason(r, today, ctx.Calendar, ctx.UrgentLateDays) : null;
        if (urgentReason is not null)
            yield return Make($"{n}|urgent|{D(r.GoalDateValue)}|{D(r.ForecastDateValue)}",
                $"ANDA {n}: at risk. {urgentReason}",
                "From your data: a forecast well after the goal, a serious action item, or a passed goal date.", r.GoalDateValue, "bad", "Your data");

        foreach (var f in WatchRules.Evaluate(r, today, ctx.Calendar, new WatchSettings(HolidayLeadBusinessDays: ctx.HolidayLeadDays)).Where(f => f.Severity != WatchSeverity.Info))
        {
            if (urgentReason is not null && f.Text == urgentReason) continue;   // the at-risk row already says this: never list one fact twice
            DateOnly? date = f.Code switch
            {
                "goal-passed" or "goal-soon" or "goal-date-calendar" => r.GoalDateValue,
                "forecast-date-calendar" => r.ForecastDateValue,
                "stale-data" => r.AsOfDate,
                "psg-after-submission" => r.PsgDateFromSheet,
                _ => null,
            };
            DateOnly? due = f.Code == "goal-passed" ? effective : f.Code is "goal-soon" or "goal-date-calendar" or "forecast-date-calendar" ? date : null;
            yield return Make($"{n}|{f.Code}|{D(date)}", $"ANDA {n}: {f.Text}", f.Why, due, CardModels.Severity(f.Severity), f.Source);
        }

        if (r.Attention is { } a && !string.IsNullOrWhiteSpace(a.Text))
            yield return Make($"{n}|attention|{StableHash(a.Text)}", $"ANDA {n}: {a.Text}", "From the record: the item that needs action.", null,
                a.Level == AttentionLevel.Bad ? "bad" : "warn", r.SourceLabel ?? "The record");
    }
}
