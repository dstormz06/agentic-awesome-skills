using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AndaCompanion.Data;
using AndaCompanion.Services;
using AndaCompanion.Services.Portfolio;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// Round 38: Today's next week also lists the person's to-do items due that week (held to mockup/round38-golden.json, written by the
/// simulation), and a card's sister ANDAs are found from an index instead of a scan of every row of the applicant. Fictional data only.
/// </summary>
[Collection("Global state")]
public class Round38Tests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public Round38Tests(HeadlessFixture fx) => _fx = fx;
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }

    private static string Folder(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "anda-hover-companion") dir = dir.Parent;
        return Path.Combine(new[] { dir?.FullName ?? throw new DirectoryNotFoundException("anda-hover-companion folder not found") }.Concat(parts).ToArray());
    }
    private static readonly Lazy<JsonElement> Gold = new(() => JsonDocument.Parse(File.ReadAllText(Folder("mockup", "round38-golden.json"))).RootElement.Clone());
    private static string? S(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetString();

    [Fact]
    public void Next_week_lists_the_to_do_items_as_the_simulation_does_in_every_set()
    {
        var g = Gold.Value; var cal = new BusinessCalendar();
        var template = new MockAndaRepository().Find("216482")!;
        static DateOnly? D(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : DateOnly.Parse(e.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        var records = g.GetProperty("records").EnumerateObject().Select(p => template with
        {
            Number = p.Name, GoalDateValue = D(p.Value.GetProperty("goal")), ForecastDateValue = D(p.Value.GetProperty("forecast")),
            ForecastDate = D(p.Value.GetProperty("forecast")) is null ? Models.AndaRecord.Unknown : "set",
        }).ToList();
        var today = DateOnly.Parse(g.GetProperty("today").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        int n = 0;
        foreach (var set in g.GetProperty("sets").EnumerateArray())
        {
            var tasks = set.GetProperty("tasks").EnumerateArray().Select(t => new NextWeek.WeekTask(t.GetProperty("list").GetString()!, t.GetProperty("title").GetString()!, S(t.GetProperty("due")))).Reverse().ToList();   // order given must not matter
            var got = NextWeek.Of(today, g.GetProperty("hour").GetInt32(), set.GetProperty("noRecords").GetBoolean() ? Array.Empty<Models.AndaRecord>() : records, cal, tasks)!;
            var want = set.GetProperty("result");
            string why = set.GetProperty("name").GetString()!;
            Assert.True(want.GetProperty("tasks").EnumerateArray().Select(t => t.GetProperty("list").GetString() + "|" + t.GetProperty("text").GetString()).SequenceEqual(got.Tasks!.Select(t => t.List + "|" + t.Text)), why);
            Assert.True(want.GetProperty("tasksMore").GetInt32() == got.TasksMore, why);
            Assert.True(S(want.GetProperty("empty")) == got.Empty, why);
            Assert.Equal(want.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("id").GetString() + " " + r.GetProperty("text").GetString()), got.Rows.Select(r => r.Id + " " + r.Text));
            n++;
        }
        Assert.Equal(5, n);
        Assert.Equal(g.GetProperty("taskMax").GetInt32(), NextWeek.TaskMax);
        foreach (var m in g.GetProperty("more").EnumerateArray()) Assert.Equal(m.GetProperty("text").GetString(), NextWeek.SaidTasksMore(m.GetProperty("n").GetInt32()));
        Assert.Equal(g.GetProperty("changes").GetProperty("items").EnumerateArray().Select(x => x.GetString()!).ToList(), ChangeLog.Entries.First(e => e.Round == 38).Items.ToList());
    }

    [Fact]
    public void Only_unfinished_items_with_a_due_date_are_read_from_both_lists()
    {
        var anda = new[] { new TodoItem { Title = "Open", Due = "2026-12-01" }, new TodoItem { Title = "Finished", Due = "2026-12-01", Done = true }, new TodoItem { Title = "No date" } };
        var mine = new[] { new MyTodoItem { Title = "Mine", Due = "2026-12-02" }, new MyTodoItem { Title = "Mine, finished", Due = "2026-12-02", Done = true } };
        Assert.Equal(new[] { "ANDA to-do list|Open|2026-12-01", "My to-do list|Mine|2026-12-02" }, NextWeek.TasksOf(anda, mine).Select(t => $"{t.List}|{t.Title}|{t.Due}"));
    }

    // ---------- sister ANDAs from an index ----------

    private static string Csv(IEnumerable<string[]> rows) => string.Join("\n", rows.Select(r => string.Join(",", r.Select(c => "\"" + c.Replace("\"", "\"\"") + "\"")))) + "\n";

    [Fact]
    public void Sisters_from_the_index_are_the_ones_the_old_scan_found_in_every_mix_of_ingredient_and_drug()
    {
        // fictional rows: the same applicant in two spellings, ingredients present and absent, drug names that share or differ in their first word
        var rnd = new Random(38);
        string[] apps = { "Fictional Labs", "FICTIONAL LABS", "Other Pharma", "" }, ings = { "", "metformin", "METFORMIN", "lisinopril" }, drugs = { "Metformin ER", "metformin, IR", "Lisinopril", "", "Zeta tabs" };
        var rows = new List<string[]> { new[] { "ANDA", "Drug", "Applicant", "Active ingredient" } };
        for (int i = 0; i < 400; i++) rows.Add(new[] { (300000 + i).ToString(), drugs[rnd.Next(drugs.Length)], apps[rnd.Next(apps.Length)], ings[rnd.Next(ings.Length)] });
        string path = Path.Combine(Temp.NewDir(), "portfolio.csv"); File.WriteAllText(path, Csv(rows), new UTF8Encoding(false));
        var repo = new PortfolioRepository(path, today: () => new DateOnly(2026, 11, 20));
        static string First(string s) => s.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        bool Same(string[] a, string[] b) => a[3].Length > 0 && b[3].Length > 0 ? string.Equals(a[3], b[3], StringComparison.OrdinalIgnoreCase)
            : First(a[1]).Length > 0 && string.Equals(First(a[1]), First(b[1]), StringComparison.OrdinalIgnoreCase);
        var data = rows.Skip(1).ToList(); int withSisters = 0;
        foreach (var r in data)
        {
            // the scan of round 33: the applicant's rows in sheet order, the same product, the first three
            var want = r[2].Length == 0 ? new List<string>() : data.Where(x => string.Equals(x[2], r[2], StringComparison.OrdinalIgnoreCase) && x[0] != r[0] && Same(x, r)).Take(3).Select(x => x[0]).ToList();
            var got = repo.Find(r[0])!.Sisters.Select(s => s.Number).ToList();
            Assert.True(want.SequenceEqual(got), $"{r[0]} ({r[1]} / {r[2]} / {r[3]}): wanted {string.Join(" ", want)}, got {string.Join(" ", got)}");
            if (got.Count > 0) withSisters++;
        }
        Assert.True(withSisters > 100, "the mix should give many rows with sisters");
    }

    [Fact]
    public void Five_thousand_rows_from_one_applicant_are_all_read_quickly()
    {
        // before round 38 this took several seconds (each record scanned every row of the applicant); the bound is loose on purpose
        var rows = new List<string[]> { new[] { "ANDA", "Drug", "Applicant", "Active ingredient" } };
        for (int i = 0; i < 5000; i++) rows.Add(new[] { (400000 + i).ToString(), $"Drug{i} tabs", "Fictional Labs", "" });
        string path = Path.Combine(Temp.NewDir(), "big.csv"); File.WriteAllText(path, Csv(rows), new UTF8Encoding(false));
        var repo = new PortfolioRepository(path, today: () => new DateOnly(2026, 11, 20));
        var watch = Stopwatch.StartNew();
        int n = repo.Numbers.Count(x => repo.Find(x) is not null);
        watch.Stop();
        Assert.Equal(5000, n);
        Assert.True(watch.ElapsedMilliseconds < 3000, $"{watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Today_shows_the_to_do_items_of_next_week_as_text_and_how_many_more()
    {
        var texts = await _fx.Session.Dispatch(() =>
        {
            var repo = new MockAndaRepository();
            Today.Look? Find(string id) => repo.Find(id) is { } rec ? Round33Tests.Look(rec) : null;
            var tasks = Enumerable.Range(0, 22).Select(i => new NextWeek.WeekTask("My to-do list", $"Fictional task {i:00}", "2026-12-0" + (1 + i % 4))).ToList();
            var week = NextWeek.Of(new DateOnly(2026, 11, 27), 10, Array.Empty<Models.AndaRecord>(), new BusinessCalendar(), tasks);
            var w = new TodayWindow(new TodayHooks(() => repo.Numbers.Select(n => Find(n)!).ToList(), Find, () => Array.Empty<string>(), () => Array.Empty<string>(), _ => { }, Week: () => week));
            try { w.Show(); Settle(); return w.List.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList(); }
            finally { w.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.Contains("Tue Dec 1 · My to-do list: Fictional task 00", texts);
        Assert.Contains(NextWeek.SaidTasksMore(2), texts);
        Assert.DoesNotContain(NextWeek.SaidEmpty, texts);
        int title = texts.IndexOf("NEXT WEEK · MON NOV 30 TO FRI DEC 4"), first = texts.IndexOf("Tue Dec 1 · My to-do list: Fictional task 00");
        Assert.True(title >= 0 && first > title);
    }

    [Fact]
    public void The_app_reads_both_lists_for_next_week()
    {
        string more = File.ReadAllText(Folder("app", "AndaCompanion", "App.Round33.cs"));
        Assert.Contains("NextWeek.TasksOf(_todo.Items, _myTodo.Items)", more);
    }
}
