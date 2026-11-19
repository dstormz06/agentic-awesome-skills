using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using AndaCompanion.Models;
using AndaCompanion.Services;
using AndaCompanion.Theming;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>Round 32 in the Windows app's windows: the reference file's line on the card, and its own number of days in Settings.</summary>
[Collection("Global state")]
public class Round32UiTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public Round32UiTests(HeadlessFixture fx) => _fx = fx;
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }

    [Fact]
    public async Task The_reference_line_on_the_card_turns_STALE_in_words_and_by_its_look_and_keeps_step_while_the_card_is_open()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            using var zone = new ZoneScope("America/New_York");
            ThemeManager.Apply(Appearances.Default);
            var saved = new DateTime(2026, 11, 1, 14, 30, 0, DateTimeKind.Utc); var now = saved.AddDays(18); int days = 45;
            var repo = new Data.MockAndaRepository(); var record = repo.Find("216482")!;
            ReferenceInfo Line(AndaRecord _) { var s = Clock.RefSaved(Clock.Ms(saved), Clock.Ms(now), days, Clock.Zone); return new ReferenceInfo(null, "Sep 17, 2025 · Drugs@FDA, file " + s.Text, s.Stale); }
            var w = new CardWindow(new OfflineBriefService(), repo.Find) { CloseWhenDeactivated = false, UtcNow = () => now, Reference = Line };
            try
            {
                w.Show(record, new PixelPoint(40, 40), false); Settle();
                var rld = w.FindControl<TextBlock>("RldText")!; var warn = Application.Current!.Resources["C.Warn"];
                var seen = new List<(string Text, bool Bold, bool Warn)>();
                void Look() { Settle(); seen.Add((rld.Text ?? "", rld.FontWeight == FontWeight.SemiBold, ReferenceEquals(rld.Foreground, warn))); }
                Look();                                                       // 0: 18 days old, fresh
                now = saved.AddDays(19); w.MinutePassed(); Look();            // 1: a day on, nobody told the card
                now = saved.AddDays(46); w.MinutePassed(); Look();            // 2: older than 45 days
                days = 60; w.TimeChanged(); Look();                           // 3: the person chose 60 days in Settings
                return seen;
            }
            finally { w.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.Equal(("Sep 17, 2025 · Drugs@FDA, file saved Nov 1, 2026 (18 days ago)", false, false), r[0]);
        Assert.Equal(("Sep 17, 2025 · Drugs@FDA, file saved Nov 1, 2026 (19 days ago)", false, false), r[1]);
        Assert.Equal(("Sep 17, 2025 · Drugs@FDA, file saved Nov 1, 2026 (46 days ago) · STALE: older than 45 days", true, true), r[2]);
        Assert.Equal(("Sep 17, 2025 · Drugs@FDA, file saved Nov 1, 2026 (46 days ago)", false, false), r[3]);
    }

    [Fact]
    public async Task Settings_offers_the_reference_files_own_days_with_a_name_and_says_what_was_chosen()
    {
        string home = Temp.NewDir();
        var r = await _fx.Session.Dispatch(() =>
        {
            using var env = new EnvScope(AppPaths.HomeEnvVar, home);
            ThemeManager.Apply(Appearances.Default);
            ConnectionStatus.Reset();
            var s = new Settings(); int changed = 0;
            var w = new SettingsWindow(s, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => changed++, null, new QuickReplyService(Path.Combine(Temp.NewDir(), "r.json")));
            try
            {
                w.Show(); Settle();
                w.FindControl<Expander>("SecLook")!.IsExpanded = true; Settle();
                var box = w.FindControl<ComboBox>("RefStaleBox")!; var mine = w.FindControl<ComboBox>("StaleBox")!; var hint = w.FindControl<TextBlock>("TimeHint")!;
                var open = (AutomationProperties.GetName(box), box.Items.Cast<string>().ToArray(), box.SelectedIndex, box.IsEffectivelyVisible, hint.IsVisible);
                box.SelectedIndex = 4; Settle();
                var ninety = (hint.Text, s.RefStaleAfterDays, s.StaleAfterDays, mine.SelectedIndex, changed, File.ReadAllText(Path.Combine(home, "settings.json")).Contains("\"RefStaleAfterDays\": 90"));
                box.SelectedIndex = 0; Settle();
                return (open, ninety, fourteen: (hint.Text, s.RefStaleAfterDays));
            }
            finally { w.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.Equal("Mark the Drugs@FDA reference file as stale when it is older than", r.open.Item1);
        Assert.Equal(new[] { "14 days", "30 days", "45 days", "60 days", "90 days" }, r.open.Item2);
        Assert.Equal((2, true, false), (r.open.Item3, r.open.Item4, r.open.Item5));
        Assert.Equal(("A Drugs@FDA file older than 90 days is now marked STALE.", 90, 7, 2, 1, true), r.ninety);      // the days for my own data are a separate choice and stay
        Assert.Equal(("A Drugs@FDA file older than 14 days is now marked STALE.", 14), r.fourteen);
    }
}
