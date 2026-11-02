using System.Text.Json;
using System.Text.Json.Nodes;
using AndaCompanion.Data;
using AndaCompanion.Models;
using AndaCompanion.Services;
using AndaCompanion.Services.Portfolio;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// The Edge extension has its own JavaScript copy of the rules, so it can work with no app installed. This fixture is the
/// contract between the two: the app writes what it produces for a fixed set of inputs, the extension's tests must reproduce
/// it exactly, and this test fails if the app's behaviour changes without the fixture being regenerated
/// (set ANDA_WRITE_GOLDEN=1 and run this test, then commit the changed files).
/// </summary>
internal static class GoldenFixture
{
    private static readonly DateOnly Today = new(2026, 11, 2);

    public static string ExtensionDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "anda-hover-companion") dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("anda-hover-companion folder not found"), "extension");
    }

    private static JsonNode? N(object? o) => JsonSerializer.SerializeToNode(o, CardModels.Json);
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");

    private static CardContext Ctx(DateOnly today, int lead, int lateDays) =>
        new(today, new BusinessCalendar(), lead, lateDays, r => ReferenceResolver.For(r, null, "", null, false));

    private static JsonObject CtxJson(DateOnly today, int lead, int lateDays) => new() { ["today"] = D(today), ["lead"] = lead, ["lateDays"] = lateDays };

    public static JsonObject Build(string xlsxPath)
    {
        var demo = new MockAndaRepository();
        var root = new JsonObject { ["version"] = 1, ["today"] = D(Today) };

        // ---- recognizer ----
        var texts = new[]
        {
            "216482", "ANDA 216482", "anda-216482", "ANDA#216482", "see ANDA 214990, then the labeling", "(216482)", "Ref: 216482.", "a\n216482\nb",
            "12345", "1234567", "21648a", "abc", "", "   ", "9216482", "2164829", "x216482y", "NDA 021234", "NDA021234", "NDA: 021234", "NDA No. 021234",
            "nda #021234", "DMF 012345", "IND 123456", "BLA 125000", "MF-012345", "ANDA/NDA 216482", "NDA 021234 and ANDA 216482", "ANDA 021234", "IND 123456; 214990",
        };
        root["recognizer"] = N(texts.Select(t => new { text = t, number = AndaValidator.Extract(t) }));

        // ---- calendar ----
        var cal = new BusinessCalendar();
        var extra = new BusinessCalendar(new[] { (new DateOnly(2026, 12, 24), "Executive order closing") });
        var dates = new[]
        {
            "2026-01-01", "2026-01-19", "2026-02-16", "2026-05-25", "2026-06-19", "2026-07-02", "2026-07-03", "2026-07-06", "2026-09-07", "2026-09-22", "2026-10-03", "2026-10-09",
            "2026-10-12", "2026-10-13", "2026-11-11", "2026-11-25", "2026-11-26", "2026-11-27", "2026-12-23", "2026-12-24", "2026-12-25", "2026-12-28", "2027-01-01", "2027-06-17",
            "2027-06-18", "2027-06-21", "2027-07-02", "2027-07-05", "2027-07-06", "2027-11-10", "2027-11-11", "2027-12-23", "2027-12-24", "2027-12-27", "2028-12-29", "2029-01-19", "2029-01-22",
        }.Select(DateOnly.Parse).ToArray();
        JsonObject Flag(BusinessCalendar c, DateOnly d, int lead)
        {
            var f = c.Evaluate(d, lead);
            return new JsonObject
            {
                ["date"] = D(d), ["lead"] = lead, ["kind"] = f.Kind.ToString().ToLowerInvariant(), ["name"] = f.HolidayName,
                ["near"] = f.BusinessDaysFromHoliday, ["before"] = f.Before, ["prev"] = D(f.PreviousBusinessDay), ["next"] = D(f.NextBusinessDay),
            };
        }
        var calArr = new JsonArray();
        foreach (var d in dates) foreach (int lead in new[] { 0, 2, 3, 5 }) calArr.Add(Flag(cal, d, lead));
        var extraArr = new JsonArray();
        foreach (var d in new[] { "2026-12-23", "2026-12-24", "2026-12-28" }.Select(DateOnly.Parse)) extraArr.Add(Flag(extra, d, 3));
        var kinds = new System.Text.StringBuilder();
        for (var d = new DateOnly(2026, 1, 1); d <= new DateOnly(2027, 12, 31); d = d.AddDays(1))
            kinds.Append(cal.KindOf(d) switch { DayKind.Weekend => 'W', DayKind.Holiday => 'H', _ => 'B' });
        var holidays = new JsonArray();
        for (int y = 2025; y <= 2030; y++)
            holidays.Add(new JsonObject { ["year"] = y, ["items"] = N(BusinessCalendar.HolidaysOf(y).Select(h => new { observed = D(h.Observed), actual = D(h.Actual), name = h.Name, regional = h.Regional })) });
        root["calendar"] = new JsonObject
        {
            ["flags"] = calArr, ["extraClosure"] = new JsonObject { ["date"] = "2026-12-24", ["name"] = "Executive order closing" },
            ["extraFlags"] = extraArr, ["kinds2026to2027"] = kinds.ToString(), ["holidays"] = holidays,
        };

        // ---- demo records ----
        var demoRecords = new[] { "216482", "214990" }.Select(n => demo.Find(n)!).ToList();
        root["demoRecords"] = N(demoRecords);

        // ---- cards: demo and synthetic records under several clocks and settings ----
        var d1 = demoRecords[0];
        var synthetic = new List<(string Label, AndaRecord Record)>
        {
            ("demo-at-risk", d1), ("demo-healthy", demoRecords[1]),
            ("weekday-goal", d1 with { GoalDateValue = new DateOnly(2026, 9, 22), ForecastDateValue = new DateOnly(2026, 9, 24) }),
            ("near-holidays", d1 with { Demo = false, SourceLabel = "Your portfolio: Test.xlsx", AsOfDate = new DateOnly(2026, 10, 30), GoalDateValue = new DateOnly(2026, 11, 25), ForecastDateValue = new DateOnly(2026, 12, 24) }),
            ("goal-passed", d1 with { Demo = false, SourceLabel = "Your portfolio: Test.xlsx", AsOfDate = new DateOnly(2026, 10, 30), GoalDateValue = new DateOnly(2026, 10, 20) }),
            ("stale-data", d1 with { Demo = false, SourceLabel = "Your portfolio: Test.xlsx", AsOfDate = new DateOnly(2026, 10, 10), GoalDateValue = new DateOnly(2026, 12, 1), ForecastDateValue = new DateOnly(2026, 12, 2) }),
            ("psg-after-submission", d1 with { Demo = false, SourceLabel = "Your portfolio: Test.xlsx", AsOfDate = new DateOnly(2026, 11, 1), SubmittedDate = new DateOnly(2026, 3, 1), PsgDateFromSheet = new DateOnly(2026, 8, 21), GoalDateValue = new DateOnly(2026, 12, 1), ForecastDateValue = new DateOnly(2026, 12, 2) }),
            ("no-goal-date", d1 with { Demo = false, SourceLabel = "Your portfolio: Test.xlsx", AsOfDate = new DateOnly(2026, 11, 1), GoalDateValue = null, ForecastDateValue = null }),
            ("goal-soon", d1 with { Demo = false, SourceLabel = "Your portfolio: Test.xlsx", AsOfDate = new DateOnly(2026, 11, 2), GoalDateValue = new DateOnly(2026, 11, 5), ForecastDateValue = new DateOnly(2026, 11, 5) }),
        };
        var ctxs = new[] { (Today, 3, 7), (new DateOnly(2026, 9, 30), 0, 3), (new DateOnly(2026, 10, 1), 5, 14) };
        var cards = new JsonArray();
        foreach (var (label, rec) in synthetic)
            foreach (var (today, lead, late) in ctxs)
                cards.Add(new JsonObject { ["label"] = label, ["record"] = N(rec), ["ctx"] = CtxJson(today, lead, late), ["expected"] = N(CardModels.Build(rec, Ctx(today, lead, late))) });
        root["cards"] = cards;

        // ---- watch list over the demo records ----
        root["watchlistDemo"] = new JsonObject
        {
            ["records"] = N(demoRecords), ["ctx"] = CtxJson(Today, 3, 7),
            ["expected"] = N(WatchList.Build(demo, Ctx(Today, 3, 7))),
        };

        // ---- draft check ----
        var drafts = new[]
        {
            "Hi all, quick status. ANDA 216482 is on track, with a goal date of Oct 10, 2026 and a forecast of Oct 17, 2026. ANDA 214990 is running late. ANDA 123456 needs a call.",
            "ANDA 216482 goal date is Oct 3, 2026.", "Nothing about any application here.", "", "   ",
            "ANDA 216482 is running late. The forecast is 10/17/2026.", "ANDA 214990 is ahead of schedule. Goal 8/15, forecast Aug 6.",
            "ANDA 216482 and ANDA 214990 are both on track.", "Update on 216482: forecast date is 2026-10-18.\n\nIt is behind schedule.",
            "ANDA 216482: goal Oct 3, forecast Oct 17, behind schedule. ANDA 214990: goal Aug 15, forecast Aug 6, ahead of goal.",
            "NDA 021234 is on track.", "ANDA 999999 is delayed and ANDA 888888 is delayed too.",
        };
        root["drafts"] = N(drafts.Select(t =>
        {
            var r = DraftCheck.Check(t, demo.Find);
            return new
            {
                text = t,
                findings = r.Findings.Select(f => new { kind = f.Kind switch { DraftFindingKind.Difference => "diff", DraftFindingKind.UnknownAnda => "unknown", DraftFindingKind.Ok => "ok", _ => "note" }, text = f.Text }),
                numbersChecked = r.NumbersChecked, datesChecked = r.DatesChecked, statusChecked = r.StatusChecked,
            };
        }));

        // ---- portfolio files: CSV and the committed .xlsx ----
        var cases = new List<(string Name, string Text, Dictionary<string, string>? Overrides)>
        {
            ("Portfolio.csv",
             "Project list (my tracker)\n" +
             "ANDA,Product,Applicant,Goal date,Forecast date,Status,My notes,Cycle,Date submitted,PSG date\n" +
             "216482,\"Metformin HCl ER 500 mg, tablets\",Meridian,2026-10-03,2026-10-17,\"IR out, response due\",\"Said \"\"call\"\" Tuesday\",2,2026-03-01,2026-08-21\n" +
             "214990,Metformin HCl ER 750 mg,Meridian,10/12/2026,,Labeling pending,,3,2026-02-10,\n" +
             "ANDA 300001,Lisinopril 20 mg,Harbor,2026-11-25,2026-12-24,In review,,1,,\n" +
             "bad row,Nothing,,2026-01-01,,,,,,\n" +
             "300002,Atorvastatin,Harbor,2026-10-20,2026-10-25,Waiting,,1,,\n", null),
            ("Semicolon.csv",
             "Application No;Product Name;Company;GDUFA Goal Date;Projected Date;Current Status;Comments;Review Cycle;Received;Active Ingredient;Dosage Form;Strength\n" +
             "400001;Gabapentin caps;Birch;3-Oct-2026;Oct 17, 2026;Under review;;1;2-Mar-2026;Gabapentin;Capsule;300 mg\n" +
             "400002;Gabapentin caps;Birch;11/27/2026;12/4/2026;In queue;Call Tuesday;2;;Gabapentin;Capsule;100 mg\n", null),
            ("NoAnda.csv", "Name,Date\nx,2026-01-01\n", null),
            ("Minimal.csv", "ANDA,Goal\n123456,2026-12-01\n123457,\n", null),
            ("Overrides.csv", "Col A,Col B,Col C\n555555,2026-11-30,Widget\n", new Dictionary<string, string> { ["number"] = "Col A", ["goal"] = "Col B", ["drug"] = "Col C" }),
            ("Dates.csv",
             "ANDA,Goal\n" +
             "800001,10/3/26\n800002,3-Oct-26\n800003,10/3/31\n800004,2026/12/01\n800005,3 Dec 2026\n800006,Oct 3 2026\n800007,\"October 3, 2026\"\n" +
             "800008,2026-10-03 00:00:00\n800009,2026-10-03T08:15:00\n800010,10/3/2026 12:00:00 AM\n800011,13/45/2026\n800012,\"Feb 30, 2026\"\n800013,2026-02-30\n" +
             "800014,\"Sept 3, 2026\"\n800015,03-oct-2026\n800016,1/2/1980\n800017,not a date\n", null),
            ("Tabs.csv", "﻿ANDA\tDrug\tGoal\tForecast\r\n600001\tTab drug\t2026-12-01\t2026-11-20\r\n\r\n", null),
        };
        var portfolios = new JsonArray();
        var savedAt = new DateTime(2026, 10, 28, 9, 30, 0, DateTimeKind.Local);
        var ctx0 = Ctx(Today, 3, 7);
        JsonObject PortfolioCase(string name, string source, string? text, string? xlsxBase64, Dictionary<string, string>? ov, string path)
        {
            File.SetLastWriteTime(path, savedAt);
            var repo = new PortfolioRepository(path, ov, null, () => Today);
            var numbers = repo.Numbers.OrderBy(n => n, StringComparer.Ordinal).ToList();
            var o = new JsonObject
            {
                ["name"] = name, ["source"] = source, ["text"] = text, ["xlsxFile"] = xlsxBase64,
                ["overrides"] = ov is null ? null : N(ov), ["savedAt"] = new JsonObject { ["y"] = 2026, ["m"] = 10, ["d"] = 28, ["hh"] = 9, ["mm"] = 30 },
                ["today"] = D(Today),
                ["expected"] = new JsonObject
                {
                    ["ok"] = repo.Status.Ok, ["count"] = repo.Status.Count, ["message"] = repo.Status.Message,
                    ["headers"] = N(repo.Headers), ["mapping"] = N(repo.Mapping), ["numbers"] = N(repo.Numbers),
                    ["cards"] = N(numbers.Select(n => CardModels.Build(repo.Find(n)!, ctx0))),
                    ["watchlist"] = N(WatchList.Build(repo, ctx0)),
                },
            };
            return o;
        }
        string tmp = Temp.NewDir();
        foreach (var (name, text, ov) in cases)
        {
            string path = Path.Combine(tmp, name);
            File.WriteAllText(path, text);
            portfolios.Add(PortfolioCase(name, "csv", text, null, ov, path));
        }
        string xcopy = Path.Combine(tmp, "Portfolio.xlsx");
        File.Copy(xlsxPath, xcopy, true);
        portfolios.Add(PortfolioCase("Portfolio.xlsx", "xlsx", null, "fixtures/portfolio.xlsx", null, xcopy));
        root["portfolios"] = portfolios;
        return root;
    }

    public static void WriteXlsx(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        TestWorkbook.Create(path, new object?[][]
        {
            new object?[] { "ANDA", "Product", "Applicant", "GDUFA Goal Date", "Forecast Date", "Status", "RPM Notes", "Cycle", "Date Submitted" },
            new object?[] { "700001", "Warfarin tabs", "Cedar", TestWorkbook.Date(2026, 10, 3), TestWorkbook.Date(2026, 10, 17), "IR response due", "Check with CMC", 2, TestWorkbook.Date(2026, 3, 1) },
            new object?[] { "700002", "Warfarin tabs 2 mg", "Cedar", "2026-11-26", null, "Waiting", null, 1, null },
            new object?[] { "ANDA 700003", "Sertraline", "Elm", TestWorkbook.Date(2026, 12, 24), TestWorkbook.Date(2026, 12, 18), "Final labeling", "Holiday week", 3, TestWorkbook.Date(2026, 1, 15) },
        }, sharedStrings: true, hiddenFirst: new[] { "Secret" });
    }

    public static string DemoRecordsJs(JsonNode records) =>
        "// Generated from the app's fictional demo records by ExtensionGoldenTests. Do not edit: set ANDA_WRITE_GOLDEN=1 and run that test.\n" +
        "export const DEMO_RECORDS = " + records.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + ";\n";
}

public class ExtensionGoldenTests
{
    private static string Norm(string s) => s.Replace("\r\n", "\n");

    [Fact]
    public void The_committed_fixture_for_the_extension_matches_what_the_app_produces()
    {
        string ext = GoldenFixture.ExtensionDir();
        string xlsx = Path.Combine(ext, "tests", "fixtures", "portfolio.xlsx");
        string golden = Path.Combine(ext, "tests", "golden.json");
        string demoJs = Path.Combine(ext, "src", "core", "demo-records.js");
        bool write = Environment.GetEnvironmentVariable("ANDA_WRITE_GOLDEN") == "1";
        if (write || !File.Exists(xlsx)) GoldenFixture.WriteXlsx(xlsx);

        var built = GoldenFixture.Build(xlsx);
        string builtJson = built.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        string builtJs = GoldenFixture.DemoRecordsJs(built["demoRecords"]!);
        if (write)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(golden)!);
            Directory.CreateDirectory(Path.GetDirectoryName(demoJs)!);
            File.WriteAllText(golden, builtJson + "\n");
            File.WriteAllText(demoJs, builtJs);
            return;
        }
        Assert.True(File.Exists(golden), "extension/tests/golden.json is missing. Run this test with ANDA_WRITE_GOLDEN=1 and commit the result.");
        var committed = JsonNode.Parse(File.ReadAllText(golden))!;
        Assert.True(JsonNode.DeepEquals(committed, built),
            "The app's output no longer matches extension/tests/golden.json. If the change is intended, regenerate with ANDA_WRITE_GOLDEN=1 and update the extension to match.");
        Assert.Equal(Norm(builtJs), Norm(File.ReadAllText(demoJs)));
    }
}
