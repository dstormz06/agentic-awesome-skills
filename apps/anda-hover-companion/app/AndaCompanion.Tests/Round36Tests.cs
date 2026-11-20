using System.Text.Json;
using AndaCompanion.Data;
using AndaCompanion.Services;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// Round 36: Today's "Since you last looked" and "Next week". The simulation wrote mockup/round36-golden.json (tools/round36-golden.cjs);
/// the app's rules (Services/LastLook.cs) must give the same answers. All data here is fictional.
/// </summary>
public class Round36Tests
{
    private static string Folder(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "anda-hover-companion") dir = dir.Parent;
        return Path.Combine(new[] { dir?.FullName ?? throw new DirectoryNotFoundException("anda-hover-companion folder not found") }.Concat(parts).ToArray());
    }
    private static readonly Lazy<JsonElement> Doc = new(() => JsonDocument.Parse(File.ReadAllText(Folder("mockup", "round36-golden.json"))).RootElement.Clone());
    private static JsonElement G(string name) => Doc.Value.GetProperty(name);
    private static LastLook.Look? LookOf(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : JsonSerializer.Deserialize<LastLook.Look>(e.GetRawText());
    private static string Json(object? o) => JsonSerializer.Serialize(o);

    [Fact]
    public void The_fingerprint_of_an_open_item_is_the_simulations()
    {
        foreach (var f in G("fingerprints").EnumerateArray())
            Assert.Equal(f.GetProperty("print").GetString(), LastLook.Fingerprint(f.GetProperty("text").GetString()!));
        Assert.Equal(G("lookMax").GetInt32(), LastLook.Max);
        Assert.Equal(G("said").GetProperty("first").GetString(), LastLook.SaidFirst);
        Assert.Equal(G("said").GetProperty("none").GetString(), LastLook.SaidNone);
    }

    [Fact]
    public void The_look_kept_of_the_demo_records_is_the_simulations()
    {
        var want = LookOf(G("demoLook"))!;
        var got = LastLook.Of(new MockAndaRepository().Numbers.Select(n => new MockAndaRepository().Find(n)!), DateTime.Parse(want.At, null, System.Globalization.DateTimeStyles.AdjustToUniversal));
        Assert.Equal(want.At, got.At);
        Assert.Equal(Json(want.Items.OrderBy(k => k.Key)), Json(got.Items.OrderBy(k => k.Key)));
    }

    [Fact]
    public void What_moved_between_two_looks_is_the_simulations_for_every_case()
    {
        var words = new Dictionary<string, string> { ["216482"] = "IR response due in 5 days — Biopharmaceutics", ["300001"] = "Labeling comments due" };
        int n = 0;
        foreach (var c in G("cases").EnumerateArray())
        {
            var (rows, more) = LastLook.Since(LookOf(c.GetProperty("before")), LookOf(c.GetProperty("now")), id => words.TryGetValue(id, out var w) ? w : null);
            var want = c.GetProperty("result");
            Assert.Equal(want.GetProperty("rows").EnumerateArray().Select(r => (r.GetProperty("id").GetString(), r.GetProperty("kind").GetString(), r.GetProperty("text").GetString())).ToList(),
                rows.Select(r => ((string?)r.Id, (string?)r.Kind, (string?)r.Text)).ToList());
            Assert.Equal(want.GetProperty("more").GetInt32(), more);
            n++;
        }
        Assert.Equal(8, n);
    }

    [Fact]
    public void Next_week_with_records_made_for_it_is_the_simulations()
    {
        var template = new MockAndaRepository().Find("216482")!; var cal = new BusinessCalendar();
        static DateOnly? D(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : DateOnly.Parse(e.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        var records = G("made").GetProperty("records").EnumerateObject().Select(p => template with
        {
            Number = p.Name, GoalDateValue = D(p.Value.GetProperty("goal")), ForecastDateValue = D(p.Value.GetProperty("forecast")),
            ForecastDate = D(p.Value.GetProperty("forecast")) is null ? Models.AndaRecord.Unknown : "set",
        }).Reverse().ToList();                                                                        // the order given must not matter
        int n = 0;
        foreach (var w in G("made").GetProperty("weeks").EnumerateArray())
        {
            var got = NextWeek.Of(DateOnly.Parse(w.GetProperty("today").GetString()!, System.Globalization.CultureInfo.InvariantCulture), w.GetProperty("hour").GetInt32(), records, cal);
            var want = w.GetProperty("result"); string why = w.GetProperty("why").GetString()!;
            if (want.ValueKind == JsonValueKind.Null) { Assert.True(got is null, why); n++; continue; }
            Assert.Equal(want.GetProperty("title").GetString(), got!.Title);
            Assert.Equal(want.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("id").GetString() + " " + r.GetProperty("text").GetString()).ToList(), got.Rows.Select(r => r.Id + " " + r.Text).ToList());
            Assert.Equal(want.GetProperty("holidays").EnumerateArray().Select(x => x.GetString()).ToList(), got.Holidays.ToList<string?>());
            n++;
        }
        Assert.Equal(3, n);
        // the app's own guard: a record whose forecast is shown as unknown is never listed by a forecast date it may still carry
        var odd = template with { Number = "320009", GoalDateValue = null, ForecastDate = Models.AndaRecord.Unknown, ForecastDateValue = new DateOnly(2026, 7, 1) };
        Assert.Equal(NextWeek.SaidEmpty, NextWeek.Of(new DateOnly(2026, 6, 26), 9, new[] { odd }, cal)!.Empty);
    }

    [Fact]
    public void Data_that_cannot_be_read_is_neither_compared_nor_kept_and_other_data_is_not_compared()
    {
        var repo = new MockAndaRepository(); var recs = repo.Numbers.Select(x => repo.Find(x)!).ToList();
        var kept = LastLook.Of(recs, new DateTime(2026, 7, 15, 21, 5, 0, DateTimeKind.Utc), "file:0badf00d");
        var now = LastLook.Of(recs, new DateTime(2026, 7, 16, 14, 42, 0, DateTimeKind.Utc), "file:0badf00d");
        Assert.Equal((kept, (string?)null, true), LastLook.Decide(kept, now, readable: true));
        Assert.Equal((null, LastLook.SaidUnreadable, false), LastLook.Decide(kept, now, readable: false));
        Assert.Equal((null, LastLook.SaidUnreadable, false), LastLook.Decide(kept, LastLook.Of(Array.Empty<Models.AndaRecord>(), DateTime.UtcNow, "file:0badf00d"), readable: true));   // no ANDA in it
        Assert.Equal((null, LastLook.SaidOtherData, true), LastLook.Decide(kept, now with { Source = "demo" }, readable: true));
        Assert.Equal((null, (string?)null, true), LastLook.Decide(null, now, readable: true));
        Assert.Equal("1 more change; the card of each ANDA shows its own.", LastLook.SaidMore(1));
        Assert.Equal("2 more changes; the card of each ANDA shows its own.", LastLook.SaidMore(2));
        // the source is kept in the file, and an older file without one reads as other data
        string path = Path.Combine(Temp.NewDir(), "last-look.json");
        LastLook.Save(path, now); Assert.Equal("file:0badf00d", LastLook.Load(path)!.Source);
        Assert.Equal(LastLook.SaidOtherData, LastLook.Decide(now with { Source = null }, now, true).Note);
        // the view Today is given: the decision, with the time the compared look was kept, in the person's zone
        var ny = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        Assert.Equal(new LastLook.View(kept, now, "Compared with what this PC kept when you last closed Today: Jul 15, 2026, 5:05 PM EDT.", true), LastLook.ViewOf(kept, now, true, ny));
        Assert.Equal(new LastLook.View(null, now, LastLook.SaidUnreadable, false), LastLook.ViewOf(kept, now, false, ny));
        Assert.Equal(new LastLook.View(null, now, null, true), LastLook.ViewOf(null, now, true, ny));
    }

    [Fact]
    public void A_kept_file_with_dates_that_cannot_be_read_does_not_stop_Today()
    {
        string path = Path.Combine(Temp.NewDir(), "last-look.json");
        File.WriteAllText(path, "{\"at\":\"not a time\",\"items\":{\"300001\":{\"goal\":\"x\",\"forecast\":\"2027-02-30\",\"item\":null}}}");
        var kept = LastLook.Load(path)!;
        var now = new LastLook.Look("2026-07-16T14:42:00.000Z", new Dictionary<string, LastLook.Item> { ["300001"] = new("2027-02-01", "2027-02-10", null) });
        var (rows, _) = LastLook.Since(kept, now, _ => null);
        Assert.Equal(new[] { "Goal date set: Mon Feb 1.", "Forecast set: Wed Feb 10." }, rows.Select(r => r.Text));
    }

    [Fact]
    public void A_kept_file_locked_for_a_moment_is_not_set_aside()
    {
        string dir = Temp.NewDir(), path = Path.Combine(dir, "last-look.json");
        File.WriteAllText(path, "{\"at\":\"2026-07-16T14:42:00.000Z\",\"items\":{}}");
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Null(LastLook.Load(path));
        Assert.True(File.Exists(path));                                                               // still there, read next time
        Assert.Empty(Directory.GetFiles(dir, "*unreadable*"));
        Assert.NotNull(LastLook.Load(path));
    }

    [Fact]
    public void Next_week_is_shown_and_lists_what_the_simulation_lists_on_every_day()
    {
        var repo = new MockAndaRepository(); var records = repo.Numbers.Select(x => repo.Find(x)!).ToList(); var cal = new BusinessCalendar();
        int n = 0;
        foreach (var w in G("weeks").EnumerateArray())
        {
            var got = NextWeek.Of(DateOnly.Parse(w.GetProperty("today").GetString()!, System.Globalization.CultureInfo.InvariantCulture), w.GetProperty("hour").GetInt32(), records, cal);
            var want = w.GetProperty("result");
            string why = w.GetProperty("why").GetString()!;
            if (want.ValueKind == JsonValueKind.Null) { Assert.True(got is null, why); n++; continue; }
            Assert.NotNull(got);
            Assert.Equal(want.GetProperty("title").GetString(), got!.Title);
            Assert.Equal(want.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("id").GetString() + " " + r.GetProperty("text").GetString()).ToList(), got.Rows.Select(r => r.Id + " " + r.Text).ToList());
            Assert.Equal(want.GetProperty("holidays").EnumerateArray().Select(x => x.GetString()).ToList(), got.Holidays.ToList<string?>());
            Assert.Equal(want.GetProperty("empty").ValueKind == JsonValueKind.Null ? null : want.GetProperty("empty").GetString(), got.Empty);
            n++;
        }
        Assert.Equal(12, n);
    }

    [Fact]
    public void A_look_is_kept_on_this_PC_and_a_file_that_cannot_be_read_is_set_aside_not_written_over()
    {
        string dir = Temp.NewDir(), path = Path.Combine(dir, "last-look.json");
        Assert.Null(LastLook.Load(path));
        var repo = new MockAndaRepository(); var look = LastLook.Of(repo.Numbers.Select(x => repo.Find(x)!), new DateTime(2026, 7, 16, 14, 42, 0, DateTimeKind.Utc));
        Assert.True(LastLook.Save(path, look));
        var again = LastLook.Load(path)!;
        Assert.Equal(Json(look.Items), Json(again.Items)); Assert.Equal(look.At, again.At);
        string text = File.ReadAllText(path);
        Assert.DoesNotContain("IR response", text);                  // the item's words are never kept, only its fingerprint
        Assert.DoesNotContain("Metformin", text);                    // nor anything else of the record
        File.WriteAllText(path, "{ not json");
        Assert.Null(LastLook.Load(path));
        Assert.False(File.Exists(path));                              // set aside ...
        Assert.Single(Directory.GetFiles(dir), f => f.Contains("last-look") && f.Contains("unreadable"));   // ... and kept, never deleted
    }
}
