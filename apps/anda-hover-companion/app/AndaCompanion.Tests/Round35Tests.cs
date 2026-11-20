using System.Text.Json;
using System.Text.RegularExpressions;
using AndaCompanion.Services;
using AndaCompanion.Theming;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>Round 35: one type scale of six sizes in every window, and the tray menu of seven choices (see also Round35UiTests).</summary>
public class Round35Tests
{
    private static string Folder(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "anda-hover-companion") dir = dir.Parent;
        return Path.Combine(new[] { dir?.FullName ?? throw new DirectoryNotFoundException("anda-hover-companion folder not found") }.Concat(parts).ToArray());
    }

    private const string Num = @"(\d+(?:\.\d+)?)";
    // every way a window's source sets a size of text
    private static readonly Regex[] Ways =
    {
        new(@"FontSize=""" + Num + @""""), new(@"Property=""FontSize"" Value=""" + Num + @""""),
        new(@"FontSize = [^;,{}\n?]*?\? " + Num + " : " + Num + @"(?=[,;\s}])"), new(@"FontSize = " + Num + @"(?=[,;\s}])"),
        new(@"DateSize \{ get; init; \} = " + Num), new(@"DateSize = " + Num), new(@"AttentionLine\(" + Num + @"\)"), new(@"double size = " + Num),
        new(@"size: " + Num), new(@"(?:MailLine|Text)\((?:[^()\n]|\([^()\n]*\))*?, " + Num + @"(?=[,)])"),
    };

    [Fact]
    public void The_scale_is_six_named_sizes_and_an_older_size_goes_to_the_nearest_step_up_to_it()
    {
        Assert.Equal(new[] { 11d, 12, 13, 15, 20, 34 }, TypeScale.All.Select(s => s.Size));
        Assert.Equal(new[] { "Caption", "Footnote", "Body", "Callout", "Title", "Display" }, TypeScale.All.Select(s => s.Name));
        foreach (var (old, now) in new[] { (9.0, 11.0), (11, 11), (11.5, 12), (12, 12), (12.5, 13), (13.5, 13), (14, 15), (16, 15), (17, 20), (24, 20), (30, 34), (38, 34) })
            Assert.Equal(now, TypeScale.Of(old));
    }

    [Fact]
    public void Every_window_uses_only_the_six_sizes()
    {
        var allowed = TypeScale.All.Select(s => s.Size).ToHashSet();
        var bad = new List<string>(); int seen = 0;
        foreach (var file in Directory.EnumerateFiles(Folder("app", "AndaCompanion"), "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".cs") || f.EndsWith(".axaml")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            if (file.EndsWith("TypeScale.cs")) continue;
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
                foreach (var way in Ways)
                    foreach (Match m in way.Matches(lines[i]))
                        for (int g = 1; g < m.Groups.Count; g++)
                        {
                            if (!m.Groups[g].Success) continue;
                            seen++;
                            double v = double.Parse(m.Groups[g].Value, System.Globalization.CultureInfo.InvariantCulture);
                            bool hiddenSay = v == 1 && lines[i].Contains("Opacity") && lines[i].Contains('0');      // a live region only a screen reader reads
                            if (!allowed.Contains(v) && !hiddenSay) bad.Add($"{Path.GetFileName(file)}:{i + 1}: {v}");
                        }
        }
        Assert.True(seen > 300, $"only {seen} sizes found: the scan is not reading the windows");
        Assert.True(bad.Count == 0, "sizes outside the scale: " + string.Join(", ", bad.Take(12)));
    }

    // ---------- the tray menu ----------
    private static readonly Lazy<JsonElement> Doc = new(() => JsonDocument.Parse(File.ReadAllText(Folder("mockup", "round35-golden.json"))).RootElement.Clone());

    private static List<string> Flat(IEnumerable<object> entries) => entries.Select(e => e is TrayMenu.Entry t ? t.Label + (t.Items is null ? "" : " [" + string.Join(" | ", t.Items) + "]") : (string)e).ToList();
    private static List<string> FromGolden(string edition) => Doc.Value.GetProperty(edition).EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()!
        : e.GetProperty("label").GetString() + " [" + string.Join(" | ", e.GetProperty("items").EnumerateArray().Select(x => x.GetString()).Where(x => x != TrayMenu.NotInApp)) + "]").ToList();

    [Fact]
    public void The_tray_menu_has_the_simulations_choices_in_its_order_in_both_editions()
    {
        Assert.Equal(FromGolden("focused"), Flat(TrayMenu.Layout(allTools: false)));
        Assert.Equal(FromGolden("all"), Flat(TrayMenu.Layout(allTools: true)));
        var focused = TrayMenu.Layout(false).Select(e => e.Label).Where(l => l != "Exit").ToList();
        Assert.Equal(new[] { "Lookup ANDA…", "Today…", "Review a draft…", "Ask about a selection…", "More", "Settings…", "Help" }, focused);   // seven choices, and Exit
        Assert.Contains("News and alerts…", Doc.Value.GetRawText());                                       // the simulation has division news; the app does not yet
    }

    [Fact]
    public void The_app_builds_its_menu_from_the_layout_and_has_an_action_for_every_choice()
    {
        string app = File.ReadAllText(Folder("app", "AndaCompanion", "App.axaml.cs"));
        Assert.Contains("TrayMenu.Build(Make,", app);
        foreach (var entry in TrayMenu.Layout(allTools: true))
            foreach (var label in entry.Items is { } items ? items : new[] { entry.Label })
                Assert.Contains($"\"{label}\" =>", app);
        Assert.Equal(ChangeLog.Version, "Prototype, round " + ChangeLog.Entries[0].Round);
        Assert.Equal(Doc.Value.GetProperty("changes").GetProperty("items").EnumerateArray().Select(x => x.GetString()!).ToList(), ChangeLog.Entries.First(e => e.Round == 35).Items.ToList());
    }
}
