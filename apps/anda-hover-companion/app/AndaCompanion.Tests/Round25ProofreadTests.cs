using System.Text.Json;
using AndaCompanion.Data;
using AndaCompanion.Models;
using AndaCompanion.Services;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// Round 25: Proofread, the extra fact checks, the recall row and the one question before sending come to the Windows app.
/// The simulation wrote mockup/proofread-golden.json (and the larger proofread-stress-golden.json) with its clock fixed at Jul 16, 2026;
/// the app must give the same answer for every case, character for character.
/// </summary>
[Collection("Global state")]
public class Round25ProofreadTests
{
    private static readonly MockAndaRepository Repo = new();
    private static readonly DateOnly Today = new(2026, 7, 16);
    private static readonly BusinessCalendar Calendar = new();

    private static string Folder(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "anda-hover-companion") dir = dir.Parent;
        return Path.Combine(new[] { dir?.FullName ?? throw new DirectoryNotFoundException("anda-hover-companion folder not found") }.Concat(parts).ToArray());
    }

    private static IReadOnlyList<DraftNote> Notes(AndaRecord r) => DraftNotes.For(r, null, Today);

    private static List<string> Mismatches(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Folder("mockup", file)));
        Assert.Equal("2026-07-16", doc.RootElement.GetProperty("today").GetString());
        var bad = new List<string>(); int n = 0, reviewed = 0;
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            n++;
            string draft = c.GetProperty("draft").GetString()!;
            var pr = Proofreader.Check(draft, Repo.Find);
            var fx = FactExtras.Check(draft, Repo.Find, Notes, Today);
            var want = c.GetProperty("proofread");
            void Same(string what, string expected, string actual) { if (expected != actual) bad.Add($"case {n} {what}\n  draft:    {draft}\n  expected: {expected}\n  actual:   {actual}"); }
            Same("items", string.Join("\n", want.GetProperty("items").EnumerateArray().Select(x => x.GetString())), string.Join("\n", pr.Items.Select(i => i.Text)));
            Same("corrected", want.GetProperty("corrected").GetString()!, pr.Corrected);
            Same("fixes", want.GetProperty("fixes").GetInt32().ToString(), pr.Fixes.ToString());
            var facts = c.GetProperty("facts");
            Same("differences", string.Join("\n", facts.GetProperty("differences").EnumerateArray().Select(x => x.GetString())), string.Join("\n", fx.Differences));
            Same("notes", string.Join("\n", facts.GetProperty("notes").EnumerateArray().Select(x => x.GetString())), string.Join("\n", fx.Notes));
            Same("alsoChecked", facts.GetProperty("alsoChecked").GetString() ?? "(none)", fx.AlsoChecked ?? "(none)");
            if (c.TryGetProperty("review", out var review))
            {
                // The stress file also holds the earlier review (round 17) and the one question, so those rules are held to the same awkward drafts.
                var r = DraftReview.Check(draft, Repo.Find, Today, Calendar);
                Same("review", string.Join("\n", review.EnumerateArray().Select(x => x.GetString())),
                    string.Join("\n", r.Findings.Select(f => (f.Kind switch { DraftFindingKind.Difference => "diff", DraftFindingKind.UnknownAnda => "unknown", DraftFindingKind.Ok => "ok", _ => "note" }) + " | " + f.Text)));
                var q = BeforeSend.Ask(draft, r, fx);
                Same("question", c.GetProperty("question").GetString() ?? "(none)", q is null ? "(none)" : (q.Gap ? "gap | " : "none | ") + q.Text);
                reviewed++;
            }
        }
        Assert.True(n > 0, "no cases in " + file);
        if (file.Contains("stress")) Assert.Equal(n, reviewed);
        return bad;
    }

    [Fact]
    public void The_app_gives_the_simulations_answer_for_every_shared_case()
    {
        var bad = Mismatches("proofread-golden.json");
        Assert.True(bad.Count == 0, bad.Count + " differences from the simulation:\n" + string.Join("\n", bad.Take(12)));
    }

    [Fact]
    public void The_app_gives_the_simulations_answer_for_several_hundred_awkward_drafts()
    {
        var bad = Mismatches("proofread-stress-golden.json");
        Assert.True(bad.Count == 0, bad.Count + " differences from the simulation:\n" + string.Join("\n", bad.Take(12)));
    }

    [Fact]
    public void A_date_that_also_stands_in_the_persons_own_note_is_pointed_out_and_recall_puts_that_note_first()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Folder("mockup", "proofread-stress-golden.json")));
        var noted = doc.RootElement.GetProperty("noted");
        using var zone = new ZoneScope("America/New_York");                                                      // the simulation wrote its cases on a computer in Eastern time
        var quick = new QuickNotesService(Path.Combine(Temp.NewDir(), "quick-notes.json"), () => new DateTime(2026, 7, 16, 14, 42, 0, DateTimeKind.Utc));
        Assert.True(quick.Add(noted.GetProperty("number").GetString(), noted.GetProperty("note").GetString()).Ok);
        IReadOnlyList<DraftNote> NotesOf(AndaRecord r) => DraftNotes.For(r, quick, Today);
        Assert.Equal(noted.GetProperty("who").GetString(), NotesOf(Repo.Find("216482")!)[0].Who);

        var bad = new List<string>(); int n = 0;
        foreach (var c in noted.GetProperty("cases").EnumerateArray())
        {
            n++;
            string draft = c.GetProperty("draft").GetString()!;
            var fx = FactExtras.Check(draft, Repo.Find, NotesOf, Today);
            var rc = Recall.For(draft, Repo.Find, NotesOf);
            void Same(string what, string expected, string actual) { if (expected != actual) bad.Add($"case {n} {what}\n  draft:    {draft}\n  expected: {expected}\n  actual:   {actual}"); }
            Same("notes", string.Join("\n", c.GetProperty("notes").EnumerateArray().Select(x => x.GetString())), string.Join("\n", fx.Notes));
            Same("differences", string.Join("\n", c.GetProperty("differences").EnumerateArray().Select(x => x.GetString())), string.Join("\n", fx.Differences));
            string want = string.Join("\n", c.GetProperty("recall").EnumerateArray().Select(g =>
                g.GetProperty("id").GetString() + " latest " + (g.GetProperty("latest").GetString() ?? "(none)") + " notes "
                + string.Join("; ", g.GetProperty("notes").EnumerateArray().Select(x => x.GetProperty("who").GetString() + " [" + string.Join(", ", x.GetProperty("shared").EnumerateArray().Select(w => w.GetString())) + "]"))
                + " items " + string.Join("; ", g.GetProperty("items").EnumerateArray().Select(x => x.GetString()))));
            string have = string.Join("\n", rc.Select(g => g.Number + " latest " + (g.Latest ?? "(none)") + " notes "
                + string.Join("; ", g.Notes.Select(x => x.Note.Who + " [" + string.Join(", ", x.Shared) + "]"))
                + " items " + string.Join("; ", g.Items.Select(i => $"{i.Title} | {i.Due} | {(i.Urgent ? "true" : "false")} | {i.Source}"))));
            Same("recall", want, have);
        }
        Assert.True(n >= 8, "the cases with a note are missing");
        Assert.True(bad.Count == 0, bad.Count + " differences from the simulation:\n" + string.Join("\n", bad.Take(8)));
    }
}
