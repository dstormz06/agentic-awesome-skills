using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AndaCompanion.Services;

/// <summary>
/// Round 40: an MCP client over Streamable HTTP (protocol 2025-06-18; 2025-03-26 is accepted from a server). It starts a session
/// (initialize, then notifications/initialized), keeps the session id the server gives, lists tools (following the cursor) and calls one,
/// and ends the session with a DELETE when asked. Every answer is matched to its request by id, whether it comes as one JSON message or
/// as an event stream, and is read only up to <see cref="McpRules.MaxResponseBytes"/>. The time limit covers a whole operation (a list of
/// several pages included). A session the server has forgotten (404) is started again once. Nothing is written to a file but the event
/// log, which keeps counts only.
/// </summary>
public sealed class McpClient : IDisposable
{
    private readonly McpServerSettings _server;
    private readonly Func<string, string?> _env;
    private readonly HttpClient _http;
    private TimeSpan _timeout;
    private string? _session, _version;
    private int _nextId;

    /// <summary>Not redirected: a token meant for this server is never carried to another address.</summary>
    internal static HttpClientHandler DefaultHandler() => new() { AllowAutoRedirect = false };

    /// <summary>For tests only: a shorter time limit than any offered in Settings.</summary>
    internal TimeSpan TimeLimit { set => _timeout = value; }

    public McpClient(McpServerSettings server, Func<string, string?>? env = null, HttpMessageHandler? handler = null)
    {
        _server = server; _env = env ?? Environment.GetEnvironmentVariable;
        _http = handler is null ? new HttpClient(DefaultHandler()) : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = Timeout.InfiniteTimeSpan;    // the limit is ours (RunAsync), over the whole operation and the whole answer
        _timeout = TimeSpan.FromSeconds(CustomApiSettings.TimeoutChoices.Contains(server.TimeoutSeconds) ? server.TimeoutSeconds : 30);
    }

    public async Task<IReadOnlyList<McpTool>> ListToolsAsync(CancellationToken ct)
    {
        var tools = await RunAsync(ReadToolsAsync, ct);
        EventLog.Record("sent", $"MCP tools listed, {EventLog.Count(tools.Count)} tools");
        return tools;
    }

    public async Task<McpCallResult> CallToolAsync(string tool, JsonObject args, CancellationToken ct)
    {
        EventLog.Record("sent", $"MCP request, {EventLog.Count(McpRules.Characters(args))} characters");
        var result = await RunAsync(t => RequestAsync("tools/call", new JsonObject { ["name"] = tool, ["arguments"] = args.DeepClone() }, t), ct);
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
    /// Ends the session, as the protocol asks: a DELETE with the session id, within five seconds. A server that refuses or does not answer
    /// is left alone; nothing is said and nothing is logged, since nothing of the person's is sent.
    /// </summary>
    public async Task EndAsync()
    {
        if (_session is null) return;
        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var request = NewRequest(HttpMethod.Delete, _version).Request;
            using var _ = await _http.SendAsync(request, limit.Token);
        }
        catch (Exception ex) when (ex is McpException or HttpRequestException or IOException or OperationCanceledException) { }
        finally { _session = null; _version = null; }
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
        HttpRequestException or IOException => $"The server at {ApiTarget.Host(_server.Endpoint)} could not be reached, or stopped answering.",
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

    private sealed class SessionGoneException() : Exception("session gone");

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
            catch (SessionGoneException) when (!again) { _session = null; _version = null; }
            catch (SessionGoneException) { throw new McpException("The server forgot the session again just after it was started; nothing more was sent."); }
        }
    }

    /// <summary>The session counts as started only once the server has taken "initialized" (round 40 review).</summary>
    private async Task EnsureSessionAsync(CancellationToken ct)
    {
        if (_version is not null) return;
        _session = null;
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
        await PostAsync("notifications/initialized", null, null, version, ct);
        _version = version;
    }

    /// <summary>A request: its answer's result, or a sentence.</summary>
    private async Task<JsonObject> AskAsync(string method, JsonObject p, string? version, CancellationToken ct) =>
        await PostAsync(method, p, ++_nextId, version, ct) ?? throw new McpException("The server's answer had no result.");

    /// <summary>One message to the server. A request (with an id) is answered; a notification (no id) is only taken (202).</summary>
    private async Task<JsonObject?> PostAsync(string method, JsonObject? p, int? id, string? version, CancellationToken ct)
    {
        var (request, token) = NewRequest(HttpMethod.Post, version);
        using (request)
        {
            var message = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method };
            if (id is not null) message["id"] = id.Value;
            if (p is not null) message["params"] = p.DeepClone();      // the same request may be sent again after a forgotten session
            request.Content = new StringContent(message.ToJsonString(), Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == HttpStatusCode.NotFound && _session is not null && method != "initialize") throw new SessionGoneException();
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new McpException("The server refused the request: it needs a token this app does not have, or the token is not accepted.");
            if (!response.IsSuccessStatusCode) throw new McpException($"The server answered with an error ({(int)response.StatusCode}).");
            if (method == "initialize")
                _session = response.Headers.TryGetValues("Mcp-Session-Id", out var ids) && ids.FirstOrDefault() is { } s && s.Length is > 0 and <= 200 && s.All(c => c is >= '!' and <= '~') ? s : null;
            if (id is null) return null;

            await using var body = new Limited(await response.Content.ReadAsStreamAsync(ct));
            bool events = string.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase);
            var answer = events ? await FromEventsAsync(body, id.Value, ct) : AnswerTo(McpJson.Parse(await ReadAllAsync(body, ct)), id.Value);
            if (answer["error"] is JsonObject error)
            {
                string said = McpRules.OneLine(Str(error["message"]) ?? "no reason given", 300);
                if (token is not null) said = said.Replace(token, "[token]", StringComparison.Ordinal);       // a server that repeats the token does not get it shown
                throw new McpException($"The server could not do this: {said}");
            }
            return answer["result"] as JsonObject;
        }
    }

    /// <summary>A request to this server with its headers: the session, the version, and the token. Checked before anything is sent.</summary>
    private (HttpRequestMessage Request, string? Token) NewRequest(HttpMethod method, string? version)
    {
        var (uri, problem) = ApiTarget.Address(_server.Endpoint);
        if (uri is null) throw new McpException(problem!);
        var (token, tokenProblem) = ApiTarget.Token(_server.SecretEnvVar, _env);
        if (tokenProblem is not null) throw new McpException(tokenProblem);
        var (auth, authProblem) = ApiTarget.Authorization(_server.AuthHeader, _server.AuthScheme, token);
        if (authProblem is not null) throw new McpException(authProblem);

        var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (_session is not null) request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _session);
        if (version is not null) request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", version);
        if (auth is { } h && !request.Headers.TryAddWithoutValidation(h.Key, h.Value))
        {
            request.Dispose();
            throw new McpException("The token header name is not valid: letters, digits and - only, for example Authorization. Nothing was sent.");
        }
        return (request, token);
    }

    /// <summary>The answer must be to this request (round 40 review: a JSON answer was taken whatever its id).</summary>
    private static JsonObject AnswerTo(JsonNode? message, int id) =>
        message is JsonObject m && IsAnswerTo(m, id) ? m : throw new McpException("The server's answer was not an answer to this request.");

    private static bool IsAnswerTo(JsonObject m, int id) =>
        m["id"] is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<int>(out int got) && got == id && (m["result"] is not null || m["error"] is not null);

    private static async Task<string> ReadAllAsync(Stream body, CancellationToken ct)
    {
        var buffer = new MemoryStream();
        await body.CopyToAsync(buffer, ct);
        return Encoding.UTF8.GetString(buffer.ToArray());
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
                    JsonNode? m = null;
                    try { m = McpJson.Parse(data.ToString()); } catch (McpException) { /* not JSON: passed over */ }
                    data.Clear();
                    if (m is JsonObject o && IsAnswerTo(o, id)) return o;
                }
                if (line is null) throw new McpException("The server ended its answer before it answered.");
                continue;
            }
            if (line.StartsWith("data:", StringComparison.Ordinal)) data.Append(data.Length > 0 ? "\n" : "").Append(line.AsSpan(5).TrimStart(' '));
        }
    }

    /// <summary>
    /// An answer read only up to <see cref="McpRules.MaxResponseBytes"/>, counted in bytes as they arrive, so that a line with no end cannot
    /// fill the memory. A stream, not a buffer: an event stream is read as it comes and the answer taken as soon as it is there, since a
    /// server may keep the stream open after it.
    /// </summary>
    private sealed class Limited(Stream inner) : Stream
    {
        private long _read;
        private int Count(int n) => (_read += n) > McpRules.MaxResponseBytes ? throw new McpException("The server's answer is larger than this app reads (1 MB).") : n;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => Count(await inner.ReadAsync(buffer, ct));
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }

    private static string? Str(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    public void Dispose() => _http.Dispose();
}

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
