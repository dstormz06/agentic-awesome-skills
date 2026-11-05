namespace AndaCompanion.Services;

/// <summary>The sections of the Settings window, in the order they are shown.</summary>
public enum SettingsSection { Look, Start, Dates, Files, MyTodo, Actions, Connections, Maintenance }

public static class SettingsSections
{
    public static readonly IReadOnlyList<(SettingsSection Id, string Key, string Title)> All = new (SettingsSection, string, string)[]
    {
        (SettingsSection.Look, "Look", "Look and feel"),
        (SettingsSection.Start, "Start", "How it starts"),
        (SettingsSection.Dates, "Dates", "Dates"),
        (SettingsSection.Files, "Files", "My files and writing"),
        (SettingsSection.MyTodo, "MyTodo", "My to-do list"),
        (SettingsSection.Actions, "Actions", "My actions, agents and ANDA to-do list"),
        (SettingsSection.Connections, "Connections", "AI and connections"),
        (SettingsSection.Maintenance, "Maintenance", "Maintenance"),
    };

    public static string Key(SettingsSection s) => All.First(x => x.Id == s).Key;
    public static string Title(SettingsSection s) => All.First(x => x.Id == s).Title;
}

/// <summary>What is true right now that the Settings window should draw attention to. Each entry is a plain sentence, or null if all is well.</summary>
public sealed record SettingsFacts(
    IReadOnlySet<SettingsSection> ShortcutProblems, string? PortfolioProblem, string? ConnectionProblem, string? ActionsProblem, bool PortfolioChosen)
{
    public static readonly SettingsFacts AllWell = new(new HashSet<SettingsSection>(), null, null, null, false);
}

/// <summary>How one section starts: open or collapsed, and the short reason shown beside its heading.</summary>
public sealed record SectionAdvice(SettingsSection Section, bool Open, bool Attention, string? Reason);

/// <summary>
/// Settings arrange themselves. Everything is collapsed except what is needed most, in this order of importance:
/// 1) a section with something that needs a look (always open, however many there are), 2) when the person is just starting, the two things a
/// new user needs first, 3) up to two sections the person opens often. At most three sections are opened for reasons 2 and 3 together, so the window stays short.
/// It is a fixed set of rules, not a model, and every opened section says why. The only thing remembered is a count of how often each section was opened.
/// </summary>
public static class SettingsAdvisor
{
    public const int MaxSuggested = 3, MinUsesToSuggest = 3, MaxUsesKept = 1000;

    public static IReadOnlyList<SectionAdvice> Advise(Settings settings, SettingsFacts facts)
    {
        var open = new Dictionary<SettingsSection, (bool Attention, string Reason)>();
        if (settings.AdaptiveSettings)
        {
            // 1. needs a look
            if (facts.ShortcutProblems.Contains(SettingsSection.Start)) open[SettingsSection.Start] = (true, "Needs a look: a shortcut is used by another program");
            if (facts.ShortcutProblems.Contains(SettingsSection.MyTodo)) open[SettingsSection.MyTodo] = (true, "Needs a look: its shortcut is used by another program");
            if (facts.ShortcutProblems.Contains(SettingsSection.Actions)) open[SettingsSection.Actions] = (true, "Needs a look: an action's shortcut is used by another program");
            else if (facts.ActionsProblem is { } ap) open[SettingsSection.Actions] = (true, "Needs a look: " + ap);
            if (facts.PortfolioProblem is { } pp) open[SettingsSection.Files] = (true, "Needs a look: " + pp);
            if (facts.ConnectionProblem is { } cp) open[SettingsSection.Connections] = (true, "Needs a look: " + cp);

            // 2. just starting
            int extra = 0;
            if (!settings.GettingStartedDone)
            {
                if (!open.ContainsKey(SettingsSection.Files) && !facts.PortfolioChosen) { open[SettingsSection.Files] = (false, "Start here: choose your portfolio"); extra++; }
                if (!open.ContainsKey(SettingsSection.Start)) { open[SettingsSection.Start] = (false, "Start here: your shortcuts"); extra++; }
            }

            // 3. opened often
            foreach (var used in settings.SectionUse
                         .Select(kv => (Section: SettingsSections.All.FirstOrDefault(x => x.Key == kv.Key), Count: kv.Value))
                         .Where(x => x.Section.Key is not null && x.Count >= MinUsesToSuggest && x.Section.Id != SettingsSection.Maintenance)
                         .OrderByDescending(x => x.Count).ThenBy(x => (int)x.Section.Id).Take(2))
            {
                if (open.ContainsKey(used.Section.Id) || extra >= MaxSuggested) continue;
                open[used.Section.Id] = (false, "You open this often");
                extra++;
            }
        }
        return SettingsSections.All.Select(x => open.TryGetValue(x.Id, out var o) ? new SectionAdvice(x.Id, true, o.Attention, o.Reason) : new SectionAdvice(x.Id, false, false, null)).ToList();
    }

    /// <summary>Only known sections, with sensible counts. Used for a saved file and a restored backup alike.</summary>
    public static Dictionary<string, int> CleanUse(Dictionary<string, int>? use)
    {
        var clean = new Dictionary<string, int>();
        if (use is null) return clean;
        foreach (var (k, v) in use)
            if (SettingsSections.All.Any(x => x.Key == k) && v > 0) clean[k] = Math.Min(v, MaxUsesKept);
        return clean;
    }

    /// <summary>Counts one more opening of a section. When the total grows large the counts are halved, so what was used long ago fades.</summary>
    public static void Count(Dictionary<string, int> use, SettingsSection section)
    {
        string key = SettingsSections.Key(section);
        use[key] = Math.Min(MaxUsesKept, (use.TryGetValue(key, out int n) ? n : 0) + 1);
        if (use.Values.Sum() > 120) foreach (var k in use.Keys.ToList()) use[k] = (use[k] + 1) / 2;
    }
}
