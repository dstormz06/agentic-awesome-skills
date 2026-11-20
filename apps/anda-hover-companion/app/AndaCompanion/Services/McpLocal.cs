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
            if (OperatingSystem.IsWindows()) McpJob.Hold(_p);       // stopped by Windows even if this app crashes (round 41, the owner's yes)
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
/// Round 41 (the owner asked for it after the review): every program this app starts is put in one Windows job object that this app
/// holds open and that is set to stop everything in it when it is closed. Windows closes it when this app ends in any way, a crash
/// included, so no program is left running behind it. Best effort: the program is put in the job just after it starts, so a child it
/// starts in that first instant could stay outside (the normal stop still stops its whole tree); if Windows refuses, the program runs
/// without it, as before. The job holds nothing else of this app's: a browser or file this app opens is not in it.
/// </summary>
internal static class McpJob
{
    private const uint KillOnJobClose = 0x2000;         // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
    private const int ExtendedLimitInformation = 9;     // JobObjectExtendedLimitInformation

    private static readonly Lazy<SafeFileHandle?> Job = new(() => OperatingSystem.IsWindows() ? Create() : null);

    /// <summary>Puts the program in this app's job; false when Windows refused (the program then runs without it).</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static bool Hold(Process process) => Job.Value is { } job && Assign(job, process);

    /// <summary>A new job set to stop everything in it when its last handle is closed, or null. A test makes its own to close.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static SafeFileHandle? Create()
    {
        var job = CreateJobObjectW(IntPtr.Zero, null);
        if (job.IsInvalid) { job.Dispose(); return null; }
        var info = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = KillOnJobClose } };
        if (!SetInformationJobObject(job, ExtendedLimitInformation, ref info, (uint)Marshal.SizeOf<ExtendedLimits>())) { job.Dispose(); return null; }
        return job;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static bool Assign(SafeFileHandle job, Process process)
    {
        try { return AssignProcessToJobObject(job, process.Handle); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BasicLimits
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }

    /// <summary>JOBOBJECT_EXTENDED_LIMIT_INFORMATION: 144 bytes in a 64-bit process, 112 in a 32-bit one.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObjectW(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass, ref ExtendedLimits info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
}
