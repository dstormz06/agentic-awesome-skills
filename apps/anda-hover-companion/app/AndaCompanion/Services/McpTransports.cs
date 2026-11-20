using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace AndaCompanion.Services;

/// <summary>How MCP messages reach a server: one message out, and for a request the answer to it, already matched by id.</summary>
internal interface IMcpTransport : IDisposable
{
    /// <summary>The sentence when the connection breaks.</summary>
    string Unreachable { get; }
    /// <summary>False when the session can no longer be used (a program that stopped); the client then starts a new one.</summary>
    bool Alive { get; }
    Task<JsonObject?> ExchangeAsync(JsonObject message, int? id, string? version, CancellationToken ct);
    /// <summary>The server forgot the session, or a new one is about to start.</summary>
    void Forget();
    /// <summary>A server's words with the token taken out, if the server repeated it.</summary>
    string Redact(string text);
    Task EndAsync();
}

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

    internal static HttpClientHandler DefaultHandler() => new() { AllowAutoRedirect = false };

    public McpHttp(McpServerSettings server, Func<string, string?> env, HttpMessageHandler? handler)
    {
        _server = server; _env = env;
        _http = handler is null ? new HttpClient(DefaultHandler()) : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = Timeout.InfiniteTimeSpan;    // the limit is the client's, over the whole operation and the whole answer
    }

    public string Unreachable => $"The server at {ApiTarget.Host(_server.Endpoint)} could not be reached, or stopped answering.";
    public bool Alive => true;
    public void Forget() => _session = null;
    public string Redact(string text) => _token is { Length: > 0 } t ? text.Replace(t, "[token]", StringComparison.Ordinal) : text;

    public async Task<JsonObject?> ExchangeAsync(JsonObject message, int? id, string? version, CancellationToken ct)
    {
        string method = McpClient.Str(message["method"]) ?? "";
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

/// <summary>A program this app started: its input and output, and how to stop it. A test gives one that runs in memory.</summary>
public interface IMcpProcess : IDisposable
{
    Stream Input { get; }
    Stream Output { get; }
    /// <summary>True when the program ended within the time given.</summary>
    Task<bool> EndedWithinAsync(TimeSpan time);
    void Kill();
}

/// <summary>How a program on this PC is opened (to check it) and started. <see cref="Real"/> is the file and the process; a test gives its own.</summary>
public sealed record McpLocal(Func<string, Stream> Open, Func<ProcessStartInfo, IMcpProcess> Start)
{
    /// <summary>The file is opened for reading with others allowed only to read, so it cannot be changed or replaced while it is held.</summary>
    public static readonly McpLocal Real = new(path => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read), info => new RealProcess(info));

    private sealed class RealProcess : IMcpProcess
    {
        private readonly Process _p;
        public RealProcess(ProcessStartInfo info)
        {
            _p = Process.Start(info) ?? throw new McpException("Windows did not start the program.");
            _ = _p.StandardError.BaseStream.CopyToAsync(Stream.Null);     // what the program writes to its error stream is read and dropped, so it never blocks
        }
        public Stream Input => _p.StandardInput.BaseStream;
        public Stream Output => _p.StandardOutput.BaseStream;
        public async Task<bool> EndedWithinAsync(TimeSpan time)
        {
            using var limit = new CancellationTokenSource(time);
            try { await _p.WaitForExitAsync(limit.Token); return true; } catch (OperationCanceledException) { return false; }
        }
        public void Kill() { try { if (!_p.HasExited) _p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } }
        public void Dispose() => _p.Dispose();
    }
}

/// <summary>
/// Round 41: an MCP server that is a program on this PC (stdio), allowed only as IT approved it: the full path of an .exe and its SHA-256,
/// with fixed arguments, all from Settings. Before each start the file's SHA-256 is checked, and the file is held so that it cannot be
/// changed or replaced until the program has started. It is started directly, never through a shell. Messages are one JSON line each way;
/// each answer is read only up to <see cref="McpRules.MaxResponseBytes"/>; lines that are not the answer (notifications, the program's own
/// requests) are passed over. Ending the session closes the program's input, waits two seconds, then stops it and anything it started.
/// </summary>
internal sealed class McpProgram(McpServerSettings server, McpLocal local) : IMcpTransport
{
    private IMcpProcess? _process;
    private LineReader? _output;
    private string Name => McpRules.ProgramName(server.Program);

    public string Unreachable => $"The program {Name} stopped answering.";
    public bool Alive => _process is not null;
    public void Forget() { }
    public string Redact(string text) => text;

    public async Task<JsonObject?> ExchangeAsync(JsonObject message, int? id, string? version, CancellationToken ct)
    {
        var process = _process ?? Start();
        try
        {
            await process.Input.WriteAsync(Encoding.UTF8.GetBytes(message.ToJsonString() + "\n"), ct);
            await process.Input.FlushAsync(ct);
            if (id is null) return null;
            while (true)
            {
                string line = await _output!.ReadLineAsync(ct) ?? throw new McpException($"The program {Name} stopped before it answered.");
                if (line.Trim().Length == 0) continue;
                JsonNode? m = null;
                try { m = McpJson.Parse(line); } catch (McpException) { /* not JSON: passed over */ }
                if (m is JsonObject o && McpClient.IsAnswerTo(o, id.Value)) return o;
            }
        }
        catch (Exception ex) when (ex is IOException or McpException) { Stop(); throw; }      // a program that broke off is not used again; a new press starts it afresh
    }

    private IMcpProcess Start()
    {
        if (McpRules.ProgramProblem(server.Program, server.ProgramSha256) is { } problem) throw new McpException(problem);
        IMcpProcess process;
        Stream file;
        try { file = local.Open(server.Program); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException) { throw new McpException(McpRules.SaidNoProgram(Name)); }
        using (file)
        {
            // held open from the check of its SHA-256 until the program has started, so what runs is what was checked
            if (Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant() != server.ProgramSha256) throw new McpException(McpRules.SaidHashMismatch(Name));
            var info = new ProcessStartInfo(server.Program)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            foreach (var a in server.ProgramArguments) info.ArgumentList.Add(a);
            try { process = local.Start(info); }
            catch (System.ComponentModel.Win32Exception) { throw new McpException(McpRules.SaidNoProgram(Name)); }
        }
        EventLog.Record("connection", "MCP program started");
        _output = new LineReader(process.Output);
        return _process = process;
    }

    public async Task EndAsync()
    {
        if (_process is not { } process) return;
        _process = null; _output = null;
        try { process.Input.Close(); } catch (IOException) { }
        if (!await process.EndedWithinAsync(TimeSpan.FromSeconds(2))) process.Kill();
        process.Dispose();
        EventLog.Record("connection", "MCP program stopped");
    }

    private void Stop()
    {
        if (_process is not { } process) return;
        _process = null; _output = null;
        process.Kill(); process.Dispose();
        EventLog.Record("connection", "MCP program stopped");
    }

    public void Dispose() => Stop();

    /// <summary>One line at a time from the program, each read only up to <see cref="McpRules.MaxResponseBytes"/>, so a line with no end cannot fill the memory.</summary>
    private sealed class LineReader(Stream input)
    {
        private readonly byte[] _buffer = new byte[16_384];
        private int _start, _end;

        public async Task<string?> ReadLineAsync(CancellationToken ct)
        {
            var line = new MemoryStream();
            while (true)
            {
                if (_start == _end)
                {
                    _start = 0; _end = await input.ReadAsync(_buffer, ct);
                    if (_end == 0) return line.Length > 0 ? Text(line) : null;
                }
                int newline = Array.IndexOf(_buffer, (byte)'\n', _start, _end - _start), stop = newline < 0 ? _end : newline;
                if (line.Length + (stop - _start) > McpRules.MaxResponseBytes) throw new McpException(McpRules.SaidTooLarge);
                line.Write(_buffer, _start, stop - _start);
                _start = newline < 0 ? _end : newline + 1;
                if (newline >= 0) return Text(line);
            }
        }

        private static string Text(MemoryStream line) => Encoding.UTF8.GetString(line.ToArray()).TrimEnd('\r');
    }
}
