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
    public void Forgetting_removes_the_kept_look_and_never_fails()
    {
        string path = Path.Combine(Temp.NewDir(), "last-look.json");
        LastLook.Forget(path);                                                      // nothing there: nothing happens
        Assert.True(LastLook.Save(path, new LastLook.Look("2026-07-16T14:42:00.000Z", new Dictionary<string, LastLook.Item>())));
        LastLook.Forget(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Settings_offers_the_switch_and_turning_it_off_says_so_and_saves()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            var s = new Settings(); int changed = 0;
            var w = new SettingsWindow(s, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => changed++);
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
                return (wasOn, label, hint: texts.Contains(LastLook.SwitchHint), saidOff, after, changesOff, saidOn, on: s.SinceLastLook);
            }
            finally { w.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.True(r.wasOn);
        Assert.Equal(LastLook.SwitchLabel, r.label);
        Assert.True(r.hint);
        Assert.Equal(LastLook.SaidSwitchOff, r.saidOff);
        Assert.False(r.after);
        Assert.True(r.changesOff >= 1);                                             // saved through the app, which then removes the file
        Assert.Equal(LastLook.SaidSwitchOn, r.saidOn);
        Assert.True(r.on);
    }

    [Fact]
    public void The_app_reads_and_keeps_nothing_when_the_switch_is_off_and_removes_what_was_kept()
    {
        string app = File.ReadAllText(Folder("app", "AndaCompanion", "App.axaml.cs")), more = File.ReadAllText(Folder("app", "AndaCompanion", "App.Round33.cs"));
        Assert.Contains("if (!_settings.SinceLastLook) LastLook.Forget(AppPaths.LastLookFile);", app);      // in ApplySettings: from Settings or a restored backup
        Assert.Contains("Looks: !_settings.SinceLastLook ? null : () =>", more);                           // no group, nothing read
        Assert.Contains("KeepLook: look => { if (_settings.SinceLastLook) LastLook.Save(", more);           // nothing kept, even if switched off while Today is open
    }
}
