using System.Text.Json;
using AndaCompanion.Services;
using Xunit;

namespace AndaCompanion.Tests;

public class FuzzTests
{
    /// <summary>Reads each note in ANDA_FUZZ_IN with the app's reader and writes what it found to ANDA_FUZZ_OUT, for a script to compare with the simulation's copy.</summary>
    [FuzzFact]
    public void Write_what_the_app_reads_from_a_file_of_notes()
    {
        string input = Environment.GetEnvironmentVariable("ANDA_FUZZ_IN")!, output = Environment.GetEnvironmentVariable("ANDA_FUZZ_OUT")!;
        var today = new DateOnly(2026, 11, 4);
        var notes = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(input))!;
        object Norm(SuggestedItem? i) => i is null ? null! : new { title = i.Title, category = (int)i.Category, due = i.Due is { } d ? d.ToString("yyyy-MM-dd") : "", high = i.High, repeat = (int)i.Repeat, repeatDay = i.RepeatDay, number = i.Number ?? "", actionable = i.Actionable };
        var rows = notes.Select(n => new
        {
            items = NoteParser.Parse(n, today).Select(Norm).ToList(),
            one = Norm(NoteParser.ParseOne(n, today)),
            due = NoteParser.ReadDue(n, today) is { } d ? d.ToString("yyyy-MM-dd") : "",
        }).ToList();
        File.WriteAllText(output, JsonSerializer.Serialize(rows));
    }
}
