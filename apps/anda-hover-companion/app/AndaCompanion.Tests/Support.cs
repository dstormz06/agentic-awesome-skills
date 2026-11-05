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

/// <summary>Saves pictures of windows for a person to look at. Runs only when ANDA_SHOTS_DIR names a folder; elsewhere it is reported as skipped.</summary>
public sealed class ShotFactAttribute : FactAttribute
{
    public ShotFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANDA_SHOTS_DIR"))) Skip = "Set ANDA_SHOTS_DIR to save pictures of the windows";
    }
}

/// <summary>Runs only when ANDA_FUZZ_IN names a JSON file of notes to read (a differential check against the browser simulation's copy of the reader).</summary>
public sealed class FuzzFactAttribute : FactAttribute
{
    public FuzzFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANDA_FUZZ_IN"))) Skip = "Set ANDA_FUZZ_IN and ANDA_FUZZ_OUT to compare the notes reader with the simulation's copy";
    }
}
