using System.Text.Json;
using AndaCompanion.Services;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// Round 42: the timeline checks (immediate and longer term), the summary for a team lead, the goal dates as a calendar file, the division
/// news read from paragraphs and the limits, held to the simulation's answers in mockup/round42-golden.json. Fictional numbers only.
/// </summary>
public class Round42Tests
{
    private static string Folder(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "anda-hover-companion") dir = dir.Parent;
        return Path.Combine(new[] { dir?.FullName ?? throw new DirectoryNotFoundException("anda-hover-companion folder not found") }.Concat(parts).ToArray());
    }
    internal static readonly Lazy<JsonElement> Gold = new(() => JsonDocument.Parse(File.ReadAllText(Folder("mockup", "round42-golden.json"))).RootElement.Clone());
    private static string? S(JsonElement e) => e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    /// <summary>A limit as the simulation reads it: kept only when it is a whole number (a string, null or 30.5 is not).</summary>
    private static TimelineSettings SettingsOf(JsonElement e)
    {
        var s = new TimelineSettings();
        foreach (var (key, _, _) in TimelineSettings.Limits)
            s.Set(key, e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : -1);
        return s;
    }

    internal static TimelineInput InputOf(JsonElement i) => new(
        i.GetProperty("today").GetString()!,
        i.GetProperty("records").EnumerateArray().Select(r => new TimelineRecord(r.GetProperty("number").GetString()!, S(r.GetProperty("drug")), S(r.GetProperty("goal")), S(r.GetProperty("forecast")))).ToList(),
        i.GetProperty("look").ValueKind == JsonValueKind.Object
            ? i.GetProperty("look").EnumerateObject().ToDictionary(p => p.Name, p => (S(p.Value.GetProperty("goal")), S(p.Value.GetProperty("forecast"))))
            : null,
        i.GetProperty("todos").EnumerateArray().Select(t => new TimelineTodo(S(t.GetProperty("id")), S(t.GetProperty("number")), S(t.GetProperty("title")), S(t.GetProperty("due")), t.GetProperty("done").GetBoolean())).ToList(),
        i.GetProperty("statusSince").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!),
        SettingsOf(i.GetProperty("settings")));

    internal static string Show(TimelineAlert a) => $"{a.Key} · {a.Number ?? "null"} · {a.Code} · {a.Horizon} · {a.Sev} · {a.Date ?? "null"} · {a.Text}";
    private static string ShowGold(JsonElement a) => $"{a.GetProperty("key").GetString()} · {S(a.GetProperty("number")) ?? "null"} · {a.GetProperty("code").GetString()} · {a.GetProperty("horizon").GetString()} · {a.GetProperty("sev").GetString()} · {S(a.GetProperty("date")) ?? "null"} · {a.GetProperty("text").GetString()}";

    [Fact]
    public void The_timeline_checks_give_the_simulations_answers_in_every_case()
    {
        var cal = new BusinessCalendar();
        foreach (var c in Gold.Value.GetProperty("cases").EnumerateArray())
        {
            var got = Timeline.Alerts(InputOf(c.GetProperty("input")), cal).Select(Show).ToList();
            var want = c.GetProperty("alerts").EnumerateArray().Select(ShowGold).ToList();
            Assert.True(want.SequenceEqual(got), c.GetProperty("name").GetString() + "\nwant:\n" + string.Join("\n", want) + "\ngot:\n" + string.Join("\n", got));
        }
    }

    [Fact]
    public void The_limits_their_choices_labels_and_normalizing_are_the_simulations()
    {
        var g = Gold.Value;
        foreach (var (key, choices, label) in TimelineSettings.Limits)
        {
            Assert.Equal(g.GetProperty("defaults").GetProperty(key).GetInt32(), new TimelineSettings().Get(key));
            Assert.Equal(g.GetProperty("choices").GetProperty(key).EnumerateArray().Select(x => x.GetInt32()), choices);
            Assert.Equal(g.GetProperty("labels").GetProperty(key).GetString(), label);
        }
        var sets = g.GetProperty("cases").EnumerateArray().Where((_, i) => i % 2 == 0).Select(c => c.GetProperty("input").GetProperty("settings")).ToList();
        var normalized = g.GetProperty("normalized").EnumerateArray().ToList();
        for (int i = 0; i < sets.Count; i++)
        {
            var n = TimelineSettings.Normalize(SettingsOf(sets[i]));
            foreach (var (key, _, _) in TimelineSettings.Limits) Assert.Equal(normalized[i].GetProperty(key).GetInt32(), n.Get(key));
        }
        Assert.Equal(new[] { 10, 30, 5, 3, 90, 30, 2 }, TimelineSettings.Limits.Select(l => TimelineSettings.Normalize(null).Get(l.Key)));
    }

    [Fact]
    public void The_summary_the_calendar_file_and_the_sentences_are_the_simulations()
    {
        var g = Gold.Value; var cal = new BusinessCalendar();
        var first = g.GetProperty("cases")[0];
        var input = InputOf(first.GetProperty("input"));
        var alerts = Timeline.Alerts(input, cal);
        string? Drug(string n) => input.Records.FirstOrDefault(r => r.Number == n)?.Drug;
        Assert.Equal(g.GetProperty("summary").GetString(), Timeline.Summary(alerts, input.Today, Drug));
        Assert.Equal(g.GetProperty("summaryNone").GetString(), Timeline.Summary(Array.Empty<TimelineAlert>(), input.Today, Drug));
        Assert.Equal(g.GetProperty("summaryLaterOnly").GetString(), Timeline.Summary(alerts.Where(a => a.Horizon == "later").ToList(), input.Today, Drug));
        var (text, count) = Timeline.Calendar(input.Records, input.Today);
        Assert.Equal(g.GetProperty("calendar").GetProperty("text").GetString(), text);
        Assert.Equal(g.GetProperty("calendar").GetProperty("count").GetInt32(), count);
        var said = g.GetProperty("said");
        Assert.Equal(said.GetProperty("todayTitle").GetString(), Timeline.TodayTitle);
        Assert.Equal(said.GetProperty("todayNone").GetString(), Timeline.TodayNone);
        Assert.Equal(said.GetProperty("todayMore").GetString(), Timeline.TodayMore(3));
        Assert.Equal(said.GetProperty("copied").GetString(), Timeline.SaidCopied(2, 1));
        Assert.Equal(said.GetProperty("calendar").GetString(), Timeline.SaidCalendar(4));
        Assert.Equal(said.GetProperty("hint").GetString(), Timeline.Hint);
    }

    [Fact]
    public void Division_news_is_read_from_dated_paragraphs_as_the_simulation_reads_it()
    {
        var g = Gold.Value;
        var got = DivisionNews.Parse(g.GetProperty("paragraphs").EnumerateArray().Select(p => p.GetString()), g.GetProperty("today").GetString()!);
        var want = g.GetProperty("news").EnumerateArray().Select(n => (n.GetProperty("key").GetString(), n.GetProperty("date").GetString(), n.GetProperty("text").GetString())).ToList();
        Assert.Equal(want, got.Select(n => ((string?)n.Key, (string?)n.Date, (string?)n.Text)).ToList());
    }
}
