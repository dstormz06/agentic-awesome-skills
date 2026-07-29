using Avalonia;

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
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
}
