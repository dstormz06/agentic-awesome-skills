using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AndaCompanion.Services;

/// <summary>A program this app started: its input and output, and how to stop it. A test gives one that runs in memory.</summary>
internal interface IMcpProcess : IDisposable
{
    Stream Input { get; }
    Stream Output { get; }
    /// <summary>True when the program ended within the time given.</summary>
    Task<bool> EndedWithinAsync(TimeSpan time);
    void Kill();
}

/// <summary>How a program on this PC is opened (to check it) and started. <see cref="Real"/> is the file and the process; a test gives its own.</summary>
internal sealed record McpLocal(Func<string, Stream> Open, Func<ProcessStartInfo, IMcpProcess> Start)
{
    /// <summary>The file is opened for reading with others allowed only to read, so it cannot be changed or replaced while it is held.</summary>
    public static readonly McpLocal Real = new(OpenHeld, info => new RealProcess(info));

    /// <summary>
    /// Round 41 review: the path typed must be the file itself on a drive of this PC. Once the file is open, Windows says where it really is;
    /// a network drive, a link or junction anywhere in the path, a substituted drive or a short name gives another answer, and is refused.
    /// </summary>
    private static Stream OpenHeld(string path)
    {
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (OperatingSystem.IsWindows() && !SameFile(path, FinalPath(file.SafeFileHandle)))
        {
            file.Dispose();
            throw new McpException(McpRules.SaidNotThere(McpRules.ProgramName(path)));
        }
        return file;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, [Out] char[] path, uint size, uint flags);

    /// <summary>Where Windows says the open file is (for example \\?\C:\Program Files\Example\mcp-server.exe), or null.</summary>
    private static string? FinalPath(SafeFileHandle file)
    {
        var buffer = new char[1024];
        uint n = GetFinalPathNameByHandleW(file, buffer, (uint)buffer.Length, 0);
        return n is > 0 and < 1024 ? new string(buffer, 0, (int)n) : null;
    }

    /// <summary>
    /// True when Windows' own path for the open file is the path typed (letters in any case): a local drive, no link, no network share.
    /// A network path comes back as \\?\UNC\server\share\…, which never equals a typed path (that starts with a drive letter), so the one
    /// comparison refuses it too.
    /// </summary>
    internal static bool SameFile(string typed, string? final)
    {
        if (final is null) return false;
        const string Long = @"\\?\";
        string f = final.StartsWith(Long, StringComparison.Ordinal) ? final[Long.Length..] : final;
        return string.Equals(f, typed, StringComparison.OrdinalIgnoreCase);
    }

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
