using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using AndaCompanion.Services;
using AndaCompanion.Theming;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>Round 11: change things in the app, not in code. Model name, backup, restore, reset.</summary>
public class CustomizationTests
{
    [Theory]
    [InlineData("claude-opus-4-8", true)] [InlineData("claude-opus-5-5", true)] [InlineData("anthropic.claude-opus-5-5", true)] [InlineData("us.anthropic/claude:1", true)]
    [InlineData("", false)] [InlineData("  ", false)] [InlineData("has space", false)] [InlineData("-leading", false)] [InlineData("a\"b", false)]
    public void A_model_name_is_a_plain_identifier(string name, bool ok) => Assert.Equal(ok, Settings.IsValidModelName(name));

    [Fact]
    public void The_model_in_Settings_is_what_the_Claude_connection_uses_and_a_bad_one_falls_back()
    {
        var env = (string n) => n == "ANTHROPIC_API_KEY" ? "k" : null;
        var c = ChatCompleterFactory.Create(env, null, new Settings { Provider = ProviderChoice.Claude, ClaudeModel = "claude-opus-5-5" });
        Assert.Equal("Anthropic · claude-opus-5-5", c!.ProviderName);
        Assert.Equal("Anthropic · claude-opus-4-8", new AnthropicChatCompleter("not valid!").ProviderName);
        Assert.Equal("Anthropic · claude-opus-4-8", new AnthropicChatCompleter().ProviderName);

        string path = Path.Combine(Temp.NewDir(), "s.json");
        File.WriteAllText(path, "{\"ClaudeModel\":\"bad name\"}");
        Assert.Equal("claude-opus-4-8", SettingsService.Load(path).ClaudeModel);
    }

    private static (Settings S, PromptLibraryService L, QuickReplyService R) Fresh()
    {
        string dir = Temp.NewDir();
        return (new Settings(), new PromptLibraryService(Path.Combine(dir, "p.json")), new QuickReplyService(Path.Combine(dir, "r.json")));
    }

    [Fact]
    public void A_backup_holds_the_customizations_but_no_secret_and_no_file_location()
    {
        var (s, l, r) = Fresh();
        s.Tone = "Warm"; s.SignOff = "Thanks, Pat"; s.Provider = ProviderChoice.MyApi; s.CustomApi = new() { Name = "Division API", Endpoint = "https://x.example/v1", SecretEnvVar = "MY_TOKEN" };
        s.PortfolioPath = @"C:\Users\pat\portfolio.xlsx"; s.NotesFolder = @"C:\Users\pat\notes"; s.ReferenceDataPath = @"C:\Users\pat\drugsfda.zip";
        r.Upsert(new("Mine", "Hi {number}"));
        string json = SettingsBackup.Export(s, l, r, new DateTime(2026, 11, 3, 9, 0, 0));
        Assert.Contains("Division API", json); Assert.Contains("Thanks, Pat", json); Assert.Contains("Mine", json); Assert.Contains("MY_TOKEN", json);
        Assert.DoesNotContain("portfolio.xlsx", json); Assert.DoesNotContain("notes", json.Replace("Recent project notes", "")); Assert.DoesNotContain("drugsfda.zip", json);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(SettingsBackup.Version, doc.RootElement.GetProperty("Version").GetInt32());
    }

    [Fact]
    public void Restoring_applies_everything_repairs_bad_values_and_keeps_this_PCs_file_locations()
    {
        var (s, l, r) = Fresh();
        s.Tone = "Warm"; s.Provider = ProviderChoice.MyApi; s.CustomApi = new() { Name = "Division API" }; s.AutoHideSeconds = 40; s.ClaudeModel = "claude-opus-5-5";
        r.Upsert(new("Mine", "Hi {number}")); l.Upsert(new("My lens", "Be brief."));
        string json = SettingsBackup.Export(s, l, r);

        var (t, l2, r2) = Fresh();
        t.PortfolioPath = @"D:\other-pc\portfolio.xlsx";
        var (ok, message) = SettingsBackup.Import(json, t, l2, r2);
        Assert.True(ok, message);
        Assert.Equal("Warm", t.Tone); Assert.Equal("Division API", t.CustomApi.Name);
        Assert.Equal(ProviderChoice.Auto, t.Provider);                              // a restore never switches on a connection that sends data off the PC (round 15)
        Assert.Contains("My API was not switched on", message);
        Assert.Equal(40, t.AutoHideSeconds); Assert.Equal("claude-opus-5-5", t.ClaudeModel);
        Assert.Equal(@"D:\other-pc\portfolio.xlsx", t.PortfolioPath);              // not overwritten
        Assert.Contains(r2.Replies, x => x.Name == "Mine"); Assert.Contains(l2.Prompts, x => x.Name == "My lens");
        Assert.Contains("File locations on this PC were kept", message);

        // a hand-edited backup with bad values is repaired, not trusted
        string edited = json.Replace("\"Tone\": \"Warm\"", "\"Tone\": \"Sarcastic\"").Replace("\"AutoHideSeconds\": 40", "\"AutoHideSeconds\": 7");
        var (u, l3, r3) = Fresh();
        Assert.True(SettingsBackup.Import(edited, u, l3, r3).Ok);
        Assert.Equal("", u.Tone); Assert.Equal(10, u.AutoHideSeconds);
    }

    [Theory]
    [InlineData("not json at all", "not a backup from this app")]
    [InlineData("{\"Version\":99,\"Settings\":{}}", "newer version")]
    [InlineData("{\"Version\":0,\"Settings\":{}}", "not a backup from this app")]
    [InlineData("[]", "not a backup from this app")]
    public void A_file_that_is_not_a_backup_changes_nothing_and_says_so(string text, string expected)
    {
        var (s, l, r) = Fresh();
        s.Tone = "Plain";
        var (ok, message) = SettingsBackup.Import(text, s, l, r);
        Assert.False(ok); Assert.Contains(expected, message); Assert.Equal("Plain", s.Tone);
        Assert.False(SettingsBackup.Import(new string('x', SettingsBackup.MaxBytes + 1), s, l, r).Ok);
    }

    [Fact]
    public void Reset_returns_the_starting_values_and_starter_lists_but_keeps_the_persons_files()
    {
        var (s, l, r) = Fresh();
        s.Tone = "Warm"; s.AutoHideSeconds = 40; s.MiddleClickTrigger = true; s.PortfolioPath = @"C:\data.xlsx";
        r.Upsert(new("Mine", "x")); l.Upsert(new("My lens", "x"));
        SettingsBackup.Reset(s, l, r);
        Assert.Equal("", s.Tone); Assert.Equal(10, s.AutoHideSeconds); Assert.False(s.MiddleClickTrigger);
        Assert.Equal(@"C:\data.xlsx", s.PortfolioPath);
        Assert.Equal(QuickReplyService.Starters.Count, r.Replies.Count); Assert.Equal(PromptLibraryService.Starters.Count, l.Prompts.Count);
    }

    [Fact]
    public void Lists_restored_from_a_backup_obey_the_same_limits_as_typed_ones()
    {
        var (s, l, r) = Fresh();
        r.ReplaceAll(Enumerable.Range(0, 60).Select(i => new QuickReply($"r{i}", "x")).Append(new QuickReply("", "no name")).Append(new QuickReply("long", new string('x', 5000))));
        Assert.Equal(QuickReplyService.MaxReplies, r.Replies.Count);
        l.ReplaceAll(new[] { new LensPrompt("A", "x"), new LensPrompt("a", "y"), new LensPrompt(new string('n', 200), "x") });
        Assert.Single(l.Prompts);                                                  // duplicates collapse (any case), over-long names are dropped
        Assert.Equal("y", l.Prompts[0].Text);
    }
}

[Collection("Global state")]
public class Round11UiTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public Round11UiTests(HeadlessFixture fx) => _fx = fx;
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }

    [Fact]
    public async Task Settings_offers_a_section_list_the_model_box_and_a_two_step_reset()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            string dir = Temp.NewDir();
            var s = new Settings { Tone = "Warm", AutoHideSeconds = 40, ShowAllTools = true };
            var w = new SettingsWindow(s, new PromptLibraryService(Path.Combine(dir, "p.json")), () => { }, replies: new QuickReplyService(Path.Combine(dir, "r.json")));
            w.Show(); Settle();
            var jump = w.FindControl<ComboBox>("JumpBox")!;
            var items = jump.Items.Cast<string>().ToList();
            var model = w.FindControl<TextBox>("ClaudeModelBox")!.Text;
            var reset = w.FindControl<Button>("SettingsReset")!;
            reset.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle();
            var afterFirst = (reset.Content?.ToString(), s.Tone);
            reset.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle();
            var afterSecond = (s.Tone, s.AutoHideSeconds);
            w.Close(); Settle();
            return (items, model, afterFirst, afterSecond);
        }, CancellationToken.None);
        Assert.True(r.items.Count >= 12, "sections listed: " + r.items.Count);
        Assert.Contains("Connection · Which API, if any", r.items);
        Assert.Contains("Back up, restore, reset", r.items);
        Assert.Contains("My writing · Tone, style, sign-off, quick replies", r.items);
        Assert.Equal("claude-opus-4-8", r.model);
        Assert.Equal(("Click again to reset", "Warm"), r.afterFirst);              // the first click only asks
        Assert.Equal(("", 10), r.afterSecond);                                      // the second one resets
    }
}
