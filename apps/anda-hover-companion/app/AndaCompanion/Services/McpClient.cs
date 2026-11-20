using System.Text.Json;
using System.Text.Json.Nodes;

namespace AndaCompanion.Services;

/// <summary>How MCP messages reach a server: one message out, and for a request the answer to it, already matched by id.</summary>
internal interface IMcpTransport : IDisposable
{
    /// <summary>The sentence when the connection breaks.</summary>
    string Unreachable { get; }
    /// <summary>False when the session can no longer be used (a program that stopped); the client then starts a new one.</summary>
    bool Alive { get; }
    /// <summary>Sends a message; for a request (an id), returns the answer to it. An "initialize" starts a new session.</summary>
    Task<JsonObject?> ExchangeAsync(JsonObject message, int? id, string? version, CancellationToken ct);
    /// <summary>True when nothing can be sent until the person confirms a start (a program on this PC that is not running); never for https.</summary>
    bool NeedsStart { get; }
    /// <summary>The person confirmed the start: the next exchange may start the program, once. Nothing for https.</summary>
    void Permit();
    /// <summary>A server's words with the token taken out, if the server repeated it (https; a program is given no token).</summary>
    string Redact(string text);
    Task EndAsync();
}

/// <summary>
/// Round 40: an MCP client (protocol 2025-06-18; 2025-03-26 is accepted from a server). It starts a session (initialize, then
/// notifications/initialized), lists tools (following the cursor) and calls one, and ends the session when asked. Round 41: the messages go
/// through a transport: an https server (<see cref="McpHttp"/>), or a program on this PC that IT approved (<see cref="McpProgram"/>). Every
/// answer is matched to its request by id and read only up to <see cref="McpRules.MaxResponseBytes"/>. The time limit covers a whole
/// operation. A session the server has forgotten (404, https only) is started again once. Nothing is written to a file but the event log,
/// which keeps counts only.
/// </summary>
public sealed class McpClient : IDisposable
{
    private readonly IMcpTransport _transport;
    private TimeSpan _timeout;
    private string? _version;
    private int _nextId;

    /// <summary>Not redirected: a token meant for this server is never carried to another address.</summary>
    internal static HttpClientHandler DefaultHandler() => McpHttp.DefaultHandler();

    /// <summary>For tests only: a shorter time limit than any offered in Settings.</summary>
    internal TimeSpan TimeLimit { set => _timeout = value; }

    /// <param name="withheld">For a program on this PC: the environment variables it is not given (<see cref="McpRules.WithheldVariables"/>).</param>
    public McpClient(McpServerSettings server, IReadOnlyCollection<string>? withheld = null) : this(server, null, null, null, withheld) { }

    /// <param name="env">For tests: where the token is read (the environment otherwise).</param>
    /// <param name="handler">For tests: a server that answers without a network.</param>
    /// <param name="local">For tests: how a program on this PC is opened and started (the real file and process otherwise).</param>
    internal McpClient(McpServerSettings server, Func<string, string?>? env, HttpMessageHandler? handler = null, McpLocal? local = null, IReadOnlyCollection<string>? withheld = null)
    {
        _transport = server.IsProgram
            ? new McpProgram(server, local ?? McpLocal.Real, withheld ?? McpRules.AlwaysWithheld)
            : new McpHttp(server, env ?? Environment.GetEnvironmentVariable, handler);
        _timeout = TimeSpan.FromSeconds(CustomApiSettings.TimeoutChoices.Contains(server.TimeoutSeconds) ? server.TimeoutSeconds : 30);
    }

    /// <summary>
    /// Round 41: a program on this PC that is not running. It starts only after <see cref="PermitStart"/>, which the windows call on the
    /// person's second press (<see cref="McpStartConfirm"/>); until then a request is refused before anything is started or sent.
    /// </summary>
    public bool NeedsStart => _transport.NeedsStart;

    /// <summary>The person confirmed the start: the next request may start the program, once.</summary>
    public void PermitStart() => _transport.Permit();

    public async Task<IReadOnlyList<McpTool>> ListToolsAsync(CancellationToken ct)
    {
        var tools = await RunAsync(ReadToolsAsync, ct);
        EventLog.Record("sent", $"MCP tools listed, {EventLog.Count(tools.Count)} tools");
        return tools;
    }

    public async Task<McpCallResult> CallToolAsync(string tool, JsonObject args, CancellationToken ct)
    {
        var result = await RunAsync(async t =>
        {
            // round 41 review: logged once the session is up, so a request refused before anything could go (a program not running,
            // an address that cannot be used) is not logged as sent
            await EnsureSessionAsync(t);
            EventLog.Record("sent", $"MCP request, {EventLog.Count(McpRules.Characters(args))} characters");
            return await RequestAsync("tools/call", new JsonObject { ["name"] = tool, ["arguments"] = args.DeepClone() }, t);
        }, ct);
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
        return new McpCallResult(string.Join("\n\n", parts), result["isError"] is JsonValue e && e.GetValueKind() == JsonValueKind.True);
    }

    /// <summary>
    /// Ends the session: https, a DELETE with the session id within five seconds; a program, its input closed and the program stopped.
    /// A server that refuses or does not answer is left alone; nothing is said, since nothing of the person's is sent.
    /// </summary>
    public async Task EndAsync()
    {
        try { await _transport.EndAsync(); }
        finally { _version = null; }
    }

    // ---------- one operation ----------

    /// <summary>
    /// The time limit covers the whole operation; whatever goes wrong becomes one sentence for the person (never the server's body or the
    /// token) and one line of the event log. Cancelled by the caller (the window was closed): passed on as it is, not logged.
    /// </summary>
    private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(_timeout);
        try { return await work(limit.Token); }
        catch (Exception ex) when (Said(ex, ct) is { } said)
        {
            EventLog.Record("send_failed", "MCP request: no answer");
            throw new McpException(said);
        }
    }

    private string? Said(Exception ex, CancellationToken ct) => ex switch
    {
        McpException m => m.Message,
        OperationCanceledException when !ct.IsCancellationRequested => "The server did not answer in time.",
        // round 41 review: with the revocation check on, a certificate refused or not checkable is said as such, not as "not reachable"
        HttpRequestException { InnerException: System.Security.Authentication.AuthenticationException } => McpRules.SaidCertificate,
        HttpRequestException or IOException => _transport.Unreachable,
        _ => null,
    };

    private async Task<IReadOnlyList<McpTool>> ReadToolsAsync(CancellationToken ct)
    {
        var tools = new List<McpTool>(); var names = new HashSet<string>(StringComparer.Ordinal); string? cursor = null;
        for (int page = 0; page < 10 && tools.Count < McpRules.MaxTools; page++)
        {
            var p = new JsonObject(); if (cursor is not null) p["cursor"] = cursor;
            var result = await RequestAsync("tools/list", p, ct);
            foreach (var t in (result["tools"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                // a name that cannot be shown exactly as it is sent (empty, too long, a control or invisible character), or a second tool of
                // the same name, is not offered: what the person reads is what goes
                string name = Str(t["name"]) ?? "";
                if (name.Length is 0 or > McpRules.MaxToolNameChars || name.Any(char.IsControl) || ActionFill.WithoutHidden(name).Text != name || !names.Add(name)) continue;
                tools.Add(new McpTool(name, McpRules.OneLine(Str(t["title"]), 80), McpRules.OneLine(Str(t["description"]), McpRules.MaxDescriptionChars),
                    t["inputSchema"] is JsonObject schema ? (JsonObject)schema.DeepClone() : new JsonObject()));
                if (tools.Count >= McpRules.MaxTools) break;
            }
            cursor = Str(result["nextCursor"]); if (string.IsNullOrEmpty(cursor)) break;
        }
        return tools;
    }

    // ---------- the protocol ----------

    /// <summary>A request in a session; a session the server has forgotten is started again once, and a second time is a sentence.</summary>
    private async Task<JsonObject> RequestAsync(string method, JsonObject p, CancellationToken ct)
    {
        for (bool again = false; ; again = true)
        {
            try
            {
                await EnsureSessionAsync(ct);
                return await AskAsync(method, p, _version, ct);
            }
            catch (McpSessionGoneException) when (!again) { _version = null; }
            catch (McpSessionGoneException) { throw new McpException("The server forgot the session again just after it was started; nothing more was sent."); }
        }
    }

    /// <summary>The session counts as started only once the server has taken "initialized" (round 40 review), and while the transport lives.</summary>
    private async Task EnsureSessionAsync(CancellationToken ct)
    {
        if (_version is not null && _transport.Alive) return;
        _version = null;
        var init = new JsonObject
        {
            ["protocolVersion"] = McpRules.ProtocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "ANDA Companion (Notula)", ["version"] = ChangeLog.Version },
        };
        var result = await AskAsync("initialize", init, null, ct);
        string? version = Str(result["protocolVersion"]);
        if (version is null || !McpRules.Versions.Contains(version))
            throw new McpException($"The server speaks MCP version {McpRules.OneLine(version ?? "(none)", 20)}, which this app does not; nothing more was sent.");
        try { await _transport.ExchangeAsync(Message("notifications/initialized", null, null), null, version, ct); }
        catch (Exception ex) when (ex is not McpSessionGoneException) { await EndAsync(); throw; }      // the session the server opened is ended, not left behind
        _version = version;
    }

    /// <summary>A request: its answer's result, or a sentence.</summary>
    private async Task<JsonObject> AskAsync(string method, JsonObject p, string? version, CancellationToken ct)
    {
        int id = ++_nextId;
        var answer = await _transport.ExchangeAsync(Message(method, p, id), id, version, ct) ?? throw new McpException("The server's answer had no result.");
        if (answer["error"] is JsonObject error)
            // a server that repeats the token does not get it shown: replaced before the message is cut, so no part of it is left (round 40 re-check)
            throw new McpException($"The server could not do this: {McpRules.OneLine(_transport.Redact(Str(error["message"]) ?? "no reason given"), 300)}");
        return answer["result"] as JsonObject ?? throw new McpException("The server's answer had no result.");
    }

    private static JsonObject Message(string method, JsonObject? p, int? id)
    {
        var message = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method };
        if (id is not null) message["id"] = id.Value;
        if (p is not null) message["params"] = p.DeepClone();      // the same request may be sent again after a forgotten session
        return message;
    }

    /// <summary>The answer must be to this request (round 40 review: a JSON answer was taken whatever its id).</summary>
    internal static bool IsAnswerTo(JsonObject m, int id) =>
        m["id"] is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<int>(out int got) && got == id && (m["result"] is not null || m["error"] is not null);

    internal static string? Str(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    public void Dispose() => _transport.Dispose();
}

/// <summary>The https server forgot the session (404); the client starts it again once.</summary>
internal sealed class McpSessionGoneException() : Exception("session gone");

/// <summary>JSON from a server, read whole before it is used.</summary>
internal static class McpJson
{
    /// <summary>
    /// Round 40 review: JsonNode reads an object lazily and reports a duplicate key only when the object is first used, as an error no handler
    /// expects. Every object is read here, at once, so such an answer is simply "not JSON".
    /// </summary>
    public static JsonNode? Parse(string text)
    {
        try { var node = JsonNode.Parse(text); Visit(node); return node; }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException) { throw new McpException("The server's answer was not JSON."); }
    }

    private static void Visit(JsonNode? node)
    {
        if (node is JsonObject o) foreach (var kv in o) Visit(kv.Value);
        else if (node is JsonArray a) foreach (var x in a) Visit(x);
    }
}
