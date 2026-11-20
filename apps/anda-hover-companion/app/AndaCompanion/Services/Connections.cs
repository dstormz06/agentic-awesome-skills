using System.Globalization;
using System.Text;

namespace AndaCompanion.Services;

public sealed record ConnAi(string Name, string Where);
public sealed record ConnAgent(string Name, string Where, bool Auto);
public sealed record ConnOutlook(string Where);

/// <summary>What is switched on: the AI connection if one is ready, the agents that are on, and Outlook if it is on.</summary>
public sealed record ConnState(ConnAi? Ai, IReadOnlyList<ConnAgent> Agents, ConnOutlook? Outlook);
public sealed record ConnItem(string Name, string Sends, string Where, string Kept);
public sealed record ConnResult(string Line, IReadOnlyList<ConnItem> Items, IReadOnlyList<string> Text);

/// <summary>
/// What is switched on, said the same way in every window: "Offline · Local only", or each connection by name. For each one: what it sends,
/// where, and what is kept. "Switched on" is a statement about settings; whether a service answers is said by <see cref="ConnectionStatus"/>,
/// only after a real request. A port of the simulation's connections(), rule for rule (mockup/round29-golden.json).
/// </summary>
public static class Connections
{
    public const string Offline = "Offline · Local only";
    public const string Note = "“Update now” for the Drugs@FDA file fetches the one address you paste, only when you press it. “Open ANDA page” opens an address you set, in your browser. Neither is a standing connection.";
    private const string Nowhere = "no address yet, so nothing can be sent";

    public static ConnResult Describe(ConnState c)
    {
        var items = new List<ConnItem>();
        if (c.Ai is { } ai)
            items.Add(new($"{ai.Name} (AI)", "When a card is opened in full: the whole record of that ANDA from your sheet, with its notes, and your lens. When you press Send: the text shown to you first and, for a writing action, your tone, standing instructions and sign-off. Nothing else.",
                ai.Where.Length > 0 ? ai.Where : Nowhere, "This tool keeps nothing of what is sent. What the service keeps is set by your office's agreement with it."));
        foreach (var a in c.Agents)
            items.Add(new($"{a.Name} (agent)", a.Auto
                    ? "By itself when a card is opened in full: the fields of that ANDA that its prompt names, never selected text. Also when you press Run or Send."
                    : "Only when you press Run or Send: its prompt, filled in and shown to you.",
                a.Where.Length > 0 ? a.Where : Nowhere, "Its answer is kept in memory until you close the app, and counts as fresh for the minutes you chose. It is never written to a file."));
        if (c.Outlook is { } o)
            items.Add(new("Outlook (read-only)", "When you sign in: this app's ID and the read-only permission it asks for. After that, only a search for the six-digit number of the open ANDA.", o.Where, "Mail is shown, never stored. The sign-in is held in memory until you sign out or close the app."));

        var names = items.Select(i => i.Name).ToList();
        string line = names.Count == 0 ? Offline
            : "Switched on: " + string.Join(" · ", names.Take(3)) + (names.Count > 3 ? $" · and {(names.Count - 3).ToString(CultureInfo.InvariantCulture)} more" : "");
        var text = new List<string>();
        if (names.Count == 0) { text.Add(Offline); text.Add("Nothing is switched on, so nothing leaves this PC."); }
        else
        {
            text.Add(line);
            foreach (var i in items) { text.Add(i.Name); text.Add("  Sends: " + i.Sends); text.Add("  To: " + i.Where); text.Add("  Kept: " + i.Kept); }
        }
        text.Add(Note);
        return new ConnResult(line, items, text);
    }

    /// <summary>
    /// What this app has switched on, read from the settings and the environment without making any request and without building a client.
    /// An AI connection counts only when it could really be used: chosen in Settings and complete.
    /// </summary>
    public static ConnState From(Settings s, Func<string, string?>? env = null)
    {
        env ??= Environment.GetEnvironmentVariable;
        bool key = !string.IsNullOrEmpty(env("ANTHROPIC_API_KEY"));
        ConnAi? ai = null;
        switch (s.Provider)
        {
            case ProviderChoice.Claude:
                if (key) ai = new("Claude", "api.anthropic.com");
                break;
            case ProviderChoice.MyApi:
                if (CustomApiConfig.Inspect(s.CustomApi, env).Ok) ai = new(Shown(s.CustomApi.Name, 60) is { Length: > 0 } n ? n : "My API", Host(s.CustomApi.Endpoint));
                break;
            case ProviderChoice.Auto:
                string provider = env("ANDA_COMPANION_PROVIDER")?.Trim().ToLowerInvariant() ?? "anthropic";
                if (provider == "anthropic" && key) ai = new("Claude", "api.anthropic.com");
                else if (provider == "elsa" && ElsaConfig.Inspect(env) is { Status: ElsaStatus.Ready, Settings: { } elsa }) ai = new("Elsa", Host(elsa.Endpoint));
                break;
        }
        var agents = (s.Agents ?? new()).Where(a => a is { Enabled: true })
            .Select(a => new ConnAgent(Shown(a.Name, 60) is { Length: > 0 } n ? n : "Agent", Host(a.Api?.Endpoint), a.AutoRun != AgentRun.Never)).ToList();
        ConnOutlook? outlook = s.Outlook is { Enabled: true } o ? new(o.Cloud == OutlookCloud.UsGovernment ? "login.microsoftonline.us and graph.microsoft.us" : "login.microsoftonline.com and graph.microsoft.com") : null;
        return new ConnState(ai, agents, outlook);
    }

    /// <summary>The line written to the event log at start: what is already on. Never a name or an address.</summary>
    public static string StartLine(ConnState c) =>
        $"at start: AI {(c.Ai is null ? "off" : "on")}, agents on: {c.Agents.Count.ToString(CultureInfo.InvariantCulture)}, Outlook {(c.Outlook is null ? "off" : "on")}";

    /// <summary>
    /// The lines for the event log when what is switched on has changed: switched on or off, or going somewhere else (another service,
    /// another address, another agent). The same words as the simulation; never a name or an address.
    /// </summary>
    public static IReadOnlyList<string> Changes(ConnState before, ConnState now)
    {
        var lines = new List<string>();
        if ((now.Ai is null) != (before.Ai is null)) lines.Add(now.Ai is null ? "AI switched off" : "AI switched on");
        else if (now.Ai is not null && now.Ai != before.Ai) lines.Add("AI connection changed");
        if (now.Agents.Count != before.Agents.Count) lines.Add($"agents switched on: {before.Agents.Count.ToString(CultureInfo.InvariantCulture)} to {now.Agents.Count.ToString(CultureInfo.InvariantCulture)}");
        else if (!now.Agents.SequenceEqual(before.Agents)) lines.Add("agents changed");
        if ((now.Outlook is null) != (before.Outlook is null)) lines.Add(now.Outlook is null ? "Outlook switched off" : "Outlook switched on");
        else if (now.Outlook is not null && now.Outlook != before.Outlook) lines.Add("Outlook connection changed");
        return lines;
    }

    private static string Host(string? address) =>
        Uri.TryCreate((address ?? "").Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) ? u.IdnHost : "";      // the form the network uses, so a look-alike letter shows as "xn--…"

    /// <summary>A name the person typed, for the line: one line, and no invisible character that could reorder what is read.</summary>
    private static string Shown(string? s, int max) => OneLine(ActionFill.WithoutHidden(s ?? "").Text, max);

    private static string OneLine(string? s, int max)
    {
        var sb = new StringBuilder();
        foreach (char ch in s ?? "") sb.Append(char.IsControl(ch) ? ' ' : ch);
        string t = string.Join(" ", sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return t.Length > max ? t[..max].TrimEnd() : t;
    }
}
