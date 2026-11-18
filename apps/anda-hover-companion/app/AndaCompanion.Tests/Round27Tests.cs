using System.Text.Json;
using AndaCompanion.Services;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// Round 27: two documents compared by paragraphs, what a stored prompt is given, the check of an AI answer, signs worth a second look, the
/// reminder for a reply, the hand-off page and a report checked against the person's data. The simulation wrote mockup/round27-golden.json
/// (tools/round27-golden.cjs) with its clock fixed at Jul 16, 2026; the app must give the same answers, character for character.
/// </summary>
public class Round27Tests
{
    private static string Folder(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "anda-hover-companion") dir = dir.Parent;
        return Path.Combine(new[] { dir?.FullName ?? throw new DirectoryNotFoundException("anda-hover-companion folder not found") }.Concat(parts).ToArray());
    }

    private static JsonDocument Golden() => JsonDocument.Parse(File.ReadAllText(Folder("mockup", "round27-golden.json")));
    private static string[] Strings(JsonElement e) => e.EnumerateArray().Select(x => x.GetString()!).ToArray();
    private static string Join(IEnumerable<string> lines) => string.Join("\n", lines);
    private static void Fail(List<string> bad) => Assert.True(bad.Count == 0, bad.Count + " differences from the simulation:\n" + string.Join("\n", bad.Take(8)));
    private static void Same(List<string> bad, string what, string expected, string actual)
    {
        if (expected != actual) bad.Add($"{what}\n  expected: {JsonSerializer.Serialize(expected.Length > 600 ? expected[..600] : expected)}\n  actual:   {JsonSerializer.Serialize(actual.Length > 600 ? actual[..600] : actual)}");
    }

    [Fact]
    public void Two_documents_are_compared_exactly_as_the_simulation_compares_them()
    {
        using var doc = Golden(); var bad = new List<string>(); int n = 0;
        foreach (var c in doc.RootElement.GetProperty("docs").EnumerateArray())
        {
            n++;
            var r = DocCompare.Compare(c.GetProperty("a").GetString(), c.GetProperty("b").GetString()); var want = c.GetProperty("result");
            Same(bad, $"case {n} counts", $"{want.GetProperty("parasA")} {want.GetProperty("parasB")} {want.GetProperty("same")} {want.GetProperty("changed")} {want.GetProperty("added")} {want.GetProperty("removed")} {(want.GetProperty("skipped").GetBoolean() ? "true" : "false")}",
                $"{r.ParasA} {r.ParasB} {r.Same} {r.Changed} {r.Added} {r.Removed} {(r.Skipped ? "true" : "false")}");
            Same(bad, $"case {n} changes", Join(want.GetProperty("changes").EnumerateArray().Select(x => $"{x.GetProperty("kind").GetString()}|{x.GetProperty("at")}|{x.GetProperty("from").GetString()}|{x.GetProperty("to").GetString()}")),
                Join(r.Changes.Select(x => $"{x.Kind}|{x.At}|{x.From}|{x.To}")));
            Same(bad, $"case {n} summary", Join(Strings(c.GetProperty("summary"))), Join(DocCompare.Summary(r, "Earlier.txt", "Later.txt")));
            Same(bad, $"case {n} report", Join(Strings(c.GetProperty("report"))), Join(DocCompare.Report(r, "Earlier.txt", "Later.txt")));
        }
        Assert.True(n >= 14, "cases are missing"); Fail(bad);
    }

    [Fact]
    public void A_stored_prompt_is_given_exactly_what_the_simulation_gives_it()
    {
        using var doc = Golden(); var bad = new List<string>(); int n = 0;
        var today = DateOnly.Parse(doc.RootElement.GetProperty("today").GetString()!);
        foreach (var c in doc.RootElement.GetProperty("material").EnumerateArray())
        {
            n++;
            string a = c.GetProperty("a").GetString()!; bool whole = c.GetProperty("whole").GetBoolean(); string template = c.GetProperty("template").GetString()!;
            string material = c.GetProperty("b").ValueKind == JsonValueKind.Null
                ? CompareMaterial.For(new[] { "One line of a report." }, "Report.csv", a, null, "", whole)
                : CompareMaterial.For(DocCompare.Report(DocCompare.Compare(a, c.GetProperty("b").GetString()), "Earlier.txt", "Later.txt"), "Earlier.txt", a, "Later.txt", c.GetProperty("b").GetString()!, whole);
            Same(bad, $"case {n} material", c.GetProperty("material").GetString()!, material);
            Same(bad, $"case {n} prompt", c.GetProperty("prompt").GetString()!, CompareMaterial.Prompt(template, material, today));
        }
        Assert.True(n >= 6, "cases are missing");
        Same(bad, "table text", doc.RootElement.GetProperty("tableText").GetString()!, CompareMaterial.TableText(FileCompare.SampleB));
        foreach (var c in doc.RootElement.GetProperty("clean").EnumerateArray()) Same(bad, "clean", c.GetProperty("clean").GetString()!, CompareMaterial.Clean(c.GetProperty("text").GetString()));
        var cut = Strings(doc.RootElement.GetProperty("cutText"));
        Same(bad, "cut 1", cut[0], CompareMaterial.CutText("short", 1800));
        Same(bad, "cut 2", cut[1], CompareMaterial.CutText(Join(Enumerable.Range(0, 80).Select(i => $"Line {i} of a long report with some words.")), 1800));
        Same(bad, "cut 3", cut[2], CompareMaterial.CutText(new string('x', 2500), 1800));
        var samples = doc.RootElement.GetProperty("sampleDocs");
        Same(bad, "sample letters", samples[0].GetProperty("name").GetString() + "\n" + samples[0].GetProperty("text").GetString() + "\n" + samples[1].GetProperty("name").GetString() + "\n" + samples[1].GetProperty("text").GetString(),
            DocCompare.SampleNameA + "\n" + DocCompare.SampleA + "\n" + DocCompare.SampleNameB + "\n" + DocCompare.SampleB);
        Fail(bad);
    }

    [Fact]
    public void An_AI_answer_is_checked_exactly_as_the_simulation_checks_it()
    {
        using var doc = Golden(); var bad = new List<string>(); int n = 0;
        string facts = doc.RootElement.GetProperty("facts").GetString()!;
        foreach (var c in doc.RootElement.GetProperty("answers").EnumerateArray())
        {
            n++;
            var r = AnswerCheck.Check(c.GetProperty("answer").GetString(), facts); var want = c.GetProperty("result");
            Same(bad, $"case {n}", $"{want.GetProperty("checked")}\n{Join(Strings(want.GetProperty("missing")))}\n{Join(Strings(want.GetProperty("lines")))}", $"{r.Checked}\n{Join(r.Missing)}\n{Join(r.Lines)}");
        }
        Assert.True(n >= 8, "cases are missing"); Fail(bad);
    }

    [Fact]
    public void Signs_worth_a_second_look_are_the_simulations_and_never_a_verdict()
    {
        using var doc = Golden(); var bad = new List<string>(); int n = 0;
        foreach (var c in doc.RootElement.GetProperty("signs").EnumerateArray())
        {
            n++;
            var r = SecondLook.Check(c.GetProperty("text").GetString()); var want = c.GetProperty("result");
            Same(bad, $"case {n}", Join(Strings(want.GetProperty("items"))) + "\n" + (want.GetProperty("respond").GetBoolean() ? "respond" : "no"), Join(r.Items) + "\n" + (r.Respond ? "respond" : "no"));
            Assert.DoesNotContain(r.Items, i => i.Contains("written by", StringComparison.OrdinalIgnoreCase) || i.Contains("AI-generated", StringComparison.OrdinalIgnoreCase) || i.Contains("probably", StringComparison.OrdinalIgnoreCase));
        }
        Assert.True(n >= 9, "cases are missing");
        Same(bad, "not a verdict", doc.RootElement.GetProperty("notAVerdict").GetString()!, SecondLook.NotAVerdict);
        Same(bad, "none", doc.RootElement.GetProperty("none").GetString()!, SecondLook.None);
        Same(bad, "respond", Join(Strings(doc.RootElement.GetProperty("respond"))), Join(SecondLook.Response));
        Same(bad, "second opinion", doc.RootElement.GetProperty("secondOpinion").GetString()!, SecondLook.AiTemplate);
        Fail(bad);
    }

    [Fact]
    public void A_reply_asked_for_by_a_date_is_found_exactly_as_the_simulation_finds_it()
    {
        using var doc = Golden(); var bad = new List<string>(); int n = 0;
        var today = DateOnly.Parse(doc.RootElement.GetProperty("today").GetString()!);
        foreach (var c in doc.RootElement.GetProperty("replies").EnumerateArray())
        {
            n++;
            var r = ReplyBy.Find(c.GetProperty("text").GetString(), today); var want = c.GetProperty("result");
            string expected = want.ValueKind == JsonValueKind.Null ? "(none)" : $"{want.GetProperty("day").GetString()}|{want.GetProperty("shown").GetString()}|{want.GetProperty("title").GetString()}|{want.GetProperty("number").GetString() ?? "(none)"}";
            Same(bad, $"case {n}", expected, r is null ? "(none)" : $"{r.Day}|{r.Shown}|{r.Title}|{r.Number ?? "(none)"}");
        }
        Assert.True(n >= 14, "cases are missing"); Fail(bad);
    }

    [Fact]
    public void A_hand_off_page_is_written_exactly_as_the_simulation_writes_it()
    {
        using var doc = Golden(); var bad = new List<string>(); int n = 0;
        foreach (var c in doc.RootElement.GetProperty("handoff").EnumerateArray())
        {
            n++;
            var entries = c.GetProperty("entries").EnumerateArray().Select(e => new HandoffEntry(e.GetProperty("status").GetString()!, Strings(e.GetProperty("watch")), Strings(e.GetProperty("open")), Strings(e.GetProperty("notes")))).ToList();
            Same(bad, $"case {n}", Join(Strings(c.GetProperty("lines"))), Join(Handoff.Text(c.GetProperty("title").GetString()!, entries, c.GetProperty("today").GetString()!, c.GetProperty("notes").GetBoolean())));
        }
        Assert.True(n >= 4, "cases are missing"); Fail(bad);
    }

    [Fact]
    public void A_report_is_checked_against_my_data_exactly_as_the_simulation_checks_it()
    {
        using var doc = Golden(); var bad = new List<string>(); int n = 0;
        var mine = doc.RootElement.GetProperty("mine").EnumerateObject().ToDictionary(p => p.Name, p => (p.Value.GetProperty("goal").GetString()!, p.Value.GetProperty("forecast").GetString()!));
        foreach (var c in doc.RootElement.GetProperty("reports").EnumerateArray())
        {
            n++;
            var t = c.GetProperty("report");
            var table = new CompareTable(t.GetProperty("name").GetString()!, Strings(t.GetProperty("headers")), t.GetProperty("rows").EnumerateArray().Select(r => (IReadOnlyList<string>)Strings(r)).ToList());
            var r = ReportCheck.Check(table, id => mine.TryGetValue(id, out var d) ? d : null); var want = c.GetProperty("result");
            Same(bad, $"case {n} counts", $"{want.GetProperty("named")} {want.GetProperty("known")} {want.GetProperty("agree")} {want.GetProperty("differ")} {want.GetProperty("unread")} {want.GetProperty("repeated")} {Join(Strings(want.GetProperty("unknown")))}",
                $"{r.Named} {r.Known} {r.Agree} {r.Differ} {r.Unread} {r.Repeated} {Join(r.Unknown)}");
            Same(bad, $"case {n} lines", Join(Strings(want.GetProperty("lines"))), Join(r.Lines));
            Same(bad, $"case {n} diffs", Join(Strings(want.GetProperty("diffs"))), Join(r.Diffs));
        }
        Assert.True(n >= 10, "cases are missing"); Fail(bad);
    }
}
