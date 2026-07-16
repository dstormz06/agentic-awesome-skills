using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using AndaCompanion.Data;
using AndaCompanion.Services;
using AndaCompanion.Theming;
using AndaCompanion.UI;

namespace AndaCompanion.Preview;

/// <summary>
/// Headless render harness: opens the real CardWindow in every appearance and
/// writes PNG frames, so the card can be pixel-reviewed without a Windows box.
/// Usage: dotnet run [outputDir]
/// </summary>
public class PreviewApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<PreviewApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public static class Program
{
    public static async Task Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : ".";
        Directory.CreateDirectory(outDir);

        using var session = HeadlessUnitTestSession.StartNew(typeof(PreviewApp));
        var repo = new MockAndaRepository();
        var briefs = new OfflineBriefService();

        var shots = new (AppearanceId Id, ThemeMode Mode, string Name, string Anda, bool Expanded)[]
        {
            (AppearanceId.A, ThemeMode.Light, "app-a-light", "216482", false),
            (AppearanceId.A, ThemeMode.Dark, "app-a-dark", "216482", false),
            (AppearanceId.B, ThemeMode.Light, "app-b-light", "216482", true),
            (AppearanceId.C, ThemeMode.Dark, "app-c", "216482", false),
            (AppearanceId.D, ThemeMode.Light, "app-d-light", "216482", false),
            (AppearanceId.D, ThemeMode.Dark, "app-d-dark", "216482", false),
            (AppearanceId.E, ThemeMode.Light, "app-e-light", "214990", false),
        };

        foreach (var shot in shots)
        {
            await session.Dispatch(() =>
            {
                ThemeManager.Apply(shot.Id, shot.Mode);
                var window = new CardWindow(briefs, repo.Find);
                var record = repo.Find(shot.Anda)!;
                window.Show(record, new PixelPoint(40, 40), mini: false);
                if (shot.Expanded)
                {
                    // drive the expand button the way a user would
                    window.FindControl<Button>("ExpBtn")!
                        .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                }
                // let the offline brief + layout settle, then force a frame
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var frame = window.CaptureRenderedFrame();
                if (frame is null) { Console.WriteLine($"{shot.Name}: no frame"); return 1; }
                string path = Path.Combine(outDir, shot.Name + ".png");
                frame.Save(path);
                Console.WriteLine($"{shot.Name}: {frame.PixelSize.Width}x{frame.PixelSize.Height} -> {path}");
                window.Close();
                return 0;
            }, CancellationToken.None);
        }
        Console.WriteLine("done");
    }
}
