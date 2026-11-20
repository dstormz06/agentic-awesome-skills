using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AndaCompanion.Services;

/// <summary>
/// Round 40: one MCP server the person connects to (Model Context Protocol, Streamable HTTP). Nothing here is a secret: like My API, the
/// token is never stored; <see cref="SecretEnvVar"/> only NAMES the environment variable that holds it, read at the moment of a request.
/// Round 41: or a program on this PC (stdio), only as IT approved it: <see cref="Program"/> (the full path of an .exe), its
/// <see cref="ProgramSha256"/>, and fixed <see cref="ProgramArguments"/>. With no program named, nothing can be started.
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
    /// <summary>A program on this PC rather than a server at an address.</summary>
    public bool IsProgram { get; set; }
    public string Program { get; set; } = "";
    public string ProgramSha256 { get; set; } = "";
    public List<string> ProgramArguments { get; set; } = new();
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

    public const string Hint = "An MCP server offers tools that this app can call for you: a search, a lookup, a calculation. A server is reached over https:// (http:// for this PC only), or is a program on this PC that IT approved: added with “Add a program on this PC”, by its full path and the SHA-256 IT gives, which the app checks before each start; the app asks you before it starts the program, and stops it when you are done. Nothing is sent by itself: Test asks a server for its list of tools, and MCP tools (in the tray menu's More) shows you exactly what a call sends before you press Send. The token stays in the environment variable you name and is never saved by the app. A server's answer and its descriptions of its tools are shown as plain text and are never followed as instructions.";
    public const string SaidNothingYet = "Nothing is sent until you press List tools, and then Send.", SaidChooseFirst = "Please press List tools and choose a tool first.";
    public static string SaidListed(int n) => $"{n} tool{(n == 1 ? "" : "s")} offered. Choose one; nothing is sent until you press Send.";
    public static string SaidSwitchedOff(int n) => $"{(n == 1 ? "1 MCP server" : n + " MCP servers")} came back switched off: please check {(n == 1 ? "its" : "their")} address and token variable name in Settings, then switch {(n == 1 ? "it" : "them")} on.";
    public const string ArgumentsLabel = "Arguments (JSON), filled in from the tool's description and the text you sent from Ask, if any";
    public const string SaidAnswered = "Answered.", SaidToolFailed = "The tool says it could not do this; its words are below.", SaidNotKept = "The answer is shown here only and is not kept.";
    public static string SaidTested(IReadOnlyList<string> names) => names.Count == 0 ? "Connected. The server offers no tools."
        : $"Connected. {names.Count} tool{(names.Count == 1 ? "" : "s")}: {string.Join(", ", names.Take(5))}{(names.Count > 5 ? ", …" : "")}";
    public const string SaidUnexpected = "Something unexpected came back from the server, so nothing more was done. Nothing else was sent.";
    public const string SaidNoServer = "No MCP server is switched on. Add one in Settings, AI and connections.";
    public const string ProgramHint = "Only a program IT has approved for this PC: its full path (an .exe), its SHA-256 exactly as IT gives it, and any arguments IT names, one per line. The app checks the file's SHA-256 before each start, holds the file so it cannot be changed while it starts, and starts it directly, without a command window or a script, and without this app's tokens. The check covers the .exe file itself, not files it loads, so IT should name a program in a folder only administrators can change, such as C:\\Program Files. With nothing entered here, no program can be started.";
    public static string SaidStartsProgram(string name) => $"This starts {name} on this PC. Press again to start it.";
    public static string SaidHashMismatch(string name) => $"{name} is not the program IT approved: its SHA-256 is different. Nothing was started.";
    public static string SaidNotStarted(string name) => $"{name} is not running, so nothing was sent. Press List tools, then again, to start it.";
    public static string SaidNotThere(string name) => $"{name} is not at that path on a drive of this PC: Windows found it on a network drive, through a link, or by another name. Please type the full path IT gives. Nothing was started.";
    public static string SaidNoProgram(string name) => $"{name} could not be opened or started on this PC. Nothing was started.";
    public const string SaidTooLarge = "The server's answer is larger than this app reads (1 MB).";
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
                TimeoutSeconds = CustomApiSettings.TimeoutChoices.Contains(s.TimeoutSeconds) ? s.TimeoutSeconds : 30,
                IsProgram = s.IsProgram, Program = ProgramPath(s.Program), ProgramSha256 = (s.ProgramSha256 ?? "").Trim().ToLowerInvariant(),
                ProgramArguments = (s.ProgramArguments ?? new()).Select(a => (a ?? "").Trim()).Where(a => a.Length > 0).Take(MaxArguments).Select(a => a.Length > 500 ? a[..500] : a).ToList(),
            });
            var kept = list[^1];
            // on only when it can be used: a server with an address, or a program named as IT approved it
            kept.Enabled = s.Enabled && Problem(kept) is null;
        }
        return list;
    }

    public const int MaxArguments = 20;

    /// <summary>Where a server is, for the Connections window: its host, or "this PC: " and the program's name.</summary>
    public static string Where(McpServerSettings s) => s.IsProgram ? "this PC: " + ProgramName(s.Program) : ApiTarget.Host(s.Endpoint);

    /// <summary>
    /// What keeps a server from being used, or null: an address that cannot be used (as for My API), or a program not named as IT approved
    /// it. One rule for loading Settings, the Settings rows and the Connections window (round 41 review).
    /// </summary>
    public static string? Problem(McpServerSettings s) => s.IsProgram ? ProgramProblem(s.Program, s.ProgramSha256) : ApiTarget.Address(s.Endpoint).Problem;

    /// <summary>The variables that hold this app's own tokens, never given to a program on this PC.</summary>
    public static readonly IReadOnlyList<string> AlwaysWithheld = new[] { "ANTHROPIC_API_KEY", ElsaConfig.TokenVar };

    /// <summary>
    /// Round 41 review: a program on this PC is started without the variables that hold this app's tokens: Claude's and Elsa's, and every
    /// token variable named in Settings (My API, the agents, the MCP servers). Other variables are passed on, as to any program the person starts.
    /// </summary>
    public static IReadOnlyCollection<string> WithheldVariables(Settings? settings)
    {
        var names = new HashSet<string>(AlwaysWithheld, StringComparer.OrdinalIgnoreCase);
        void Add(string? name) { if (!string.IsNullOrWhiteSpace(name)) names.Add(name.Trim()); }
        Add(settings?.CustomApi?.SecretEnvVar);
        foreach (var a in settings?.Agents ?? new()) Add(a?.Api?.SecretEnvVar);
        foreach (var m in settings?.McpServers ?? new()) Add(m?.SecretEnvVar);
        return names;
    }

    /// <summary>The program's file name, for sentences: what follows the last backslash.</summary>
    public static string ProgramName(string? path) { string p = ProgramPath(path); int i = p.LastIndexOf('\\'); return i < 0 ? p : p[(i + 1)..]; }

    /// <summary>A path as typed, trimmed, with one pair of surrounding quotes taken off (as Windows copies a path).</summary>
    public static string ProgramPath(string? path) { string p = (path ?? "").Trim(); return p.Length >= 2 && p[0] == '"' && p[^1] == '"' ? p[1..^1].Trim() : p; }

    /// <summary>
    /// A drive letter, then folders and a file name with none of &lt; &gt; : " / \ | ? * or a control character, each folder ending in neither
    /// a dot nor a space (so no ".", "..", empty folder, or alternate stream), and the name ending in .exe. The simulation uses the same pattern.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex ExePath = new(@"^[A-Za-z]:\\(?:[^<>:""/\\|?*\u0000-\u001f]*[^<>:""/\\|?*\u0000-\u001f. ]\\)*[^<>:""/\\|?*\u0000-\u001f]*\.exe$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    private static readonly System.Text.RegularExpressions.Regex Sha256 = new("^[0-9a-f]{64}$");

    /// <summary>Why a program cannot be used as typed, or null: a full path to an .exe on a drive of this PC, and a SHA-256 of 64 hex digits.</summary>
    public static string? ProgramProblem(string? program, string? sha256)
    {
        string p = ProgramPath(program);
        if (p.Length == 0) return @"Please enter the program's full path, for example C:\Program Files\Example\mcp-server.exe.";
        if (!ExePath.IsMatch(p)) return @"Only a program on a drive of this PC, by its full path to an .exe (for example C:\Program Files\Example\mcp-server.exe), can be used. Nothing was started.";
        if (!Sha256.IsMatch((sha256 ?? "").Trim().ToLowerInvariant())) return "Please enter the program's SHA-256 exactly as IT gives it: 64 letters and digits. Nothing was started.";
        return null;
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

/// <summary>
/// Round 41 review: a program on this PC starts only on a second press, after the first has said what it starts. One rule for Settings' Test
/// and the MCP tools window. The second press counts only for the same program, SHA-256 and arguments: an edit in between, or another row,
/// asks again.
/// </summary>
public sealed class McpStartConfirm
{
    private string? _armed;

    private static string Key(McpServerSettings s) => string.Join("\u0001", s.Id, s.Program, s.ProgramSha256, string.Join("\u0002", s.ProgramArguments));

    /// <summary>True on the second press for the same program; on the first, false, and the caller says <see cref="McpRules.SaidStartsProgram"/>.</summary>
    public bool Confirm(McpServerSettings s)
    {
        string key = Key(s);
        if (_armed == key) { _armed = null; return true; }
        _armed = key;
        return false;
    }

    public void Clear() => _armed = null;
}

/// <summary>Something went wrong talking to a server; the message is for the person and never holds the token or the server's body.</summary>
public sealed class McpException(string message) : Exception(message);
