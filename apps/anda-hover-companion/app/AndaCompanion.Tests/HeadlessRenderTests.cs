using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AndaCompanion.Data;
using AndaCompanion.Services;
using AndaCompanion.Theming;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>The real app (all styles, resources, windows) with the desktop lifecycle (tray, hotkeys) switched off.</summary>
public class TestApp : AndaCompanion.App
{
    public override void OnFrameworkInitializationCompleted() { }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class HeadlessFixture : IDisposable
{
    public HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(TestApp));
    public void Dispose() => Session.Dispose();
}

/// <summary>Opens the real windows in every appearance and inspects what actually rendered.</summary>
[Collection("Global state")]
public class HeadlessRenderTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    private static readonly MockAndaRepository Repo = new();

    public HeadlessRenderTests(HeadlessFixture fx) => _fx = fx;

    public static IEnumerable<object[]> Cases() =>
        from a in Appearances.All
        from n in new[] { "216482", "214990" }
        select new object[] { a.Id, n };

    [Theory, MemberData(nameof(Cases))]
    public async Task Card_renders_light_with_real_content(AppearanceId id, string number)
    {
        var result = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(id);
            var window = new CardWindow(new OfflineBriefService(), Repo.Find) { CloseWhenDeactivated = false };
            window.Show(Repo.Find(number)!, new PixelPoint(40, 40), mini: false);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var frame = window.CaptureRenderedFrame();
            var surface = (SolidColorBrush)window.FindControl<Border>("Root")!.Background!;
            var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
            window.Close();
            return (frame, surface.Color, texts);
        }, CancellationToken.None);

        Assert.NotNull(result.frame);
        Assert.InRange(result.frame!.PixelSize.Width, 300, 700);
        Assert.InRange(result.frame.PixelSize.Height, 300, 900);

        // light surface: relative luminance well above mid
        double lum = (0.2126 * result.Color.R + 0.7152 * result.Color.G + 0.0722 * result.Color.B) / 255.0;
        Assert.True(lum > 0.9, $"{id} card surface luminance {lum:0.00}");

        // real record content reached the screen
        Assert.Contains(result.texts, t => t.Contains(number));
        Assert.Contains(result.texts, t => t.Contains("Metformin"));
        Assert.Contains(result.texts, t => t.StartsWith("Copy status") || t.Contains("Copy status"));
    }

    [Fact]
    public async Task Drawer_expansion_widens_the_card_and_shows_forecast_drivers()
    {
        var (collapsed, expanded, texts) = await _fx.Session.Dispatch<(double, double, List<string>)>(() =>
        {
            ThemeManager.Apply(AppearanceId.A);
            var window = new CardWindow(new OfflineBriefService(), Repo.Find) { CloseWhenDeactivated = false };
            window.Show(Repo.Find("216482")!, new PixelPoint(40, 40), mini: false);
            Dispatcher.UIThread.RunJobs();
            double before = window.FindControl<Border>("Root")!.Width;
            window.FindControl<Button>("ExpBtn")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            double after = window.FindControl<Border>("Root")!.Width;
            // The detail lives in a collapsed Expander (content is created lazily), so read the named control.
            var all = new List<string> { window.FindControl<TextBlock>("ForecastDetailText")!.Text ?? "" };
            window.Close();
            return (before, after, all);
        }, CancellationToken.None);

        Assert.True(expanded > collapsed);
        Assert.Contains(texts, t => t.Contains("Baseline from comparable cycle-2 reviews"));
    }

    [Fact]
    public async Task Peek_mode_hides_the_brief_and_offers_more()
    {
        var (briefVisible, moreVisible) = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.A);
            var window = new CardWindow(new OfflineBriefService(), Repo.Find) { CloseWhenDeactivated = false };
            window.Show(Repo.Find("216482")!, new PixelPoint(40, 40), mini: true);
            Dispatcher.UIThread.RunJobs();
            var result = (window.FindControl<Control>("BriefSlot")!.IsVisible, window.FindControl<Control>("MoreBtn")!.IsVisible);
            window.Close();
            return result;
        }, CancellationToken.None);

        Assert.False(briefVisible);
        Assert.True(moreVisible);
    }

    [Fact]
    public async Task A_long_notice_wraps_instead_of_running_off_the_screen()
    {
        double width = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.A);
            var chip = new ChipWindow();
            chip.ShowNotice("Ctrl+Alt+A is already used by another program. Use Ctrl+Alt+Space or the tray icon.", new PixelPoint(10, 10));
            Dispatcher.UIThread.RunJobs();
            double w = chip.Bounds.Width;
            chip.Close();
            return w;
        }, CancellationToken.None);

        Assert.InRange(width, 50, 380);
    }

    [Fact]
    public async Task Settings_exposes_every_control_the_docs_promise()
    {
        var names = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.A);
            var win = new SettingsWindow(new Settings(), new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => { });
            win.Show();
            Dispatcher.UIThread.RunJobs();
            var found = new[] { "AppearancePanel", "ConsentPanel", "CaptureCheck", "OpacitySlider", "LensCombo", "TestBtn", "CloseBtn" }
                .Where(n => win.FindControl<Control>(n) is not null).ToList();
            int radios = win.GetVisualDescendants().OfType<RadioButton>().Count();
            win.Close();
            return (found, radios);
        }, CancellationToken.None);

        Assert.Equal(7, names.found.Count);
        Assert.Equal(5 + 3, names.radios);      // five appearances + three consent models
    }

    [Fact]
    public async Task Capture_checkbox_writes_through_to_settings()
    {
        var settings = new Settings { CaptureViaCopy = true };
        await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.A);
            var win = new SettingsWindow(settings, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => { });
            using var home = new EnvScope(AppPaths.HomeEnvVar, Temp.NewDir());     // Apply() saves settings; keep it off real AppData
            win.Show();
            win.FindControl<CheckBox>("CaptureCheck")!.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            win.Close();
            return 0;
        }, CancellationToken.None);

        Assert.False(settings.CaptureViaCopy);
    }
}
