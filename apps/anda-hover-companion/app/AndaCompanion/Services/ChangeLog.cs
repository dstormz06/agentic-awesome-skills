namespace AndaCompanion.Services;

/// <summary>The version the tool shows, and what changed, newest first. The same list as the simulation's (mockup/round29-golden.json).</summary>
public static class ChangeLog
{
    public const string Version = "Prototype, round 31";

    public static readonly IReadOnlyList<(int Round, IReadOnlyList<string> Items)> Entries = new (int, IReadOnlyList<string>)[]
    {
        (31, new[] { "Every time is written in your time zone, with its abbreviation and the right daylight time. You can choose another zone.", "Every piece of data says where it is from and as of when, and how long ago.", "Data older than the days you choose is marked STALE.", "A clock in Settings, and “minutes ago” on the card, are brought up to date once a minute." }),
        (30, new[] { "Every change to a list keeps a copy of it, and one press brings a copy back.", "You choose how many copies are kept.", "Finished to-do items can be searched in the archive and put back.", "You choose after how many days a finished item is archived." }),
        (29, new[] { "Every window says what is switched on, or “Offline · Local only”.", "A list of the connections: what each one sends, where, and what is kept.", "An event log you can read and export. It holds times, kinds and counts only.", "This page.", "Kept copies of your sheet are deleted only when you ask and confirm." }),
        (28, new[] { "No AI connection is on until you choose one.", "A file of yours that cannot be read is set aside, never written over.", "Finished to-do items are archived, not deleted.", "The invite can be accepted by pressing the same shortcut again.", "No launcher script." }),
        (27, new[] { "Compare two documents and run one of your prompts on the result.", "Check an AI answer against the files.", "Signs worth a second look, a hand-off page, a meeting page and a follow-up for a reply." }),
        (26, new[] { "Compare two files.", "Four reading prompts, a calendar invitation and a note from a selection.", "My usage." }),
    };

    public static IReadOnlyList<string> Text()
    {
        var lines = new List<string> { Version };
        foreach (var (round, items) in Entries) { lines.Add(""); lines.Add($"Round {round}"); lines.AddRange(items.Select(i => "- " + i)); }
        return lines;
    }
}
