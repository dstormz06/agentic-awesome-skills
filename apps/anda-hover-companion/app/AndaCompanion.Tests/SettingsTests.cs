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
        Assert.Equal(AppearanceId.A, s.Appearance);      // A is the default
        Assert.Equal(ConsentModel.Full, s.Consent);
        Assert.Equal(1.0, s.CardOpacity);
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
        Assert.Equal(AppearanceId.A, s.Appearance);
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
