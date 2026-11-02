using System.IO.Compression;
using System.Text;
using AndaCompanion.Models;
using AndaCompanion.Services.Portfolio;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>Builds small .xlsx files for tests without any spreadsheet library: a zip of the XML parts Excel itself writes.</summary>
internal static class TestWorkbook
{
    /// <param name="rows">Cells as strings; a <see cref="DateCell"/> is written as an Excel date serial with a date format.</param>
    public static void Create(string path, object?[][] rows, string sheetName = "Portfolio", bool sharedStrings = false, string[]? hiddenFirst = null)
    {
        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Part(string name, string xml) { var e = zip.CreateEntry(name); using var w = new StreamWriter(e.Open(), new UTF8Encoding(false)); w.Write(xml); }

        var shared = new List<string>();
        string Col(int i) { string s = ""; i++; while (i > 0) { s = (char)('A' + (i - 1) % 26) + s; i = (i - 1) / 26; } return s; }
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        for (int r = 0; r < rows.Length; r++)
        {
            sb.Append($"<row r=\"{r + 1}\">");
            for (int c = 0; c < rows[r].Length; c++)
            {
                var v = rows[r][c]; string rc = Col(c) + (r + 1);
                if (v is null) continue;
                if (v is DateCell d) sb.Append($"<c r=\"{rc}\" s=\"1\"><v>{d.Serial.ToString(System.Globalization.CultureInfo.InvariantCulture)}</v></c>");
                else if (v is int or double) sb.Append($"<c r=\"{rc}\"><v>{Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)}</v></c>");
                else if (sharedStrings) { shared.Add(v.ToString()!); sb.Append($"<c r=\"{rc}\" t=\"s\"><v>{shared.Count - 1}</v></c>"); }
                else sb.Append($"<c r=\"{rc}\" t=\"inlineStr\"><is><t>{System.Security.SecurityElement.Escape(v.ToString())}</t></is></c>");
            }
            sb.Append("</row>");
        }
        sb.Append("</sheetData></worksheet>");

        Part("[Content_Types].xml", "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"xml\" ContentType=\"application/xml\"/></Types>");
        Part("_rels/.rels", "<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"/>");
        var sheetsXml = new StringBuilder(); var relsXml = new StringBuilder(); int n = 1;
        foreach (var hidden in hiddenFirst ?? Array.Empty<string>())
        {
            sheetsXml.Append($"<sheet name=\"{hidden}\" sheetId=\"{n}\" state=\"hidden\" r:id=\"rId{n}\"/>");
            relsXml.Append($"<Relationship Id=\"rId{n}\" Type=\"x\" Target=\"worksheets/sheet{n}.xml\"/>");
            Part($"xl/worksheets/sheet{n}.xml", "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Secret</t></is></c></row></sheetData></worksheet>");
            n++;
        }
        sheetsXml.Append($"<sheet name=\"{sheetName}\" sheetId=\"{n}\" r:id=\"rId{n}\"/>");
        relsXml.Append($"<Relationship Id=\"rId{n}\" Type=\"x\" Target=\"worksheets/sheet{n}.xml\"/>");
        Part($"xl/worksheets/sheet{n}.xml", sb.ToString());
        Part("xl/workbook.xml", $"<?xml version=\"1.0\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>{sheetsXml}</sheets></workbook>");
        Part("xl/_rels/workbook.xml.rels", $"<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">{relsXml}</Relationships>");
        Part("xl/styles.xml", "<?xml version=\"1.0\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><cellXfs count=\"2\"><xf numFmtId=\"0\"/><xf numFmtId=\"14\"/></cellXfs></styleSheet>");
        if (sharedStrings)
            Part("xl/sharedStrings.xml", "<?xml version=\"1.0\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                string.Concat(shared.Select(s => $"<si><t>{System.Security.SecurityElement.Escape(s)}</t></si>")) + "</sst>");
    }

    public sealed record DateCell(DateTime Date)
    {
        public double Serial => (Date - new DateTime(1899, 12, 30)).TotalDays;
    }

    public static DateCell Date(int y, int m, int d) => new(new DateTime(y, m, d));
}

public class TableReaderTests
{
    private static string File_(string name = "p.xlsx") => Path.Combine(Temp.NewDir(), name);

    [Fact]
    public void Reads_strings_numbers_and_Excel_dates_from_an_xlsx()
    {
        string path = File_();
        TestWorkbook.Create(path, new object?[][]
        {
            new object?[] { "ANDA", "Drug", "Goal Date", "Cycle" },
            new object?[] { "216482", "Metformin", TestWorkbook.Date(2026, 10, 3), 2 },
        });
        var t = TableReader.Read(path);
        Assert.Equal(new[] { "ANDA", "Drug", "Goal Date", "Cycle" }, t.Headers);
        var row = Assert.Single(t.Rows);
        Assert.Equal("216482", row[0]); Assert.Equal("Metformin", row[1]); Assert.Equal("2026-10-03", row[2]); Assert.Equal("2", row[3]);
    }

    [Fact]
    public void Shared_strings_work_the_way_Excel_writes_them()
    {
        string path = File_();
        TestWorkbook.Create(path, new object?[][] { new object?[] { "ANDA", "Notes" }, new object?[] { "216482", "Call applicant & check <IR>" } }, sharedStrings: true);
        Assert.Equal("Call applicant & check <IR>", TableReader.Read(path).Rows[0][1]);
    }

    [Fact]
    public void Hidden_sheets_are_never_read_and_the_sheet_with_an_ANDA_column_wins()
    {
        string path = File_();
        TestWorkbook.Create(path, new object?[][] { new object?[] { "ANDA No." }, new object?[] { "216482" } }, sheetName: "Mine", hiddenFirst: new[] { "Secret" });
        var t = TableReader.Read(path);
        Assert.Equal("Mine", t.SheetName);
        Assert.DoesNotContain("Secret", t.Headers);
    }

    [Fact]
    public void The_header_row_is_found_below_title_rows()
    {
        string path = File_();
        TestWorkbook.Create(path, new object?[][]
        {
            new object?[] { "My portfolio" }, new object?[] { "Updated Nov 1" }, new object?[] { "ANDA", "Goal" }, new object?[] { "216482", "10/3/2026" },
        });
        var t = TableReader.Read(path);
        Assert.Equal(new[] { "ANDA", "Goal" }, t.Headers);
        Assert.Single(t.Rows);
    }

    [Fact]
    public void Csv_handles_quotes_commas_newlines_and_semicolon_delimiters()
    {
        var t = TableReader.ParseCsv("ANDA,Drug,Notes\r\n216482,\"Metformin, ER\",\"line one\nline \"\"two\"\"\"\r\n214990,Other,\r\n");
        Assert.Equal("Metformin, ER", t.Rows[0][1]);
        Assert.Equal("line one\nline \"two\"", t.Rows[0][2]);
        Assert.Equal(2, t.Rows.Count);
        var semi = TableReader.ParseCsv("ANDA;Goal\n216482;2026-10-03\n");
        Assert.Equal("2026-10-03", semi.Rows[0][1]);
        var tab = TableReader.ParseCsv("ANDA\tGoal\n216482\t10/3/2026\n");
        Assert.Equal("10/3/2026", tab.Rows[0][1]);
    }

    [Fact]
    public void Csv_files_are_read_from_disk_with_a_byte_order_mark()
    {
        string path = File_("p.csv");
        File.WriteAllText(path, "ANDA,Drug\n216482,Metformin\n", new UTF8Encoding(true));
        Assert.Equal("ANDA", TableReader.Read(path).Headers[0]);
    }

    [Theory]
    [InlineData("")] [InlineData("does-not-exist.xlsx")]
    public void A_missing_file_gets_a_plain_message(string name) =>
        Assert.Contains("not found", Assert.Throws<PortfolioException>(() => TableReader.Read(name.Length == 0 ? "" : Path.Combine(Temp.NewDir(), name))).Message);

    [Fact]
    public void Unsupported_extensions_and_damaged_files_say_what_to_do()
    {
        string doc = File_("p.docx"); File.WriteAllText(doc, "x");
        Assert.Contains("not supported", Assert.Throws<PortfolioException>(() => TableReader.Read(doc)).Message);
        string bad = File_("p.xlsx"); File.WriteAllText(bad, "this is not a zip");
        Assert.Contains("could not be read", Assert.Throws<PortfolioException>(() => TableReader.Read(bad)).Message);
        string empty = File_("e.csv"); File.WriteAllText(empty, "");
        Assert.Contains("empty", Assert.Throws<PortfolioException>(() => TableReader.Read(empty)).Message);
    }

    [Fact]
    public void A_file_held_open_by_another_program_still_reads_when_that_program_allows_sharing_and_says_so_when_it_does_not()
    {
        string path = File_();
        TestWorkbook.Create(path, new object?[][] { new object?[] { "ANDA" }, new object?[] { "216482" } });
        using (var excel = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))   // Excel keeps it open this way
            Assert.Single(TableReader.Read(path).Rows);
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Contains("could not be opened", Assert.Throws<PortfolioException>(() => TableReader.Read(path)).Message);
    }

    [Fact]
    public void The_reader_never_changes_the_file()
    {
        string path = File_();
        TestWorkbook.Create(path, new object?[][] { new object?[] { "ANDA" }, new object?[] { "216482" } });
        var before = (File.GetLastWriteTimeUtc(path), new FileInfo(path).Length, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))));
        TableReader.Read(path);
        Assert.Equal(before, (File.GetLastWriteTimeUtc(path), new FileInfo(path).Length, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))));
    }

    [Theory]
    [InlineData("mm/dd/yyyy", true)] [InlineData("d-mmm-yy", true)] [InlineData("yyyy-mm-dd", true)] [InlineData("[$-409]mmmm d, yyyy", true)]
    [InlineData("General", false)] [InlineData("0.00", false)] [InlineData("#,##0", false)] [InlineData("\"d\"0", false)]
    public void Date_formats_are_told_apart_from_number_formats(string code, bool isDate) => Assert.Equal(isDate, TableReader.IsDateFormat(code));

    [Theory]
    [InlineData(46298, "2026-10-03")]
    [InlineData(1, "1899-12-31")]
    [InlineData(0, "0")]
    public void Serial_numbers_become_ISO_dates(double serial, string expected) => Assert.Equal(expected, TableReader.FromSerial(serial, false));

    [Fact]
    public void Column_letters_become_indexes() { Assert.Equal(0, TableReader.ColumnIndex("A1")); Assert.Equal(25, TableReader.ColumnIndex("Z9")); Assert.Equal(26, TableReader.ColumnIndex("AA10")); }
}

public class PortfolioRepositoryTests
{
    private static readonly DateOnly Today = new(2026, 11, 2);

    private static (string Path, PortfolioRepository Repo) Sample(string? notesFolder = null, IReadOnlyDictionary<string, string>? overrides = null)
    {
        string path = Path.Combine(Temp.NewDir(), "Portfolio.xlsx");
        TestWorkbook.Create(path, new object?[][]
        {
            new object?[] { "ANDA #", "Product", "Applicant", "GDUFA Goal Date", "Forecast", "Status", "RPM Notes", "Cycle", "Submission Date", "PSG Date" },
            new object?[] { "216482", "Metformin HCl ER Tablets, 500 mg", "Meridian (fictional)", TestWorkbook.Date(2026, 11, 10), TestWorkbook.Date(2026, 11, 20), "Amendment under review", "Waiting on IR response", 2, TestWorkbook.Date(2026, 1, 12), TestWorkbook.Date(2026, 8, 21) },
            new object?[] { "214990", "Metformin HCl ER Tablets, 750 mg", "Meridian (fictional)", TestWorkbook.Date(2026, 12, 1), null, "Labeling pending", null, 3, null, null },
            new object?[] { "not a number", "x" },
            new object?[] { "NDA 021234", "should not count as an ANDA" },
        });
        return (path, new PortfolioRepository(path, overrides, notesFolder, () => Today));
    }

    [Fact]
    public void Headers_are_matched_by_their_words_and_the_status_line_says_what_loaded()
    {
        var (_, repo) = Sample();
        Assert.True(repo.Status.Ok);
        Assert.Equal(2, repo.Status.Count);
        Assert.Contains("Loaded 2 ANDAs from Portfolio.xlsx", repo.Status.Message);
        Assert.Contains("2 rows without a readable ANDA number skipped", repo.Status.Message);
        Assert.Equal("ANDA #", repo.Mapping["number"]); Assert.Equal("GDUFA Goal Date", repo.Mapping["goal"]); Assert.Equal("RPM Notes", repo.Mapping["notes"]);
    }

    [Fact]
    public void A_record_shows_exactly_what_the_sheet_has()
    {
        var (_, repo) = Sample();
        var r = repo.Find("216482")!;
        Assert.Equal("Metformin HCl ER Tablets, 500 mg", r.Drug);
        Assert.Equal("Nov 10, 2026", r.GoalDate); Assert.Equal("· 8 days", r.GoalDays); Assert.Equal(8, r.DaysToGoal);
        Assert.Equal("Nov 20, 2026", r.ForecastDate); Assert.Equal("▲ +10 d vs goal", r.ForecastDelta); Assert.True(r.ForecastLate); Assert.Equal(10, r.ForecastDeltaDays);
        Assert.Equal("Meridian (fictional) · Cycle 2", r.Meta);
        Assert.Equal("Amendment under review", r.Latest);
        Assert.False(r.Demo); Assert.Equal("Your portfolio: Portfolio.xlsx", r.SourceLabel);
        Assert.Equal(new DateOnly(2026, 1, 12), r.SubmittedDate); Assert.Equal(new DateOnly(2026, 8, 21), r.PsgDateFromSheet);
        Assert.Contains(r.Notes, n => n.Who == "Your sheet" && n.Text == "Waiting on IR response");
        Assert.Contains("portfolio file", r.DataAsOf);
    }

    [Fact]
    public void A_forecast_or_progress_that_is_not_in_the_sheet_is_unknown_never_estimated()
    {
        var (_, repo) = Sample();
        var r = repo.Find("214990")!;
        Assert.False(r.HasForecast); Assert.False(r.HasProgress);
        Assert.Equal(AndaRecord.Unknown, r.ForecastDate); Assert.Equal("no forecast in your sheet", r.ForecastDelta);
        Assert.Null(r.ForecastDeltaDays); Assert.False(r.ForecastLate);
    }

    [Fact]
    public void Unknown_numbers_and_other_application_types_are_not_found()
    {
        var (_, repo) = Sample();
        Assert.Null(repo.Find("999999"));
        Assert.Null(repo.Find("021234"));
    }

    [Fact]
    public void Sisters_are_other_rows_from_the_same_applicant_and_product()
    {
        var (_, repo) = Sample();
        Assert.Equal("214990", Assert.Single(repo.Find("216482")!.Sisters).Number);
    }

    [Fact]
    public void Goal_days_follow_the_calendar_in_both_directions()
    {
        string path = Path.Combine(Temp.NewDir(), "p.csv");
        File.WriteAllText(path, "ANDA,Goal\n111111,2026-11-02\n222222,2026-11-03\n333333,2026-10-30\n444444,2026-11-01\n");
        var repo = new PortfolioRepository(path, today: () => Today);
        Assert.Equal("· today", repo.Find("111111")!.GoalDays);
        Assert.Equal("· 1 day", repo.Find("222222")!.GoalDays);
        Assert.Equal("· 3 days ago", repo.Find("333333")!.GoalDays);
        Assert.Equal("· 1 day ago", repo.Find("444444")!.GoalDays);
    }

    [Fact]
    public void An_edit_in_the_file_shows_on_the_next_lookup()
    {
        var (path, repo) = Sample();
        Assert.Equal("Amendment under review", repo.Find("216482")!.Latest);
        Thread.Sleep(50);
        TestWorkbook.Create(path, new object?[][] { new object?[] { "ANDA", "Status" }, new object?[] { "216482", "Approved" } });
        File.SetLastWriteTime(path, DateTime.Now.AddMinutes(1));
        Assert.Equal("Approved", repo.Find("216482")!.Latest);
    }

    [Fact]
    public void A_column_override_wins_over_detection_and_a_missing_number_column_is_explained()
    {
        string path = Path.Combine(Temp.NewDir(), "p.csv");
        File.WriteAllText(path, "Ref,Target\n216482,2026-12-01\n");
        var none = new PortfolioRepository(path, today: () => Today);
        Assert.False(none.Status.Ok);
        Assert.Contains("No column for the ANDA number", none.Status.Message);
        var fixedUp = new PortfolioRepository(path, new Dictionary<string, string> { ["number"] = "Ref", ["goal"] = "Target" }, today: () => Today);
        Assert.True(fixedUp.Status.Ok);
        Assert.Equal("Dec 1, 2026", fixedUp.Find("216482")!.GoalDate);
    }

    [Fact]
    public void A_bad_file_gives_an_unloaded_repository_with_a_message_not_an_exception()
    {
        var repo = new PortfolioRepository(Path.Combine(Temp.NewDir(), "gone.xlsx"));
        Assert.False(repo.Status.Ok);
        Assert.Contains("not found", repo.Status.Message);
        Assert.Null(repo.Find("216482"));
    }

    [Fact]
    public void Notes_from_a_folder_are_added_newest_first_and_limited()
    {
        string notes = Temp.NewDir();
        File.WriteAllText(Path.Combine(notes, "old.txt"), "ANDA 216482: called applicant\nunrelated line\n");
        File.SetLastWriteTime(Path.Combine(notes, "old.txt"), new DateTime(2026, 9, 1));
        File.WriteAllText(Path.Combine(notes, "new.md"), "- 216482 IR response received\n- NDA 216482 is a different thing\n");
        File.SetLastWriteTime(Path.Combine(notes, "new.md"), new DateTime(2026, 10, 30));
        var (_, repo) = Sample(notesFolder: notes);
        var folderNotes = repo.Find("216482")!.Notes.Where(n => n.Who != "Your sheet").ToList();
        Assert.Equal(2, folderNotes.Count);
        Assert.StartsWith("new.md", folderNotes[0].Who);
        Assert.Contains("IR response received", folderNotes[0].Text);
        Assert.DoesNotContain(folderNotes, n => n.Text.Contains("different thing"));
    }
}

public class NotesIndexTests
{
    [Fact]
    public void Docx_text_is_read_and_a_damaged_file_is_skipped()
    {
        string dir = Temp.NewDir();
        string docx = Path.Combine(dir, "meeting.docx");
        using (var zip = ZipFile.Open(docx, ZipArchiveMode.Create))
        {
            var e = zip.CreateEntry("word/document.xml");
            using var w = new StreamWriter(e.Open());
            w.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>ANDA 216482 </w:t></w:r><w:r><w:t>needs a call</w:t></w:r></w:p><w:p><w:r><w:t>nothing here</w:t></w:r></w:p></w:body></w:document>");
        }
        File.WriteAllText(Path.Combine(dir, "broken.docx"), "not a zip");
        var index = NotesIndex.Build(dir);
        Assert.Equal("ANDA 216482 needs a call", Assert.Single(index["216482"]).Text);
    }

    [Fact]
    public void Limits_hold_for_depth_size_and_missing_folders()
    {
        string dir = Temp.NewDir();
        Directory.CreateDirectory(Path.Combine(dir, "a", "b", "c"));
        File.WriteAllText(Path.Combine(dir, "a", "b", "c", "deep.txt"), "ANDA 216482 too deep");
        File.WriteAllText(Path.Combine(dir, "a", "ok.txt"), "ANDA 216482 fine");
        File.WriteAllText(Path.Combine(dir, "huge.txt"), "ANDA 216482 " + new string('x', NotesIndex.MaxFileBytes + 10));
        var index = NotesIndex.Build(dir);
        Assert.Equal("ANDA 216482 fine", Assert.Single(index["216482"]).Text);
        Assert.Empty(NotesIndex.Build(Path.Combine(dir, "nope")));
        Assert.Empty(NotesIndex.Build(null));
    }

    [Fact]
    public void Long_lines_are_trimmed_to_a_short_snippet_and_each_file_contributes_a_few()
    {
        string dir = Temp.NewDir();
        File.WriteAllText(Path.Combine(dir, "n.txt"), "ANDA 216482 " + new string('y', 500) + "\n" + string.Join("\n", Enumerable.Repeat("ANDA 216482 again", 10)));
        var list = NotesIndex.Build(dir)["216482"];
        Assert.Equal(NotesIndex.MaxPerFile, list.Count);
        Assert.True(list[0].Text.Length <= NotesIndex.MaxSnippetChars);
    }
}
