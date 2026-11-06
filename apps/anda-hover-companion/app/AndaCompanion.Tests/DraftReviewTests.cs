using System.Text.Json;
using System.Text.Json.Nodes;
using AndaCompanion.Data;
using AndaCompanion.Services;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>"Review a draft": the checks that need no AI. Today is Wednesday Nov 4, 2026 in every test, so every weekday below was worked out from a calendar.</summary>
public class DraftReviewTests
{
    private static readonly MockAndaRepository Repo = new();
    private static readonly DateOnly Today = new(2026, 11, 4);
    private static readonly BusinessCalendar Calendar = new();
    private static DraftCheckResult Run(string draft) => DraftReview.Check(draft, Repo.Find, Today, Calendar);
    private static IEnumerable<string> Texts(DraftCheckResult r, DraftFindingKind kind) => r.Findings.Where(f => f.Kind == kind).Select(f => f.Text);

    // ---------- weekdays ----------

    [Fact]
    public void A_weekday_that_belongs_to_its_date_passes_and_one_that_does_not_is_flagged_with_the_real_weekday()
    {
        var ok = Run("The call is on Friday, November 6, 2026.");
        Assert.False(ok.HasDifferences);
        Assert.Contains("1 weekday against its date", ok.Findings[0].Text);

        var bad = Run("The call is on Friday, November 7, 2026.");
        var f = Assert.Single(Texts(bad, DraftFindingKind.Difference));
        Assert.Contains("November 7, 2026 is a Saturday", f);
        Assert.Contains("Friday, November 7, 2026", f);
    }

    [Theory]
    [InlineData("Fri Nov 6, 2026")]
    [InlineData("Friday, 11/6/2026")]
    [InlineData("Friday 6 Nov 2026 is wrong order but not read")]       // a day before the month is not read, so it is not judged
    public void Other_ways_of_writing_a_weekday_with_a_date_are_read(string text)
    {
        Assert.False(Run(text).HasDifferences);
    }

    [Fact]
    public void Without_a_year_a_weekday_is_judged_only_against_the_years_around_today_and_only_softly()
    {
        // Nov 7 is a Friday in 2025, so "Friday, Nov 7" is believable for last year's draft: nothing is said.
        Assert.Empty(Texts(Run("See you Friday, Nov 7."), DraftFindingKind.Note).Where(t => t.Contains("Nov 7")));
        // No Monday falls on Nov 7 in 2025, 2026 or 2027: a note (not a difference), naming the nearest year.
        var r = Run("See you Monday, Nov 7.");
        Assert.False(r.HasDifferences);
        var note = Assert.Single(Texts(r, DraftFindingKind.Note), t => t.Contains("Monday"));
        Assert.Contains("in 2026, November 7 is a Saturday, not a Monday", note);
    }

    [Fact]
    public void A_date_that_does_not_exist_is_flagged()
    {
        Assert.Contains("not a real date", Assert.Single(Texts(Run("Friday, Feb 30, 2026 works."), DraftFindingKind.Difference)));
        Assert.Contains("not a real date", Assert.Single(Texts(Run("Friday, Feb 30 works."), DraftFindingKind.Difference)));
    }

    // ---------- deadlines ----------

    [Fact]
    public void A_deadline_on_a_weekend_a_federal_holiday_or_in_the_past_gets_a_note_and_an_ordinary_one_does_not()
    {
        Assert.Contains("Sunday", Assert.Single(Texts(Run("Please respond by Nov 8, 2026."), DraftFindingKind.Note), t => t.Contains("not a business day")));
        var holiday = Assert.Single(Texts(Run("Please respond by Nov 11, 2026."), DraftFindingKind.Note), t => t.Contains("federal holiday"));
        Assert.Contains("Nov 11, 2026", holiday);
        Assert.Contains("in the past", Assert.Single(Texts(Run("Please respond by Oct 20, 2026."), DraftFindingKind.Note), t => t.Contains("past")));

        var fine = Run("Please respond by Nov 10, 2026.");
        Assert.DoesNotContain(fine.Findings, f => f.Kind == DraftFindingKind.Note && f.Text.Contains("Nov 10"));
        Assert.Contains("1 deadline against the calendar", fine.Findings[0].Text);
    }

    [Fact]
    public void An_impossible_deadline_date_is_flagged_but_February_29_without_a_year_is_left_alone()
    {
        Assert.Contains("November 31 is not a real date", Assert.Single(Texts(Run("Please reply by Nov 31."), DraftFindingKind.Difference)));
        Assert.Contains("February 29, 2026 is not a real date", Assert.Single(Texts(Run("Please reply by Feb 29, 2026."), DraftFindingKind.Difference)));
        Assert.False(Run("Please reply by Feb 29.").HasDifferences);               // it may be a leap year the draft means
        Assert.False(Run("See you Friday, Feb 29.").HasDifferences);
        Assert.Contains("February 30 is not a real date", Assert.Single(Texts(Run("See you Friday, Feb 30."), DraftFindingKind.Difference)));
    }

    [Fact]
    public void A_result_with_a_note_does_not_claim_there_is_nothing_to_look_at()
    {
        var withNote = Run("See you Monday, Nov 7.");
        Assert.Contains("no differences, but please read the note below", withNote.Findings[0].Text);
        Assert.DoesNotContain("nothing found", withNote.Findings[0].Text);
        Assert.Contains("nothing found that needs a second look", Run("See you Friday, Nov 6, 2026.").Findings[0].Text);
    }

    [Fact]
    public void A_date_without_a_deadline_word_is_not_treated_as_a_deadline()
    {
        var r = Run("We spoke on Nov 8, 2026 about the labeling.");
        Assert.DoesNotContain("deadline", r.Findings[0].Text);
        Assert.DoesNotContain(r.Findings, f => f.Text.Contains("business day"));
    }

    // ---------- the draft against itself ----------

    [Fact]
    public void Two_different_goal_dates_for_one_ANDA_are_flagged_even_when_neither_matches_a_record()
    {
        var r = Run("ANDA 999999 has a goal date of Aug 15, 2026. Update: the goal date for ANDA 999999 is Aug 18, 2026.");
        var f = Assert.Single(Texts(r, DraftFindingKind.Difference), t => t.Contains("2 different goal dates"));
        Assert.Contains("ANDA 999999", f);
        Assert.Contains("Aug 15, 2026 and Aug 18, 2026", f);
        // the same date written twice is not a contradiction
        Assert.DoesNotContain(Run("ANDA 999999 has a goal date of Aug 15. Again: the goal date for ANDA 999999 is Aug 15, 2026.").Findings, x => x.Text.Contains("different goal dates"));
    }

    [Fact]
    public void A_placeholder_left_in_the_draft_is_flagged_once_each()
    {
        var r = Run("Dear [NAME], the review of ANDA 216482 is TBD. TBD again, and [TBD].");
        var found = Texts(r, DraftFindingKind.Difference).Where(t => t.StartsWith("A placeholder")).ToList();
        Assert.Contains(found, t => t.Contains("“[NAME]”"));
        Assert.Contains(found, t => t.Contains("“TBD”"));
        Assert.Contains(found, t => t.Contains("“[TBD]”"));
        Assert.Equal(3, found.Count);
        Assert.DoesNotContain(Run("The letter is to be delivered.").Findings, f => f.Kind == DraftFindingKind.Difference);
    }

    [Fact]
    public void A_word_typed_twice_is_noted_but_that_that_and_had_had_are_left_alone()
    {
        Assert.Contains("“the” appears twice", Assert.Single(Texts(Run("Please see the the attached memo."), DraftFindingKind.Note), t => t.Contains("twice")));
        Assert.DoesNotContain(Run("I know that that is right, and she had had enough.").Findings, f => f.Text.Contains("twice"));
        Assert.DoesNotContain(Run("It was a very very good result of the team's team effort.").Findings, f => f.Text.Contains("“team")); // "team's team" is not a repeat
    }

    [Fact]
    public void An_ANDA_number_with_the_wrong_number_of_digits_gets_a_note()
    {
        Assert.Contains("has 5 digits", Assert.Single(Texts(Run("Please check ANDA 21648 again."), DraftFindingKind.Note), t => t.Contains("digits")));
        Assert.Contains("has 7 digits", Assert.Single(Texts(Run("Please check ANDA No. 2164821 again."), DraftFindingKind.Note), t => t.Contains("digits")));
        Assert.DoesNotContain(Run("Please check ANDA 216482 again.").Findings, f => f.Text.Contains("digits;"));
    }

    // ---------- together with the record check, and the receipt ----------

    [Fact]
    public void The_record_check_still_runs_and_its_differences_come_first()
    {
        var r = Run("ANDA 216482 has a goal date of Oct 5. Reply by Friday, November 7, 2026.");
        Assert.True(r.HasDifferences);
        var diffs = Texts(r, DraftFindingKind.Difference).ToList();
        Assert.Contains("goal date as Oct 5", diffs[0]);
        Assert.Contains("is a Saturday", diffs[1]);
        Assert.DoesNotContain(r.Findings, f => f.Kind == DraftFindingKind.Ok);
    }

    [Fact]
    public void A_clean_draft_lists_everything_that_was_checked_and_ends_with_what_was_not()
    {
        var r = Run("ANDA 216482: the goal date is Oct 3, 2026 and the forecast is Oct 17, 2026. We are running late. Please reply by Friday, November 6, 2026.");
        Assert.False(r.HasDifferences);
        var ok = r.Findings[0];
        Assert.Equal(DraftFindingKind.Ok, ok.Kind);
        Assert.Contains("1 ANDA number", ok.Text);
        Assert.Contains("2 goal or forecast dates against the record", ok.Text);
        Assert.Contains("1 on-track or late statement", ok.Text);
        Assert.Contains("1 weekday against its date", ok.Text);
        Assert.Contains("1 deadline against the calendar", ok.Text);
        Assert.Contains("leftover placeholders, repeated words and short ANDA numbers", ok.Text);
        Assert.Contains("nothing found that needs a second look", ok.Text);
        var last = r.Findings[^1];
        Assert.Equal(DraftFindingKind.Note, last.Kind);
        Assert.Contains("Not checked: spelling, grammar", last.Text);
        Assert.Contains("not that the draft is correct", last.Text);
        Assert.Equal(1, r.NumbersChecked); Assert.Equal(4, r.DatesChecked); Assert.Equal(1, r.StatusChecked);
    }

    [Fact]
    public void An_empty_draft_and_a_draft_with_nothing_to_check_say_so()
    {
        Assert.Contains("Nothing to check yet", Run("  ").Findings[0].Text);
        var r = Run("Hello. Thanks for the update.");
        Assert.Contains(r.Findings, f => f.Text.Contains("No ANDA numbers found"));
        Assert.Equal(DraftFindingKind.Ok, r.Findings[0].Kind);
        Assert.Contains("Not checked", r.Findings[^1].Text);
    }

    [Fact]
    public void The_same_draft_on_the_same_day_always_gives_the_same_answer_and_odd_input_never_throws()
    {
        const string draft = "ANDA 216482 goal Oct 5. By Nov 8, 2026 [TBD] the the. Monday, Nov 7. ANDA 2164.";
        Assert.Equal(Run(draft).Findings, Run(draft).Findings);
        var nasty = new[]
        {
            new string('a', 50_000), string.Concat(Enumerable.Repeat("Fri Nov 7 ", 5_000)), string.Concat(Enumerable.Repeat("by Nov 8 ", 5_000)),
            string.Concat(Enumerable.Repeat("the the ", 5_000)), string.Concat(Enumerable.Repeat("[", 20_000)), "Friday, ٣/٤/٢٠٢٦", "by ３/４/２０２７", "Friday, Feb 99, 9999", "Friday, 0/0/0000",
        };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var text in nasty) Assert.NotNull(Run(text));
        Assert.True(sw.ElapsedMilliseconds < 8000, $"{sw.ElapsedMilliseconds} ms");
    }

    // ---------- the shared fixture with the browser simulation ----------

    private static readonly string[] GoldenDrafts =
    {
        "The call is on Friday, November 6, 2026.", "The call is on Friday, November 7, 2026.", "Fri Nov 6, 2026", "Friday, 11/6/2026", "Friday, 11/7/2026",
        "See you Friday, Nov 7.", "See you Monday, Nov 7.", "Friday, Feb 30, 2026 works.", "Friday, Feb 30 works.", "Sat Nov 7, 2026 and Sun Nov 8, 2026 and Mon Nov 9, 2026",
        "Please respond by Nov 8, 2026.", "Please respond by Nov 11, 2026.", "Please respond by Oct 20, 2026.", "Please respond by Nov 10, 2026.", "Please respond by Nov 10.",
        "no later than 12/24/2026 and before Dec 25 and due Friday, Nov 6", "We spoke on Nov 8, 2026 about the labeling.",
        "ANDA 999999 has a goal date of Aug 15, 2026. Update: the goal date for ANDA 999999 is Aug 18, 2026.", "ANDA 999999 has a goal date of Aug 15. Again: the goal date for ANDA 999999 is Aug 15, 2026.",
        "Dear [NAME], the review of ANDA 216482 is TBD. TBD again, and [TBD].", "The letter is to be delivered.", "tbd and xx and XXX and ??? and <<insert>> and {{date}} and Lorem Ipsum",
        "Please see the the attached memo.", "I know that that is right, and she had had enough.", "It was a very very good result.", "Please check ANDA 21648 again.", "Please check ANDA No. 2164821 again.", "Please check ANDA 216482 again.",
        "ANDA 216482 has a goal date of Oct 5. Reply by Friday, November 7, 2026.",
        "ANDA 216482: the goal date is Oct 3, 2026 and the forecast is Oct 17, 2026. We are running late. Please reply by Friday, November 6, 2026.",
        "ANDA 214990 is on track, forecast Aug 6, goal Aug 15.", "ANDA 214990 is running late.", "Hello. Thanks for the update.", "  ", "",
        "ANDA 216482 and ANDA 214990 both look fine by Friday, Nov 6.", "ANDA 123456 goal date Aug 1.",
        "Monday, Nov 9, 2026 and Monday, Nov 9, 2026 and Monday, Nov 9, 2026", "by Nov 31", "due Feb 29, 2026", "Friday, Feb 29 is the leap day", "by Feb 29", "Friday, Nov 31", "Friday, 13/45/2026", "Friday, ٣/٤/٢٠٢٦ and by ３/４/２０２٧",
        "The meeting is Thursday, Nov 5. Reply by Friday, Nov 6. The goal date for ANDA 216482 is Oct 3, then Oct 3, 2026.",
    };

    private static JsonObject GoldenJson()
    {
        string Kind(DraftFindingKind k) => k switch { DraftFindingKind.Difference => "diff", DraftFindingKind.UnknownAnda => "unknown", DraftFindingKind.Ok => "ok", _ => "note" };
        var cases = new JsonArray();
        foreach (var draft in GoldenDrafts)
        {
            var r = Run(draft);
            cases.Add(new JsonObject
            {
                ["draft"] = draft, ["numbers"] = r.NumbersChecked, ["dates"] = r.DatesChecked, ["status"] = r.StatusChecked,
                ["findings"] = new JsonArray(r.Findings.Select(f => (JsonNode)new JsonObject { ["kind"] = Kind(f.Kind), ["text"] = f.Text }).ToArray()),
            });
        }
        return new JsonObject { ["version"] = 1, ["today"] = "2026-11-04", ["cases"] = cases };
    }

    private static string GoldenPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "anda-hover-companion") dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("anda-hover-companion folder not found"), "mockup", "review-golden.json");
    }

    /// <summary>The browser simulation has its own copy of the review. This file is the contract: the app writes what it produces, the simulation's test must reproduce it exactly.</summary>
    [Fact]
    public void The_review_matches_the_fixture_the_simulation_must_reproduce()
    {
        string json = GoldenJson().ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        string path = GoldenPath();
        if (Environment.GetEnvironmentVariable("ANDA_WRITE_GOLDEN") == "1") File.WriteAllText(path, json.Replace("\r\n", "\n") + "\n");
        Assert.True(File.Exists(path), "review-golden.json is missing: run this test with ANDA_WRITE_GOLDEN=1 and commit the file");
        Assert.Equal(JsonNode.Parse(File.ReadAllText(path))!.ToJsonString(), JsonNode.Parse(json)!.ToJsonString());
    }
}
