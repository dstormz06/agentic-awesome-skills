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
/// Headless render harness: opens the real windows in every appearance and
/// writes PNG frames, so the app can be pixel-reviewed without a Windows box.
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

        var cardShots = new (AppearanceId Id, string Name, string Anda, bool Expanded, double Opacity)[]
        {
            (AppearanceId.A, "app-a", "216482", false, 1.0),
            (AppearanceId.B, "app-b-expanded", "216482", true, 1.0),
            (AppearanceId.C, "app-c", "216482", false, 1.0),
            (AppearanceId.D, "app-d", "216482", false, 1.0),
            (AppearanceId.E, "app-e-healthy", "214990", false, 1.0),
            (AppearanceId.A, "app-a-transparent-88", "216482", false, 0.88),
        };

        foreach (var shot in cardShots)
        {
            await session.Dispatch(() =>
            {
                ThemeManager.Apply(shot.Id, shot.Opacity);
                var window = new CardWindow(briefs, repo.Find);
                window.Show(repo.Find(shot.Anda)!, new PixelPoint(40, 40), mini: false);
                if (shot.Expanded)
                {
                    window.FindControl<Button>("ExpBtn")!
                        .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                }
                return Capture(window, shot.Name, outDir);
            }, CancellationToken.None);
        }

        // settings + prompt library, in the default appearance
        await session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.A);
            var settings = new Settings();
            var prompts = new PromptLibraryService();
            var win = new SettingsWindow(settings, prompts, () => { });
            win.Show();
            return Capture(win, "app-settings", outDir);
        }, CancellationToken.None);

        await session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.A);
            var settings = new Settings { ActiveLens = "Facility focus" };
            var prompts = new PromptLibraryService();
            var win = new PromptLibraryWindow(prompts, settings, () => { });
            win.Show();
            return Capture(win, "app-prompt-library", outDir);
        }, CancellationToken.None);

        Console.WriteLine("done");
    }

    private static int Capture(Window window, string name, string outDir)
    {
        // Composite over a light desktop tone (headless default is black, which
        // misrepresents a light-only product — especially the transparency demo).
        window.Background = new Avalonia.Media.SolidColorBrush(
            Avalonia.Media.Color.Parse("#E9E7E3"));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = window.CaptureRenderedFrame();
        if (frame is null) { Console.WriteLine($"{name}: no frame"); return 1; }
        string path = Path.Combine(outDir, name + ".png");
        frame.Save(path);
        Console.WriteLine($"{name}: {frame.PixelSize.Width}x{frame.PixelSize.Height} -> {path}");
        window.Close();
        return 0;
    }
}
