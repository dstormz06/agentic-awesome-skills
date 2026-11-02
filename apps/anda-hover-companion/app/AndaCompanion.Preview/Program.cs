using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
/// <summary>
/// Subclasses the REAL application so every frame carries the real app-level
/// styles (tooltips, focus rings, primary buttons, slider). Only the desktop
/// lifecycle (tray icon, hotkeys) is skipped — screenshots must never diverge
/// from what a user sees.
/// </summary>
public class PreviewApp : AndaCompanion.App
{
    public override void OnFrameworkInitializationCompleted() { /* no tray/hotkeys in headless */ }

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

        // Every look is rendered, alternating the at-risk and the healthy record so both states show.
        var cardShots = new List<(AppearanceId Id, string Name, string Anda, bool Expanded, double Opacity)>();
        foreach (var look in Appearances.All.Where(a => !a.IsCompact))
        {
            string id = look.Id.ToString().ToLowerInvariant();
            cardShots.Add((look.Id, $"app-{id}", "216482", false, 1.0));
            cardShots.Add((look.Id, $"app-{id}-healthy", "214990", false, 1.0));
        }
        cardShots.Add((AppearanceId.K, "app-k-expanded", "216482", true, 1.0));
        cardShots.Add((AppearanceId.B, "app-b-expanded", "216482", true, 1.0));
        cardShots.Add((AppearanceId.K, "app-k-transparent-88", "216482", false, 0.88));

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

        // The compact boxes: the at-risk record, plus the healthy one for the strip and the badge.
        foreach (var (id, number, name) in new[]
        {
            (AppearanceId.L, "216482", "compact-l"), (AppearanceId.L, "214990", "compact-l-healthy"),
            (AppearanceId.M, "216482", "compact-m"), (AppearanceId.M, "214990", "compact-m-healthy"),
            (AppearanceId.N, "216482", "compact-n"), (AppearanceId.N, "214990", "compact-n-healthy"),
            (AppearanceId.O, "216482", "compact-o"), (AppearanceId.O, "214990", "compact-o-healthy"),
        })
        {
            await session.Dispatch(() =>
            {
                ThemeManager.Apply(id);
                var box = new CompactCardWindow(repo.Find(number)!, () => { });
                box.ShowNear(new PixelPoint(40, 40));
                return Capture(box, name, outDir);
            }, CancellationToken.None);
        }

        // settings + prompt library, in the default appearance
        await session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var settings = new Settings();
            var prompts = new PromptLibraryService();
            var win = new SettingsWindow(settings, prompts, () => { });
            win.Show();
            return Capture(win, "app-settings", outDir);
        }, CancellationToken.None);

        await session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
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
        // The entrance animation (fade + settle) has not run in a headless frame; show the settled state.
        foreach (var b in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Border>()
                     .Where(b => b.Transitions is { Count: > 0 }))
        {
            b.Transitions = null; b.Opacity = 1; b.RenderTransform = null;
        }
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
