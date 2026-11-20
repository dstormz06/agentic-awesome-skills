using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AndaCompanion.Services;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// Round 37: a switch in Settings for Today's "Since you last looked". Off: Today has no such group, nothing is read or kept, and what
/// was kept is removed. The words are the simulation's (mockup/round37-golden.json). Fictional data only.
/// </summary>
[Collection("Global state")]
public class Round37Tests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public Round37Tests(HeadlessFixture fx) => _fx = fx;
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }

    private static string Folder(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "anda-hover-companion") dir = dir.Parent;
        return Path.Combine(new[] { dir?.FullName ?? throw new DirectoryNotFoundException("anda-hover-companion folder not found") }.Concat(parts).ToArray());
    }
    /// <summary>A source file without its comments, so that a check of the wiring is not met by a line put out of action (round 37 review).</summary>
    public static string Code(string path) => string.Join("\n", File.ReadAllLines(path).Select(l => { int i = l.IndexOf("//", StringComparison.Ordinal); return i < 0 ? l : l[..i]; }));

    private static readonly Lazy<JsonElement> Gold = new(() => JsonDocument.Parse(File.ReadAllText(Folder("mockup", "round37-golden.json"))).RootElement.Clone());
    private static string G(string name) => Gold.Value.GetProperty(name).GetString()!;

    [Fact]
    public void The_switch_says_what_the_simulation_says_and_is_on_by_default()
    {
        Assert.Equal(G("label"), LastLook.SwitchLabel);
        Assert.Equal(G("hint"), LastLook.SwitchHint);
        Assert.Equal(G("off"), LastLook.SaidSwitchOff);
        Assert.Equal(G("on"), LastLook.SaidSwitchOn);
        Assert.Equal(Gold.Value.GetProperty("changes").GetProperty("items").EnumerateArray().Select(x => x.GetString()!).ToList(), ChangeLog.Entries.First(e => e.Round == 37).Items.ToList());
        Assert.True(new Settings().SinceLastLook);
        // a settings file written before round 37 does not hold it: it reads as on, as before
        Assert.True(JsonSerializer.Deserialize<Settings>("{}")!.SinceLastLook);
    }

    [Fact]
    public void Off_is_part_of_a_backup_and_comes_back()
    {
        string dir = Temp.NewDir();
        var lenses = new PromptLibraryService(Path.Combine(dir, "p.json")); var replies = new QuickReplyService(Path.Combine(dir, "r.json"));
        string json = SettingsBackup.Export(new Settings { SinceLastLook = false }, lenses, replies);
        Assert.Contains("SinceLastLook", json);
        var target = new Settings();
        var (ok, _) = SettingsBackup.Import(json, target, lenses, replies);
        Assert.True(ok);
        Assert.False(target.SinceLastLook);
    }

    [Fact]
    public void Forgetting_removes_the_kept_look_with_its_set_aside_and_unfinished_copies_and_nothing_else()
    {
        string dir = Temp.NewDir(), path = Path.Combine(dir, "last-look.json");
        Assert.True(LastLook.Forget(path));                                        // nothing there: nothing happens
        var look = new LastLook.Look("2026-07-16T14:42:00.000Z", new Dictionary<string, LastLook.Item>());
        Assert.True(LastLook.Save(path, look));
        File.WriteAllText(Path.Combine(dir, "last-look.unreadable-20261120-110000.json"), "{");
        File.WriteAllText(path + ".tmp", "{");
        File.WriteAllText(Path.Combine(dir, "settings.json"), "{}");            // another file of the app's folder stays
        Assert.True(LastLook.Forget(path));
        Assert.Equal(new[] { "settings.json" }, Directory.GetFiles(dir).Select(Path.GetFileName));
    }

    [Fact]
    public void The_switchs_two_decisions_keep_only_while_on_and_forget_only_when_off()
    {
        string dir = Temp.NewDir(), path = Path.Combine(dir, "last-look.json");
        var shown = new LastLook.Look("2026-07-16T14:42:00.000Z", new Dictionary<string, LastLook.Item> { ["300001"] = new("2027-01-04", null, null) });
        Assert.False(LastLook.KeepIfOn(new Settings { SinceLastLook = false }, path, shown, new DateTime(2026, 7, 16, 15, 0, 0, DateTimeKind.Utc)));
        Assert.False(File.Exists(path));
        Assert.True(LastLook.KeepIfOn(new Settings(), path, shown, new DateTime(2026, 7, 16, 15, 0, 0, DateTimeKind.Utc)));
        Assert.Equal("2026-07-16T15:00:00.000Z", LastLook.Load(path)!.At);       // what was shown, at the time Today closed
        Assert.True(LastLook.ForgetIfOff(new Settings(), path)); Assert.True(File.Exists(path));            // on: nothing removed
        Assert.True(LastLook.ForgetIfOff(new Settings { SinceLastLook = false }, path)); Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task An_open_Today_follows_the_switch_at_once_and_keeps_only_what_the_switch_allows()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            var repo = new Data.MockAndaRepository(); bool on = false; int kept = 0;
            Today.Look? Find(string id) => repo.Find(id) is { } rec ? Round33Tests.Look(rec) : null;
            var now = LastLook.Of(repo.Numbers.Select(n => repo.Find(n)!), new DateTime(2026, 7, 16, 14, 42, 0, DateTimeKind.Utc));
            var w = new TodayWindow(new TodayHooks(() => repo.Numbers.Select(n => Find(n)!).ToList(), Find, () => Array.Empty<string>(), () => Array.Empty<string>(), _ => { },
                Looks: () => on ? new LastLook.View(null, now, null, true) : null, KeepLook: _ => kept++));
            List<string> T() => w.List.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
            try
            {
                w.Show(); Settle(); bool before = T().Contains("SINCE YOU LAST LOOKED");
                on = true; w.RefreshLooks(); Settle(); bool afterOn = T().Contains("SINCE YOU LAST LOOKED") && T().Contains(LastLook.SaidFirst);
                on = false; w.RefreshLooks(); Settle(); bool afterOff = T().Contains("SINCE YOU LAST LOOKED");
                on = true; w.RefreshLooks(); Settle();
                w.Close(); Settle();
                return (before, afterOn, afterOff, kept);
            }
            catch { w.Close(); Settle(); throw; }
        }, CancellationToken.None);
        Assert.False(r.before);                // opened while off: no group
        Assert.True(r.afterOn);                // turned on while open: the group comes, starting from nothing
        Assert.False(r.afterOff);              // turned off while open: it goes at once
        Assert.Equal(1, r.kept);               // on again and closed: the look is kept (the app's KeepIfOn decides on the file)
    }

    [Fact]
    public async Task Settings_offers_the_switch_and_turning_it_off_says_so_and_saves()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            var s = new Settings(); int changed = 0;
            int forgot = 0;
            var w = new SettingsWindow(s, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => changed++,
                new SettingsHooks(() => "", () => null, () => "", ForgetLastLook: () => { forgot++; return forgot > 1; }));        // the first removal fails
            try
            {
                w.Show(); Settle();
                // the section may be folded, so the controls are found by name, not among what is drawn
                var check = w.FindControl<CheckBox>("LastLookCheck")!; var status = w.FindControl<TextBlock>("LastLookStatus")!;
                var texts = new List<string> { w.FindControl<TextBlock>("LastLookHint")!.Text ?? "" };
                bool wasOn = check.IsChecked == true; string label = check.Content as string ?? "";
                check.IsChecked = false; Settle();
                string saidOff = status.Text ?? "";
                bool after = s.SinceLastLook; int changesOff = changed;
                check.IsChecked = true; Settle();
                string saidOn = status.Text ?? "";
                check.IsChecked = false; Settle();
                string saidOffAgain = status.Text ?? "";
                check.IsChecked = true; Settle();
                bool help = Avalonia.Automation.AutomationProperties.GetHelpText(check) == LastLook.SwitchHint;
                return (wasOn, label, hint: texts.Contains(LastLook.SwitchHint), saidOff, after, changesOff, saidOn, on: s.SinceLastLook, saidOffAgain, help, forgot);
            }
            finally { w.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.True(r.wasOn);
        Assert.Equal(LastLook.SwitchLabel, r.label);
        Assert.True(r.hint);
        Assert.Equal(LastLook.SaidSwitchOffNotYet, r.saidOff);                    // the removal failed: it says so, and when it is tried again
        Assert.Equal(LastLook.SaidSwitchOff, r.saidOffAgain);
        Assert.Equal(2, r.forgot);
        Assert.True(r.help);
        Assert.False(r.after);
        Assert.True(r.changesOff >= 1);                                             // saved through the app, which then removes the file
        Assert.Equal(LastLook.SaidSwitchOn, r.saidOn);
        Assert.True(r.on);
    }

    [Fact]
    public void The_app_reads_and_keeps_nothing_when_the_switch_is_off_and_removes_what_was_kept()
    {
        // read without comments, so a line put out of action does not count; what each call does is tested above
        string app = Code(Folder("app", "AndaCompanion", "App.axaml.cs")), more = Code(Folder("app", "AndaCompanion", "App.Round33.cs"));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(app, @"LastLook\.ForgetIfOff\(_settings, AppPaths\.LastLookFile\);").Count);   // at start and in ApplySettings
        Assert.Contains("_todayWindow?.RefreshLooks();", app);                                               // an open Today follows the switch
        Assert.Contains("ForgetLastLook: () => LastLook.Forget(AppPaths.LastLookFile)", app);              // Settings removes, and says whether it could
        Assert.Contains("Looks: () => _settings.SinceLastLook ? LastLook.ViewOf(", System.Text.RegularExpressions.Regex.Replace(more, @"\s+", " "));   // off: no group, nothing read
        Assert.EndsWith(": null,", System.Text.RegularExpressions.Regex.Replace(more, @"\s+", " ").Split("Looks: () => _settings.SinceLastLook ?")[1].Split("KeepLook:")[0].Trim());
        Assert.Contains("KeepLook: look => LastLook.KeepIfOn(_settings, AppPaths.LastLookFile, look, Clock.UtcNow()),", more);   // nothing kept while off
    }
}
