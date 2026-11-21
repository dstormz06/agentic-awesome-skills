using Avalonia.Controls;

namespace AndaCompanion.UI;

/// <summary>
/// Round 35: the tray menu as data, in the simulation's order (mockup/round35-golden.json). Seven choices in the focused edition (Lookup,
/// Today, Review a draft, Ask, More, Settings, Help) and Exit; with all tools on, Lists comes before More. A group holds its own choices; a
/// group inside a group (Appearance, Auto-hide, My actions) is listed by name. The app builds its menu from this list, so the two cannot drift.
/// </summary>
public static class TrayMenu
{
    public sealed record Entry(string Label, IReadOnlyList<string>? Items = null);


    /// <summary>One choice of the menu, and whether the focused edition shows it (round 40 review: no longer worked out from labels).</summary>
    private sealed record Choice(string Label, bool Focused = true, IReadOnlyList<Choice>? Items = null);

    private static readonly IReadOnlyList<Choice> Menu = new Choice[]
    {
        new("Lookup ANDA…"), new("Today…"), new("Review a draft…"), new("Ask about a selection…"),
        new("Lists", Focused: false, Items: new Choice[] { new("ANDA to-do list…"), new("My to-do list…"), new("Notes to my to-do list…"), new("My usage…") }),
        new("More", Items: new Choice[]
        {
            new("Compare two files…"), new("Hand-off page…"), new("News and alerts…"), new("Quick replies…"),
            new("Prompt library…", Focused: false), new("MCP tools…", Focused: false), new("My actions", Focused: false),
            new("Appearance"), new("Auto-hide"), new("Connections and event log…"), new("Backups and archive…"), new("Demo: ANDA at risk"), new("Demo: ANDA on track"),
        }),
        new("Settings…"),
        new("Help", Items: new Choice[] { new("Help: first steps…"), new("About and what changed…") }),
        new("Exit"),
    };

    /// <summary>What each edition shows, in order: the choices, and inside a group its own (a choice inside a hidden group is hidden with it).</summary>
    public static IReadOnlyList<Entry> Layout(bool allTools) =>
        Menu.Where(c => allTools || c.Focused).Select(c => new Entry(c.Label, c.Items?.Where(i => allTools || i.Focused).Select(i => i.Label).ToList())).ToList();

    /// <summary>
    /// Builds the menu with all tools, in the layout's order. <paramref name="make"/> gives the item for a choice (and its action);
    /// <paramref name="fullOnly"/> marks an item the focused edition hides; <paramref name="line"/> is the disabled hotkey line, placed with a
    /// separator before Exit. Kept apart from the app so a test can build it and read what is shown.
    /// </summary>
    public static NativeMenu Build(Func<string, NativeMenuItem> make, Action<NativeMenuItem> fullOnly, NativeMenuItem line)
    {
        var menu = new NativeMenu();
        foreach (var c in Menu)
        {
            if (c.Label == "Exit") { menu.Add(new NativeMenuItemSeparator()); menu.Add(line); }
            var top = c.Items is null ? make(c.Label) : new NativeMenuItem(c.Label) { Menu = new NativeMenu() };
            foreach (var i in c.Items ?? Array.Empty<Choice>()) { var child = make(i.Label); if (!i.Focused) fullOnly(child); top.Menu!.Add(child); }
            if (!c.Focused) fullOnly(top);
            menu.Add(top);
        }
        return menu;
    }
}
