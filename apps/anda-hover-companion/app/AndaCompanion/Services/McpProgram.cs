using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace AndaCompanion.Services;

/// <summary>
/// Round 41: an MCP server that is a program on this PC (stdio), allowed only as IT approved it: the full path of an .exe and its SHA-256,
/// with fixed arguments, all from Settings. It starts only when the person has pressed twice (<see cref="Permit"/>, used once); a program
/// that stopped, broke off or ran out of time is never started again by itself. Before each start the file's SHA-256 is checked, and the
/// file is held so that it cannot be changed or replaced until the program has started. It is started directly, never through a shell,
/// without the environment variables that hold this app's tokens. Messages are one JSON line each way. The program's output is read all
/// the time by a pump, so a program that writes while it is being written to never blocks; only answers are kept (the newest eight), each
/// read only up to <see cref="McpRules.MaxResponseBytes"/>. Ending the session closes the program's input, waits two seconds, then stops it
/// and anything it started. Every program still running when the app ends is stopped (<see cref="StopAll"/>).
/// </summary>
internal sealed class McpProgram(McpServerSettings server, McpLocal local, IReadOnlyCollection<string> withheld) : IMcpTransport
{
    private static readonly HashSet<McpProgram> Started = new();

    /// <summary>Stops every program this app started and has not stopped yet. Called once, when the app ends (App's Exit).</summary>
    public static void StopAll()
    {
        McpProgram[] all;
        lock (Started) all = Started.ToArray();
        foreach (var p in all) p.Stop();
    }

    /// <summary>One run of the program: its process, the answers its pump has read, and why the pump stopped, if it failed.</summary>
    private sealed class Run(IMcpProcess process)
    {
        public IMcpProcess Process { get; } = process;
        public Channel<JsonObject> Answers { get; } = Channel.CreateBounded<JsonObject>(
            new BoundedChannelOptions(8) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });
        public volatile McpException? Failure;
    }

    private Run? _run;
    private bool _permitted;
    private string Name => McpRules.ProgramName(server.Program);

    public string Unreachable => $"The program {Name} stopped answering.";
    public bool Alive => _run is not null;
    public bool NeedsStart => _run is null;
    public string Redact(string text) => text;

    /// <summary>The person pressed a second time after being told what starts: the next exchange may start the program, once.</summary>
    public void Permit() => _permitted = true;

    public async Task<JsonObject?> ExchangeAsync(JsonObject message, int? id, string? version, CancellationToken ct)
    {
        var run = _run ?? Start();
        try
        {
            // the pipe to a program may not heed a cancel; WaitAsync does, and the program is then stopped, which ends the write
            await run.Process.Input.WriteAsync(Encoding.UTF8.GetBytes(message.ToJsonString() + "\n"), ct).AsTask().WaitAsync(ct);
            await run.Process.Input.FlushAsync(ct).WaitAsync(ct);
            if (id is null) return null;
            var answers = run.Answers.Reader;
            while (await answers.WaitToReadAsync(ct))
                while (answers.TryRead(out var m))
                    if (McpClient.IsAnswerTo(m, id.Value)) return m;
            throw run.Failure ?? new McpException($"The program {Name} stopped before it answered.");
        }
        // a program that broke off, flooded or ran out of time is stopped, and is not used again until the person presses twice
        catch (Exception ex) when (ex is IOException or McpException or OperationCanceledException or ObjectDisposedException) { Stop(); throw; }
    }

    private Run Start()
    {
        if (!_permitted) throw new McpException(McpRules.SaidNotStarted(Name));
        _permitted = false;
        if (McpRules.ProgramProblem(server.Program, server.ProgramSha256) is { } problem) throw new McpException(problem);
        Stream file;
        try { file = local.Open(server.Program); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException) { throw new McpException(McpRules.SaidNoProgram(Name)); }
        IMcpProcess process;
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
            foreach (var name in withheld) info.Environment.Remove(name);      // this app's tokens are not passed on (round 41 review)
            try { process = local.Start(info); }
            catch (System.ComponentModel.Win32Exception) { throw new McpException(McpRules.SaidNoProgram(Name)); }
        }
        var run = new Run(process);
        _ = Task.Run(() => PumpAsync(run));
        _run = run;
        lock (Started) Started.Add(this);
        EventLog.Record("connection", "MCP program started");
        return run;
    }

    /// <summary>Reads the program's output until it ends: answers are kept, anything else (notifications, its own requests, not JSON) passed over.</summary>
    private static async Task PumpAsync(Run run)
    {
        var lines = new LineReader(run.Process.Output);
        try
        {
            while (await lines.ReadLineAsync() is { } line)
            {
                if (line.Trim().Length == 0) continue;
                JsonNode? m = null;
                try { m = McpJson.Parse(line); } catch (McpException) { /* not JSON: passed over */ }
                if (m is JsonObject o && o["method"] is null && o["id"] is not null) run.Answers.Writer.TryWrite(o);
            }
        }
        catch (McpException ex) { run.Failure = ex; }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { /* the program was stopped */ }
        finally { run.Answers.Writer.TryComplete(); }
    }

    public async Task EndAsync()
    {
        if (Interlocked.Exchange(ref _run, null) is not { } run) return;
        lock (Started) Started.Remove(this);
        try { run.Process.Input.Close(); } catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
        if (!await run.Process.EndedWithinAsync(TimeSpan.FromSeconds(2))) run.Process.Kill();
        run.Process.Dispose();
        EventLog.Record("connection", "MCP program stopped");
    }

    private void Stop()
    {
        if (Interlocked.Exchange(ref _run, null) is not { } run) return;
        lock (Started) Started.Remove(this);
        run.Process.Kill(); run.Process.Dispose();
        EventLog.Record("connection", "MCP program stopped");
    }

    public void Dispose() => Stop();

    /// <summary>One line at a time from the program, each read only up to <see cref="McpRules.MaxResponseBytes"/>, so a line with no end cannot fill the memory.</summary>
    private sealed class LineReader(Stream input)
    {
        private readonly byte[] _buffer = new byte[16_384];
        private int _start, _end;

        public async Task<string?> ReadLineAsync()
        {
            var line = new MemoryStream();
            while (true)
            {
                if (_start == _end)
                {
                    _start = 0; _end = await input.ReadAsync(_buffer);
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
