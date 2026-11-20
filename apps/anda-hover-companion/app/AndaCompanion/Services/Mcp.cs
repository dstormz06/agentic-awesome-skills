using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AndaCompanion.Services;

/// <summary>
/// Round 40: one MCP server the person connects to (Model Context Protocol, Streamable HTTP). Nothing here is a secret: like My API, the
/// token is never stored; <see cref="SecretEnvVar"/> only NAMES the environment variable that holds it, read at the moment of a request.
/// Servers that run as a program on this PC (stdio) are not offered: starting programs from this app is what endpoint security forbids.
/// </summary>
public sealed class McpServerSettings
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string AuthHeader { get; set; } = "Authorization";
    public string AuthScheme { get; set; } = "Bearer";
    public string SecretEnvVar { get; set; } = "";
    public bool Enabled { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>One tool a server offers, as it described itself. Its words are the server's: shown as text, never followed.</summary>
public sealed record McpTool(string Name, string Title, string Description, JsonObject InputSchema);

/// <summary>What a call gave back: the text parts in order, and whether the tool itself said it failed.</summary>
public sealed record McpCallResult(string Text, bool ToolError);

public static class McpRules
{
    public const int MaxServers = 8, MaxNameChars = 40, MaxTools = 200, MaxToolNameChars = 128, MaxDescriptionChars = 2000, MaxArgumentChars = 20_000;
    public const long MaxResponseBytes = 1_000_000;
    public const string ProtocolVersion = "2025-06-18";
    /// <summary>The versions this app speaks; a server that answers with another one is told nothing more.</summary>
    public static readonly IReadOnlyList<string> Versions = new[] { "2025-06-18", "2025-03-26" };

    public const string Hint = "An MCP server offers tools that this app can call for you: a search, a lookup, a calculation. Only servers that answer over https:// are offered (http:// for this PC only); a server that runs as a program on this PC is not, because this app does not start programs. Nothing is sent by itself: Test asks a server for its list of tools, and MCP tools (in the tray menu's More) shows you exactly what a call sends before you press Send. The token stays in the environment variable you name and is never saved by the app. A server's answer and its descriptions of its tools are shown as plain text and are never followed as instructions.";
    public const string SaidNothingYet = "Nothing is sent until you press List tools, and then Send.", SaidChooseFirst = "Please press List tools and choose a tool first.";
    public static string SaidListed(int n) => $"{n} tool{(n == 1 ? "" : "s")} offered. Choose one; nothing is sent until you press Send.";
    public static string SaidSwitchedOff(int n) => $"{(n == 1 ? "1 MCP server" : n + " MCP servers")} came back switched off: please check {(n == 1 ? "its" : "their")} address and token variable name in Settings, then switch {(n == 1 ? "it" : "them")} on.";
    public const string ArgumentsLabel = "Arguments (JSON), filled in from the tool's description and the text you sent from Ask, if any";
    public const string SaidAnswered = "Answered.", SaidToolFailed = "The tool says it could not do this; its words are below.", SaidNotKept = "The answer is shown here only and is not kept.";
    public static string SaidTested(IReadOnlyList<string> names) => names.Count == 0 ? "Connected. The server offers no tools."
        : $"Connected. {names.Count} tool{(names.Count == 1 ? "" : "s")}: {string.Join(", ", names.Take(5))}{(names.Count > 5 ? ", …" : "")}";
    public const string SaidUnexpected = "Something unexpected came back from the server, so nothing more was done. Nothing else was sent.";
    public const string SaidNoServer = "No MCP server is switched on. Add one in Settings, AI and connections.";
    public const string SaidNotJson = "The arguments must be one JSON object, for example {\"query\": \"text\"}.";

    /// <summary>At most eight servers, each with an id, a one-line name, a known time limit; one with no address stays off.</summary>
    public static List<McpServerSettings> Normalize(IEnumerable<McpServerSettings>? servers)
    {
        var list = new List<McpServerSettings>(); var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in (servers ?? Array.Empty<McpServerSettings>()).Where(x => x is not null).Take(MaxServers))
        {
            string name = OneLine(s.Name, MaxNameChars);
            string id = AgentRules.Slug(string.IsNullOrWhiteSpace(s.Id) ? name : s.Id);
            if (id.Length == 0) id = "mcp";
            // a second server of the same name gets -2, -3…; the name is shortened to make room, so the number is never cut off
            string baseId = id;
            for (int n = 2; !ids.Add(id); n++)
            {
                string suffix = "-" + n.ToString(CultureInfo.InvariantCulture);
                id = (baseId.Length + suffix.Length > 40 ? baseId[..(40 - suffix.Length)].TrimEnd('-') : baseId) + suffix;
            }
            list.Add(new McpServerSettings
            {
                Id = id, Name = name.Length > 0 ? name : "MCP server", Endpoint = (s.Endpoint ?? "").Trim(),
                AuthHeader = (s.AuthHeader ?? "").Trim(), AuthScheme = (s.AuthScheme ?? "").Trim(), SecretEnvVar = (s.SecretEnvVar ?? "").Trim(),
                Enabled = s.Enabled && (s.Endpoint ?? "").Trim().Length > 0,
                TimeoutSeconds = CustomApiSettings.TimeoutChoices.Contains(s.TimeoutSeconds) ? s.TimeoutSeconds : 30,
            });
        }
        return list;
    }

    /// <summary>
    /// The arguments offered for a tool, for the person to read and change before anything is sent: each property its schema names, in its
    /// order, as an empty value of its type; the selected text goes into the first required text property (or else the first text property).
    /// The simulation fills them the same way (mockup/round40-golden.json).
    /// </summary>
    public static string ArgumentsFor(McpTool tool, string? selection)
    {
        var args = new JsonObject();
        var props = tool.InputSchema["properties"] as JsonObject;
        var required = (tool.InputSchema["required"] as JsonArray)?.Select(x => x?.GetValueKind() == JsonValueKind.String ? x.GetValue<string>() : null).OfType<string>().ToHashSet() ?? new();
        string? textTarget = null;
        if (props is not null)
        {
            var texts = props.Where(p => TypeOf(p.Value) == "string").Select(p => p.Key).ToList();
            textTarget = texts.FirstOrDefault(required.Contains) ?? texts.FirstOrDefault();
            foreach (var (key, schema) in props)
                args[key] = TypeOf(schema) switch
                {
                    "string" => JsonValue.Create(key == textTarget ? (selection ?? "").Trim() : ""),
                    "number" or "integer" => JsonValue.Create(0),
                    "boolean" => JsonValue.Create(false),
                    "array" => new JsonArray(),
                    "object" => new JsonObject(),
                    _ => null,
                };
        }
        return args.ToJsonString(Shown);
    }

    private static string? TypeOf(JsonNode? schema) => schema?["type"] switch
    {
        JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
        JsonArray a => a.Select(x => x?.GetValueKind() == JsonValueKind.String ? x.GetValue<string>() : null).FirstOrDefault(t => t is not null and not "null"),
        _ => null,
    };

    /// <summary>The arguments the person wrote: one JSON object, within the size limit, or a sentence saying why not.</summary>
    public static (JsonObject? Args, string? Problem) ParseArguments(string? text)
    {
        string t = (text ?? "").Trim();
        if (t.Length == 0) return (new JsonObject(), null);
        if (t.Length > MaxArgumentChars) return (null, $"The arguments are longer than {MaxArgumentChars.ToString("N0", CultureInfo.InvariantCulture)} characters.");
        try { return JsonNode.Parse(t) is JsonObject o ? (o, null) : (null, SaidNotJson); }
        catch (JsonException) { return (null, SaidNotJson); }
    }

    /// <summary>What the person is shown before Send: exactly what goes, and where.</summary>
    public static string Preview(McpServerSettings server, string tool, JsonObject args, string host) =>
        $"To {server.Name} ({host}), the tool {tool}, with these arguments:\n{args.ToJsonString(Shown)}";

    /// <summary>A server's words, or a name, for the screen: one line, no control or invisible characters, within a length (the simulation's oneLine).</summary>
    public static string OneLine(string? s, int max)
    {
        var sb = new StringBuilder();
        foreach (char ch in ActionFill.WithoutHidden(s ?? "").Text) sb.Append(char.IsControl(ch) ? ' ' : ch);
        string t = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        return t.Length > max ? t[..max].TrimEnd() : t;
    }

    /// <summary>JSON as the person reads it: indented, and only what JSON itself requires escaped (the simulation's JSON.stringify(x, null, 2)).</summary>
    public static readonly JsonSerializerOptions Shown = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The size of the arguments for the event log: their compact JSON, counted as the simulation counts JSON.stringify(args).length.</summary>
    /// A character outside the basic plane (an emoji) is written here as two \u escapes, 12 characters, where JSON.stringify writes its two
    /// UTF-16 units as they are; each such pair counts as 2. A backslash the person typed is itself escaped, so an even run before \u is text.
    public static int Characters(JsonObject args)
    {
        string json = args.ToJsonString(Compact);
        return json.Length - 10 * SurrogatePair.Matches(json).Count;
    }
    private static readonly JsonSerializerOptions Compact = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly System.Text.RegularExpressions.Regex SurrogatePair = new(@"(?<!\\)(?:\\\\)*\\u[dD][89abAB][0-9a-fA-F]{2}\\u[dD][c-fC-F][0-9a-fA-F]{2}");
}

/// <summary>Something went wrong talking to a server; the message is for the person and never holds the token or the server's body.</summary>
public sealed class McpException(string message) : Exception(message);
