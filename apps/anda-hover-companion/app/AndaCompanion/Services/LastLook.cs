using System.Text.Json;
using System.Text.Json.Serialization;
using AndaCompanion.Models;

namespace AndaCompanion.Services;

/// <summary>
/// Round 36: "Since you last looked", in Today. What this PC keeps of a look: for each ANDA its goal date, its forecast date and a short
/// fingerprint of its open item (never the item's words), with the time it was kept. It is kept in last-look.json beside the settings when
/// Today is closed, read when Today opens, and sent nowhere. The rules are the simulation's (mockup/round36-golden.json).
/// </summary>
public static class LastLook
{
    public sealed record Item([property: JsonPropertyName("goal")] string? Goal, [property: JsonPropertyName("forecast")] string? Forecast, [property: JsonPropertyName("item")] string? Print);
    /// <param name="Source">Whose data the look is of: "demo", or "file:" and a fingerprint of the sheet's path (app only; the simulation has one
    /// source). Two looks of different data are never compared (round 36 review).</param>
    public sealed record Look([property: JsonPropertyName("at")] string At, [property: JsonPropertyName("items")] IReadOnlyDictionary<string, Item> Items,
        [property: JsonPropertyName("source"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Source = null);
    public sealed record Change(string Id, string Kind, string Text);

    public const int Max = 40;
    public const string SaidFirst = "From now on, what moves in your data between two looks is listed here.";
    public const string SaidNone = "Nothing has moved since you last looked.";
    public static string SaidNote(long atMs, TimeZoneInfo zone, string? ianaId = null) => $"Compared with what this PC kept when you last closed Today: {Clock.Label(atMs, zone, false, ianaId)}.";
    public static string SaidMore(int more) => $"{more} more change{(more == 1 ? "" : "s")}; the card of each ANDA shows its own.";
    // round 37: the switch in Settings, beside Recent ANDAs (the simulation's words, mockup/round37-golden.json)
    public const string SwitchLabel = "List what moved in my data since I last closed Today";
    public const string SwitchHint = "Kept on this PC when you close Today, and never sent anywhere: for each ANDA its number, goal date, forecast date and a short fingerprint of its open item (never the item's words), and when it was kept. Turning this off removes what is kept, and Today keeps nothing for it.";
    public const string SaidSwitchOff = "Since you last looked is off. What this PC kept for it was removed.";
    public const string SaidSwitchOffNotYet = "Since you last looked is off. What this PC kept for it could not be removed just now; it is tried again each time the app starts or a setting changes.";
    public const string SaidSwitchOn = "Since you last looked is on. The next time you close Today is the first look kept.";
    public const string SaidUndated = "Compared with what this PC kept when you last closed Today.";
    public const string SaidUnreadable = "Your data could not be read just now, so nothing is compared, and the look kept last time stays as it was.";
    public const string SaidOtherData = "The look kept last time was of other data (the sample records, or another file), so nothing is compared this time. From now on, what moves is listed here.";

    /// <summary>
    /// Whether to compare and whether to keep (round 36 review). Data that could not be read, or that has no ANDA in it, is neither compared
    /// nor kept, so that a sheet open in another program never lists every ANDA as gone and never overwrites the last good look. A look of
    /// other data (the sample records, another file) is not compared, but the new one is kept. Note is null when the caller says when.
    /// </summary>
    public static (Look? Compare, string? Note, bool Keep) Decide(Look? kept, Look now, bool readable)
    {
        if (!readable || now.Items.Count == 0) return (null, SaidUnreadable, false);
        if (kept is null) return (null, null, true);
        if (kept.Source != now.Source) return (null, SaidOtherData, true);
        return (kept, null, true);
    }

    /// <summary>FNV-1a over the UTF-16 code units, as eight hex digits: the simulation's. It tells that an item changed without keeping what it says.</summary>
    public static string Fingerprint(string text)
    {
        uint h = 0x811c9dc5;
        foreach (char c in text) { h ^= c; h = unchecked(h * 0x01000193); }
        return h.ToString("x8");
    }

    private static string? Iso(DateOnly? d) => d?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    private static DateOnly? Day(string? iso) => DateOnly.TryParseExact(iso ?? "", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : null;

    public static Look Of(IEnumerable<AndaRecord> records, DateTime utc, string? source = null)
    {
        var items = new SortedDictionary<string, Item>(StringComparer.Ordinal);
        foreach (var r in records)
            items[r.Number] = new Item(Iso(r.GoalDateValue), Iso(r.HasForecast ? r.ForecastDateValue : null), r.Attention is { Text: { Length: > 0 } t } ? Fingerprint(t) : null);
        return new Look(Stamp(utc), items, source);
    }

    /// <summary>A time as a look keeps it: UTC, to the millisecond, as the simulation writes it.</summary>
    public static string Stamp(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>What moved between two looks, ANDA by ANDA in number order: added or gone, the goal date, the forecast, the open item.</summary>
    public static (List<Change> Rows, int More) Since(Look? before, Look? now, Func<string, string?> itemText)
    {
        if (before?.Items is null || now?.Items is null) return (new(), 0);
        var rows = new List<Change>();
        var ids = before.Items.Keys.Concat(now.Items.Keys).Distinct().Where(x => x.Length == 6 && x.All(char.IsAsciiDigit)).OrderBy(x => x, StringComparer.Ordinal);
        foreach (var id in ids)
        {
            before.Items.TryGetValue(id, out var b0); now.Items.TryGetValue(id, out var n0);
            Item? b = Clean(b0), n = Clean(n0);
            if (b is null) { rows.Add(new(id, "added", "New in your data.")); continue; }
            if (n is null) { rows.Add(new(id, "gone", "No longer in your data.")); continue; }
            if (b.Goal != n.Goal)
                rows.Add(new(id, "goal", b.Goal is not null && n.Goal is not null ? $"Goal date moved from {S(b.Goal)} to {S(n.Goal)}."
                    : n.Goal is not null ? $"Goal date set: {S(n.Goal)}." : "Goal date removed."));
            if (b.Forecast != n.Forecast)
            {
                int k = b.Forecast is not null && n.Forecast is not null ? Day(n.Forecast)!.Value.DayNumber - Day(b.Forecast)!.Value.DayNumber : 0;
                rows.Add(new(id, "forecast", b.Forecast is not null && n.Forecast is not null
                    ? $"Forecast moved from {S(b.Forecast)} to {S(n.Forecast)} ({Math.Abs(k)} day{(Math.Abs(k) == 1 ? "" : "s")} {(k > 0 ? "later" : "earlier")})."
                    : n.Forecast is not null ? $"Forecast set: {S(n.Forecast)}." : "Forecast removed."));
            }
            if (b.Print != n.Print)
                rows.Add(new(id, "item", n.Print is not null ? $"New item needs you: {itemText(id) ?? "see the card"}." : "The item that needed you is no longer in your data."));
        }
        // at most Max changes, but an ANDA's changes are never cut part-way: the last one shown is shown whole
        int cut = Math.Min(rows.Count, Max);
        while (cut > 0 && cut < rows.Count && rows[cut].Id == rows[cut - 1].Id) cut++;
        return (rows.Take(cut).ToList(), rows.Count - cut);
    }

    /// <summary>A date that cannot be read (a kept file edited by hand, say) counts as none, never as a date it might mean.</summary>
    private static string? Ok(string? iso) => Day(iso) is { } d && Iso(d) == iso ? iso : null;
    private static Item? Clean(Item? x) => x is null ? null : new Item(Ok(x.Goal), Ok(x.Forecast), x.Print);

    private static string S(string iso) => Day(iso) is { } d ? WatchRules.Short(d) : iso;

    // ---------- kept on this PC ----------
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>The look kept, or null when there is none. A file that cannot be read is set aside, never written over, and counts as none.</summary>
    public static Look? Load(string path)
    {
        if (!File.Exists(path)) return null;
        var (text, _) = SafeFile.Read(path);
        if (text is null) return null;          // locked for a moment (a sync or a scan): not set aside, read again next time
        try
        {
            var look = JsonSerializer.Deserialize<Look>(text);
            if (look?.Items is not null && look.At is not null) return look with { Items = look.Items.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value) };
        }
        catch (JsonException) { }
        SafeFile.SetAside(path);
        return null;
    }

    /// <summary>
    /// Round 37: the switch is off, or Reset: what was kept is removed: the file, a copy left while it was being written, and any copy set aside
    /// because it could not be read (round 37 review). True when none is left; a file that cannot be removed now is tried again later.
    /// </summary>
    public static bool Forget(string path)
    {
        string dir = Path.GetDirectoryName(path) ?? ".", stem = Path.GetFileNameWithoutExtension(path) + ".";
        var all = new List<string> { path, path + ".tmp" };
        try { if (Directory.Exists(dir)) all.AddRange(Directory.EnumerateFiles(dir).Where(f => { var n = Path.GetFileName(f); return n.StartsWith(stem, StringComparison.OrdinalIgnoreCase) && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase); })); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
        bool clear = true;
        foreach (var f in all.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try { if (File.Exists(f)) File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            if (File.Exists(f)) clear = false;
        }
        return clear;
    }

    /// <summary>Round 37 review: the switch's two decisions, apart from the app so that a test runs them. Keep: only while on. Forget: when off.</summary>
    public static bool KeepIfOn(Settings settings, string path, Look shown, DateTime utc) => settings.SinceLastLook && Save(path, shown with { At = Stamp(utc) });
    public static bool ForgetIfOff(Settings settings, string path) => settings.SinceLastLook || Forget(path);

    public static bool Save(string path, Look look) => SafeFile.Write(path, JsonSerializer.Serialize(look, Options));
}

/// <summary>Round 36: next week, in Today, from Thursday noon to Sunday: the goal and forecast dates from Monday to Friday of the coming week, and its federal holidays.</summary>
public static class NextWeek
{
    public sealed record Row(string Id, string Text);
    public sealed record Result(string Title, IReadOnlyList<Row> Rows, IReadOnlyList<string> Holidays, string? Empty, IReadOnlyList<TaskRow>? Tasks = null, int TasksMore = 0);
    public const string SaidEmpty = "Nothing of yours falls due next week.";

    /// <summary>Round 38: one of the person's unfinished to-do items with a due date (yyyy-MM-dd), from "ANDA to-do list" or "My to-do list".</summary>
    public sealed record WeekTask(string List, string Title, string? Due);
    public sealed record TaskRow(string List, string Text);
    public const int TaskMax = 20;
    public static string SaidTasksMore(int n) => $"{n} more to-do item{(n == 1 ? "" : "s")} due next week; the to-do lists show them all.";

    /// <summary>The unfinished items with a due date of both to-do lists, as Today's next week reads them. Read when Today opens; nothing is kept.</summary>
    public static List<WeekTask> TasksOf(IEnumerable<TodoItem> anda, IEnumerable<MyTodoItem> mine) =>
        anda.Where(i => !i.Done && !string.IsNullOrEmpty(i.Due)).Select(i => new WeekTask("ANDA to-do list", i.Title, i.Due))
            .Concat(mine.Where(i => !i.Done && !string.IsNullOrEmpty(i.Due)).Select(i => new WeekTask("My to-do list", i.Title, i.Due))).ToList();

    /// <summary>Round 38 review: the simulation's trim() (JavaScript's white space and line ends), so both programs drop and trim the same titles.</summary>
    private static readonly char[] JsSpace = "\t\n\v\f\r \u00A0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF".ToCharArray();
    /// <summary>A to Z as a to z, every other character as it is: the simulation's order of titles, whatever the language.</summary>
    private static string AsciiLower(string s) => string.Create(s.Length, s, (span, src) => { for (int i = 0; i < src.Length; i++) span[i] = src[i] is >= 'A' and <= 'Z' ? (char)(src[i] + 32) : src[i]; });

    public static Result? Of(DateOnly today, int hour, IEnumerable<AndaRecord> records, BusinessCalendar cal, IEnumerable<WeekTask>? tasks = null)
    {
        var dow = today.DayOfWeek;
        if (!(dow is DayOfWeek.Friday or DayOfWeek.Saturday or DayOfWeek.Sunday || (dow == DayOfWeek.Thursday && hour >= 12))) return null;
        int ahead = (8 - (int)dow) % 7; if (ahead == 0) ahead = 7;
        var mon = today.AddDays(ahead); var fri = mon.AddDays(4);
        var holidays = new List<string>();
        for (var d = mon; d <= fri; d = d.AddDays(1)) if (cal.HolidayName(d) is { } name) holidays.Add($"{WatchRules.Short(d)}: {name} (federal holiday)");
        var found = new List<(DateOnly When, string Id, string Text)>();
        foreach (var r in records.OrderBy(r => r.Number, StringComparer.Ordinal))
            foreach (var (what, when) in new[] { ("Goal date", r.GoalDateValue), ("Forecast", r.HasForecast ? r.ForecastDateValue : null) })
                if (when is { } t && t >= mon && t <= fri) found.Add((t, r.Number, $"{what} {WatchRules.Short(t)}"));
        var rows = found.OrderBy(x => x.When).ThenBy(x => x.Id, StringComparer.Ordinal).Select(x => new Row(x.Id, x.Text)).ToList();
        // round 38: the person's to-do items due that week, by date, then list, then title; a date that cannot be read or a blank title is left out
        var due = (tasks ?? Array.Empty<WeekTask>())
            .Select(k => (k, t: DateOnly.TryParseExact(k.Due ?? "", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : (DateOnly?)null))
            .Where(x => x.t is { } d && d >= mon && d <= fri && (x.k.Title ?? "").Trim(JsSpace).Length > 0)
            .OrderBy(x => x.t).ThenBy(x => x.k.List, StringComparer.Ordinal).ThenBy(x => AsciiLower(x.k.Title), StringComparer.Ordinal).ToList();
        var shown = due.Take(TaskMax).Select(x => new TaskRow(x.k.List, $"{WatchRules.Short(x.t!.Value)} · {x.k.List}: {x.k.Title.Trim(JsSpace)}")).ToList();
        return new Result($"NEXT WEEK · {WatchRules.Short(mon)} TO {WatchRules.Short(fri)}".ToUpperInvariant(), rows, holidays, rows.Count > 0 || due.Count > 0 ? null : SaidEmpty, shown, due.Count - shown.Count);
    }
}
