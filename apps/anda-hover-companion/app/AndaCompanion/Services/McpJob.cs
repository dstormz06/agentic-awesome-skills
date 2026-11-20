using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AndaCompanion.Services;

/// <summary>
/// Round 41 (the owner asked for it after the review): every program this app starts is put in one Windows job object that this app
/// holds open and that is set to stop everything in it when it is closed. Windows closes it when this app ends in any way, a crash
/// included, so no program is left running behind it. Best effort: the program is put in the job just after it starts, so a child it
/// starts in that first instant could stay outside (the normal stop still stops its whole tree); if Windows refuses, the program runs
/// without it, as before, and the event log says so. The job holds nothing else of this app's: a browser or file this app opens is not in it.
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

    /// <summary>True when the program is in this app's job (for tests).</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static bool Holds(Process process) => Job.Value is { } job && IsProcessInJob(process.Handle, job, out bool inJob) && inJob;

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
    private static extern bool IsProcessInJob(IntPtr process, SafeFileHandle job, out bool result);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
}
