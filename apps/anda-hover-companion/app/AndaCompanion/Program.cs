using System.Runtime.InteropServices;
using Avalonia;
using AndaCompanion.Services;

namespace AndaCompanion;

public static class Program
{
    // Avalonia configuration, don't remove; also used by visual designer and the preview harness.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    [STAThread]
    public static int Main(string[] args)
    {
        var selfTest = SelfTestOptions.Parse(args);
        if (selfTest is not null)
        {
            // Hermetic: a diagnostic run never reads or writes the user's real settings or prompts.
            Environment.SetEnvironmentVariable(AppPaths.HomeEnvVar, Path.Combine(selfTest.OutDir, "home"));
            App.SelfTestRun = selfTest;
        }
        else if (OperatingSystem.IsWindows() && !AcquireSingleInstance())
        {
            // Double-clicking twice is the most common demo mistake, because the tray icon can sit
            // under the ^ overflow. Say so plainly instead of starting a second copy that fights for hotkeys.
            MessageBox(IntPtr.Zero,
                "ANDA Hover Companion is already running.\n\n" +
                "Look for its icon near the clock. Windows 11 may hide it under the ^ arrow; " +
                "drag it onto the taskbar to keep it visible.\n\nPress Ctrl+Alt+Space to open the lookup box.",
                "ANDA Hover Companion", 0x40 /* MB_ICONINFORMATION */);
            return 0;
        }

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // A WinExe that dies silently is the worst demo failure. Leave a log and say where it is.
            string log = Path.Combine(AppPaths.Root, "crash.log");
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                File.AppendAllText(log, $"{DateTime.UtcNow:u}\n{ex}\n\n");
            }
            catch { /* nothing more we can do */ }
            if (OperatingSystem.IsWindows())
                MessageBox(IntPtr.Zero, $"ANDA Hover Companion stopped unexpectedly.\n\n{ex.Message}\n\nDetails: {log}",
                    "ANDA Hover Companion", 0x10 /* MB_ICONERROR */);
            return 1;
        }
    }

    private static Mutex? _singleInstance;   // held for the life of the process

    private static bool AcquireSingleInstance()
    {
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\AndaHoverCompanion.SingleInstance", out bool createdNew);
        return createdNew;
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
