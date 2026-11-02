using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AndaCompanion.Data;
using AndaCompanion.Models;
using AndaCompanion.Services;
using AndaCompanion.Services.Portfolio;
using AndaCompanion.Theming;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

public class FirstStepTests
{
    private static readonly BusinessCalendar Cal = new();
    private static readonly MockAndaRepository Repo = new();

    [Theory]
    [InlineData(ConsentModel.Whisper, true, true, FirstStep.Peek)]       // urgent in whisper mode: peek by itself
    [InlineData(ConsentModel.Whisper, true, false, FirstStep.Invite)]    // not urgent: the invite
    [InlineData(ConsentModel.Whisper, false, true, FirstStep.Invite)]    // the person turned escalation off
    [InlineData(ConsentModel.PeekFirst, true, true, FirstStep.Peek)]
    [InlineData(ConsentModel.PeekFirst, false, false, FirstStep.Peek)]
    [InlineData(ConsentModel.Full, true, true, FirstStep.Full)]
    [InlineData(ConsentModel.Full, false, false, FirstStep.Full)]
    public void The_first_step_follows_the_consent_model_and_urgency(ConsentModel consent, bool escalate, bool urgent, FirstStep expected) =>
        Assert.Equal(expected, UrgencyPolicy.Decide(consent, escalate, urgent));

    [Fact]
    public void With_the_demo_records_and_the_new_default_the_at_risk_one_peeks_and_the_healthy_one_invites()
    {
        var s = new Settings();
        var risk = Repo.Find("216482")!; var fine = Repo.Find("214990")!;
        var today = new DateOnly(2026, 11, 2);
        Assert.Equal(FirstStep.Peek, UrgencyPolicy.Decide(s.Consent, s.EscalateUrgent, UrgencyPolicy.IsUrgent(risk, today, Cal, s.UrgentLateDays)));
        Assert.Equal(FirstStep.Invite, UrgencyPolicy.Decide(s.Consent, s.EscalateUrgent, UrgencyPolicy.IsUrgent(fine, today, Cal, s.UrgentLateDays)));
    }
}

/// <summary>The new windows and card slots, rendered for real in the headless host.</summary>
[Collection("Global state")]
public class Phase1UiTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    private static readonly MockAndaRepository Repo = new();

    public Phase1UiTests(HeadlessFixture fx) => _fx = fx;

    private sealed class FakeCompleter(string reply) : IChatCompleter
    {
        public string ProviderName => "Fake model";
        public List<string> Users { get; } = new();
        public Task<string> CompleteAsync(string system, string user, int maxTokens, CancellationToken ct) { Users.Add(user); return Task.FromResult(reply); }
    }

    private static Settings TestSettings() => new();
    private static PromptLibraryService Prompts() => new(Path.Combine(Temp.NewDir(), "p.json"));

    /// <summary>Runs queued work and one render tick so no layout is left pending when the isolated app is torn down.</summary>
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }

    private static string[] Texts(Window w) => w.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();

    // ---------- the card ----------

    [Fact]
    public async Task The_card_shows_the_watch_line_for_a_goal_on_a_weekend_and_lists_the_evidence_in_the_drawer()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var w = new CardWindow(new OfflineBriefService(), Repo.Find) { CloseWhenDeactivated = false, Today = () => new DateOnly(2026, 11, 2) };
            w.Show(Repo.Find("216482")!, new PixelPoint(40, 40), false);
            Settle();
            var slot = w.FindControl<Control>("WatchSlot")!;
            var res = (slot.IsVisible, w.FindControl<TextBlock>("WatchText")!.Text, w.FindControl<Control>("WatchExpander")!.IsVisible,
                       w.FindControl<TextBlock>("WatchHeader")!.Text, w.FindControl<StackPanel>("WatchPanel")!.Children.OfType<TextBlock>().Select(t => t.Text ?? "").ToList());
            w.Close();
            return res;
        }, CancellationToken.None);
        Assert.True(r.Item1);
        Assert.Contains("falls on a weekend: Sat Oct 3", r.Item2);
        Assert.Contains("more in the drawer", r.Item2);
        Assert.True(r.Item3);
        Assert.Equal("Watch list · 2", r.Item4);
        Assert.Contains(r.Item5, t => t.StartsWith("Source: U.S. federal calendar (OPM)"));
        Assert.Contains(r.Item5, t => t.Contains("this tool does not decide"));
    }

    [Fact]
    public async Task The_watch_line_is_hidden_in_a_peek_and_when_there_is_nothing_to_watch()
    {
        var (peek, none) = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var w = new CardWindow(new OfflineBriefService(), Repo.Find) { CloseWhenDeactivated = false, Today = () => new DateOnly(2026, 11, 2) };
            w.Show(Repo.Find("216482")!, new PixelPoint(40, 40), true);
            Settle();
            bool a = w.FindControl<Control>("WatchSlot")!.IsVisible; w.Close();

            var weekday = Repo.Find("216482")! with { GoalDateValue = new DateOnly(2026, 9, 22), ForecastDateValue = new DateOnly(2026, 9, 24) };
            var w2 = new CardWindow(new OfflineBriefService(), Repo.Find) { CloseWhenDeactivated = false, Today = () => new DateOnly(2026, 11, 2) };
            w2.Show(weekday, new PixelPoint(40, 40), false);
            Settle();
            bool b = w2.FindControl<Control>("WatchSlot")!.IsVisible || w2.FindControl<Control>("WatchExpander")!.IsVisible;
            w2.Close();
            return (a, b);
        }, CancellationToken.None);
        Assert.False(peek);
        Assert.False(none);
    }

    [Fact]
    public async Task A_record_from_a_sheet_with_no_forecast_or_progress_shows_dashes_not_estimates()
    {
        string path = Path.Combine(Temp.NewDir(), "P.csv");
        File.WriteAllText(path, "ANDA,Product,Goal\n123456,Test drug,2026-12-01\n");
        var r = await _fx.Session.Dispatch(() =>
        {
            var repo = new PortfolioRepository(path, today: () => new DateOnly(2026, 11, 2));
            ThemeManager.Apply(AppearanceId.K);
            var w = new CardWindow(new OfflineBriefService(), repo.Find) { CloseWhenDeactivated = false, Today = () => new DateOnly(2026, 11, 2) };
            w.Show(repo.Find("123456")!, new PixelPoint(40, 40), false);
            Settle();
            var res = (w.FindControl<ProgressControl>("Progress")!.IsVisible, w.FindControl<TextBlock>("PctText")!.Text, w.FindControl<TextBlock>("ForecastText")!.Text,
                       w.FindControl<TextBlock>("ForecastDeltaText")!.Text, w.FindControl<TextBlock>("ForecastConfText")!.IsVisible, w.FindControl<TextBlock>("FreshnessText")!.Text);
            w.Close();
            return res;
        }, CancellationToken.None);
        Assert.False(r.Item1);
        Assert.Equal("", r.Item2);
        Assert.Equal(AndaRecord.Unknown, r.Item3);
        Assert.Equal("no forecast in your sheet", r.Item4);
        Assert.False(r.Item5);
        Assert.Contains("your portfolio file", r.Item6);
    }

    [Fact]
    public async Task The_reference_lines_show_only_what_is_known_and_say_so_when_it_is_not()
    {
        var rec = Repo.Find("216482")! with { PsgDateFromSheet = new DateOnly(2026, 8, 21) };
        var shown = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var w = new CardWindow(new OfflineBriefService(), Repo.Find)
            {
                CloseWhenDeactivated = false, Today = () => new DateOnly(2026, 11, 2),
                Reference = r => new ReferenceInfo(DrugsAtFdaFile.PsgOf(r), "not shown: needs the active ingredient and dosage form in your sheet"),
            };
            w.Show(rec, new PixelPoint(40, 40), false);
            Settle();
            var res = (w.FindControl<Control>("RefSlot")!.IsVisible, w.FindControl<TextBlock>("PsgText")!.Text, w.FindControl<TextBlock>("RldText")!.Text);
            w.Close();
            var w2 = new CardWindow(new OfflineBriefService(), Repo.Find) { CloseWhenDeactivated = false, Today = () => new DateOnly(2026, 11, 2) };
            w2.Show(Repo.Find("216482")!, new PixelPoint(40, 40), false);
            Settle();
            bool hidden = !w2.FindControl<Control>("RefSlot")!.IsVisible;
            w2.Close();
            return (res, hidden);
        }, CancellationToken.None);
        Assert.True(shown.res.Item1);
        Assert.Equal("Aug 21, 2026 · from your sheet, not an official source", shown.res.Item2);
        Assert.StartsWith("not shown:", shown.res.Item3);
        Assert.True(shown.hidden);
    }

    [Theory]
    [InlineData(AppearanceId.K)] [InlineData(AppearanceId.A)] [InlineData(AppearanceId.C)] [InlineData(AppearanceId.H)] [InlineData(AppearanceId.I)] [InlineData(AppearanceId.J)]
    public async Task A_progress_form_squeezed_to_almost_no_width_draws_nothing_instead_of_throwing(AppearanceId id)
    {
        // Regression: the K line's dot clamp inverted on a zero-width control and threw inside the render pass.
        await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(id);
            var bar = new ProgressControl { Value = 40 };
            var w = new Window { Width = 4, Height = 60, Content = new Grid { Width = 4, Children = { bar } } };
            w.Show();
            Settle();
            bar.Value = 0; Settle();
            w.Close(); Settle();
        }, CancellationToken.None);
    }

    // ---------- the draft window ----------

    [Fact]
    public async Task The_draft_window_checks_as_soon_as_it_opens_and_works_offline_with_AI_buttons_off()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var w = new DraftWindow("ANDA 216482 has a goal date of Oct 5 and is on track.", Repo.Find, new WritingService(null), TestSettings(), Prompts());
            w.Show();
            Settle();
            var res = (Texts(w).Where(t => t.Contains("goal date as Oct 5")).ToList(), Texts(w).Any(t => t.Contains("sounds on track")),
                       w.FindControl<Button>("ProofBtn")!.IsEnabled, w.FindControl<Button>("RewriteBtn")!.IsEnabled, w.FindControl<Button>("FactBtn")!.IsEnabled,
                       w.FindControl<TextBlock>("AiNote")!.Text);
            w.Close();
            return res;
        }, CancellationToken.None);
        Assert.Single(r.Item1);
        Assert.True(r.Item2);
        Assert.False(r.Item3); Assert.False(r.Item4); Assert.False(r.Item5);
        Assert.Contains("AI is off", r.Item6);
    }

    [Fact]
    public async Task Proofread_shows_exactly_what_will_be_sent_and_sends_nothing_until_Send_is_pressed()
    {
        var fake = new FakeCompleter("Corrected text.");
        var settings = TestSettings(); settings.StandingInstructions = "Use plain words.";
        var r = await _fx.Session.Dispatch(async () =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var w = new DraftWindow("Teh status of ANDA 216482.", Repo.Find, new WritingService(fake), settings, Prompts());
            w.Show();
            Settle();
            w.FindControl<Button>("ProofBtn")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Settle();
            var preview = w.FindControl<TextBox>("PreviewBox")!.Text;
            int before = fake.Users.Count;
            bool previewVisible = w.FindControl<Control>("PreviewPanel")!.IsVisible;
            w.FindControl<Button>("SendBtn")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 40 && !w.FindControl<Control>("ResultPanel")!.IsVisible || (w.FindControl<TextBox>("ResultBox")!.Text ?? "") == ""; i++) { await Task.Delay(50); Settle(); }
            var res = (preview, before, previewVisible, w.FindControl<TextBox>("ResultBox")!.Text, w.FindControl<TextBlock>("ResultLabel")!.Text, fake.Users.Count);
            w.Close();
            return res;
        }, CancellationToken.None);
        Assert.True(r.previewVisible);
        Assert.Contains("<draft>\nTeh status of ANDA 216482.\n</draft>", r.preview);
        Assert.Contains("Use plain words.", r.preview);
        Assert.Equal(0, r.before);                                  // nothing had been sent when the preview was showing
        Assert.Equal("Corrected text.", r.Item4);
        Assert.Contains("GENERATED, VERIFY BEFORE SENDING", r.Item5);
        Assert.Equal(1, r.Item6);
        Assert.Contains("Teh status", fake.Users[0]);
    }

    [Fact]
    public async Task Cancel_in_the_preview_sends_nothing()
    {
        var fake = new FakeCompleter("x");
        int sent = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var w = new DraftWindow("Hello ANDA 216482.", Repo.Find, new WritingService(fake), TestSettings(), Prompts());
            w.Show(); Settle();
            w.FindControl<Button>("RewriteBtn")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle();
            w.FindControl<Button>("CancelBtn")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle();
            bool hidden = !w.FindControl<Control>("PreviewPanel")!.IsVisible;
            w.Close();
            return hidden ? fake.Users.Count : -1;
        }, CancellationToken.None);
        Assert.Equal(0, sent);
    }

    [Fact]
    public async Task Fact_check_previews_the_matching_record_and_an_empty_draft_is_refused_politely()
    {
        var fake = new FakeCompleter("x");
        var (preview, emptyNote) = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var w = new DraftWindow("ANDA 216482 is late.", Repo.Find, new WritingService(fake), TestSettings(), Prompts());
            w.Show(); Settle();
            w.FindControl<Button>("FactBtn")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle();
            string p = w.FindControl<TextBox>("PreviewBox")!.Text ?? "";
            w.FindControl<TextBox>("DraftBox")!.Text = "";
            Settle();
            w.FindControl<Button>("ProofBtn")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle();
            string n = w.FindControl<TextBlock>("ResultNote")!.Text ?? "";
            w.Close();
            return (p, n);
        }, CancellationToken.None);
        Assert.Contains("<record number=\"216482\">", preview);
        Assert.Contains("Type or paste a draft first", emptyNote);
        Assert.Empty(fake.Users);
    }

    // ---------- settings ----------

    [Fact]
    public async Task Settings_shows_the_portfolio_status_and_a_column_mapping_when_a_file_is_chosen()
    {
        string path = Path.Combine(Temp.NewDir(), "Portfolio.csv");
        File.WriteAllText(path, "ANDA,Product,Goal,Weird Forecast\n123456,Test,2026-12-01,2026-12-10\n");
        var settings = new Settings { PortfolioPath = path };
        var repo = new PortfolioRepository(path, settings.PortfolioColumns, today: () => new DateOnly(2026, 11, 2));
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var w = new SettingsWindow(settings, Prompts(), () => { }, new SettingsHooks(() => "Hotkeys: ok", () => repo, () => ""));
            w.Show(); Settle();
            var combos = w.FindControl<StackPanel>("ColumnsPanel")!.Children.OfType<Grid>().Select(g => g.Children.OfType<ComboBox>().Single()).ToList();
            var res = (w.FindControl<TextBlock>("PortfolioStatus")!.Text, w.FindControl<Expander>("ColumnsExpander")!.IsVisible, combos.Count,
                       w.FindControl<TextBox>("PeekKeyBox")!.Text, w.FindControl<TextBox>("DraftKeyBox")!.Text, w.FindControl<TextBlock>("KeyStatus")!.Text);
            // choose the odd forecast header by hand
            var forecast = combos[PortfolioColumns.Fields.ToList().FindIndex(f => f.Key == "forecast")];
            forecast.SelectedItem = "Weird Forecast"; Settle();
            var saved = settings.PortfolioColumns.GetValueOrDefault("forecast");
            w.Close();
            return (res, saved);
        }, CancellationToken.None);
        Assert.Contains("Loaded 1 ANDA from Portfolio.csv", r.res.Item1);
        Assert.True(r.res.Item2);
        Assert.Equal(PortfolioColumns.Fields.Count, r.res.Item3);
        Assert.Equal("Ctrl+Alt+A", r.res.Item4); Assert.Equal("Ctrl+Alt+D", r.res.Item5);
        Assert.Equal("Hotkeys: ok", r.res.Item6);
        Assert.Equal("Weird Forecast", r.saved);
    }

    [Fact]
    public void Key_presses_become_shortcuts_and_modifier_only_presses_are_ignored()
    {
        Assert.Equal("Ctrl+Alt+D", HotkeyCapture.FromKey(Avalonia.Input.Key.D, Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Alt));
        Assert.Equal("Ctrl+Shift+F9", HotkeyCapture.FromKey(Avalonia.Input.Key.F9, Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift));
        Assert.Equal("Ctrl+Alt+Space", HotkeyCapture.FromKey(Avalonia.Input.Key.Space, Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Alt));
        Assert.Equal("Ctrl+Alt+7", HotkeyCapture.FromKey(Avalonia.Input.Key.D7, Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Alt));
        Assert.Null(HotkeyCapture.FromKey(Avalonia.Input.Key.LeftCtrl, Avalonia.Input.KeyModifiers.Control));
        Assert.Null(HotkeyCapture.FromKey(Avalonia.Input.Key.Escape, Avalonia.Input.KeyModifiers.None));
    }
}
