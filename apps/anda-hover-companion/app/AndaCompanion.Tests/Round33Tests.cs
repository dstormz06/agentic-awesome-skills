using System.Text.Json;
using AndaCompanion.Data;
using AndaCompanion.Models;
using AndaCompanion.Services;
using AndaCompanion.Services.Portfolio;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// Round 33: five features the simulation has had since rounds 18 and 19 come to the Windows app (card details with Copy, text size and
/// spacing, recent and kept ANDAs, One Bar, Today). The simulation wrote mockup/round33-golden.json (tools/round33-golden.cjs); the app
/// must give the same answers. All data here is fictional.
/// </summary>
[Collection("Global state")]
public class Round33Tests
{
    private static string Folder(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "anda-hover-companion") dir = dir.Parent;
        return Path.Combine(new[] { dir?.FullName ?? throw new DirectoryNotFoundException("anda-hover-companion folder not found") }.Concat(parts).ToArray());
    }
    private static readonly Lazy<JsonElement> Doc = new(() => JsonDocument.Parse(File.ReadAllText(Folder("mockup", "round33-golden.json"))).RootElement.Clone());
    private static JsonElement G(string name) => Doc.Value.GetProperty(name);
    private static string? S(JsonElement e) => e.ValueKind == JsonValueKind.String ? e.GetString() : null;
    private static List<string> L(JsonElement e) => e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().Select(x => x.GetString()!).ToList() : new();

    private static readonly MockAndaRepository Demo = new();
    private static readonly DateOnly Day = new(2026, 7, 16);
    internal static Today.Look Look(AndaRecord r, DateOnly? today = null)
    {
        var cal = new BusinessCalendar();
        return new Today.Look(r, UrgencyPolicy.IsUrgent(r, today ?? Day, cal), WatchRules.Evaluate(r, today ?? Day, cal));
    }
    private static Today.Look? Find(string id) => Demo.Find(id) is { } r ? Look(r) : null;

    // ---------- card details ----------
    [Fact]
    public void The_columns_a_card_can_show_and_the_usual_choice_are_the_simulations()
    {
        Assert.Equal(G("detailFields").EnumerateArray().Select(f => (f[0].GetString()!, f[1].GetString()!)).ToList(), CardDetails.Fields.ToList());
        Assert.Equal(L(G("detailDefault")), CardDetails.DefaultFields.ToList());
        Assert.Equal(G("notInSheet").GetString(), CardDetails.NotInSheet);
        Assert.Equal(G("noneChosen").GetString(), CardDetails.NoneChosen);
        foreach (var c in G("copied").EnumerateArray()) Assert.Equal(c.GetProperty("text").GetString(), CardDetails.Copied(c.GetProperty("what").GetString()!));
    }

    [Fact]
    public void Every_value_of_the_two_sample_records_is_the_simulations()
    {
        int n = 0;
        foreach (var c in G("detailValues").EnumerateArray())
        {
            var r = Demo.Find(c.GetProperty("id").GetString()!)!; string key = c.GetProperty("key").GetString()!;
            Assert.Equal(S(c.GetProperty("value")), CardDetails.Value(r, key)); n++;
        }
        Assert.Equal(28, n);
    }

    [Fact]
    public void What_the_details_show_for_18_choices_of_columns_is_the_simulations()
    {
        int n = 0;
        foreach (var c in G("details").EnumerateArray())
        {
            var r = Demo.Find(c.GetProperty("id").GetString()!)!; var want = c.GetProperty("result");
            var view = CardDetails.Rows(r, c.GetProperty("fields").ValueKind == JsonValueKind.Array ? L(c.GetProperty("fields")) : null);      // what is not a list of columns chooses none
            Assert.Equal(want.GetProperty("rows").EnumerateArray().Select(x => (x.GetProperty("key").GetString(), x.GetProperty("label").GetString(), S(x.GetProperty("value")), x.GetProperty("text").GetString(), x.GetProperty("copy").GetBoolean(), x.GetProperty("copyName").GetString())).ToList(),
                view.Rows.Select(x => ((string?)x.Key, (string?)x.Label, x.Value, (string?)x.Text, x.Copy, (string?)x.CopyName)).ToList());
            Assert.Equal((want.GetProperty("have").GetInt32(), want.GetProperty("count").GetString(), want.GetProperty("empty").GetString(), want.GetProperty("copyAll").GetBoolean(), want.GetProperty("block").GetString()),
                (view.Have, view.Count, view.Empty, view.CopyAll, view.Block));
            n++;
        }
        Assert.Equal(18, n);
    }

    [Fact]
    public void A_portfolio_file_gives_its_own_columns_to_the_details_and_what_it_lacks_is_said_to_be_missing()
    {
        string dir = Temp.NewDir(), path = Path.Combine(dir, "Portfolio.csv");
        File.WriteAllText(path, "ANDA,Product,Applicant,Applicant short name,Established Name,Project description,U.S. Agent,US agent email,Applicant e-mail,My notes,Form 356h,Module 3.2.P.1,Team Lead,Division,FEI,Goal date\n" +
            "930001,Alpha tabs,Birch Labs (fictional),Birch,alphazine tablets,Cycle 1,Agent Co (fictional),agent@agent.example,reg@birch.example,Call Tuesday,S:\\930001\\356h.pdf,S:\\930001\\32p1.pdf,Lead B (demo),Division X (demo),3000000001,2027-03-02\n" +
            "930002,Beta tabs,Birch Labs (fictional),,,,,,,,,,,,,2027-04-06\n");
        var repo = new PortfolioRepository(path, null, null, () => new DateOnly(2026, 11, 19));
        var all = CardDetails.Fields.Select(f => f.Key).ToList();
        var full = CardDetails.Rows(repo.Find("930001")!, all);
        Assert.Equal(12, full.Have); Assert.Equal("· 12 of 12 in your sheet", full.Count);
        Assert.Equal(new[] { "Birch Labs (fictional) (Birch)", "alphazine tablets", "Cycle 1", "Agent Co (fictional)", "agent@agent.example", "reg@birch.example", "Call Tuesday", "S:\\930001\\356h.pdf", "S:\\930001\\32p1.pdf", "Lead B (demo)", "Division X (demo)", "3000000001" },
            full.Rows.Select(x => x.Text).ToArray());
        Assert.StartsWith("ANDA 930001\nApplicant: Birch Labs (fictional) (Birch)\nEstablished name: alphazine tablets\n", full.Block);
        var thin = CardDetails.Rows(repo.Find("930002")!, all);
        Assert.Equal(1, thin.Have); Assert.Equal("Birch Labs (fictional)", thin.Rows[0].Text);      // no short name in this row: the applicant as it is
        Assert.All(thin.Rows.Skip(1), x => { Assert.Equal(CardDetails.NotInSheet, x.Text); Assert.False(x.Copy); Assert.Null(x.Value); });
        Assert.Equal("ANDA 930002\nApplicant: Birch Labs (fictional)", thin.Block);
        Assert.Equal("From Portfolio.csv.", CardDetails.Source(repo.Find("930001")!, "Portfolio.csv"));
        Assert.Equal("From the fictional demo records.", CardDetails.Source(Demo.Find("216482")!, null));
    }

    [Fact]
    public void A_detail_column_never_takes_a_column_the_card_already_uses_and_a_sheet_without_them_still_loads()
    {
        string dir = Temp.NewDir(), path = Path.Combine(dir, "Plain.csv");
        File.WriteAllText(path, "ANDA,Drug,Applicant,Status,Goal date\n930003,Gamma caps,Cedar (fictional),In review,2027-05-04\n");
        var repo = new PortfolioRepository(path, null, null, () => new DateOnly(2026, 11, 19));
        Assert.True(repo.Status.Ok);
        var view = CardDetails.Rows(repo.Find("930003")!, CardDetails.DefaultFields);
        Assert.Equal(1, view.Have); Assert.Equal("· 1 of 9 in your sheet", view.Count); Assert.True(view.CopyAll);
        // "Description" names the project description only when it is not already the drug column
        var headers = new[] { "ANDA", "Description", "Goal date", "Agent" };
        var map = PortfolioColumns.Detect(headers);
        var more = PortfolioColumns.DetectDetails(headers, map);
        Assert.Equal("Description", map["notes"]);                                                   // as before round 33: a column headed "Description" is the notes
        Assert.Equal("Description", more["project"]); Assert.Equal("", more["myNotes"]);             // it is shown as the project description, and there are no notes besides it
        Assert.Equal("Agent", more["agentCo"]);
        Assert.Empty(PortfolioColumns.DetectDetails(new[] { "ANDA", "Goal date" }, PortfolioColumns.Detect(new[] { "ANDA", "Goal date" })));
        // a column headed "Established name" that serves as the drug is shown under that name too; one that is free is simply used
        var shared = new[] { "ANDA", "Established name" }; Assert.Equal("Established name", PortfolioColumns.DetectDetails(shared, PortfolioColumns.Detect(shared))["established"]);
        // a sheet whose notes column is the project description and has no other notes: "My notes" is not in the sheet, the project is
        string p2 = Path.Combine(dir, "Project.csv");
        File.WriteAllText(p2, "ANDA,Project description\n930004,Second cycle\n");
        var r4 = new PortfolioRepository(p2, null, null, () => new DateOnly(2026, 11, 19)).Find("930004")!;
        Assert.Equal("Second cycle", CardDetails.Value(r4, "project")); Assert.Null(CardDetails.Value(r4, "myNotes"));
    }

    [Fact]
    public void The_chosen_columns_are_kept_in_the_fixed_order_and_only_known_ones()
    {
        Assert.Equal(new[] { "applicant", "fei" }, CardDetails.Normalize(new[] { "fei", "nothing", "applicant", "fei" }));
        Assert.Empty(CardDetails.Normalize(null)); Assert.Empty(CardDetails.Normalize(Array.Empty<string>()));
        Assert.Null(CardDetails.Value(new MockAndaRepository().Find("216482")! with { Details = null }, "applicant"));
    }

    // ---------- recent and kept ----------
    [Fact]
    public void Thirty_two_presses_leave_the_lists_as_the_simulation_leaves_them()
    {
        Assert.Equal((G("recentMax").GetInt32(), G("pinMax").GetInt32(), G("pinFull").GetString()), (Recents.RecentMax, Recents.PinMax, Recents.PinFull));
        Assert.Equal(L(G("pinLabels")), new List<string> { Recents.PinLabel(false), Recents.PinLabel(true) });
        Assert.Equal((G("recentsSaid").GetProperty("off").GetString(), G("recentsSaid").GetProperty("cleared").GetString()), (Recents.SaidOff, Recents.SaidCleared));
        List<string> recents = new(), pins = new(); bool on = true; int n = 0;
        bool Known(string x) => Demo.Find(x) is not null || x.StartsWith("1000", StringComparison.Ordinal);
        foreach (var c in G("lists").EnumerateArray())
        {
            string what = c.GetProperty("what").GetString()!, id = c.GetProperty("id").GetString()!; string? result = null;
            if (what == "look") recents = Recents.After(recents, id, on);
            else if (what == "keep") (pins, result) = Recents.PinAfter(pins, id);
            else if (what == "off") { on = false; recents = new(); } else if (what == "on") on = true; else recents = new();
            string at = $"step {n}: {what} {id}";
            Assert.True(S(c.GetProperty("result")) == result, at);
            Assert.True(L(c.GetProperty("recents")).SequenceEqual(recents), at + " recents"); Assert.True(L(c.GetProperty("pins")).SequenceEqual(pins), at + " pins");
            var listed = Recents.Listed(pins, recents, Known);
            Assert.True(L(c.GetProperty("listed")).SequenceEqual(listed), at + " listed");
            Assert.True(L(c.GetProperty("names")).SequenceEqual(listed.Select(x => Recents.Name(x, pins.Contains(x)))), at + " names");
            n++;
        }
        Assert.Equal(32, n);
    }

    [Fact]
    public void What_is_kept_is_cleaned_when_it_is_read_and_a_press_never_changes_the_list_it_was_given()
    {
        Assert.Equal(new[] { "216482", "214990" }, Recents.Clean(new[] { "216482", "abc", "216482", "21499", "214990", "２１６４８２" }, 8));
        Assert.Equal(5, Recents.Clean(Enumerable.Range(100000, 20).Select(i => i.ToString()), Recents.PinMax).Count);
        Assert.Empty(Recents.Clean(null, 8));
        var before = new List<string> { "216482" };
        _ = Recents.After(before, "214990", true); _ = Recents.PinAfter(before, "214990");
        Assert.Equal(new[] { "216482" }, before);
        Assert.Equal("refused", Recents.PinAfter(before, null).Result); Assert.Equal("refused", Recents.PinAfter(before, "1234567").Result);
        Assert.False(Recents.Six("２１６４８２")); Assert.False(Recents.Six(null)); Assert.True(Recents.Six("000000"));
    }

    // ---------- One Bar ----------
    [Fact]
    public void One_Bar_offers_the_simulations_rows_for_52_pairs_of_typed_and_selected_text()
    {
        foreach (var c in G("hero").EnumerateArray()) Assert.Equal(c.GetProperty("text").GetString(), OneBar.Hero(Demo.Find(c.GetProperty("id").GetString()!)!));
        int n = 0;
        foreach (var c in G("bar").EnumerateArray())
        {
            string typed = c.GetProperty("typed").GetString()!, sel = c.GetProperty("sel").GetString()!;
            var want = c.GetProperty("rows").EnumerateArray().Select(x => (x.GetProperty("ic").GetString()!, x.GetProperty("t").GetString()!, x.GetProperty("s").GetString()!, x.GetProperty("act").GetString()!, x.GetProperty("text").GetString()!)).ToList();
            var got = OneBar.Rows(typed, sel, Demo.Find).Select(x => (x.Icon, x.Title, x.Sub, x.Act, x.Text)).ToList();
            Assert.True(want.SequenceEqual(got), $"typed {JsonSerializer.Serialize(typed)}, selected {JsonSerializer.Serialize(sel)}: {JsonSerializer.Serialize(got.Select(g => g.Title))}");
            n++;
        }
        Assert.Equal(52, n);
        Assert.All(OneBar.Rows(null, null, Demo.Find), r => Assert.Contains(r.Act, new[] { "draft", "ask" }));
    }

    [Fact]
    public void The_lookup_window_says_why_what_was_typed_is_not_an_ANDA_number()
    {
        int n = 0;
        foreach (var c in G("lookupErrors").EnumerateArray()) { Assert.Equal(c.GetProperty("text").GetString(), OneBar.LookupError(S(c.GetProperty("typed")))); n++; }
        Assert.Equal(18, n);
    }

    // ---------- Today ----------
    [Fact]
    public void Today_lists_and_says_what_the_simulation_lists_and_says()
    {
        Assert.Equal((G("todaySaid").GetProperty("nothing").GetString(), G("todaySaid").GetProperty("choose").GetString()), (Today.SaidNothing, Today.SaidChoose));
        var all = Demo.Numbers.Select(x => Find(x)!).ToList();
        Assert.Equal(2, all.Count);
        int n = 0;
        foreach (var c in G("today").EnumerateArray())
        {
            var got = Today.Groups(L(c.GetProperty("pins")), L(c.GetProperty("recents")), all, Find);
            var want = c.GetProperty("groups").EnumerateArray().ToList();
            Assert.Equal(want.Count, got.Count);
            for (int i = 0; i < want.Count; i++)
            {
                Assert.Equal(want[i].GetProperty("title").GetString(), got[i].Title);
                Assert.Equal(L(want[i].GetProperty("ids")), got[i].Ids.ToList());
                Assert.Equal(want[i].GetProperty("rows").EnumerateArray().Select(x => (x.GetProperty("id").GetString()!, x.GetProperty("mark").GetString()!, x.GetProperty("line").GetString()!)).ToList(),
                    got[i].Rows.Select(x => (x.Id, x.Mark, x.Line)).ToList());
                Assert.Equal(0, got[i].More);
            }
            n++;
        }
        Assert.Equal(4, n);
        foreach (var c in G("todayMain").EnumerateArray())
        {
            var w = c.GetProperty("result"); var m = Today.Main(Find(c.GetProperty("id").GetString()!)!);
            Assert.Equal((w.GetProperty("meta").GetString(), w.GetProperty("tone").GetString(), w.GetProperty("figure").GetString(), w.GetProperty("words").GetString(), w.GetProperty("dates").GetString(), S(w.GetProperty("attn")), S(w.GetProperty("watch")), w.GetProperty("status").GetString()),
                (m.Meta, m.Tone, m.Figure, m.Words, m.Dates, m.Attn, m.Watch, m.Status));
        }
    }

    [Fact]
    public void A_record_without_a_forecast_says_so_and_a_large_portfolio_shows_forty_and_points_to_the_watch_list()
    {
        // The simulation has no record without a forecast and only two records; these answers are the app's own.
        string dir = Temp.NewDir(), path = Path.Combine(dir, "Many.csv");
        File.WriteAllLines(path, new[] { "ANDA,Product,Goal date,Forecast date" }.Concat(Enumerable.Range(0, 60).Select(i => $"{940000 + i},Drug {i},2026-11-21,{(i == 0 ? "" : i == 1 ? "2026-11-21" : i == 2 ? "2026-11-20" : "2026-12-25")}")));
        var today = new DateOnly(2026, 11, 19);
        var repo = new PortfolioRepository(path, null, null, () => today);
        var all = repo.Numbers.Select(x => Look(repo.Find(x)!, today)).ToList();
        var none = all.Single(x => x.Record.Number == "940000");
        Assert.Equal("No forecast in your data", Today.Line(none.Record)); Assert.Equal("No forecast in your data", OneBar.Hero(none.Record));
        var main = Today.Main(none);
        Assert.Equal((AndaRecord.Unknown, "no forecast in your data", "good"), (main.Figure, main.Words, main.Tone));
        Assert.Equal("On the goal date", Today.Line(all.Single(x => x.Record.Number == "940001").Record)); Assert.Equal("On", Today.Main(all.Single(x => x.Record.Number == "940001")).Figure);
        Assert.Equal("1 d ahead", Today.Line(all.Single(x => x.Record.Number == "940002").Record)); Assert.Equal("day ahead of the goal", Today.Main(all.Single(x => x.Record.Number == "940002")).Words);
        var groups = Today.Groups(new[] { "940005", "999999" }, new[] { "940005", "940006" }, all, id => repo.Find(id) is { } r ? Look(r, today) : null);
        Assert.Equal(Today.NeedLookMax, groups[0].Rows.Count); Assert.Equal(20, groups[0].More); Assert.Equal("20 more are in the watch list.", Today.More(20)); Assert.Equal("1 more is in the watch list.", Today.More(1));
        Assert.Equal("r", groups[0].Rows[0].Mark);                                                      // the most pressing first
        Assert.True(groups[0].Rows.Select(x => "rwg".IndexOf(x.Mark, StringComparison.Ordinal)).SequenceEqual(groups[0].Rows.Select(x => "rwg".IndexOf(x.Mark, StringComparison.Ordinal)).OrderBy(x => x)));
        Assert.Equal(new[] { "940005" }, groups[1].Ids.ToArray());                                      // a kept number the data does not know is not listed
        Assert.Equal(new[] { "940006" }, groups[2].Ids.ToArray());                                      // a kept one is not listed again as recent
    }

    // ---------- display ----------
    [Fact]
    public void Text_size_and_spacing_use_the_simulations_factors()
    {
        int n = 0;
        foreach (var c in G("display").EnumerateObject())
        {
            var parts = c.Name.Split('/');
            Assert.Equal(c.Value.GetProperty("text").GetDouble(), Display.TextScale(parts[0])); Assert.Equal(c.Value.GetProperty("spacing").GetDouble(), Display.Spacing(parts[1])); n++;
        }
        Assert.Equal(4, n);
        Assert.Equal((1.0, 1.0, "standard", "comfortable"), (Display.TextScale("huge"), Display.Spacing(null), Display.TextSize(null), Display.Density("tight")));
    }

    // ---------- settings ----------
    [Fact]
    public void Settings_move_to_version_5_by_themselves_with_everything_as_it_looked_before()
    {
        string path = Path.Combine(Temp.NewDir(), "settings.json");
        File.WriteAllText(path, "{\"SchemaVersion\":4,\"Tone\":\"Warm\",\"TimeZone\":\"America/Denver\",\"RefStaleAfterDays\":90}");      // written by round 32
        var s = SettingsService.Load(path);
        Assert.Equal((5, "Warm", "America/Denver", 90), (s.SchemaVersion, s.Tone, s.TimeZone, s.RefStaleAfterDays));
        Assert.Equal(("standard", "comfortable", "box", true), (s.TextSize, s.Density, s.LookupStyle, s.RememberRecents));
        Assert.Empty(s.Recents); Assert.Empty(s.Pins); Assert.Equal(CardDetails.DefaultFields.ToList(), s.CardFields);
        Assert.Equal(5, Settings.CurrentSchema); Assert.Null(SettingsService.LoadNote);
        s.TextSize = "xl"; s.Density = "compact"; s.LookupStyle = "bar"; s.Recents = new() { "216482" }; s.Pins = new() { "214990" }; s.CardFields = new() { "fei" };
        Assert.True(SettingsService.Save(s, path)); Assert.Contains("\"SchemaVersion\": 5", File.ReadAllText(path));
        var again = SettingsService.Load(path);
        Assert.Equal(("xl", "compact", "bar"), (again.TextSize, again.Density, again.LookupStyle));
        Assert.Equal(new[] { "216482" }, again.Recents.ToArray()); Assert.Equal(new[] { "214990" }, again.Pins.ToArray()); Assert.Equal(new[] { "fei" }, again.CardFields.ToArray());
        File.WriteAllText(path, "{\"SchemaVersion\":1,\"Tone\":\"Warm\"}");                                                      // four versions back: every step is taken
        Assert.Equal((5, "standard", 45), (SettingsService.Load(path).SchemaVersion, SettingsService.Load(path).TextSize, SettingsService.Load(path).RefStaleAfterDays));
    }

    [Fact]
    public void A_setting_that_is_not_one_of_the_choices_goes_back_to_its_usual_value_and_the_lists_are_cleaned()
    {
        string path = Path.Combine(Temp.NewDir(), "settings.json");
        File.WriteAllText(path, "{\"SchemaVersion\":5,\"TextSize\":\"enormous\",\"Density\":\"tight\",\"LookupStyle\":\"wheel\",\"Recents\":[\"216482\",\"x\",\"216482\",\"1\"],\"Pins\":[\"100001\",\"100002\",\"100003\",\"100004\",\"100005\",\"100006\",\"bad\"],\"CardFields\":[\"fei\",\"nothing\",\"applicant\"]}");
        var s = SettingsService.Load(path);
        Assert.Equal(("standard", "comfortable", "box"), (s.TextSize, s.Density, s.LookupStyle));
        Assert.Equal(new[] { "216482" }, s.Recents.ToArray()); Assert.Equal(5, s.Pins.Count); Assert.Equal(new[] { "applicant", "fei" }, s.CardFields.ToArray());
        File.WriteAllText(path, "{\"SchemaVersion\":5,\"RememberRecents\":false,\"Recents\":[\"216482\"],\"CardFields\":[]}");
        s = SettingsService.Load(path);
        Assert.Empty(s.Recents);                                                                     // switched off: nothing is kept
        Assert.Empty(s.CardFields);                                                                  // choosing no column is a choice, not a mistake
        File.WriteAllText(path, "{\"SchemaVersion\":5,\"CardFields\":null,\"Recents\":null,\"Pins\":null}");
        s = SettingsService.Load(path);
        Assert.Equal(CardDetails.DefaultFields.ToList(), s.CardFields); Assert.Empty(s.Recents); Assert.Empty(s.Pins);
    }

    [Fact]
    public void A_backup_carries_the_kept_ANDAs_and_the_display_choices_and_never_the_recent_ones()
    {
        string dir = Temp.NewDir();
        var lenses = new PromptLibraryService(Path.Combine(dir, "lenses.json")); var replies = new QuickReplyService(Path.Combine(dir, "replies.json"));
        var mine = new Settings { TextSize = "large", Density = "compact", LookupStyle = "bar", Pins = new() { "214990" }, Recents = new() { "216482" }, CardFields = new() { "applicant", "fei" }, RememberRecents = false };
        string json = SettingsBackup.Export(mine, lenses, replies);
        using (var doc = JsonDocument.Parse(json)) Assert.Empty(doc.RootElement.GetProperty("Settings").GetProperty("Recents").EnumerateArray());
        var other = new Settings { Recents = new() { "100001" } };
        var (ok, _) = SettingsBackup.Import(json, other, lenses, replies);
        Assert.True(ok);
        Assert.Equal(("large", "compact", "bar", false), (other.TextSize, other.Density, other.LookupStyle, other.RememberRecents));
        Assert.Equal(new[] { "214990" }, other.Pins.ToArray()); Assert.Equal(new[] { "applicant", "fei" }, other.CardFields.ToArray());
        Assert.Equal(new[] { "100001" }, other.Recents.ToArray());                                     // this PC's own recent numbers are left as they are
        // a backup made before round 33 holds none of these: the person's choices are left as they are
        var before = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
        var held = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(before["Settings"].GetRawText())!;
        foreach (var k in new[] { "TextSize", "Density", "LookupStyle", "RememberRecents", "Recents", "Pins", "CardFields" }) held.Remove(k);
        before["Settings"] = JsonSerializer.SerializeToElement(held);
        var kept = new Settings { TextSize = "xl", Pins = new() { "216482" }, CardFields = new() { "division" }, LookupStyle = "bar" };
        Assert.True(SettingsBackup.Import(JsonSerializer.Serialize(before), kept, lenses, replies).Ok);
        Assert.Equal(("xl", "bar"), (kept.TextSize, kept.LookupStyle)); Assert.Equal(new[] { "216482" }, kept.Pins.ToArray()); Assert.Equal(new[] { "division" }, kept.CardFields.ToArray());
    }
}
