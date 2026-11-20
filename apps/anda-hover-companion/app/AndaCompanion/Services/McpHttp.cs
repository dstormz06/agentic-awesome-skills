using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace AndaCompanion.Services;

/// <summary>
/// Round 40: an MCP server over Streamable HTTP. The session id the server gives is sent back; an answer may be one JSON message or an event
/// stream; either is read only up to <see cref="McpRules.MaxResponseBytes"/>. Redirects are not followed, so a token is never carried to
/// another address.
/// </summary>
internal sealed class McpHttp : IMcpTransport
{
    private readonly McpServerSettings _server;
    private readonly Func<string, string?> _env;
    private readonly HttpClient _http;
    private string? _session, _token;

    /// <summary>
    /// No redirect followed, and the server's certificate checked against its issuer's revocation list (the security build's rule CA5399,
    /// round 41): a revoked certificate is refused. Where that list cannot be reached, the server is said to be not reachable.
    /// </summary>
    internal static HttpClientHandler DefaultHandler() => new() { AllowAutoRedirect = false, CheckCertificateRevocationList = true };

    public McpHttp(McpServerSettings server, Func<string, string?> env, HttpMessageHandler? handler)
    {
        _server = server; _env = env;
        _http = handler is null ? new HttpClient(DefaultHandler()) : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = Timeout.InfiniteTimeSpan;    // the limit is the client's, over the whole operation and the whole answer
    }

    public string Unreachable => $"The server at {ApiTarget.Host(_server.Endpoint)} could not be reached, or stopped answering.";
    public bool Alive => true;
    public bool NeedsStart => false;
    public void Permit() { }
    public string Redact(string text) => _token is { Length: > 0 } t ? text.Replace(t, "[token]", StringComparison.Ordinal) : text;

    public async Task<JsonObject?> ExchangeAsync(JsonObject message, int? id, string? version, CancellationToken ct)
    {
        string method = McpClient.Str(message["method"]) ?? "";
        if (method == "initialize") _session = null;       // a new session: the old id, if any, is not sent
        using var request = NewRequest(HttpMethod.Post, version);
        request.Content = new StringContent(message.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotFound && _session is not null && method != "initialize") throw new McpSessionGoneException();
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new McpException("The server refused the request: it needs a token this app does not have, or the token is not accepted.");
        if (!response.IsSuccessStatusCode) throw new McpException($"The server answered with an error ({(int)response.StatusCode}).");
        if (method == "initialize")
            _session = response.Headers.TryGetValues("Mcp-Session-Id", out var ids) && ids.FirstOrDefault() is { } s && s.Length is > 0 and <= 200 && s.All(c => c is >= '!' and <= '~') ? s : null;
        if (id is null) return null;

        await using var body = new Limited(await response.Content.ReadAsStreamAsync(ct));
        bool events = string.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase);
        if (events) return await FromEventsAsync(body, id.Value, ct);
        var buffer = new MemoryStream();
        await body.CopyToAsync(buffer, ct);
        return McpJson.Parse(Encoding.UTF8.GetString(buffer.ToArray())) is JsonObject m && McpClient.IsAnswerTo(m, id.Value)
            ? m : throw new McpException("The server's answer was not an answer to this request.");
    }

    /// <summary>A DELETE with the session id, within five seconds; any failure is passed over.</summary>
    public async Task EndAsync()
    {
        if (_session is null) return;
        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var request = NewRequest(HttpMethod.Delete, null);
            using var _ = await _http.SendAsync(request, limit.Token);
        }
        catch (Exception ex) when (ex is McpException or HttpRequestException or IOException or OperationCanceledException) { }
        finally { _session = null; }
    }

    /// <summary>A request to this server with its headers: the session, the version, and the token. Checked before anything is sent.</summary>
    private HttpRequestMessage NewRequest(HttpMethod method, string? version)
    {
        var (uri, problem) = ApiTarget.Address(_server.Endpoint);
        if (uri is null) throw new McpException(problem!);
        var (token, tokenProblem) = ApiTarget.Token(_server.SecretEnvVar, _env);
        if (tokenProblem is not null) throw new McpException(tokenProblem);
        var (auth, authProblem) = ApiTarget.Authorization(_server.AuthHeader, _server.AuthScheme, token);
        if (authProblem is not null) throw new McpException(authProblem);
        _token = token;

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
        return request;
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
                    if (m is JsonObject o && McpClient.IsAnswerTo(o, id)) return o;
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
        private int Count(int n) => (_read += n) > McpRules.MaxResponseBytes ? throw new McpException(McpRules.SaidTooLarge) : n;
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

    public void Dispose() => _http.Dispose();
}
