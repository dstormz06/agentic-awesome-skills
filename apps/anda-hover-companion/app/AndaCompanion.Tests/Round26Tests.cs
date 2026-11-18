using System.Text.Json;
using AndaCompanion.Services;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// Round 26: Compare two files, the calendar invitation from a selection, the usage summary and the reading prompts.
/// The simulation wrote mockup/compare-golden.json (tools/compare-golden.cjs) with its clock fixed at Jul 16, 2026; the app must give the same answers.
/// </summary>
public class Round26Tests
{
    private static string Folder(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "anda-hover-companion") dir = dir.Parent;
        return Path.Combine(new[] { dir?.FullName ?? throw new DirectoryNotFoundException("anda-hover-companion folder not found") }.Concat(parts).ToArray());
    }

    private static JsonDocument Golden() => JsonDocument.Parse(File.ReadAllText(Folder("mockup", "compare-golden.json")));
    private static string[] Strings(JsonElement e) => e.EnumerateArray().Select(x => x.GetString()!).ToArray();
    private static CompareTable Table(JsonElement e) =>
        new(e.GetProperty("name").GetString()!, Strings(e.GetProperty("headers")), e.GetProperty("rows").EnumerateArray().Select(r => (IReadOnlyList<string>)Strings(r)).ToList());

    [Fact]
    public void Comparing_two_files_gives_the_simulations_answer_for_every_shared_case()
    {
        using var doc = Golden();
        var bad = new List<string>(); int n = 0;
        foreach (var c in doc.RootElement.GetProperty("compare").EnumerateArray())
        {
            n++;
            var a = Table(c.GetProperty("a")); var b = Table(c.GetProperty("b"));
            var r = FileCompare.Compare(a, b, c.GetProperty("key").GetString());
            var want = c.GetProperty("result");
            void Same(string what, string expected, string actual) { if (expected != actual) bad.Add($"case {n} {what}\n  expected: {expected}\n  actual:   {actual}"); }
            Same("key", want.GetProperty("key").GetString() ?? "(none)", r.Key ?? "(none)");
            Same("keyWhy", want.GetProperty("keyWhy").GetString()!, r.KeyWhy);
            Same("shared", string.Join("|", Strings(want.GetProperty("shared"))), string.Join("|", r.Shared));
            Same("counts", $"{want.GetProperty("same")} {want.GetProperty("changedRows")} {want.GetProperty("changedCells")} {want.GetProperty("addedCount")} {want.GetProperty("removedCount")} {want.GetProperty("blankA")} {want.GetProperty("blankB")}",
                $"{r.Same} {r.ChangedRows} {r.ChangedCells} {r.AddedCount} {r.RemovedCount} {r.BlankA} {r.BlankB}");
            Same("added", string.Join("|", Strings(want.GetProperty("added"))), string.Join("|", r.Added));
            Same("removed", string.Join("|", Strings(want.GetProperty("removed"))), string.Join("|", r.Removed));
            Same("dups", string.Join("|", Strings(want.GetProperty("dupA"))) + " / " + string.Join("|", Strings(want.GetProperty("dupB"))), string.Join("|", r.DupA) + " / " + string.Join("|", r.DupB));
            Same("columns", string.Join("|", Strings(want.GetProperty("colsOnlyA"))) + " / " + string.Join("|", Strings(want.GetProperty("colsOnlyB"))), string.Join("|", r.ColsOnlyA) + " / " + string.Join("|", r.ColsOnlyB));
            Same("byColumn", string.Join("|", Strings(want.GetProperty("byColumn"))), string.Join("|", r.ByColumn.Select(x => x.Column + " = " + x.Count)));
            Same("summary", string.Join("\n", Strings(c.GetProperty("summary"))), string.Join("\n", FileCompare.Summary(r, a.Name, b.Name)));
            Same("report", string.Join("\n", Strings(c.GetProperty("report"))), string.Join("\n", FileCompare.Report(r, a.Name, b.Name)));
            Same("paste", string.Join("\n", Strings(c.GetProperty("paste"))), string.Join("\n", FileCompare.ForPaste(FileCompare.Report(r, a.Name, b.Name))));
            Same("digest", c.GetProperty("digest").GetString()!, FileCompare.Digest(r, a.Name, b.Name));
        }
        Assert.True(n >= 20, "cases are missing");
        Assert.True(bad.Count == 0, bad.Count + " differences from the simulation:\n" + string.Join("\n", bad.Take(10)));
    }

    [Fact]
    public void The_sample_files_the_prompts_and_the_usage_labels_are_the_simulations()
    {
        using var doc = Golden();
        var first = doc.RootElement.GetProperty("compare")[0];
        Assert.Equal(JsonSerializer.Serialize(Table(first.GetProperty("a"))), JsonSerializer.Serialize(FileCompare.SampleA));
        Assert.Equal(JsonSerializer.Serialize(Table(first.GetProperty("b"))), JsonSerializer.Serialize(FileCompare.SampleB));
        Assert.Equal(doc.RootElement.GetProperty("aiTemplate").GetString(), FileCompare.AiTemplate);
        Assert.Equal(doc.RootElement.GetProperty("readPrompts").EnumerateArray().Select(p => p.GetProperty("name").GetString() + "\n" + p.GetProperty("template").GetString()),
            ReadingPrompts.All.Select(p => p.Name + "\n" + p.Template));
        Assert.Equal(Strings(doc.RootElement.GetProperty("usageEvents")), UsageSummary.Events.Select(e => e.Event + " = " + e.Label));
        Assert.All(UsageSummary.Events, e => Assert.Contains(e.Event, UsageLog.Events));       // every event the summary shows can be written to the log
    }

    [Fact]
    public void A_calendar_invitation_is_made_exactly_as_the_simulation_makes_it()
    {
        using var doc = Golden();
        var today = DateOnly.Parse(doc.RootElement.GetProperty("today").GetString()!);
        var bad = new List<string>(); int n = 0;
        foreach (var c in doc.RootElement.GetProperty("invite").EnumerateArray())
        {
            var made = Invitations.For(c.GetProperty("text").GetString(), today, "20260716T120000Z", $"case-{n}@notula"); n++;
            var want = c.GetProperty("result");
            string expected = want.ValueKind == JsonValueKind.Null ? "(none)" : string.Join("\n", want.GetProperty("title").GetString(), want.GetProperty("day").GetString(), want.GetProperty("shown").GetString(), want.GetProperty("ics").GetString());
            string actual = made is null ? "(none)" : string.Join("\n", made.Title, made.Day, made.Shown, made.Ics);
            if (expected != actual) bad.Add($"case {n}\n  expected: {JsonSerializer.Serialize(expected)}\n  actual:   {JsonSerializer.Serialize(actual)}");
        }
        Assert.True(n >= 14, "cases are missing");
        Assert.True(bad.Count == 0, bad.Count + " differences from the simulation:\n" + string.Join("\n", bad.Take(6)));
    }

    [Fact]
    public void The_usage_summary_counts_the_last_7_and_30_days_as_the_simulation_does()
    {
        using var doc = Golden();
        foreach (var c in doc.RootElement.GetProperty("usage").EnumerateArray())
        {
            var days = c.GetProperty("days").EnumerateObject().ToDictionary(d => d.Name,
                d => (IReadOnlyDictionary<string, double>)d.Value.EnumerateObject().ToDictionary(e => e.Name, e => e.Value.GetDouble()));
            Assert.Equal(Strings(c.GetProperty("lines")), UsageSummary.Lines(days, DateOnly.Parse(c.GetProperty("today").GetString()!)));
        }
    }
}
