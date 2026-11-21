using System.Text.Json;
using System.Text.Json.Serialization;

namespace AndaCompanion.Services;

/// <summary>One notice near the clock: from the person's data (an alert) or division news. It never holds the person's notes.</summary>
public sealed record NoticeItem(string Key, string Kind, string? Number, string Horizon, string Text)
{
    public string KindLabel => Kind == "news" ? "DIVISION NEWS" : Horizon == "later" ? "FROM YOUR DATA · LONGER TERM" : "FROM YOUR DATA · IMMEDIATE";
    public string OpenLabel => Kind == "news" || Number is null ? "Open News and alerts" : "Open the card";
}

/// <summary>
/// Round 42 (round 19 in the simulation): which notices near the clock were shown today, which ones were already shown, and which wait until
/// tomorrow. Kept in notices.json on this PC: keys and days only (a key holds an ANDA number, a rule's name, a date and at most an eight-digit fingerprint, never text). A notice is
/// shown once; "Remind me tomorrow" brings it back the next day. At most 1, 3 or 5 a day; the rest wait in News and alerts.
/// </summary>
public sealed class NoticeState
{
    [JsonPropertyName("day")] public string Day { get; set; } = "";
    [JsonPropertyName("shown")] public int Shown { get; set; }
    [JsonPropertyName("seen")] public Dictionary<string, string> Seen { get; set; } = new(StringComparer.Ordinal);
    [JsonPropertyName("snoozed")] public Dictionary<string, string> Snoozed { get; set; } = new(StringComparer.Ordinal);
}

public static class Notices
{
    public static readonly IReadOnlyList<int> Caps = new[] { 1, 3, 5 };
    public const int DefaultCap = 3, ForgetAfterDays = 60, MaxKept = 5000, ShowSeconds = 12;
    public const string SaidBusy = "The notices file is in use just now. The app will try again at the next check.";
    public const string SaidHeld = "A program fills the screen (a slide show or a full-screen window), so the notice waits and is tried again in five minutes.";
    public const string SaidOff = "Notices are off.", SaidNothing = "Nothing new to tell you.", SaidNotRunning = "The app is not running.";
    public static string SaidCap(int cap, int more) => $"Today's limit of {cap} {(cap == 1 ? "notice" : "notices")} is reached. {more} more {(more == 1 ? "is" : "are")} in News and alerts.";
    public const string Hint = "Both are off until you switch them on. Notices, reminders and the tray dot appear only while the app is running: when it is closed nothing runs and nothing is shown. A notice is one short line near the clock. It never takes the keyboard, hides by itself, and shows the ANDA number, the rule and its date, never your own words (not your notes, a to-do item's title or the sheet's \"needs action\" text). The app checks when it starts, when your portfolio file is saved and each morning. Division news is plain text from one Word document, read from the copy synced to this PC: anyone who can edit that document can put a line on your screen, so each line is cut to 240 characters and counts toward the daily limit. A notice fires only on what the data shows; silence does not mean all is well.";

    /// <summary>The notices from the person's data: every alert that is not only for information, with its ANDA first.</summary>
    public static IReadOnlyList<NoticeItem> FromAlerts(IEnumerable<TimelineAlert> alerts) =>
        alerts.Where(a => a.Sev != "info").Select(a => new NoticeItem("d|" + a.Key, "data", a.Number, a.Horizon, Timeline.Cut(Timeline.Line(a with { Text = Timeline.Brief(a) }), 160))).ToList();

    public static IReadOnlyList<NoticeItem> FromNews(IEnumerable<NewsItem> news) => news.Select(n => new NoticeItem(n.Key, "news", null, "now", n.Text)).ToList();

    /// <summary>A new day starts the count again, ends yesterday's reminders, and forgets what was shown long ago.</summary>
    public static void Roll(NoticeState s, string today)
    {
        if (s.Day == today) return;
        s.Day = today; s.Shown = 0;
        var t = Timeline.Iso(today);
        foreach (var k in s.Snoozed.Where(kv => Timeline.Iso(kv.Value) is not { } u || t is null || u <= t).Select(kv => kv.Key).ToList()) s.Snoozed.Remove(k);
        foreach (var k in s.Seen.Where(kv => Timeline.Iso(kv.Value) is not { } d || t is null || t.Value.DayNumber - d.DayNumber > ForgetAfterDays).Select(kv => kv.Key).ToList()) s.Seen.Remove(k);
    }

    /// <summary>What has not been shown and is not waiting for tomorrow: news first, then the data, each in its own order.</summary>
    public static List<NoticeItem> Pending(NoticeState s, IEnumerable<NoticeItem> items, string today)
    {
        Roll(s, today);
        // a line given twice (a news line pasted twice) is one notice (round 42 review)
        return items.Where(i => !s.Seen.ContainsKey(i.Key) && !s.Snoozed.ContainsKey(i.Key)).DistinctBy(i => i.Key).OrderBy(i => i.Kind == "news" ? 0 : 1).ToList();
    }

    /// <summary>The next notice to show, marked shown, or null when there is none or today's limit is reached.</summary>
    public static NoticeItem? Take(NoticeState s, List<NoticeItem> pending, int cap, string today)
    {
        Roll(s, today);
        if (pending.Count == 0 || s.Shown >= (Caps.Contains(cap) ? cap : DefaultCap)) return null;
        var next = pending[0]; pending.RemoveAt(0);
        // full: the oldest is let go, so that what is shown is always kept as shown and does not come back at the next check (round 42 review)
        if (s.Seen.Count >= MaxKept && s.Seen.OrderBy(kv => kv.Value, StringComparer.Ordinal).ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key is { } oldest) s.Seen.Remove(oldest);
        s.Seen[next.Key] = today;
        s.Shown++;
        return next;
    }

    /// <summary>"Remind me tomorrow": not shown again today; shown again from tomorrow, if it still holds.</summary>
    public static void Snooze(NoticeState s, string key, string today)
    {
        s.Seen.Remove(key);
        if (Timeline.Iso(today) is { } t && s.Snoozed.Count < MaxKept) s.Snoozed[key] = Timeline.Iso(t.AddDays(1));
    }

    /// <summary>The state kept on this PC; null when the file is there but cannot be read just now (locked), so that it is not written over.</summary>
    public static NoticeState? Load(string path)
    {
        if (!File.Exists(path)) return new NoticeState();
        var (text, _) = SafeFile.Read(path);
        return FromText(text, path);
    }

    internal static NoticeState? FromText(string? text, string path)
    {
        if (text is null) return null;
        try
        {
            if (JsonSerializer.Deserialize<NoticeState>(text) is { } s)
                return new NoticeState { Day = s.Day ?? "", Shown = Math.Max(0, s.Shown), Seen = new(s.Seen ?? new(), StringComparer.Ordinal), Snoozed = new(s.Snoozed ?? new(), StringComparer.Ordinal) };
        }
        catch (JsonException) { }
        SafeFile.SetAside(path);
        return new NoticeState();
    }

    public static bool Save(string path, NoticeState s) => SafeFile.Write(path, JsonSerializer.Serialize(s));
}
