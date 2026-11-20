namespace AndaCompanion.Services;

/// <summary>The version the tool shows, and what changed, newest first. The same list as the simulation's (mockup/round29-golden.json).</summary>
public static class ChangeLog
{
    public const string Version = "Prototype, round 36";

    public static readonly IReadOnlyList<(int Round, IReadOnlyList<string> Items)> Entries = new (int, IReadOnlyList<string>)[]
    {
        (36, new[] { "Today begins with what has moved since you last looked: forecasts and goal dates that changed, ANDAs added or gone, and a new item that needs you. This PC keeps only each ANDA's two dates and a fingerprint of its open item, from when you last closed Today.", "From Thursday noon to Sunday, Today also shows next week: the goal and forecast dates that fall in it, and the federal holidays." }),
        (35, new[] { "The tray menu is shorter: Lookup, Today, Review a draft, Ask, More, Settings and Help. With all tools on, Lists holds the two to-do lists, the notes and your usage. More holds the rest; Help holds first steps and what changed.", "Text in the Windows app uses one scale of six sizes, so pages read calmer and larger text grows them together." }),
        (34, new[] { "Your own shortcut now opens the card directly. If you prefer the small invite first, choose Whisper in Settings, How it starts. A middle-click still shows the invite first.", "The card shows the answer first: what needs you, the goal and the forecast, and the first thing worth a look. The calendar, Latest, Ahead, the reference dates and the brief are one press away, under More or E.", "A click on the tray icon opens Today, which now also holds what the Watch list showed." }),
        (33, new[] { "The Windows app now has what the simulation has shown since rounds 18 and 19: details from your own sheet on an expanded card, each with Copy; larger text and compact spacing; recent ANDAs and a short list you keep; One Bar; and Today.", "In Today, what needs a look is listed with the most pressing first.", "The Edge extension says where a card's data is from, as of when in your time zone and how long ago, marks it STALE after the days you choose, and has first steps of its own." }),
        (32, new[] { "A guide of five short steps opens once, at the first start. You can leave it at any step and open it again from the tray menu: Help: first steps.", "The Drugs@FDA reference file says when it was saved and how long ago, and is marked STALE after a number of days of its own." }),
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
