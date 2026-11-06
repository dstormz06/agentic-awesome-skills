using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AndaCompanion.Data;
using AndaCompanion.Models;
using AndaCompanion.Services;
using AndaCompanion.Theming;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>The windows of round 10: the connection pill and banner, My API, My writing, the middle-click option, quick replies.</summary>
[Collection("Global state")]
public class Round10UiTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public Round10UiTests(HeadlessFixture fx) => _fx = fx;
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static AndaRecord Demo => new MockAndaRepository().Find("216482")!;

    private sealed class Fake : IChatCompleter
    {
        public string ProviderName => "Fake AI";
        public Task<string> CompleteAsync(string s, string u, int m, CancellationToken ct) => Task.FromResult("x");
    }

    private static bool IsFilled(Avalonia.Controls.Shapes.Ellipse e) => e.Fill is Avalonia.Media.ISolidColorBrush { Color.A: > 0 };
    private static string? PillText(CardWindow w) => w.FindControl<TextBlock>("ConnText")!.Text;

    [Fact]
    public async Task The_card_says_plainly_whether_an_API_is_connected_and_follows_the_real_state()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            ConnectionStatus.Configure(null);
            var w = new CardWindow(new OfflineBriefService(), new MockAndaRepository().Find) { CloseWhenDeactivated = false, Today = () => new DateOnly(2026, 11, 2) };
            w.Show(Demo, new PixelPoint(40, 40), false); Settle();
            var offline = (PillText(w), !IsFilled(w.FindControl<Avalonia.Controls.Shapes.Ellipse>("ConnDot")!));

            ConnectionStatus.Configure(new Fake()); Settle();
            var setUp = PillText(w);
            ConnectionStatus.Success("Fake AI"); Settle();
            var connected = (PillText(w), IsFilled(w.FindControl<Avalonia.Controls.Shapes.Ellipse>("ConnDot")!));
            ConnectionStatus.Failure("Fake AI", "HttpRequestException"); Settle();
            var failed = PillText(w);
            w.Close(); Settle();
            ConnectionStatus.Configure(null);
            return (offline, setUp, connected, failed);
        }, CancellationToken.None);
        Assert.Equal("OFFLINE · no API connected", r.offline.Item1);
        Assert.True(r.offline.Item2);                                            // a hollow dot: not connected
        Assert.Contains("set up, not tested", r.setUp);
        Assert.StartsWith("CONNECTED · Fake AI", r.connected.Item1);
        Assert.True(r.connected.Item2);                                           // filled only when connected
        Assert.Contains("NOT REACHABLE", r.failed);
    }

    [Fact]
    public async Task Quick_replies_show_only_when_the_person_has_some()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            bool Visible(Func<IReadOnlyList<QuickReply>>? replies)
            {
                var w = new CardWindow(new OfflineBriefService(), new MockAndaRepository().Find) { CloseWhenDeactivated = false, QuickReplies = replies };
                w.Show(Demo, new PixelPoint(40, 40), false); Settle();
                bool v = w.FindControl<Button>("RepliesChip")!.IsVisible;
                w.Close(); Settle();
                return v;
            }
            return (Visible(null), Visible(() => Array.Empty<QuickReply>()), Visible(() => QuickReplyService.Starters));
        }, CancellationToken.None);
        Assert.False(r.Item1); Assert.False(r.Item2); Assert.True(r.Item3);
    }

    [Fact]
    public async Task Settings_shows_the_connection_banner_my_API_my_writing_and_an_off_by_default_middle_click()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            ConnectionStatus.Configure(null);
            var s = new Settings { ShowAllTools = true };
            var w = new SettingsWindow(s, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => { }, replies: new QuickReplyService(Path.Combine(Temp.NewDir(), "r.json")));
            w.Show(); Settle();
            T Find<T>(string n) where T : Control => w.FindControl<T>(n)!;
            var banner = (Find<TextBlock>("ConnBannerLabel").Text, Find<TextBlock>("ConnBannerText").Text);
            var middleOff = Find<CheckBox>("MiddleCheck").IsChecked;
            var mods = Find<WrapPanel>("MiddleModPanel").Children.OfType<RadioButton>().Select(b => (b.Content?.ToString(), b.IsChecked)).ToList();
            var providers = Find<WrapPanel>("ProviderPanel").Children.OfType<RadioButton>().Select(b => b.Content?.ToString()).ToList();
            var tones = Find<WrapPanel>("TonePanel").Children.OfType<RadioButton>().Select(b => b.Content?.ToString()).ToList();
            var replies = Find<TextBlock>("RepliesSummary").Text;
            bool apiOpenAtFirst = Find<Expander>("MyApiExpander").IsExpanded;

            // choose My API and type an address that is not safe: the panel says why, nothing is sent
            Find<WrapPanel>("ProviderPanel").Children.OfType<RadioButton>().Single(b => (string?)b.Content == "My API").IsChecked = true; Settle();
            Find<TextBox>("ApiEndpoint").Text = "http://insecure.example/v1"; 
            Find<TextBox>("ApiEndpoint").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Input.InputElement.LostFocusEvent)); Settle();
            var status = Find<TextBlock>("ConnStatus").Text;
            bool apiOpenNow = Find<Expander>("MyApiExpander").IsExpanded;

            // turn the middle-click on and pick Shift
            Find<CheckBox>("MiddleCheck").IsChecked = true; Settle();
            Find<WrapPanel>("MiddleModPanel").Children.OfType<RadioButton>().Single(b => (string?)b.Content == "Shift").IsChecked = true; Settle();
            w.Close(); Settle();
            return (banner, middleOff, mods, providers, tones, replies, apiOpenAtFirst, status, apiOpenNow, s);
        }, CancellationToken.None);
        Assert.Equal("OFFLINE · no API connected", r.banner.Item1);
        Assert.Contains("nothing is sent anywhere", r.banner.Item2);
        Assert.False(r.middleOff);
        Assert.Equal(new[] { "no key", "Ctrl", "Shift", "Alt" }, r.mods.Select(m => m.Item1).ToArray());
        Assert.True(r.mods.Single(m => m.Item1 == "Ctrl").Item2);                 // Ctrl recommended and preselected
        Assert.Equal(new[] { "Auto", "Offline only", "Claude", "My API" }, r.providers.ToArray());
        Assert.Equal(new[] { "No tone preference", "Plain", "Formal", "Warm", "Direct" }, r.tones.ToArray());
        Assert.Equal($"{QuickReplyService.Starters.Count} quick replies", r.replies);
        Assert.False(r.apiOpenAtFirst);
        Assert.Equal(ProviderChoice.MyApi, r.s.Provider);
        Assert.True(r.apiOpenNow);
        Assert.Contains("https://", r.status);                                     // the unsafe address is refused in words
        Assert.True(r.s.MiddleClickTrigger);
        Assert.Equal("Shift", r.s.MiddleClickModifier);
    }

    [Fact]
    public void The_hotkey_report_names_a_shortcut_another_program_owns()
    {
        var taken = new HotkeyBinding(2, "Lookup box", "Ctrl+Alt+Space", 0, 0x20, "");
        var ok = new HotkeyBinding(1, "Select & Peek", "Ctrl+Alt+A", 0, 0x41, "");
        string text = HotkeyService.Report(new[] { new HotkeyStatus(ok, true, 0), new HotkeyStatus(taken, false, 1409) });
        Assert.Contains("✓ Select & Peek: Ctrl+Alt+A is active", text);
        Assert.Contains("✗ Lookup box: Ctrl+Alt+Space is already used by another program", text);
        Assert.Contains("the tray icon always works", text);
        Assert.Contains("could not be registered (Windows error 5)", HotkeyService.Report(new[] { new HotkeyStatus(taken, false, 5) }));
    }
}
