using AndaCompanion.Services;
using AndaCompanion.Theming;
using Xunit;

namespace AndaCompanion.Tests;

public class SettingsTests
{
    [Fact]
    public void Defaults_match_the_product_decisions()
    {
        var s = new Settings();
        Assert.Equal(AppearanceId.W, s.Appearance);      // W · Graphite Gauge is the default (round 23)
        Assert.Equal(10, s.AutoHideSeconds);
        Assert.Equal(ConsentModel.Whisper, s.Consent);     // whisper invite is the default (tester feedback)
        Assert.True(s.EscalateUrgent); Assert.Equal(7, s.UrgentLateDays); Assert.Equal(3, s.HolidayLeadDays);
        Assert.Equal(("Ctrl+Alt+A", "Ctrl+Alt+Space", "Ctrl+Alt+Shift+R"), (s.PeekHotkey, s.LookupHotkey, s.DraftHotkey));
        Assert.Null(s.PortfolioPath); Assert.Null(s.NotesFolder); Assert.Null(s.ReferenceDataPath);
        Assert.False(s.UsageLog); Assert.Equal("", s.StandingInstructions);
        Assert.Equal(0.90, s.CardOpacity);                 // a new install starts at 90% (round 20; 92% before)
        Assert.True(s.CaptureViaCopy);
        Assert.False(s.FirstRunDone);
        Assert.Null(s.ActiveLens);
    }

    [Fact]
    public void Settings_and_prompts_round_trip_and_survive_corruption()
    {
        var result = SelfTestChecks.SettingsAndPrompts(Temp.NewDir());
        Assert.True(result.Status == CheckStatus.Pass, result.Detail);
    }

    [Fact]
    public void A_missing_file_gives_defaults()
    {
        var s = SettingsService.Load(Path.Combine(Temp.NewDir(), "nope.json"));
        Assert.Equal(AppearanceId.W, s.Appearance);
    }

    [Fact]
    public void An_existing_testers_choice_is_kept_when_the_default_changes()
    {
        string path = Path.Combine(Temp.NewDir(), "settings.json");
        File.WriteAllText(path, "{ \"Appearance\": 10 }");         // saved by an earlier build: K, which is still offered
        Assert.Equal(AppearanceId.K, SettingsService.Load(path).Appearance);
        File.WriteAllText(path, "{ \"Appearance\": 0 }");          // A was retired in round 23: the default takes its place
        Assert.Equal(AppearanceId.W, SettingsService.Load(path).Appearance);
    }

    [Fact]
    public void Unknown_appearance_numbers_and_auto_hide_values_fall_back_safely()
    {
        string path = Path.Combine(Temp.NewDir(), "settings.json");
        File.WriteAllText(path, "{ \"Appearance\": 99, \"AutoHideSeconds\": -5 }");
        var s = SettingsService.Load(path);
        Assert.Equal(AppearanceId.W, s.Appearance);
        Assert.Equal(10, s.AutoHideSeconds);
    }

    [Theory]
    [InlineData(0)] [InlineData(10)] [InlineData(20)] [InlineData(40)]
    public void Each_offered_auto_hide_value_round_trips(int seconds)
    {
        string path = Path.Combine(Temp.NewDir(), "settings.json");
        SettingsService.Save(new Settings { AutoHideSeconds = seconds }, path);
        Assert.Equal(seconds, SettingsService.Load(path).AutoHideSeconds);
    }

    [Fact]
    public void New_settings_round_trip_and_invalid_ones_fall_back_safely()
    {
        string path = Path.Combine(Temp.NewDir(), "settings.json");
        SettingsService.Save(new Settings
        {
            PeekHotkey = "Ctrl+Shift+P", LookupHotkey = "Ctrl+Alt+Shift+L", DraftHotkey = "Ctrl+Alt+Shift+9", EscalateUrgent = false, UrgentLateDays = 14, HolidayLeadDays = 5,
            PortfolioPath = @"C:\Users\x\Portfolio.xlsx", PortfolioColumns = new() { ["goal"] = "Target" }, NotesFolder = @"C:\Notes", ReferenceDataPath = @"C:\d.zip",
            StandingInstructions = "Plain words.", UsageLog = true,
        }, path);
        var s = SettingsService.Load(path);
        Assert.Equal(("Ctrl+Shift+P", "Ctrl+Alt+Shift+L", "Ctrl+Alt+Shift+9"), (s.PeekHotkey, s.LookupHotkey, s.DraftHotkey));
        Assert.False(s.EscalateUrgent); Assert.Equal(14, s.UrgentLateDays); Assert.Equal(5, s.HolidayLeadDays);
        Assert.Equal("Target", s.PortfolioColumns["goal"]); Assert.Equal("Plain words.", s.StandingInstructions); Assert.True(s.UsageLog);

        File.WriteAllText(path, "{ \"PeekHotkey\": \"Ctrl+C\", \"LookupHotkey\": \"x\", \"UrgentLateDays\": 5, \"HolidayLeadDays\": 9, \"PortfolioColumns\": null, \"StandingInstructions\": null }");
        var bad = SettingsService.Load(path);
        Assert.Equal(("Ctrl+Alt+A", "Ctrl+Alt+Space", "Ctrl+Alt+Shift+R"), (bad.PeekHotkey, bad.LookupHotkey, bad.DraftHotkey));
        Assert.Equal(7, bad.UrgentLateDays); Assert.Equal(3, bad.HolidayLeadDays);
        Assert.NotNull(bad.PortfolioColumns); Assert.Equal("", bad.StandingInstructions);

        File.WriteAllText(path, "{ \"PeekHotkey\": \"Ctrl+Alt+A\", \"LookupHotkey\": \"Ctrl+Alt+A\" }");     // a duplicate resets the whole set
        Assert.Equal("Ctrl+Alt+Space", SettingsService.Load(path).LookupHotkey);
    }

    [Fact]
    public void The_whisper_default_does_not_override_what_an_existing_installation_saved()
    {
        string path = Path.Combine(Temp.NewDir(), "settings.json");
        File.WriteAllText(path, "{ \"Consent\": 0 }");       // Full, saved by an earlier build
        Assert.Equal(ConsentModel.Full, SettingsService.Load(path).Consent);
    }

    [Fact]
    public void Save_creates_the_folder_and_writes_plain_readable_json()
    {
        string path = Path.Combine(Temp.NewDir(), "deep", "er", "settings.json");
        SettingsService.Save(new Settings { Appearance = AppearanceId.C }, path);
        string json = File.ReadAllText(path);
        Assert.Contains("\"Appearance\"", json);
        Assert.DoesNotContain("sk-ant", json);           // no secret ever lands in settings
    }

    [Fact]
    public void Starter_lenses_exist_and_upsert_is_case_insensitive()
    {
        var lib = new PromptLibraryService(Path.Combine(Temp.NewDir(), "prompts.json"));
        Assert.Contains(lib.Prompts, p => p.Name == "Facility focus");
        Assert.Contains(lib.Prompts, p => p.Name == "Director summary");
        Assert.Contains(lib.Prompts, p => p.Name == "First-cycle watch");
        int before = lib.Prompts.Count;
        lib.Upsert(new LensPrompt("facility FOCUS", "replaced"));
        Assert.Equal(before, lib.Prompts.Count);
        Assert.Equal("replaced", lib.GetText("Facility focus"));
        Assert.Null(lib.GetText("No such lens"));
        Assert.Null(lib.GetText(null));
    }

    [Collection("Global state")]
    public class Paths
    {
        [Fact]
        public void Home_variable_relocates_everything()
        {
            string dir = Temp.NewDir();
            using var _ = new EnvScope(AppPaths.HomeEnvVar, dir);
            Assert.Equal(dir, AppPaths.Root);
            Assert.Equal(Path.Combine(dir, "settings.json"), AppPaths.SettingsFile);
            Assert.Equal(Path.Combine(dir, "prompts.json"), AppPaths.PromptsFile);
        }

        [Fact]
        public void Without_the_variable_data_lives_under_app_data()
        {
            using var _ = new EnvScope(AppPaths.HomeEnvVar, null);
            Assert.EndsWith("AndaCompanion", AppPaths.Root);
        }
    }
}
