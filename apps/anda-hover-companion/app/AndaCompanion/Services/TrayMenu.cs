namespace AndaCompanion.Services;

/// <summary>
/// Round 35: the tray menu as data, in the simulation's order (mockup/round35-golden.json). Seven choices in the focused edition (Lookup,
/// Today, Review a draft, Ask, More, Settings, Help) and Exit; with all tools on, Lists comes before More. A group holds its own choices; a
/// group inside a group (Appearance, Auto-hide, My actions) is listed by name. The app builds its menu from this list, so the two cannot drift.
/// </summary>
public static class TrayMenu
{
    public sealed record Entry(string Label, IReadOnlyList<string>? Items = null);

    /// <summary>What the simulation offers that the Windows app does not have yet: division news (round 19), still owed to the app.</summary>
    public const string NotInApp = "News and alerts…";

    public static IReadOnlyList<Entry> Layout(bool allTools)
    {
        var more = new List<string> { "Compare two files…", "Hand-off page…", "Quick replies…" };
        if (allTools) more.AddRange(new[] { "Prompt library…", "My actions" });
        more.AddRange(new[] { "Appearance", "Auto-hide", "Connections and event log…", "Backups and archive…", "Demo: ANDA at risk", "Demo: ANDA on track" });
        var top = new List<Entry> { new("Lookup ANDA…"), new("Today…"), new("Review a draft…"), new("Ask about a selection…") };
        if (allTools) top.Add(new("Lists", new[] { "ANDA to-do list…", "My to-do list…", "Notes to my to-do list…", "My usage…" }));
        top.Add(new("More", more));
        top.Add(new("Settings…"));
        top.Add(new("Help", new[] { "Help: first steps…", "About and what changed…" }));
        top.Add(new("Exit"));
        return top;
    }
}
