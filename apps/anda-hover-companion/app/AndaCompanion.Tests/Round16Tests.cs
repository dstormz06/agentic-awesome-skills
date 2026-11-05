using System.Text.Json;
using AndaCompanion.Services;
using AndaCompanion.Theming;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>Round 16, without windows: the checklist shortcut and mode survive a hand-edited settings file or backup, and the shortcut is registered like the others.</summary>
[Collection("Global state")]
public class Round16Tests
{
    [Theory]
    [InlineData("", "")]                                           // none stays none
    [InlineData("ctrl+alt+k", "Ctrl+Alt+K")]                       // read and written in the usual form
    [InlineData("<img src=x onerror=1>", "Ctrl+Alt+L")]            // not a shortcut: the default
    [InlineData("Ctrl+L", "Ctrl+Alt+L")]                           // one modifier is not enough: the default
    [InlineData("Ctrl+Alt+A", "")]                                 // the same as Select & Peek: none, never two features on one key
    [InlineData("Ctrl+Alt+Space", "")]
    [InlineData("Ctrl+Alt+D", "")]
    public void The_checklist_shortcut_is_repaired_when_a_saved_file_is_read(string saved, string expected)
    {
        var s = SettingsService.Normalize(new Settings { ChecklistHotkey = saved });
        Assert.Equal(expected, s.ChecklistHotkey);
    }

    [Fact]
    public void A_null_shortcut_and_an_unknown_mode_are_repaired()
    {
        var s = SettingsService.Normalize(new Settings { ChecklistHotkey = null!, ChecklistMode = (ChecklistMode)42 });
        Assert.Equal("Ctrl+Alt+L", s.ChecklistHotkey);            // an unreadable value is the default; only an empty text means "none"
        Assert.Equal(ChecklistMode.Review, s.ChecklistMode);
    }

    [Fact]
    public void A_hand_edited_backup_cannot_set_a_clashing_shortcut_or_an_odd_mode_and_keeps_local_history_out()
    {
        var dir = Temp.NewDir();
        var lenses = new PromptLibraryService(Path.Combine(dir, "p.json")); var replies = new QuickReplyService(Path.Combine(dir, "r.json"));
        var source = new Settings();
        string json = SettingsBackup.Export(source, lenses, replies);
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        node["Settings"]!["ChecklistHotkey"] = "Ctrl+Alt+A";
        node["Settings"]!["ChecklistMode"] = 9;
        node["Settings"]!["SectionUse"] = new System.Text.Json.Nodes.JsonObject { ["Look"] = 9999, ["<b>x</b>"] = 3 };
        var target = new Settings { SectionUse = new() { ["Dates"] = 2 }, GettingStartedDone = true };
        var (ok, _) = SettingsBackup.Import(node.ToJsonString(), target, lenses, replies);
        Assert.True(ok);
        Assert.Equal("", target.ChecklistHotkey);                  // it clashed with Select & Peek
        Assert.Equal(ChecklistMode.Review, target.ChecklistMode);
        Assert.Equal(new[] { "Dates" }, target.SectionUse.Keys);   // a file from another PC cannot plant or change what this PC learned
        Assert.True(target.GettingStartedDone);
    }

    [Fact]
    public void The_checklist_shortcut_is_registered_after_the_three_and_before_the_custom_actions_and_never_twice()
    {
        var actions = new List<CustomAction>
        {
            new() { Name = "Mine", Kind = ActionKind.AddToTodo, Template = "x", Hotkey = "Ctrl+Alt+L", Enabled = true },     // the same keys as the checklist
            new() { Name = "Other", Kind = ActionKind.AddToTodo, Template = "x", Hotkey = "Ctrl+Alt+J", Enabled = true },
        };
        var bindings = HotkeyService.FromCombos("Ctrl+Alt+A", "Ctrl+Alt+Space", "Ctrl+Alt+D", actions, "Ctrl+Alt+L");
        Assert.Equal(new[] { HotkeyService.IdPeek, HotkeyService.IdLookup, HotkeyService.IdDraft, HotkeyService.IdChecklist, HotkeyService.IdActionBase + 1 }, bindings.Select(b => b.Id));
        Assert.Equal(bindings.Count, bindings.Select(b => b.Combo).Distinct().Count());
        Assert.DoesNotContain(HotkeyService.FromCombos("Ctrl+Alt+A", "Ctrl+Alt+Space", "Ctrl+Alt+D", null, ""), b => b.Id == HotkeyService.IdChecklist);
        Assert.DoesNotContain(HotkeyService.FromCombos("Ctrl+Alt+A", "Ctrl+Alt+Space", "Ctrl+Alt+D", null, "nonsense"), b => b.Id == HotkeyService.IdChecklist);
        Assert.DoesNotContain(HotkeyService.FromCombos("Ctrl+Alt+A", "Ctrl+Alt+Space", "Ctrl+Alt+D", null, "Ctrl+Alt+A"), b => b.Id == HotkeyService.IdChecklist);
        var fallback = bindings.First(b => b.Id == HotkeyService.IdChecklist).Fallback;
        Assert.Contains("My checklist", fallback);
        Assert.Contains("tray icon", fallback);
    }

    [Fact]
    public void The_saved_settings_file_round_trips_the_new_fields()
    {
        string path = Path.Combine(Temp.NewDir(), "settings.json");
        var s = new Settings { ChecklistHotkey = "Ctrl+Alt+K", ChecklistMode = ChecklistMode.AddAtOnce, AdaptiveSettings = false, GettingStartedDone = true, SectionUse = new() { ["Files"] = 4 }, CardOpacity = 0.55 };
        SettingsService.Save(s, path);
        var back = SettingsService.Load(path);
        Assert.Equal("Ctrl+Alt+K", back.ChecklistHotkey);
        Assert.Equal(ChecklistMode.AddAtOnce, back.ChecklistMode);
        Assert.False(back.AdaptiveSettings);
        Assert.True(back.GettingStartedDone);
        Assert.Equal(4, back.SectionUse["Files"]);
        Assert.Equal(0.55, back.CardOpacity, 3);
        // an older file with none of the new fields gets the defaults
        File.WriteAllText(path, "{\"FirstRunDone\":true}");
        var old = SettingsService.Load(path);
        Assert.Equal("Ctrl+Alt+L", old.ChecklistHotkey);
        Assert.Equal(0.92, old.CardOpacity, 3);
        Assert.True(old.AdaptiveSettings);
        Assert.False(old.GettingStartedDone);
        Assert.Empty(old.SectionUse);
    }

    [Fact]
    public void The_three_presets_are_listed_in_the_same_order_and_cannot_be_lost()
    {
        var presets = AgentRules.Presets();
        Assert.Equal(new[] { "Alert Hub", "Patent Analysis", "Triaging Agent" }, presets.Select(p => p.Name));
        Assert.All(presets, p => { Assert.False(p.Enabled); Assert.True(string.IsNullOrEmpty(p.Api.Endpoint)); Assert.True(AgentRules.IsPreset(p.Id)); });
        // a saved list that lost one gets it back, off
        var list = AgentRules.Normalize(new List<AgentSettings> { presets[0] });
        Assert.Equal(3, list.Count);
        Assert.All(list.Skip(1), a => Assert.False(a.Enabled));
    }

    // ---------- text on a translucent card ----------

    private static string Over(string surface, double opacity, string backdrop)
    {
        int Ch(string hex, int i) => Convert.ToInt32(hex.TrimStart('#')[(i * 2)..(i * 2 + 2)], 16);
        string One(int i) => ((int)Math.Round(Ch(surface, i) * opacity + Ch(backdrop, i) * (1 - opacity))).ToString("X2");
        return "#" + One(0) + One(1) + One(2);
    }

    [Fact]
    public void The_default_look_keeps_its_text_at_AA_at_the_92_percent_start_even_over_a_black_window()
    {
        var p = Appearances.Get(Appearances.Default).Palette;
        string surface = Over(p.Bg, Settings.DefaultCardOpacity, "#000000");
        foreach (var (name, ink) in new[] { ("Ink", p.Ink), ("Ink2", p.Ink2), ("Ink3", p.Ink3), ("Accent", p.Accent), ("Good", p.Good), ("Warn", p.Warn), ("Bad", p.Bad) })
            Assert.True(AppearanceTests.Contrast(ink, surface) >= 4.5, $"{Appearances.Default} {name} on its surface at {Settings.DefaultCardOpacity:P0} over black = {AppearanceTests.Contrast(ink, surface):0.00}");
    }

    [Fact]
    public void Every_full_look_keeps_its_text_at_AA_at_the_92_percent_start_over_a_light_backdrop()
    {
        foreach (var look in Appearances.All.Where(a => !a.IsCompact))
        {
            var p = look.Palette;
            string surface = Over(p.Bg, Settings.DefaultCardOpacity, "#E6EDF3");
            foreach (var (name, ink) in new[] { ("Ink2", p.Ink2), ("Ink3", p.Ink3), ("Accent", p.Accent), ("Good", p.Good), ("Warn", p.Warn), ("Bad", p.Bad) })
                Assert.True(AppearanceTests.Contrast(ink, surface) >= 4.5, $"{look.Id} {name} = {AppearanceTests.Contrast(ink, surface):0.00}");
        }
    }

    [Fact]
    public void The_range_and_start_of_the_transparency_slider_are_40_to_100_and_92()
    {
        Assert.Equal(0.40, Settings.MinCardOpacity);
        Assert.Equal(0.92, Settings.DefaultCardOpacity);
        Assert.Equal(0.92, new Settings().CardOpacity);
        Assert.Equal(1.0, Settings.NormalizeOpacity(double.NaN));
        Assert.Equal(0.40, Settings.NormalizeOpacity(0.0));
        Assert.Equal(1.0, Settings.NormalizeOpacity(7));
    }
}

public class Round16NoteParserAttackTests
{
    private static readonly DateOnly Wed = new(2026, 11, 4);

    [Theory]
    [InlineData("call Sam on ٣/٤")]                              // Arabic-Indic digits: a Unicode digit is not a number the date code can read
    [InlineData("call Sam on ３/４/２０２７")]                       // full-width digits
    [InlineData("call Sam on 𝟐𝟓 Nov")]                           // mathematical digits
    [InlineData("call Sam in ٣ days")]
    [InlineData("pay rent on the ١٥th every month")]
    [InlineData("call Sam 2026-١١-0٤")]
    [InlineData("call Sam ٢٠٢٦-١١-٠٤")]
    public void Digits_from_other_scripts_never_crash_the_reader(string text)
    {
        var ex = Record.Exception(() => { NoteParser.Parse(text, Wed); NoteParser.ParseOne(text, Wed); NoteParser.ReadDue(text, Wed); });
        Assert.Null(ex);
    }
}
