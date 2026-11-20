using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
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

/// <summary>What a call gave back: the text parts in order, whether the tool itself said it failed, and a sentence for the person.</summary>
public sealed record McpCallResult(bool Ok, string Text, bool ToolError, string Message);

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

/// <summary>
/// Round 40: an MCP client over Streamable HTTP (protocol 2025-06-18; 2025-03-26 is accepted from a server). It starts a session
/// (initialize, then notifications/initialized), keeps the session id the server gives, lists tools (following the cursor) and calls one.
/// An answer may be one JSON message or an event stream; either is read only up to <see cref="McpRules.MaxResponseBytes"/>. A session the
/// server has forgotten (404) is started again once. Nothing is written to a file but the event log, which keeps counts only: how many
/// tools were listed, how many characters of arguments were sent, or that no answer came.
/// </summary>
public sealed class McpClient : IDisposable
{
    private readonly McpServerSettings _server;
    private readonly Func<string, string?> _env;
    private readonly HttpClient _http;
    private TimeSpan _timeout;

    internal static HttpClientHandler DefaultHandler() => new() { AllowAutoRedirect = false };

    /// <summary>For tests only: a shorter time limit than any offered in Settings.</summary>
    internal TimeSpan TimeLimit { set => _timeout = value; }
    private string? _session, _version;
    private int _nextId;

    public McpClient(McpServerSettings server, Func<string, string?>? env = null, HttpMessageHandler? handler = null)
    {
        _server = server; _env = env ?? Environment.GetEnvironmentVariable;
        // not redirected: a token meant for this server is never carried to another address (as for My API)
        _http = handler is null ? new HttpClient(DefaultHandler()) : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = Timeout.InfiniteTimeSpan;    // the time limit covers the whole answer, read to its end (below), not only its first line
        _timeout = TimeSpan.FromSeconds(CustomApiSettings.TimeoutChoices.Contains(server.TimeoutSeconds) ? server.TimeoutSeconds : 30);
    }

    /// <summary>The address, or why none can be used (checked as for My API, before anything is sent).</summary>
    public (Uri? Uri, string? Problem) Target() => ApiTarget.Address(_server.Endpoint);

    public async Task<IReadOnlyList<McpTool>> ListToolsAsync(CancellationToken ct)
    {
        IReadOnlyList<McpTool> tools;
        try { tools = await ReadToolsAsync(ct); }
        catch (McpException) { EventLog.Record("send_failed", "MCP request: no answer"); throw; }
        EventLog.Record("sent", $"MCP tools listed, {EventLog.Count(tools.Count)} tools");
        return tools;
    }

    private async Task<IReadOnlyList<McpTool>> ReadToolsAsync(CancellationToken ct)
    {
        var tools = new List<McpTool>(); string? cursor = null;
        for (int page = 0; page < 10 && tools.Count < McpRules.MaxTools; page++)
        {
            var p = new JsonObject(); if (cursor is not null) p["cursor"] = cursor;
            var result = await RequestAsync("tools/list", p, ct);
            if (result["tools"] is JsonArray list)
                foreach (var t in list.OfType<JsonObject>())
                {
                    string name = t["name"] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : "";
                    if (name.Length == 0 || name.Length > McpRules.MaxToolNameChars || name.Any(char.IsControl)) continue;     // a name that cannot be shown as it is is not offered
                    tools.Add(new McpTool(name, McpRules.OneLine(Str(t["title"]), 80), McpRules.OneLine(Str(t["description"]), McpRules.MaxDescriptionChars),
                        t["inputSchema"] is JsonObject schema ? (JsonObject)schema.DeepClone() : new JsonObject()));
                    if (tools.Count >= McpRules.MaxTools) break;
                }
            cursor = Str(result["nextCursor"]); if (string.IsNullOrEmpty(cursor)) break;
        }
        return tools;
    }

    public async Task<McpCallResult> CallToolAsync(string tool, JsonObject args, CancellationToken ct)
    {
        EventLog.Record("sent", $"MCP request, {EventLog.Count(McpRules.Characters(args))} characters");
        JsonObject result;
        try { result = await RequestAsync("tools/call", new JsonObject { ["name"] = tool, ["arguments"] = args.DeepClone() }, ct); }
        catch (McpException) { EventLog.Record("send_failed", "MCP request: no answer"); throw; }
        var parts = new List<string>();
        foreach (var c in (result["content"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            parts.Add(Str(c["type"]) switch
            {
                "text" => Str(c["text"]) ?? "",
                "image" => "[An image was returned; this app shows text only.]",
                "audio" => "[Audio was returned; this app shows text only.]",
                "resource_link" => $"[A link to {McpRules.OneLine(Str(c["uri"]), 300)}]",
                "resource" => Str((c["resource"] as JsonObject)?["text"]) ?? "[A resource was returned; this app shows text only.]",
                _ => "[A part of a kind this app does not show was returned.]",
            });
        if (parts.Count == 0 && result["structuredContent"] is JsonNode structured) parts.Add(structured.ToJsonString(McpRules.Shown));
        bool toolError = result["isError"] is JsonValue e && e.GetValueKind() == JsonValueKind.True;
        string text = string.Join("\n\n", parts);
        return new McpCallResult(!toolError, text, toolError, toolError ? "The tool says it could not do this; its words are below." : "Answered.");
    }

    // ---------- the protocol ----------

    private async Task EnsureSessionAsync(CancellationToken ct)
    {
        if (_version is not null) return;
        var init = new JsonObject
        {
            ["protocolVersion"] = McpRules.ProtocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "ANDA Companion (Notula)", ["version"] = ChangeLog.Version },
        };
        var result = await SendAsync("initialize", init, ct, isRequest: true);
        string? version = Str(result!["protocolVersion"]);
        if (version is null || !McpRules.Versions.Contains(version))
            throw new McpException($"The server speaks MCP version {McpRules.OneLine(version ?? "(none)", 20)}, which this app does not; nothing more was sent.");
        _version = version;
        await SendAsync("notifications/initialized", null, ct, isRequest: false);
    }

    private async Task<JsonObject> RequestAsync(string method, JsonObject p, CancellationToken ct)
    {
        await EnsureSessionAsync(ct);
        try { return (await SendAsync(method, p, ct, isRequest: true))!; }
        catch (SessionGoneException)
        {
            _session = null; _version = null;            // the server forgot the session: start again, once
            await EnsureSessionAsync(ct);
            try { return (await SendAsync(method, p, ct, isRequest: true))!; }
            catch (SessionGoneException) { throw new McpException("The server forgot the session again just after it was started; nothing more was sent."); }
        }
    }

    private sealed class SessionGoneException() : Exception("session gone");

    private async Task<JsonObject?> SendAsync(string method, JsonObject? p, CancellationToken ct, bool isRequest)
    {
        var (uri, problem) = Target();
        if (uri is null) throw new McpException(problem!);
        var (token, tokenProblem) = ApiTarget.Token(_server.SecretEnvVar, _env);
        if (tokenProblem is not null) throw new McpException(tokenProblem);
        if (token is not null && token.Any(char.IsControl)) throw new McpException("The token in that environment variable has a line break or another control character in it, so it was not sent.");

        int id = ++_nextId;
        var message = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method };
        if (isRequest) message["id"] = id;
        if (p is not null) message["params"] = p.DeepClone();      // the same request may be sent again after a forgotten session
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new StringContent(message.ToJsonString(), Encoding.UTF8, "application/json") };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (_session is not null) request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _session);
        if (_version is not null) request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", _version);
        string header = (_server.AuthHeader ?? "").Trim();
        if (token is not null && header.Length > 0 &&
            !request.Headers.TryAddWithoutValidation(header, (_server.AuthScheme ?? "").Trim() is { Length: > 0 } scheme ? $"{scheme} {token}" : token))
            throw new McpException("The authentication header name is not valid.");

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(_timeout);
        try { return await ExchangeAsync(request, method, id, isRequest, limit.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new McpException("The server did not answer in time."); }
        catch (Exception ex) when (ex is HttpRequestException or IOException) { throw new McpException($"The server at {uri.IdnHost} could not be reached, or stopped answering."); }
    }

    private async Task<JsonObject?> ExchangeAsync(HttpRequestMessage request, string method, int id, bool isRequest, CancellationToken ct)
    {
        using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            if (response.StatusCode == HttpStatusCode.NotFound && _session is not null && method != "initialize") throw new SessionGoneException();
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new McpException("The server refused the request: it needs a token this app does not have, or the token is not accepted.");
            if (!response.IsSuccessStatusCode) throw new McpException($"The server answered with an error ({(int)response.StatusCode}).");
            if (method == "initialize" && response.Headers.TryGetValues("Mcp-Session-Id", out var ids))
                _session = ids.FirstOrDefault() is { } s && s.Length is > 0 and <= 200 && s.All(c => c is >= '!' and <= '~') ? s : null;
            if (!isRequest) return null;                 // a notification: the server says 202 and nothing more

            string media = response.Content.Headers.ContentType?.MediaType ?? "";
            await using var body = new Limited(await response.Content.ReadAsStreamAsync(ct));
            JsonObject answer = media == "text/event-stream" ? await FromEventsAsync(body, id, ct) : AsMessage(await ReadLimitedAsync(body, ct));
            if (answer["error"] is JsonObject error)
                throw new McpException($"The server could not do this: {McpRules.OneLine(Str(error["message"]) ?? "no reason given", 300)}");
            return answer["result"] as JsonObject ?? throw new McpException("The server's answer had no result.");
        }
    }

    private static async Task<string> ReadLimitedAsync(Stream body, CancellationToken ct)
    {
        var buffer = new MemoryStream();
        await body.CopyToAsync(buffer, ct);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>An answer read only up to <see cref="McpRules.MaxResponseBytes"/>, counted in bytes as they arrive (a line with no end cannot fill the memory).</summary>
    private sealed class Limited(Stream inner) : Stream
    {
        private long _read;
        public const string TooLarge = "The server's answer is larger than this app reads (1 MB).";
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            int n = await inner.ReadAsync(buffer, ct);
            if ((_read += n) > McpRules.MaxResponseBytes) throw new McpException(TooLarge);
            return n;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = inner.Read(buffer, offset, count);
            if ((_read += n) > McpRules.MaxResponseBytes) throw new McpException(TooLarge);
            return n;
        }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }

    private static JsonObject AsMessage(string text)
    {
        try { return JsonNode.Parse(text) as JsonObject ?? throw new McpException("The server's answer was not one JSON-RPC message."); }
        catch (JsonException) { throw new McpException("The server's answer was not JSON."); }
    }

    /// <summary>An event stream: the answer is the message whose id is ours; other messages (notifications, the server's own requests) are passed over.</summary>
    private static async Task<JsonObject> FromEventsAsync(Stream body, int id, CancellationToken ct)
    {
        using var reader = new StreamReader(body, Encoding.UTF8);
        var data = new StringBuilder();
        while (true)
        {
            string? line = await reader.ReadLineAsync(ct);
            if (line is null || line.Length == 0)
            {
                if (data.Length > 0)
                {
                    JsonObject? m = null;
                    try { m = JsonNode.Parse(data.ToString()) as JsonObject; } catch (JsonException) { }
                    data.Clear();
                    if (m is not null && m["id"] is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<int>(out int got) && got == id && (m["result"] is not null || m["error"] is not null)) return m;
                }
                if (line is null) throw new McpException("The server ended its answer before it answered.");
                continue;
            }
            if (line.StartsWith("data:", StringComparison.Ordinal)) data.Append(data.Length > 0 ? "\n" : "").Append(line.AsSpan(5).TrimStart(' '));
        }
    }

    private static string? Str(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    public void Dispose() => _http.Dispose();
}
