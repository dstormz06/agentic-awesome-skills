using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AndaCompanion.Data;
using AndaCompanion.Services;
using AndaCompanion.Theming;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// Round 35, as the windows are actually built (added after the review): the tray menu built for real in both editions, the sizes of text
/// that the windows show (the theme's own sizes included, which a reading of the source cannot see), and the countdown's caption, which
/// must not be cut off. Fictional data.
/// </summary>
[Collection("Global state")]
public class Round35UiTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public Round35UiTests(HeadlessFixture fx) => _fx = fx;
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }

    private static string Folder(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "anda-hover-companion") dir = dir.Parent;
        return Path.Combine(new[] { dir?.FullName ?? throw new DirectoryNotFoundException("anda-hover-companion folder not found") }.Concat(parts).ToArray());
    }
    private static readonly Lazy<JsonElement> Gold = new(() => JsonDocument.Parse(File.ReadAllText(Folder("mockup", "round35-golden.json"))).RootElement.Clone());
    private static List<string> FromGolden(string edition) => Gold.Value.GetProperty(edition).EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()!
        : e.GetProperty("label").GetString() + " [" + string.Join(" | ", e.GetProperty("items").EnumerateArray().Select(x => x.GetString()).Where(x => x != TrayMenu.NotInApp)) + "]").ToList();

    // ---------- the tray menu, built ----------

    private static List<string> Shown(NativeMenu menu) => menu.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator && i.IsVisible && i.IsEnabled)
        .Select(i => i.Header + (i.Menu is null || i.Header == "My actions" ? "" : " [" + string.Join(" | ", i.Menu.Items.OfType<NativeMenuItem>().Where(c => c.IsVisible).Select(c => c.Header)) + "]")).ToList();

    [Theory]
    [InlineData(false, "focused")]
    [InlineData(true, "all")]
    public async Task The_menu_as_built_shows_the_simulations_choices_in_each_edition(bool allTools, string edition)
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            var full = new List<NativeMenuItem>();
            var line = new NativeMenuItem("Hotkeys: …") { IsEnabled = false };
            var menu = TrayMenu.Build(label => label == "My actions" ? new NativeMenuItem(label) { Menu = new NativeMenu() } : new NativeMenuItem(label), full.Add, line);
            foreach (var i in full) i.IsVisible = allTools;
            int lineAt = menu.Items.IndexOf(line), exitAt = menu.Items.IndexOf(menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Exit"));
            return (shown: Shown(menu), lineAt, exitAt, separatorBefore: menu.Items[lineAt - 1] is NativeMenuItemSeparator);
        }, CancellationToken.None);
        Assert.Equal(FromGolden(edition), r.shown);
        Assert.Equal(r.exitAt - 1, r.lineAt);                         // the hotkey line sits just before Exit, after a separator
        Assert.True(r.separatorBefore);
    }

    [Fact]
    public void Each_choice_in_the_app_opens_what_it_names()
    {
        // what each line of the app's menu calls; a choice wired to the wrong window, or one left out, fails here
        var want = new Dictionary<string, string>
        {
            ["Lookup ANDA…"] = "OpenLookup", ["Today…"] = "OpenToday", ["Review a draft…"] = "OpenDraft(", ["Ask about a selection…"] = "OpenAsk(",
            ["ANDA to-do list…"] = "OpenTodoWindow", ["My to-do list…"] = "OpenMyTodoWindow", ["Notes to my to-do list…"] = "OpenNotes(", ["My usage…"] = "OpenUsage",
            ["Compare two files…"] = "OpenCompare", ["Hand-off page…"] = "OpenHandoff(", ["Quick replies…"] = "OpenQuickReplies", ["Prompt library…"] = "new PromptLibraryWindow(", ["MCP tools…"] = "OpenMcp",
            ["My actions"] = "_actionsMenu =", ["Appearance"] = "BuildRadioSubmenu(\"Appearance\"", ["Auto-hide"] = "BuildRadioSubmenu(\"Auto-hide\"",
            ["Connections and event log…"] = "OpenConnections", ["Backups and archive…"] = "OpenBackups", ["Demo: ANDA at risk"] = "DemoCard(\"216482\")",
            ["Demo: ANDA on track"] = "DemoCard(\"214990\")", ["Settings…"] = "OpenSettings", ["Help: first steps…"] = "OpenFirstSteps(", ["About and what changed…"] = "OpenAbout",
            ["Exit"] = "desktop.Shutdown()",
        };
        var app = File.ReadAllLines(Folder("app", "AndaCompanion", "App.axaml.cs"));
        var labels = TrayMenu.Layout(allTools: true).SelectMany(e => e.Items is { } items ? items : new[] { e.Label }).ToList();
        Assert.Equal(want.Keys.OrderBy(k => k), labels.OrderBy(k => k));
        foreach (var label in labels)
        {
            var arm = app.Where(l => l.TrimStart().StartsWith($"\"{label}\" =>")).ToList();
            Assert.True(arm.Count == 1, $"{label}: {arm.Count} lines");
            Assert.Contains(want[label], arm[0]);
        }
        Assert.Contains(app, l => l.Contains("TrayMenu.Build(Make,"));
    }

    // ---------- the countdown's caption ----------

    [Fact]
    public async Task The_countdown_caption_fits_its_box_on_one_line_in_both_card_sizes()
    {
        var problems = await _fx.Session.Dispatch(() =>
        {
            var bad = new List<string>();
            ThemeManager.Apply(AppearanceId.X);
            var late = new MockAndaRepository().Find("216482")!;
            try
            {
                foreach (int days in new[] { 79, 1, 0, -1, -3, -120 })
                    foreach (bool mini in new[] { false, true })
                    {
                        var w = new CardWindow(new OfflineBriefService(), new MockAndaRepository().Find) { CloseWhenDeactivated = false, Today = () => new DateOnly(2026, 7, 16) };
                        try
                        {
                            w.Show(late with { DaysToGoal = days }, new PixelPoint(40, 40), mini); Settle();
                            var box = w.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "HeroCount");
                            var texts = box.GetVisualDescendants().OfType<TextBlock>().ToList();
                            var caption = texts[1];
                            double oneLine = texts[1].FontSize * 1.6;
                            if (caption.Bounds.Height > oneLine) bad.Add($"{days} {(mini ? "small" : "full")}: \"{caption.Text}\" takes two lines");
                            foreach (var t in texts)
                                if (t.DesiredSize.Width > t.Bounds.Width + 0.5 || t.Bounds.Right > box.Bounds.Width - box.BorderThickness.Right - box.Padding.Right + 0.5)
                                    bad.Add($"{days} {(mini ? "small" : "full")}: \"{t.Text}\" needs {t.DesiredSize.Width:0.#}, has {t.Bounds.Width:0.#}");
                        }
                        finally { w.Close(); Settle(); }
                    }
            }
            finally { ThemeManager.Apply(Appearances.Default); }
            return bad;
        }, CancellationToken.None);
        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }

    // ---------- the sizes of text the windows show ----------

    private static IEnumerable<(string what, double size)> Sizes(Window w) => w.GetVisualDescendants()
        .Where(v => v is TextBlock or TextPresenter or TemplatedControl && v is Visual { IsEffectivelyVisible: true })
        .Where(v => v is not TextBlock tb || !string.IsNullOrEmpty(tb.Text) || tb.Inlines?.Count > 0)
        .Select(v => (what: $"{w.GetType().Name} {v.GetType().Name} {((v as TextBlock)?.Text ?? (v as Control)?.Name ?? "")}".Trim(), size: ((AvaloniaObject)v).GetValue(TextElement.FontSizeProperty)));

    [Fact]
    public async Task Every_window_as_shown_uses_only_the_six_sizes()
    {
        var bad = await _fx.Session.Dispatch(() =>
        {
            var allowed = TypeScale.All.Select(s => s.Size).ToHashSet();
            var repo = new MockAndaRepository();
            Today.Look? Find(string id) => repo.Find(id) is { } rec ? Round33Tests.Look(rec) : null;
            var windows = new List<Window>();
            foreach (var a in Appearances.Offered)
            {
                ThemeManager.Apply(a.Id);
                var card = new CardWindow(new OfflineBriefService(), repo.Find) { CloseWhenDeactivated = false, Today = () => new DateOnly(2026, 7, 16) };
                card.Show(repo.Find("216482")!, new PixelPoint(40, 40), false); Settle();
                windows.Add(card);
            }
            ThemeManager.Apply(Appearances.Default);
            var dir = Temp.NewDir();
            var more = new Window[]
            {
                new SettingsWindow(new Settings { ShowAllTools = true }, new PromptLibraryService(Path.Combine(dir, "p.json")), () => { }, null, new QuickReplyService(Path.Combine(dir, "r.json"))),
                new TodayWindow(new TodayHooks(() => repo.Numbers.Select(n => Find(n)!).ToList(), Find, () => new[] { "214990" }, () => new[] { "216482" }, _ => { })),
                new LookupWindow(_ => { }), new CompareWindow(), new AboutWindow(), new UsageWindow(null, false),
                new QuickRepliesWindow(new QuickReplyService(Path.Combine(dir, "r2.json")), () => { }),
                new ConnectionsWindow(() => Connections.Describe(new ConnState(null, Array.Empty<ConnAgent>(), null, Array.Empty<ConnMcp>())), () => (Array.Empty<EventEntry>(), 0, 0), () => "", null),
            };
            foreach (var w in more) { w.Show(); Settle(); windows.Add(w); }
            try
            {
                return windows.SelectMany(Sizes).Where(s => !allowed.Contains(s.size) && s.size != 1)       // 1: a live region only a screen reader reads
                    .Select(s => $"{s.what} at {s.size}").Distinct().ToList();
            }
            finally { foreach (var w in windows) w.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.True(bad.Count == 0, $"{bad.Count} outside the scale: " + string.Join("; ", bad.Take(15)));
    }
}
