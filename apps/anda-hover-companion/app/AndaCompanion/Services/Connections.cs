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
            items.Add(new($"{ai.Name} (AI)", "When a full card opens: the record of that ANDA as the card shows it, with your lens and standing instructions. When you press Send: the text shown to you first. Nothing else.",
                ai.Where.Length > 0 ? ai.Where : Nowhere, "This tool keeps nothing of what is sent. What the service keeps is set by your office's agreement with it."));
        foreach (var a in c.Agents)
            items.Add(new($"{a.Name} (agent)", a.Auto
                    ? "By itself when a full card opens: the fields of that ANDA that its prompt names, never selected text. Also when you press Run or Send."
                    : "Only when you press Run or Send: its prompt, filled in and shown to you.",
                a.Where.Length > 0 ? a.Where : Nowhere, "Its answer is kept in memory for the minutes you chose, never in a file."));
        if (c.Outlook is { } o)
            items.Add(new("Outlook (read-only)", "Only a search for the six-digit number of the open ANDA.", o.Where, "Mail is shown, never stored. The sign-in is held in memory until you sign out or close the app."));

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
                if (CustomApiConfig.Inspect(s.CustomApi, env).Ok) ai = new(OneLine(s.CustomApi.Name, 60) is { Length: > 0 } n ? n : "My API", Host(s.CustomApi.Endpoint));
                break;
            case ProviderChoice.Auto:
                string provider = env("ANDA_COMPANION_PROVIDER")?.Trim().ToLowerInvariant() ?? "anthropic";
                if (provider == "anthropic" && key) ai = new("Claude", "api.anthropic.com");
                else if (provider == "elsa" && ElsaConfig.Inspect(env) is { Status: ElsaStatus.Ready, Settings: { } elsa }) ai = new("Elsa", Host(elsa.Endpoint));
                break;
        }
        var agents = (s.Agents ?? new()).Where(a => a is { Enabled: true })
            .Select(a => new ConnAgent(OneLine(a.Name, 60) is { Length: > 0 } n ? n : "Agent", Host(a.Api?.Endpoint), a.AutoRun != AgentRun.Never)).ToList();
        ConnOutlook? outlook = s.Outlook is { Enabled: true } o ? new(o.Cloud == OutlookCloud.UsGovernment ? "graph.microsoft.us" : "graph.microsoft.com") : null;
        return new ConnState(ai, agents, outlook);
    }

    /// <summary>The kinds that are on, for writing a change to the event log without naming anything the person typed.</summary>
    public static (bool Ai, int Agents, bool Outlook) Kinds(ConnState c) => (c.Ai is not null, c.Agents.Count, c.Outlook is not null);

    /// <summary>The lines for the event log when what is switched on has changed. The same words as the simulation.</summary>
    public static IReadOnlyList<string> Changes((bool Ai, int Agents, bool Outlook) before, (bool Ai, int Agents, bool Outlook) now)
    {
        var lines = new List<string>();
        if (now.Ai != before.Ai) lines.Add(now.Ai ? "AI switched on" : "AI switched off");
        if (now.Agents != before.Agents) lines.Add($"agents switched on: {before.Agents.ToString(CultureInfo.InvariantCulture)} to {now.Agents.ToString(CultureInfo.InvariantCulture)}");
        if (now.Outlook != before.Outlook) lines.Add(now.Outlook ? "Outlook switched on" : "Outlook switched off");
        return lines;
    }

    private static string Host(string? address) =>
        Uri.TryCreate((address ?? "").Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) ? u.Host : "";

    private static string OneLine(string? s, int max)
    {
        var sb = new StringBuilder();
        foreach (char ch in s ?? "") sb.Append(char.IsControl(ch) ? ' ' : ch);
        string t = string.Join(" ", sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return t.Length > max ? t[..max].TrimEnd() : t;
    }
}
