using Xunit;

namespace AndaCompanion.Tests;

/// <summary>Tests that touch process-wide state (environment variables, the clipboard, global hotkeys) never run in parallel.</summary>
[CollectionDefinition("Global state", DisableParallelization = true)]
public sealed class GlobalStateCollection { }

/// <summary>Runs only on Windows; elsewhere it is reported as skipped with the reason, never silently passed.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows only";
    }
}

public static class Temp
{
    public static string NewDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "anda-companion-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}

/// <summary>Sets an environment variable for the duration of a test and restores it after.</summary>
public sealed class EnvScope : IDisposable
{
    private readonly string _name;
    private readonly string? _previous;

    public EnvScope(string name, string? value)
    {
        _name = name;
        _previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
}
