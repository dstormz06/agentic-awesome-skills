using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AndaCompanion.Services;

/// <summary>One line of the person's own checklist: anything, for work or home. It is not tied to an ANDA, though an ANDA number in the text is kept so the card can be opened.</summary>
public sealed class ChecklistItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public ChecklistCategory Category { get; set; } = ChecklistCategory.Other;
    /// <summary>The due date, yyyy-MM-dd, or null.</summary>
    public string? Due { get; set; }
    public bool High { get; set; }
    public Repeat Repeat { get; set; }
    /// <summary>Weekly: the day of the week (0 = Sunday); monthly: the day of the month. Otherwise -1.</summary>
    public int RepeatDay { get; set; } = -1;
    public bool Done { get; set; }
    public string? DoneOn { get; set; }
    public string Created { get; set; } = "";
    public string? Number { get; set; }
    /// <summary>Where it came from: You, Notes (read from text the person highlighted or pasted) or Quick add.</summary>
    public string Source { get; set; } = "You";
}

public sealed class ChecklistFile
{
    public int Version { get; set; } = 1;
    public List<ChecklistItem> Items { get; set; } = new();
    /// <summary>How many items were finished on each day (last 60 days), so the window can say what was done this week.</summary>
    public Dictionary<string, int> DoneByDay { get; set; } = new();
}

public sealed record ChecklistAddResult(ChecklistItem? Item, bool Added, string? Reason);

/// <summary>
/// The person's checklist, kept in a plain file (<c>checklist.json</c>) on this PC and never sent anywhere. It is separate from the ANDA to-do list:
/// it is for anything, and nothing in it comes from the person's data. Items that repeat come back by themselves (daily, on weekdays, weekly, monthly),
/// and a missed repeating item is not left as a pile of overdue copies. Written by replacing the file in one step.
/// </summary>
public sealed class ChecklistService
{
    public const int MaxItems = 500, MaxTitleChars = 200, KeepDoneDays = 30, KeepCountDays = 60;
    private readonly string _path;
    private readonly object _gate = new();
    private ChecklistFile _file;

    public event Action? Changed;

    public ChecklistService(string? path = null)
    {
        _path = path ?? AppPaths.ChecklistFile;
        _file = Load(_path);
    }

    public static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static DateOnly? ParseIso(string? s) => DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static string OneLine(string? s, int max)
    {
        var sb = new StringBuilder();
        foreach (char c in s ?? "") sb.Append(char.IsControl(c) ? ' ' : c);
        string t = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        return t.Length > max ? t[..max].TrimEnd() : t;
    }

    private static ChecklistFile Load(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 4 * 1024 * 1024) return new();
            return Normalize(JsonSerializer.Deserialize<ChecklistFile>(File.ReadAllText(path)));
        }
        catch { return new(); }
    }

    /// <summary>Repairs a loaded file: limits, valid enums, dates and numbers, unique ids.</summary>
    public static ChecklistFile Normalize(ChecklistFile? f)
    {
        var clean = new ChecklistFile();
        if (f is null) return clean;
        var ids = new HashSet<string>();
        foreach (var raw in (f.Items ?? new()).Where(i => i is not null).Take(MaxItems))
        {
            string title = OneLine(raw.Title, MaxTitleChars);
            if (title.Length == 0) continue;
            string id = OneLine(raw.Id, 24);
            if (id.Length == 0 || !ids.Add(id)) { id = NewId(); ids.Add(id); }
            var repeat = Enum.IsDefined(raw.Repeat) ? raw.Repeat : Repeat.None;
            int day = repeat switch { Repeat.Weekly => raw.RepeatDay is >= 0 and <= 6 ? raw.RepeatDay : -1, Repeat.Monthly => raw.RepeatDay is >= 1 and <= 31 ? raw.RepeatDay : -1, _ => -1 };
            clean.Items.Add(new ChecklistItem
            {
                Id = id, Title = title, Category = Enum.IsDefined(raw.Category) ? raw.Category : ChecklistCategory.Other,
                Due = ParseIso(raw.Due) is { } d ? Iso(d) : null, High = raw.High, Repeat = repeat, RepeatDay = day,
                Done = raw.Done, DoneOn = ParseIso(raw.DoneOn) is { } dn ? Iso(dn) : null,
                Created = OneLine(raw.Created, 32), Number = raw.Number is { } n && Regex.IsMatch(n, "^[0-9]{6}$") ? n : null,
                Source = OneLine(raw.Source, 40) is { Length: > 0 } s ? s : "You",
            });
        }
        foreach (var (k, v) in (f.DoneByDay ?? new()).Take(500)) if (ParseIso(k) is not null && v is >= 0 and <= 100000) clean.DoneByDay[k] = v;
        return clean;
    }

    private static string NewId() => Guid.NewGuid().ToString("N")[..12];

    private static ChecklistItem Copy(ChecklistItem i) => new()
    {
        Id = i.Id, Title = i.Title, Category = i.Category, Due = i.Due, High = i.High, Repeat = i.Repeat, RepeatDay = i.RepeatDay, Done = i.Done,
        DoneOn = i.DoneOn, Created = i.Created, Number = i.Number, Source = i.Source,
    };

    public IReadOnlyList<ChecklistItem> Items { get { lock (_gate) return _file.Items.Select(Copy).ToList(); } }
    public IReadOnlyDictionary<string, int> DoneByDay { get { lock (_gate) return new Dictionary<string, int>(_file.DoneByDay); } }

    /// <summary>How many items were finished in the <paramref name="days"/> days up to and including today.</summary>
    public int FinishedInLast(int days, DateOnly today)
    {
        lock (_gate)
            return _file.DoneByDay.Where(kv => ParseIso(kv.Key) is { } d && today.DayNumber - d.DayNumber is >= 0 && today.DayNumber - d.DayNumber < days).Sum(kv => kv.Value);
    }

    public ChecklistAddResult Add(string? title, ChecklistCategory category, DateOnly? due, bool high, Repeat repeat, int repeatDay, string? number, string source, DateOnly today)
    {
        string t = OneLine(title, MaxTitleChars);
        if (t.Length == 0) return new(null, false, "Please type what needs doing.");
        if (!Enum.IsDefined(category)) category = ChecklistCategory.Other;
        if (!Enum.IsDefined(repeat)) repeat = Repeat.None;
        int day = repeat switch { Repeat.Weekly => repeatDay is >= 0 and <= 6 ? repeatDay : (int)(due ?? today).DayOfWeek, Repeat.Monthly => repeatDay is >= 1 and <= 31 ? repeatDay : (due ?? today).Day, _ => -1 };
        // A repeating item always has a date, so it is shown in the right place and comes back on time.
        if (repeat != Repeat.None && due is null) due = NoteParser.OnOrAfter(repeat, day, today);
        ChecklistItem item;
        lock (_gate)
        {
            var same = _file.Items.FirstOrDefault(i => !i.Done && i.Category == category && i.Title.Equals(t, StringComparison.OrdinalIgnoreCase));
            if (same is not null) return new(Copy(same), false, null);
            if (_file.Items.Count >= MaxItems)
            {
                var oldest = _file.Items.Where(i => i.Done && i.Repeat == Repeat.None).OrderBy(i => i.DoneOn ?? "").FirstOrDefault();
                if (oldest is null) return new(null, false, $"The checklist is full ({MaxItems} items). Please finish or delete some.");
                _file.Items.Remove(oldest);
            }
            item = new ChecklistItem
            {
                Id = NewId(), Title = t, Category = category, Due = due is { } d ? Iso(d) : null, High = high, Repeat = repeat, RepeatDay = day,
                Created = Iso(today), Number = number is not null && Regex.IsMatch(number, "^[0-9]{6}$") ? number : null,
                Source = OneLine(source, 40) is { Length: > 0 } s ? s : "You",
            };
            _file.Items.Add(item);
            SaveLocked();
        }
        Changed?.Invoke();
        return new(Copy(item), true, null);
    }

    public ChecklistAddResult AddSuggested(SuggestedItem s, string source, DateOnly today) =>
        Add(s.Title, s.Category, s.Due, s.High, s.Repeat, s.RepeatDay, s.Number, source, today);

    private bool Mutate(string id, Action<ChecklistItem> change)
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

    public bool SetDone(string id, bool done, DateOnly today) => Mutate(id, i =>
    {
        if (i.Done == done) return;
        if (done) { i.Done = true; i.DoneOn = Iso(today); Count(i.DoneOn, +1); }
        else { Count(i.DoneOn, -1); i.Done = false; i.DoneOn = null; }
    });

    private void Count(string? day, int by)
    {
        if (day is null) return;
        _file.DoneByDay[day] = Math.Max(0, (_file.DoneByDay.TryGetValue(day, out int n) ? n : 0) + by);
    }

    /// <summary>Changes an item's text and settings. Returns a plain reason if it was refused.</summary>
    public string? Edit(string id, string? title, ChecklistCategory category, DateOnly? due, bool high, Repeat repeat, int repeatDay, DateOnly? today = null)
    {
        var now = today ?? DateOnly.FromDateTime(DateTime.Now);
        string t = OneLine(title, MaxTitleChars);
        if (t.Length == 0) return "Please type what needs doing.";
        if (!Enum.IsDefined(category)) category = ChecklistCategory.Other;
        if (!Enum.IsDefined(repeat)) repeat = Repeat.None;
        bool found = Mutate(id, i =>
        {
            int day = repeat switch { Repeat.Weekly => repeatDay is >= 0 and <= 6 ? repeatDay : (int)(due ?? now).DayOfWeek, Repeat.Monthly => repeatDay is >= 1 and <= 31 ? repeatDay : (due ?? now).Day, _ => -1 };
            if (repeat != Repeat.None && due is null) due = NoteParser.OnOrAfter(repeat, day, now);
            i.Title = t; i.Category = category; i.Due = due is { } d ? Iso(d) : null; i.High = high; i.Repeat = repeat; i.RepeatDay = day;
        });
        return found ? null : "That item is no longer on the list.";
    }

    public bool MoveDue(string id, DateOnly? due) => Mutate(id, i => i.Due = due is { } d ? Iso(d) : null);

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

    /// <summary>Puts back an item that was just deleted (the Undo button). Refused if the list is full or the item is already there.</summary>
    public bool Restore(ChecklistItem item)
    {
        lock (_gate)
        {
            if (_file.Items.Count >= MaxItems || _file.Items.Any(i => i.Id == item.Id)) return false;
            _file.Items.Add(Copy(item));
            SaveLocked();
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>Removes finished items that do not repeat. Items that repeat stay: they come back by themselves.</summary>
    public int ClearFinished()
    {
        int n;
        lock (_gate) { n = _file.Items.RemoveAll(i => i.Done && i.Repeat == Repeat.None); if (n > 0) SaveLocked(); }
        if (n > 0) Changed?.Invoke();
        return n;
    }

    /// <summary>Moves every open item that is past due, and does not repeat, to today. Returns how many moved.</summary>
    public int RollOverOverdue(DateOnly today)
    {
        int n = 0;
        lock (_gate)
        {
            foreach (var i in _file.Items.Where(i => !i.Done && i.Repeat == Repeat.None && ParseIso(i.Due) is { } d && d < today)) { i.Due = Iso(today); n++; }
            if (n > 0) SaveLocked();
        }
        if (n > 0) Changed?.Invoke();
        return n;
    }

    /// <summary>The next date a repeating item falls due after the occurrence it was last due on, and never before today.</summary>
    public static DateOnly NextDue(ChecklistItem i, DateOnly today)
    {
        var basis = ParseIso(i.Due) ?? ParseIso(i.DoneOn) ?? today;
        var next = NoteParser.OnOrAfter(i.Repeat, i.RepeatDay, basis.AddDays(1));
        return next < today ? NoteParser.OnOrAfter(i.Repeat, i.RepeatDay, today) : next;
    }

    /// <summary>
    /// Run when the window opens, when it refreshes and at midnight: a repeating item finished on an earlier day comes back open with its next due date, a
    /// repeating item that was missed moves to its next occurrence instead of piling up, and old finished items and old counts are forgotten. Returns true if anything changed.
    /// </summary>
    public bool Refresh(DateOnly today)
    {
        bool changed = false;
        lock (_gate)
        {
            foreach (var i in _file.Items.Where(i => i.Repeat != Repeat.None))
            {
                if (i.Done && ParseIso(i.DoneOn) is { } doneOn && doneOn < today) { i.Due = Iso(NextDue(i, today)); i.Done = false; i.DoneOn = null; changed = true; }
                else if (!i.Done && ParseIso(i.Due) is { } due && due < today) { i.Due = Iso(NoteParser.OnOrAfter(i.Repeat, i.RepeatDay, today)); changed = true; }
            }
            changed |= _file.Items.RemoveAll(i => i.Done && i.Repeat == Repeat.None && ParseIso(i.DoneOn) is { } d && today.DayNumber - d.DayNumber > KeepDoneDays) > 0;
            changed |= _file.DoneByDay.Keys.Where(k => ParseIso(k) is not { } d || today.DayNumber - d.DayNumber > KeepCountDays).ToList().Select(k => _file.DoneByDay.Remove(k)).Any();
            if (changed) SaveLocked();
        }
        if (changed) Changed?.Invoke();
        return changed;
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

public sealed record ChecklistGroup(string Heading, IReadOnlyList<ChecklistItem> Items);

/// <summary>What the checklist window shows: groups worst-first, how today is going, and what to start with.</summary>
public sealed record ChecklistView(IReadOnlyList<ChecklistGroup> Groups, int Open, int Overdue, int DoneToday, int TodayTotal, int Finished7, string? Focus);

public static class ChecklistBuilder
{
    public const int RecentDoneDays = 7, ComingUpDays = 7;

    /// <summary>
    /// Overdue, then today, then the next seven days, then anything else, then what was finished in the last seven days. In each group a high-priority
    /// item comes first, then the earlier date, then Work, Home and Other. "Today" counts what is open and due by today plus what was finished today.
    /// </summary>
    public static ChecklistView Build(IReadOnlyList<ChecklistItem> all, DateOnly today, int finished7, ChecklistCategory? filter = null)
    {
        var items = all.Where(i => filter is null || i.Category == filter).ToList();
        DateOnly? Due(ChecklistItem i) => ChecklistService.ParseIso(i.Due);
        IEnumerable<ChecklistItem> Order(IEnumerable<ChecklistItem> q) => q
            .OrderBy(i => i.High ? 0 : 1).ThenBy(i => Due(i) is null ? 1 : 0).ThenBy(i => Due(i) ?? DateOnly.MaxValue)
            .ThenBy(i => (int)i.Category).ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase);

        var open = items.Where(i => !i.Done).ToList();
        var overdue = Order(open.Where(i => Due(i) is { } d && d < today)).ToList();
        var due0 = Order(open.Where(i => Due(i) == today)).ToList();
        var soon = Order(open.Where(i => Due(i) is { } d && d > today && d <= today.AddDays(ComingUpDays))).ToList();
        var later = Order(open.Where(i => Due(i) is null || Due(i) > today.AddDays(ComingUpDays))).ToList();
        var done = items.Where(i => i.Done && ChecklistService.ParseIso(i.DoneOn) is { } d && today.DayNumber - d.DayNumber is >= 0 && today.DayNumber - d.DayNumber < RecentDoneDays)
            .OrderByDescending(i => i.DoneOn).ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase).ToList();

        var groups = new List<ChecklistGroup>();
        if (overdue.Count > 0) groups.Add(new("OVERDUE", overdue));
        if (due0.Count > 0) groups.Add(new("TODAY", due0));
        if (soon.Count > 0) groups.Add(new("COMING UP · NEXT 7 DAYS", soon));
        if (later.Count > 0) groups.Add(new("ANYTIME", later));
        if (done.Count > 0) groups.Add(new("DONE IN THE LAST 7 DAYS", done));

        int doneToday = items.Count(i => i.Done && i.DoneOn == ChecklistService.Iso(today));
        int todayTotal = overdue.Count + due0.Count + doneToday;
        string? focus = (overdue.Count > 0 ? overdue : due0).FirstOrDefault()?.Title;
        return new ChecklistView(groups, open.Count, overdue.Count, doneToday, todayTotal, finished7, focus);
    }
}
