using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AndaCompanion.Data;
using AndaCompanion.Models;
using AndaCompanion.Services;
using AndaCompanion.Theming;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>Round 19: the default card of the simulation (T · Pocket Plain) in the Windows app. One answer first, one main action.</summary>
[Collection("Global state")]
public class Round19Tests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public Round19Tests(HeadlessFixture fx) => _fx = fx;
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static AndaRecord Late => new MockAndaRepository().Find("216482")!;
    private static AndaRecord Early => new MockAndaRepository().Find("214990")!;

    private async Task<T> Inspect<T>(AndaRecord record, bool mini, Func<CardWindow, T> read, AppearanceId id = AppearanceId.T, double opacity = 1.0)
    {
        return await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(id, opacity);
            var w = new CardWindow(new OfflineBriefService(), new MockAndaRepository().Find) { CloseWhenDeactivated = false, Today = () => new DateOnly(2026, 7, 16) };
            try
            {
                w.Show(record, new PixelPoint(40, 40), mini); Settle();
                return read(w);
            }
            finally { w.Close(); Settle(); ThemeManager.Apply(Appearances.Default); }
        }, CancellationToken.None);
    }

    private static List<string> Texts(CardWindow w) => w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToList();

    [Fact]
    public void T_is_appended_and_is_the_default_in_both_editions()
    {
        Assert.Equal(AppearanceId.T, Appearances.Default);
        Assert.Equal(CardLayout.Pocket, Appearances.T.Look.Layout);
        Assert.False(Appearances.T.IsCompact);
        Assert.Equal(AppearanceId.T, Edition.Appearance(new Settings()));                                              // nothing chosen: the default look
        Assert.Equal(AppearanceId.K, Edition.Appearance(new Settings { ShowAllTools = true, Appearance = AppearanceId.K }));   // someone who chose K keeps K
        Assert.Equal(AppearanceId.K, Edition.Appearance(new Settings { Appearance = AppearanceId.K }));                        // in the focused edition too
    }

    [Fact]
    public async Task The_card_leads_with_the_gap_and_never_shows_the_figure_alone()
    {
        var late = await Inspect(Late, false, Texts);
        Assert.Contains("14", late);
        Assert.Contains("days later than the goal", late);
        Assert.Contains("Goal Oct 3, 2026 · Forecast Oct 17, 2026 · moderate confidence", late);
        Assert.DoesNotContain("GOAL", late);                 // the two-row clock is replaced by the answer, not shown twice

        var early = await Inspect(Early, false, Texts);
        Assert.Contains("9", early);
        Assert.Contains("days ahead of the goal", early);
        Assert.Contains("Goal Aug 15, 2026 · Forecast Aug 6, 2026 · high confidence", early);
    }

    [Fact]
    public async Task The_figure_is_amber_when_late_and_green_when_early()
    {
        static Color Ink(CardWindow w) => ((ISolidColorBrush)w.GetVisualDescendants().OfType<TextBlock>().First(t => t.FontSize == 30).Foreground!).Color;
        Assert.Equal(Color.Parse(Appearances.T.Palette.Warn), await Inspect(Late, false, Ink));
        Assert.Equal(Color.Parse(Appearances.T.Palette.Good), await Inspect(Early, false, Ink));
    }

    [Fact]
    public async Task A_record_without_a_forecast_shows_the_plain_goal_row_and_no_made_up_gap()
    {
        var bare = Late with { ForecastDate = "", ForecastDelta = "", ForecastDeltaDays = null, ForecastConfidence = "", ForecastLate = false };
        var t = await Inspect(bare, false, Texts);
        Assert.DoesNotContain("days later than the goal", t);
        Assert.Contains("GOAL", t);
        Assert.Contains("Oct 3, 2026", t);
    }

    [Fact]
    public async Task Copy_status_is_the_one_filled_action_outside_the_scrolling_part()
    {
        var r = await Inspect(Late, false, w =>
        {
            var copy = w.FindControl<Button>("CopyChip")!;
            var body = w.FindControl<ScrollViewer>("Body")!;
            bool insideScroll = copy.GetVisualAncestors().Contains(body);
            var others = new[] { "RepliesChip", "SisterChip", "NotesChip", "OpenChip" }.Select(n => w.FindControl<Button>(n)!).ToList();
            return (insideScroll, Fill: ((ISolidColorBrush)copy.Background!).Color, copy.MinHeight, copy.IsEffectivelyVisible,
                    PlainOthers: others.All(b => b.BorderThickness == new Thickness(0)), Label: string.Join(" ", copy.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)));
        });
        Assert.False(r.insideScroll);                                             // it stays put while the facts scroll
        Assert.Equal(Color.Parse(Appearances.T.Palette.Accent), r.Fill);
        Assert.True(r.MinHeight >= 44);                                           // a comfortable target
        Assert.True(r.IsEffectivelyVisible);
        Assert.True(r.PlainOthers);
        Assert.Equal("Copy status Enter", r.Label);
    }

    [Fact]
    public async Task On_a_short_screen_the_facts_scroll_and_the_action_stays_in_view()
    {
        // Windows run 36: on a 1024 x 768 screen the card was 753 px tall in a 720 px work area. The limit now belongs to the card as a whole.
        var r = await Inspect(Late, false, w =>
        {
            var dock = w.GetVisualDescendants().OfType<DockPanel>().First(d => d.Name == "PocketDock");
            var body = w.FindControl<ScrollViewer>("Body")!;
            var chips = w.FindControl<StackPanel>("ChipsSlot")!;
            bool limited = !double.IsInfinity(dock.MaxHeight) && double.IsInfinity(body.MaxHeight);
            dock.MaxHeight = 420;                       // a very short screen
            Settle(); Settle();
            return (limited, DockH: dock.Bounds.Height, ChipsH: chips.Bounds.Height, BodyH: body.Bounds.Height, Scrolls: body.Extent.Height > body.Viewport.Height + 1,
                    CopyVisible: w.FindControl<Button>("CopyChip")!.IsEffectivelyVisible, ChipsBottom: chips.Bounds.Bottom);
        });
        Assert.True(r.limited);
        Assert.True(r.DockH <= 420.5, $"the card is {r.DockH} high");
        Assert.True(r.ChipsH >= 60 && r.CopyVisible, "the action area keeps its full height");
        Assert.True(r.BodyH > 100 && r.Scrolls, $"the facts scroll in the rest ({r.BodyH})");
        Assert.True(r.ChipsBottom <= r.DockH + 0.5);
    }

    [Fact]
    public async Task Enter_copies_the_status_line_and_says_so()
    {
        var label = await Inspect(Late, false, w =>
        {
            w.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Settle();
            return string.Join(" ", w.FindControl<Button>("CopyChip")!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        });
        Assert.StartsWith("Copied ✓", label);
    }

    [Fact]
    public async Task A_peek_keeps_the_answer_and_hides_the_action_until_the_full_card()
    {
        var r = await Inspect(Late, true, w => (Texts: Texts(w), Copy: w.FindControl<Button>("CopyChip")!.IsEffectivelyVisible, More: w.FindControl<Button>("MoreBtn")!.IsEffectivelyVisible));
        Assert.Contains("days later than the goal", r.Texts);
        Assert.False(r.Copy);
        Assert.True(r.More);
    }

    [Fact]
    public async Task Every_fact_of_the_standard_card_is_still_on_the_default_card()
    {
        var t = string.Join("\n", await Inspect(Late, false, Texts));
        foreach (var fact in new[] { "ANDA 216482", "Metformin HCl ER Tablets, 500 mg", "IR response due in 5 days", "Amendment (minor) received Jun 30", "Oct 3, 2026", "Oct 17, 2026", "moderate confidence" })
            Assert.Contains(fact, t);
    }

    [Fact]
    public async Task The_verified_seal_is_quiet_so_the_answer_is_the_only_color()
    {
        var r = await Inspect(Late, false, w =>
        {
            var seal = w.FindControl<Border>("Seal")!;
            return (Alpha: seal.Background is ISolidColorBrush b ? b.Color.A : (byte)255, Ink: ((ISolidColorBrush)w.FindControl<TextBlock>("SealText")!.Foreground!).Color,
                    Info: $"background {seal.Background}; look {ThemeManager.Current.Id}; seal style {ThemeManager.Current.Look.Seal}; border {seal.BorderThickness}");
        });
        Assert.True(r.Alpha == 0, r.Info);
        Assert.Equal(Color.Parse(Appearances.T.Palette.Ink2), r.Ink);
    }

    [Fact]
    public async Task The_earlier_default_K_is_unchanged()
    {
        var t = await Inspect(Late, false, Texts, AppearanceId.K);
        Assert.Contains("GOAL", t);
        Assert.Contains("FORECAST", t);
        Assert.DoesNotContain("days later than the goal", t);
    }

    [Fact]
    public void The_tray_dot_is_only_for_what_needs_the_person_today_and_its_tooltip_never_names_an_ANDA()
    {
        // Round 22: the dot used to follow the card's "urgent" rule and was on all the time. The sample data never raises it now.
        var repo = new MockAndaRepository();
        var today = new DateOnly(2026, 7, 16);
        Assert.Equal(0, TrayState.AlertCount(repo, today));
        Assert.Equal(0, TrayState.AlertCount(repo, new DateOnly(2030, 1, 1)));       // even with every sample goal date long past

        var real = Late with { Demo = false };                                         // at risk, forecast 14 days late, item due in 5 days: not today's business
        Assert.True(UrgencyPolicy.IsUrgent(real, today, new BusinessCalendar(), UrgencyPolicy.DefaultLateDays));
        Assert.False(TrayState.NeedsAttentionToday(real, today));
        Assert.True(TrayState.NeedsAttentionToday(real with { GoalDateValue = today }, today));                       // the goal date is today
        Assert.True(TrayState.NeedsAttentionToday(real with { GoalDateValue = today.AddDays(-3) }, today));           // or has passed
        Assert.False(TrayState.NeedsAttentionToday(real with { GoalDateValue = today.AddDays(1) }, today));
        foreach (string text in new[] { "IR response due today — Biopharmaceutics", "Labeling comments overdue", "Response was due 2 days ago", "PAST DUE: facility question" })
            Assert.True(TrayState.NeedsAttentionToday(real with { Attention = new AttentionItem(AttentionLevel.Warn, text) }, today), text);
        foreach (string text in new[] { "IR response due in 5 days — Biopharmaceutics", "Due date to be set", "Overdueness is not a word" })
            Assert.False(TrayState.NeedsAttentionToday(real with { Attention = new AttentionItem(AttentionLevel.Warn, text) }, today), text);
        Assert.False(TrayState.NeedsAttentionToday(real with { Demo = true, GoalDateValue = today }, today));         // a sample record never raises it

        Assert.Equal("ANDA Companion (Notula) · OFFLINE", TrayState.Tip("OFFLINE", 0));
        Assert.Equal("ANDA Companion (Notula) · OFFLINE · 1 ANDA needs your attention today", TrayState.Tip("OFFLINE", 1));
        string three = TrayState.Tip("OFFLINE", 3);
        Assert.Equal("ANDA Companion (Notula) · OFFLINE · 3 ANDAs need your attention today", three);
        Assert.DoesNotContain("216482", three);
    }

    [Fact]
    public void The_tray_picture_carries_amber_only_when_something_needs_the_person()
    {
        // Round 22: the icon at rest used to have an amber center, the same amber as the dot, so the tray always looked as if it had news.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "anda-hover-companion") dir = dir.Parent;
        string assets = Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("anda-hover-companion folder not found"), "app", "AndaCompanion", "Assets");
        int rest = AmberPixels(Path.Combine(assets, "tray-32.png")), alert = AmberPixels(Path.Combine(assets, "tray-32-alert.png"));
        Assert.Equal(0, rest);
        Assert.InRange(alert, 12, 120);          // a dot, not a wash of color
    }

    /// <summary>Counts pixels close to the alert amber (#F2A93B) in an 8-bit RGBA, non-interlaced PNG, which is what make-icons.mjs writes.</summary>
    private static int AmberPixels(string path)
    {
        byte[] f = File.ReadAllBytes(path);
        int w = 0, h = 0; using var idat = new MemoryStream();
        for (int p = 8; p + 8 <= f.Length;)
        {
            int len = (f[p] << 24) | (f[p + 1] << 16) | (f[p + 2] << 8) | f[p + 3];
            string type = System.Text.Encoding.ASCII.GetString(f, p + 4, 4);
            if (type == "IHDR")
            {
                w = (f[p + 8] << 24) | (f[p + 9] << 16) | (f[p + 10] << 8) | f[p + 11]; h = (f[p + 12] << 24) | (f[p + 13] << 16) | (f[p + 14] << 8) | f[p + 15];
                Assert.Equal((8, 6, 0), (f[p + 16], f[p + 17], f[p + 20]));      // bit depth 8, RGBA, not interlaced
            }
            else if (type == "IDAT") idat.Write(f, p + 8, len);
            p += 12 + len;
        }
        Assert.Equal((32, 32), (w, h));
        idat.Position = 0;
        using var z = new System.IO.Compression.ZLibStream(idat, System.IO.Compression.CompressionMode.Decompress);
        using var raw = new MemoryStream(); z.CopyTo(raw);
        byte[] d = raw.ToArray(); int stride = w * 4, n = 0;
        byte[] prev = new byte[stride], cur = new byte[stride];
        for (int y = 0; y < h; y++)
        {
            int o = y * (stride + 1); byte filter = d[o];
            for (int x = 0; x < stride; x++)
            {
                int a = x >= 4 ? cur[x - 4] : 0, b = prev[x], c = x >= 4 ? prev[x - 4] : 0, v = d[o + 1 + x];
                int pred = filter switch { 0 => 0, 1 => a, 2 => b, 3 => (a + b) / 2, _ => Paeth(a, b, c) };
                cur[x] = (byte)(v + pred);
            }
            for (int x = 0; x < stride; x += 4)
                if (cur[x + 3] > 200 && Math.Abs(cur[x] - 0xF2) < 24 && Math.Abs(cur[x + 1] - 0xA9) < 24 && Math.Abs(cur[x + 2] - 0x3B) < 40) n++;
            (prev, cur) = (cur, prev);
        }
        return n;
        static int Paeth(int a, int b, int c) { int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c); return pa <= pb && pa <= pc ? a : pb <= pc ? b : c; }
    }

    [Theory]
    [InlineData("CARD TRANSPARENCY", "Card transparency")]
    [InlineData("APPEARANCE · FULL CARD", "Appearance · Full card")]
    [InlineData("MY DATA · YOUR ANDA PORTFOLIO (EXCEL OR CSV)", "My data · Your ANDA portfolio (Excel or CSV)")]
    [InlineData("OUTLOOK (OPTIONAL) · READ-ONLY MAIL ABOUT THE OPEN ANDA", "Outlook (optional) · Read-only mail about the open ANDA")]
    [InlineData("DRUGS@FDA · A FILE ON THIS PC", "Drugs@FDA · A file on this PC")]
    [InlineData("WHAT I ASK THE AI", "What I ask the AI")]
    [InlineData("RLD AND PSG DATES", "RLD and PSG dates")]
    [InlineData("URGENT ANDAs", "Urgent ANDAs")]
    [InlineData("STORED PROMPTS · USED BY ASK, OR ON A SHORTCUT OF THEIR OWN", "Stored prompts · Used by Ask, or on a shortcut of their own")]
    [InlineData("SELECT & PEEK", "Select & Peek")]
    [InlineData("MY AGENTS · ALERT HUB, PATENT ANALYSIS, TRIAGING AGENT AND YOUR OWN", "My agents · Alert Hub, Patent Analysis, Triaging Agent and your own")]
    [InlineData("EDGE EXTENSION · OPTIONAL", "Edge extension · Optional")]
    [InlineData("COMPANION LENS · PROMPT LIBRARY", "Companion Lens · Prompt library")]
    [InlineData("Look and feel", "Look and feel")]                       // already in mixed case: left as it is
    [InlineData("Ask: my stored prompts", "Ask: my stored prompts")]
    public void The_Go_to_list_names_are_in_sentence_case_with_names_and_abbreviations_kept(string label, string shown) =>
        Assert.Equal(shown, SettingsSections.SentenceCase(label));

    [Fact]
    public async Task The_Go_to_list_in_Settings_has_no_name_in_capitals_and_offers_the_looks_in_the_focused_edition()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            var w = new SettingsWindow(new Settings(), new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => { });
            try
            {
                w.Show(); Settle();
                var jump = w.FindControl<ComboBox>("JumpBox")!.ItemsSource!.Cast<string>().ToList();
                w.ExpandAll(); Settle();
                var looks = w.GetVisualDescendants().OfType<RadioButton>().Where(b => b.IsEffectivelyVisible && Appearances.All.Any(look => look.Name == (b.Content as string))).ToList();
                int radios = looks.Count;
                string first = (string)looks[0].Content!;
                bool firstChecked = looks[0].IsChecked == true;
                return (jump, radios, first, firstChecked);
            }
            finally { w.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.Equal(Appearances.All.Count, r.radios);              // every look can be picked without "Show all tools"
        Assert.Equal(Appearances.Get(Appearances.Default).Name, r.first);   // the default look is listed first, and a new install has it chosen
        Assert.True(r.firstChecked);
        Assert.Contains("Look and feel", r.jump);
        Assert.Contains(r.jump, t => t.StartsWith("Appearance"));
        Assert.All(r.jump, t => Assert.True(t.Any(char.IsLower), t));
        // no word of three or more letters is left in capitals unless it is a known name or abbreviation
        var allowed = new HashSet<string> { "ANDA", "AI", "API", "PC", "RLD", "PSG", "FDA", "CSV", "US" };
        foreach (var t in r.jump)
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(t, @"\b[A-Z]{3,}\b"))
                Assert.True(allowed.Contains(m.Value), $"“{m.Value}” in “{t}”");
    }

    /// <summary>A picture for a person to look at. Not run unless ANDA_SHOTS_DIR is set.</summary>
    [ShotFact]
    public async Task Save_pictures_of_the_default_card()
    {
        string dir = Environment.GetEnvironmentVariable("ANDA_SHOTS_DIR")!;
        Directory.CreateDirectory(dir);
        foreach (var (name, rec, mini) in new[] { ("r19-card-t-late", Late, false), ("r19-card-t-early", Early, false), ("r19-card-t-peek", Late, true) })
            await Inspect(rec, mini, w => { Settle(); Settle(); w.CaptureRenderedFrame()?.Save(Path.Combine(dir, name + ".png")); return 0; });
        // the starting transparency (90% since round 20), which the focused edition now uses too
        await Inspect(Late, false, w => { Settle(); Settle(); w.CaptureRenderedFrame()?.Save(Path.Combine(dir, "r19-card-t-late-92.png")); return 0; }, AppearanceId.T, Settings.DefaultCardOpacity);
        await Inspect(Late, false, w => { Settle(); Settle(); w.CaptureRenderedFrame()?.Save(Path.Combine(dir, "r19-card-k-late-92.png")); return 0; }, AppearanceId.K, Settings.DefaultCardOpacity);
    }
}
