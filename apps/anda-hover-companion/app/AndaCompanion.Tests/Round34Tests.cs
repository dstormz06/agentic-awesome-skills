using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AndaCompanion.Data;
using AndaCompanion.Models;
using AndaCompanion.Services;
using AndaCompanion.Theming;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// Round 34, the three changes the owner chose on Nov 20, 2026: the person's own shortcut opens the card directly (Whisper stays a choice,
/// and a middle-click still asks first); the card shows the answer first and the rest is one press away; a click on the tray icon opens
/// Today, which also leads to the watch list. The simulation wrote mockup/round34-golden.json (tools/round34-golden.cjs); the app must give
/// the same answers. All data here is fictional.
/// </summary>
[Collection("Global state")]
public class Round34Tests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public Round34Tests(HeadlessFixture fx) => _fx = fx;

    private static string Folder(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "anda-hover-companion") dir = dir.Parent;
        return Path.Combine(new[] { dir?.FullName ?? throw new DirectoryNotFoundException("anda-hover-companion folder not found") }.Concat(parts).ToArray());
    }
    private static readonly Lazy<JsonElement> Doc = new(() => JsonDocument.Parse(File.ReadAllText(Folder("mockup", "round34-golden.json"))).RootElement.Clone());
    private static JsonElement G(string name) => Doc.Value.GetProperty(name);
    private static ConsentModel Model(string id) => id switch { "full" => ConsentModel.Full, "peek" => ConsentModel.PeekFirst, "whisper" => ConsentModel.Whisper, _ => throw new ArgumentException(id) };
    private static string Id(ConsentModel m) => m switch { ConsentModel.Full => "full", ConsentModel.PeekFirst => "peek", _ => "whisper" };

    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    private static void Click(Button b) { b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(); }
    private static bool Shown(CardWindow w, string name) => w.FindControl<Control>(name)!.IsEffectivelyVisible;

    // ---------- the shared answers ----------
    [Fact]
    public void The_usual_way_to_start_is_Full_and_Settings_names_the_three_ways_as_the_simulation_does()
    {
        Assert.Equal(G("defaultConsent").GetString(), Id(new Settings().Consent));
        var want = G("consents").EnumerateArray().Select(c => (c[0].GetString()!, c[1].GetString()!)).ToList();
        Assert.Equal(want, SettingsWindow.ConsentLabels.Select(c => (Id(c.Model), c.Label)).ToList());
    }

    [Fact]
    public void How_a_card_starts_matches_the_shared_answers_for_every_way_to_start()
    {
        var steps = G("steps").EnumerateArray().ToList();
        Assert.Equal(24, steps.Count);
        foreach (var s in steps)
        {
            var got = UrgencyPolicy.Decide(Model(s.GetProperty("consent").GetString()!), s.GetProperty("escalate").GetBoolean(), s.GetProperty("urgent").GetBoolean(),
                middleClick: s.GetProperty("via").GetString() == "middle");
            Assert.True(got.ToString().ToLowerInvariant() == s.GetProperty("step").GetString(), s.GetRawText() + " gave " + got);
        }
    }

    [Fact]
    public void The_watch_line_tail_the_version_and_what_changed_are_the_simulations()
    {
        foreach (var w in G("watchMore").EnumerateArray()) Assert.Equal(w.GetProperty("text").GetString(), CardWindow.WatchMoreText(w.GetProperty("n").GetInt32()));
        Assert.Equal(G("version").GetString(), ChangeLog.Version);
        var changes = G("changes");
        Assert.Equal(changes.GetProperty("round").GetInt32(), ChangeLog.Entries[0].Round);
        Assert.Equal(changes.GetProperty("items").EnumerateArray().Select(x => x.GetString()!).ToList(), ChangeLog.Entries[0].Items.ToList());
    }

    // ---------- settings move once ----------
    [Fact]
    public void Settings_kept_from_before_round_34_move_from_Whisper_to_Full_once_and_say_so()
    {
        string path = Path.Combine(Temp.NewDir(), "settings.json");
        File.WriteAllText(path, "{\"SchemaVersion\":5,\"Consent\":2,\"Tone\":\"Warm\"}");                // Whisper (2), written by round 33
        var s = SettingsService.Load(path);
        Assert.Equal((ConsentModel.Full, 6, "Warm"), (s.Consent, s.SchemaVersion, s.Tone));
        Assert.Equal(SettingsService.ConsentMovedNote, SettingsService.LoadNote);
        Assert.Contains("choose Whisper in Settings, How it starts", SettingsService.ConsentMovedNote);

        s.Consent = ConsentModel.Whisper; Assert.True(SettingsService.Save(s, path));                     // chosen after the move: kept, and nothing is said
        var again = SettingsService.Load(path);
        Assert.Equal(ConsentModel.Whisper, again.Consent); Assert.Null(SettingsService.LoadNote);

        File.WriteAllText(path, "{\"SchemaVersion\":5,\"Consent\":1}");                                   // Peek first was a choice: never changed
        Assert.Equal(ConsentModel.PeekFirst, SettingsService.Load(path).Consent); Assert.Null(SettingsService.LoadNote);
        File.WriteAllText(path, "{\"SchemaVersion\":0,\"Consent\":2,\"Provider\":0}");                     // from the start: every step, and both sentences
        var old = SettingsService.Load(path);
        Assert.Equal(ConsentModel.Full, old.Consent);
        Assert.Contains(SettingsService.ConsentMovedNote, SettingsService.LoadNote);
        Assert.Contains("The AI connection is now off", SettingsService.LoadNote);
    }

    // ---------- the answer-first card ----------
    private static (CardWindow w, MockAndaRepository repo) NewCard(ICompanionBriefService? briefs = null)
    {
        ThemeManager.Apply(Appearances.Default);
        var repo = new MockAndaRepository();
        var w = new CardWindow(briefs ?? new OfflineBriefService(), repo.Find) { CloseWhenDeactivated = false, Today = () => new DateOnly(2026, 11, 2) };
        return (w, repo);
    }
    private static readonly string[] MoreParts = { "LatestSlot", "RefSlot", "BriefSlot", "ActHint" };

    [Fact]
    public async Task The_card_opens_with_the_answer_and_More_or_E_opens_the_rest()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            var (w, repo) = NewCard();
            try
            {
                w.Show(repo.Find("216482")!, new PixelPoint(40, 40), false); Settle();
                var hidden = MoreParts.Where(n => Shown(w, n)).ToList();
                bool ribbonHidden = !w.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "Ribbon").IsEffectivelyVisible;
                var answer = new[] { "WatchSlot", "ChipsSlot", "CopyChip", "NotesChip", "MoreChip" }.Where(n => !Shown(w, n)).ToList();
                string watch = w.FindControl<TextBlock>("WatchText")!.Text ?? "";
                bool answerOnly = w.AnswerOnly;
                Click(w.FindControl<Button>("MoreChip")!);
                var shown = MoreParts.Where(n => !Shown(w, n)).ToList();
                bool ribbonShown = w.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "Ribbon").IsEffectivelyVisible;
                bool moreGone = !Shown(w, "MoreChip"), drawer = Shown(w, "Drawer");
                var focused = TopLevel.GetTopLevel(w)!.FocusManager!.GetFocusedElement() as Control;
                w.KeyPress(Key.E, RawInputModifiers.None, PhysicalKey.E, "e"); Settle();
                bool folded = w.AnswerOnly && Shown(w, "MoreChip") && !Shown(w, "LatestSlot");
                return (hidden, ribbonHidden, answer, watch, answerOnly, shown, ribbonShown, moreGone, drawer, focus: focused?.Name, folded);
            }
            finally { w.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.Empty(r.hidden);                     // the calendar, Latest, Ahead, the reference dates, the brief and the key hint wait under More
        Assert.True(r.ribbonHidden);
        Assert.Empty(r.answer);                     // the watch line and the actions, with More, are there
        Assert.EndsWith("(+1 more)", r.watch);
        Assert.True(r.answerOnly);
        Assert.Empty(r.shown); Assert.True(r.ribbonShown && r.moreGone && r.drawer);
        Assert.Equal("ExpBtn", r.focus);           // More hides itself, so the keyboard goes to the button that closes it again
        Assert.True(r.folded);                      // E folds it back
    }

    private sealed class CountingBriefs : ICompanionBriefService
    {
        public int Calls;
        public Task<CompanionBrief> GetBriefAsync(AndaRecord record, CancellationToken ct = default)
        { Calls++; return Task.FromResult(new CompanionBrief("A generated brief (fictional).", Array.Empty<string>(), Generated: true)); }
    }

    [Fact]
    public async Task With_an_AI_connection_on_nothing_is_asked_until_More_and_the_offline_brief_is_made_at_once()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            var briefs = new CountingBriefs();
            var (w, repo) = NewCard(briefs);
            var (o, _) = NewCard();
            try
            {
                w.Show(repo.Find("216482")!, new PixelPoint(40, 40), false); Settle();
                int before = briefs.Calls; bool waiting = w.BriefWaiting; string verify = w.FindControl<TextBlock>("BriefVerify")!.Text ?? "";
                Click(w.FindControl<Button>("MoreChip")!); for (int i = 0; i < 5; i++) Settle();
                int after = briefs.Calls; bool waitingAfter = w.BriefWaiting; string text = w.FindControl<TextBlock>("BriefText")!.Text ?? "";
                o.Show(repo.Find("216482")!, new PixelPoint(40, 40), false); Settle();
                bool offlineMade = !o.BriefWaiting && (o.FindControl<TextBlock>("BriefText")!.Text ?? "").Length > 20;
                return (before, waiting, verify, after, waitingAfter, text, offlineMade);
            }
            finally { w.Close(); o.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.Equal(0, r.before); Assert.True(r.waiting); Assert.Equal(CardWindow.BriefWaits, r.verify);
        Assert.Equal("asked for when you press More", CardWindow.BriefWaits);       // the simulation's words
        Assert.Equal(1, r.after); Assert.False(r.waitingAfter); Assert.Equal("A generated brief (fictional).", r.text);
        Assert.True(r.offlineMade);
    }

    [Fact]
    public async Task A_peeks_Full_brief_opens_the_card_with_the_brief_in_view()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            var (w, repo) = NewCard();
            try
            {
                w.Show(repo.Find("214990")!, new PixelPoint(40, 40), true); Settle();
                bool peek = Shown(w, "MoreBtn") && !Shown(w, "MoreChip");
                Click(w.FindControl<Button>("MoreBtn")!);
                return (peek, brief: Shown(w, "BriefSlot"), drawer: Shown(w, "Drawer"), more: Shown(w, "MoreChip"));
            }
            finally { w.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.True(r.peek); Assert.True(r.brief && r.drawer); Assert.False(r.more);
    }

    [Fact]
    public async Task The_answer_first_card_fits_a_1366_by_768_laptop_and_the_note_count_is_still_said()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            var (w, repo) = NewCard();
            try
            {
                w.Show(repo.Find("216482")!, new PixelPoint(40, 40), false); Settle();
                var root = w.FindControl<Border>("Root")!;
                double collapsed = root.Bounds.Height;
                var notes = w.FindControl<Button>("NotesChip")!;
                string label = string.Join(" ", notes.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)), said = AutomationProperties.GetName(notes) ?? "";
                Click(w.FindControl<Button>("MoreChip")!);
                double open = root.Bounds.Height;
                string labelOpen = string.Join(" ", notes.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
                return (collapsed, open, label, said, labelOpen);
            }
            finally { w.Close(); Settle(); }
        }, CancellationToken.None);
        // 768 px less the taskbar leaves about 728 px; the card's window adds 52 px of shadow margin.
        Assert.True(r.collapsed <= 728 - 52, $"the collapsed card is {r.collapsed:0} px tall");
        Assert.True(r.open > r.collapsed, $"open {r.open:0} px, collapsed {r.collapsed:0} px");
        Assert.Equal("Add a note Enter", r.label);
        Assert.StartsWith("Add a note, 3 notes.", r.said);
        Assert.Equal("Add a note · 3 notes Enter", r.labelOpen);
    }

    // ---------- Today is home ----------
    [Fact]
    public async Task Today_offers_Show_all_when_something_needs_a_look_and_it_opens_the_watch_list()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var repo = new MockAndaRepository(); int shown = 0;
            Today.Look? Find(string id) => repo.Find(id) is { } rec ? Round33Tests.Look(rec) : null;
            var all = repo.Numbers.Select(n => Find(n)!).ToList();
            var w = new TodayWindow(new TodayHooks(() => all, Find, () => Array.Empty<string>(), () => Array.Empty<string>(), _ => { }, ShowAll: () => shown++));
            var none = new TodayWindow(new TodayHooks(() => all, Find, () => Array.Empty<string>(), () => Array.Empty<string>(), _ => { }));
            var calm = new TodayWindow(new TodayHooks(() => all.Where(l => !Today.NeedsLook(l)).ToList(), Find, () => Array.Empty<string>(), () => Array.Empty<string>(), _ => { }, ShowAll: () => shown++));
            try
            {
                w.Show(); none.Show(); calm.Show(); Settle();
                bool offered = w.ShowAllButton.IsEffectivelyVisible; string label = (string)w.ShowAllButton.Content!, said = AutomationProperties.GetName(w.ShowAllButton) ?? "";
                Click(w.ShowAllButton);
                return (offered, label, said, shown, withoutHook: none.ShowAllButton.IsVisible, nothingNeeds: calm.ShowAllButton.IsVisible);
            }
            finally { w.Close(); none.Close(); calm.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.True(r.offered); Assert.Equal("Show all, with reasons…", r.label); Assert.Contains("watch list", r.said);
        Assert.Equal(1, r.shown);
        Assert.False(r.withoutHook); Assert.False(r.nothingNeeds);
    }

    [Fact]
    public void The_app_wires_the_tray_click_the_menu_and_the_middle_click_as_the_simulation_does()
    {
        string app = File.ReadAllText(Folder("app", "AndaCompanion", "App.axaml.cs")), more = File.ReadAllText(Folder("app", "AndaCompanion", "App.Round33.cs"));
        Assert.Contains("tray.Clicked += (_, _) => OpenToday();", app);
        Assert.Contains("var today = new NativeMenuItem(\"Today…\");", app);
        Assert.DoesNotContain("new NativeMenuItem(\"Watch list…\")", app);
        Assert.Contains("() => _ = OnHotkeyAsync(HotkeyService.IdPeek, middleClick: true)", app);
        Assert.Contains("UrgencyPolicy.Decide(_settings.Consent, _settings.EscalateUrgent, urgent, middleClick)", app);
        Assert.Contains("ShowAll: OpenWatchList", more);
        Assert.Contains("Click the tray icon near the clock to open Today", string.Join(" ", Guide.Steps.SelectMany(s => s.Lines)));
    }
}
