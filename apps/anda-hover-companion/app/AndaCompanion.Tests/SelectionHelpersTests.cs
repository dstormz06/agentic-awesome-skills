using System.Text.Json;
using System.Text.Json.Nodes;
using AndaCompanion.Services;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>Quick checks on selected cells and text: fixed rules, no AI. Every expected number below was added up by hand from the sample.</summary>
public class SelectionHelpersTests
{
    private const string Sheet = "ANDA\tApplicant\tGoal date\tAmount\n216482\tAcme\tOct 3, 2026\t$1,200.50\n214990\tBeta\tAug 15, 2026\t$800\n216482\tAcme\tOct 3, 2026\t$1,200.50\n\tGamma\tNov 2, 2026\t";

    [Fact]
    public void Cells_copied_from_Excel_are_read_with_quoted_cells_and_a_trailing_blank_line()
    {
        var rows = SelectionHelpers.Rows("A\tB\r\n\"x\ty\"\t\"say \"\"hi\"\"\"\r\n1\t2\r\n\r\n");
        Assert.Equal(3, rows.Length);
        Assert.Equal(new[] { "A", "B" }, rows[0]);
        Assert.Equal(new[] { "x\ty", "say \"hi\"" }, rows[1]);
        Assert.Equal(new[] { "1", "2" }, rows[2]);
        Assert.Equal(new[] { "a", "", "c" }, SelectionHelpers.Rows("a\t\tc")[0]);                     // an empty cell in the middle stays a cell
        Assert.Equal(new[] { "\"oops", "x" }, SelectionHelpers.Rows("\"oops\tx")[0]);                  // a quote that never closes is just a character
    }

    [Fact]
    public void What_the_selection_looks_like_is_said_in_words_with_the_tool_that_suits_it()
    {
        var table = SelectionHelpers.Classify(Sheet);
        Assert.Equal((SelectionKind.Table, 5, 4), (table.Kind, table.Rows, table.Columns));
        Assert.Equal("A table of 5 rows and 4 columns.", table.Label);
        Assert.Contains("Find duplicates", table.Suggestion);
        Assert.Equal(SelectionKind.Anda, SelectionHelpers.Classify("ANDA 216482").Kind);
        Assert.Equal(SelectionKind.List, SelectionHelpers.Classify("a\nb\nc").Kind);
        var draft = SelectionHelpers.Classify("Please reply by Nov 10 about the labeling.");
        Assert.Equal(SelectionKind.Text, draft.Kind);
        Assert.Contains("Review a draft", draft.Suggestion);
        Assert.Equal(SelectionKind.Empty, SelectionHelpers.Classify("  \n ").Kind);
        Assert.Equal(SelectionKind.Text, SelectionHelpers.Classify("one line of ordinary text").Kind);
        Assert.Equal(SelectionKind.List, SelectionHelpers.Classify("only\nlines\nwith one cell").Kind);
    }

    [Fact]
    public void Repeated_rows_and_values_are_found_with_their_row_numbers_and_the_headings_are_not_counted()
    {
        var lines = SelectionHelpers.FindDuplicates(Sheet);
        Assert.Equal("Found 1 repeated row and 4 repeated values in 4 columns.", lines[0]);
        Assert.Contains("Row 4 repeats row 2: 216482 | Acme | Oct 3, 2026 | $1,200.50", lines);
        Assert.Contains("Column 1 (ANDA): “216482” appears 2 times (rows 2, 4).", lines);
        Assert.Contains("Column 2 (Applicant): “Acme” appears 2 times (rows 2, 4).", lines);
        Assert.Contains(lines, l => l.StartsWith("Looked at 4 rows (not counting the headings)."));
        Assert.Equal("No repeated rows and no repeated values.", SelectionHelpers.FindDuplicates("a\tb\nc\td")[0]);
        Assert.Equal("Found 1 repeated row and 1 repeated value in 1 column.", SelectionHelpers.FindDuplicates("x\ny\nX")[0]);   // upper and lower case count as the same
    }

    [Fact]
    public void Two_columns_are_compared_value_by_value()
    {
        var lines = SelectionHelpers.CompareColumns("Our list\tTheir list\n216482\t216482\n214990\t999999\n123456\t214990");
        Assert.Equal("In both column 1 (Our list) and column 2 (Their list): 2 values.", lines[0]);
        Assert.Equal("Only in column 1 (Our list): 1: “123456”.", lines[1]);
        Assert.Equal("Only in column 2 (Their list): 1: “999999”.", lines[2]);
        Assert.Contains("Please choose two different columns between 1 and 2.", SelectionHelpers.CompareColumns("a\tb\nc\td", 1, 1));
        Assert.Contains("Please choose two different columns between 1 and 2.", SelectionHelpers.CompareColumns("a\tb\nc\td", 1, 3));
        Assert.StartsWith("Comparing needs a table", SelectionHelpers.CompareColumns("a\nb")[0]);
    }

    [Fact]
    public void Blank_cells_are_counted_by_row_and_numbers_are_totalled_but_identifiers_are_not()
    {
        var blanks = SelectionHelpers.FindBlanks(Sheet);
        Assert.Equal("2 blank cells in 1 row.", blanks[0]);
        Assert.Equal("Row 5 has 2 blank cells (column 1, 4).", blanks[1]);
        Assert.Equal("No blank cells.", SelectionHelpers.FindBlanks("a\tb\nc\td")[0]);

        var totals = SelectionHelpers.Totals(Sheet);
        Assert.Contains("Column 1 (ANDA): looks like identifiers (3 numbers of 5 or more digits), so it is not totalled.", totals);
        Assert.Contains("Column 4 (Amount): 3 numbers, total 3201, smallest 800, largest 1200.5, average 1067.", totals);
        Assert.StartsWith("No column holds mostly numbers", SelectionHelpers.Totals("a\tb\nc\td")[0]);
        var mixed = SelectionHelpers.Totals("Item\tCost\nx\t10\ny\t20.5\na\t5\nb\t5\nc\t5\nz\tn/a");
        Assert.Contains("Column 2 (Cost): 5 numbers, total 45.5, smallest 5, largest 20.5, average 9.1 (1 other entry was not a number and left out).", mixed);
        Assert.StartsWith("No column holds mostly numbers", SelectionHelpers.Totals("Item\tCost\nx\t10\ny\t20.5\nz\tn/a\nw\t(none)")[0]);     // only half of the filled cells are numbers
    }

    [Fact]
    public void Odd_and_huge_selections_never_throw_and_stay_within_limits()
    {
        var nasty = new[]
        {
            "", "\t\t\t\n\t\t\t", new string('x', 100_000), string.Concat(Enumerable.Repeat("a\tb\n", 50_000)), string.Concat(Enumerable.Repeat("\"", 5_000)),
            string.Join("\t", Enumerable.Range(0, 500)) + "\n" + string.Join("\t", Enumerable.Range(0, 500)), "٣\t٤\n٥\t٦", "\u0000\u0001\t\u0002\n1\t2",
        };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var text in nasty)
        {
            Assert.NotNull(SelectionHelpers.Classify(text));
            Assert.NotEmpty(SelectionHelpers.Describe(text)); Assert.NotEmpty(SelectionHelpers.FindDuplicates(text)); Assert.NotEmpty(SelectionHelpers.FindBlanks(text));
            Assert.NotEmpty(SelectionHelpers.Totals(text)); Assert.NotEmpty(SelectionHelpers.CompareColumns(text));
            var rows = SelectionHelpers.Rows(text);
            Assert.True(rows.Length <= SelectionHelpers.MaxRows && rows.All(r => r.Length <= SelectionHelpers.MaxColumns));
        }
        Assert.True(sw.ElapsedMilliseconds < 8000, $"{sw.ElapsedMilliseconds} ms");
    }

    // ---------- the shared fixture with the browser simulation ----------

    private static readonly string[] GoldenSelections =
    {
        Sheet, "Our list\tTheir list\n216482\t216482\n214990\t999999\n123456\t214990", "a\tb\nc\td", "x\ny\nX", "a\nb\nc", "ANDA 216482", "216482",
        "Please reply by Nov 10 about the labeling.", "one line of ordinary text", "  \n ", "", "Item\tCost\nx\t10\ny\t20.5\nz\tn/a\nw\t(none)", "Item\tCost\nx\t10\ny\t20.5\na\t5\nb\t5\nc\t5\nz\tn/a",
        "A\tB\r\n\"x\ty\"\t\"say \"\"hi\"\"\"\r\n1\t2\r\n\r\n", "a\t\tc\n1\t2\t3\n1\t2\t3", "Name\tScore\tPct\nAna\t90\t50%\nBo\t75\t25%\nCy\t\t10%",
        "216482\tAcme\n214990\tBeta\n216482\tacme\n999999\t", "Heading one\tHeading two\nONE\tone\none\tONE", "-5\t1\n5\t1\n0\t-0", "$1,000\t2.5\n$2,000\t3.5\n1,500\t",
        "only\nlines\nwith one cell\nonly",
    };

    private static JsonObject GoldenJson()
    {
        JsonArray Lines(IReadOnlyList<string> l) => new(l.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray());
        var cases = new JsonArray();
        foreach (var text in GoldenSelections)
        {
            var shape = SelectionHelpers.Classify(text);
            cases.Add(new JsonObject
            {
                ["text"] = text,
                ["shape"] = new JsonObject { ["kind"] = shape.Kind.ToString().ToLowerInvariant(), ["rows"] = shape.Rows, ["columns"] = shape.Columns, ["label"] = shape.Label, ["suggestion"] = shape.Suggestion },
                ["describe"] = Lines(SelectionHelpers.Describe(text)), ["duplicates"] = Lines(SelectionHelpers.FindDuplicates(text)),
                ["compare"] = Lines(SelectionHelpers.CompareColumns(text)), ["compare23"] = Lines(SelectionHelpers.CompareColumns(text, 2, 3)),
                ["blanks"] = Lines(SelectionHelpers.FindBlanks(text)), ["totals"] = Lines(SelectionHelpers.Totals(text)),
            });
        }
        return new JsonObject { ["version"] = 1, ["cases"] = cases };
    }

    private static string GoldenPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "anda-hover-companion") dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("anda-hover-companion folder not found"), "mockup", "selection-golden.json");
    }

    /// <summary>The browser simulation has its own copy of these checks. This file is the contract: the app writes what it produces, the simulation must reproduce it exactly.</summary>
    [Fact]
    public void The_checks_match_the_fixture_the_simulation_must_reproduce()
    {
        string json = GoldenJson().ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        string path = GoldenPath();
        if (Environment.GetEnvironmentVariable("ANDA_WRITE_GOLDEN") == "1") File.WriteAllText(path, json.Replace("\r\n", "\n") + "\n");
        Assert.True(File.Exists(path), "selection-golden.json is missing: run this test with ANDA_WRITE_GOLDEN=1 and commit the file");
        Assert.Equal(JsonNode.Parse(File.ReadAllText(path))!.ToJsonString(), JsonNode.Parse(json)!.ToJsonString());
    }
}
