using AndaCompanion.Data;
using AndaCompanion.Services;
using Xunit;

namespace AndaCompanion.Tests;

public class DraftCheckTests
{
    private static readonly MockAndaRepository Repo = new();
    private static DraftCheckResult Run(string draft) => DraftCheck.Check(draft, Repo.Find);

    // The fictional records: 216482 goal Oct 3, 2026, forecast Oct 17, 2026, late (+14 d); 214990 goal Aug 15, forecast Aug 6, ahead.

    [Fact]
    public void A_correct_draft_says_exactly_what_was_checked()
    {
        var r = Run("ANDA 216482: the goal date is Oct 3, 2026 and the forecast is Oct 17, 2026. We are running late on this one.");
        Assert.False(r.HasDifferences);
        Assert.Equal(1, r.NumbersChecked); Assert.Equal(2, r.DatesChecked); Assert.Equal(1, r.StatusChecked);
        Assert.Equal(DraftFindingKind.Ok, r.Findings[0].Kind);
        Assert.Contains("Checked 1 ANDA number, 2 goal or forecast dates and 1 on-track or late statement", r.Findings[0].Text);
    }

    [Fact]
    public void A_wrong_goal_date_is_flagged_with_both_dates()
    {
        var r = Run("ANDA 216482 has a goal date of Oct 5.");
        var f = Assert.Single(r.Findings, x => x.Kind == DraftFindingKind.Difference);
        Assert.Contains("goal date as Oct 5", f.Text);
        Assert.Contains("Oct 3, 2026", f.Text);
    }

    [Fact]
    public void A_wrong_forecast_date_is_flagged_and_a_matching_one_is_not()
    {
        Assert.Contains(Run("For ANDA 216482 our forecast is Oct 10.").Findings, f => f.Kind == DraftFindingKind.Difference && f.Text.Contains("forecast date as Oct 10"));
        Assert.False(Run("For ANDA 216482 our forecast is Oct 17.").HasDifferences);
    }

    [Theory]
    [InlineData("ANDA 216482 goal date: 10/3/2026")]
    [InlineData("ANDA 216482 goal date: 10/3")]
    [InlineData("ANDA 216482 goal date: October 3rd")]
    [InlineData("ANDA 216482 goal date: 2026-10-03")]
    [InlineData("ANDA 216482 goal date: Oct. 3, 2026")]
    public void Date_formats_are_understood(string text) => Assert.False(Run(text).HasDifferences);

    [Fact]
    public void A_year_that_disagrees_is_a_difference_but_a_missing_year_is_not()
    {
        Assert.True(Run("ANDA 216482 goal date: Oct 3, 2025").HasDifferences);
        Assert.False(Run("ANDA 216482 goal date: Oct 3").HasDifferences);
    }

    [Fact]
    public void A_date_with_no_goal_or_forecast_word_is_never_judged()
    {
        var r = Run("ANDA 216482: we are meeting on Oct 9 to discuss it.");
        Assert.False(r.HasDifferences);
        Assert.Equal(0, r.DatesChecked);
    }

    [Fact]
    public void On_track_wording_against_a_late_forecast_is_a_difference_and_the_reverse()
    {
        var late = Run("ANDA 216482 is on track.");
        Assert.Contains(late.Findings, f => f.Kind == DraftFindingKind.Difference && f.Text.Contains("sounds on track") && f.Text.Contains("14 days later than the goal"));
        var ahead = Run("ANDA 214990 is running late.");
        Assert.Contains(ahead.Findings, f => f.Kind == DraftFindingKind.Difference && f.Text.Contains("sounds late"));
        Assert.False(Run("ANDA 214990 is on track.").HasDifferences);
        Assert.False(Run("ANDA 216482 is behind schedule.").HasDifferences);
    }

    [Fact]
    public void An_unknown_number_is_reported_once_and_nothing_is_guessed_about_it()
    {
        var r = Run("ANDA 999999 has a goal date of Oct 3. Please note ANDA 999999 again.");
        Assert.Single(r.Findings, f => f.Kind == DraftFindingKind.UnknownAnda);
        Assert.True(r.HasDifferences);
    }

    [Fact]
    public void A_number_labelled_as_another_application_type_is_not_treated_as_an_ANDA()
    {
        var r = Run("NDA 216482 has a goal date of Oct 5.");
        Assert.Equal(0, r.NumbersChecked);
        Assert.Contains(r.Findings, f => f.Text.StartsWith("No ANDA numbers found"));
    }

    [Fact]
    public void A_following_sentence_inherits_the_paragraphs_single_ANDA_but_a_new_paragraph_does_not()
    {
        var r = Run("ANDA 216482 needs attention.\nThe goal date is Oct 5.\n\nThe goal date is Oct 3.");
        var diff = Assert.Single(r.Findings, f => f.Kind == DraftFindingKind.Difference);
        Assert.Contains("Oct 5", diff.Text);
        Assert.Equal(1, r.DatesChecked);                    // the second paragraph has no subject, so its date is not judged
    }

    [Fact]
    public void A_sentence_naming_several_ANDAs_is_not_checked_and_says_so()
    {
        var r = Run("ANDA 216482 and ANDA 214990 both have a goal date of Oct 3.");
        Assert.DoesNotContain(r.Findings, f => f.Kind == DraftFindingKind.Difference);
        Assert.Contains(r.Findings, f => f.Kind == DraftFindingKind.Note && f.Text.Contains("several ANDAs"));
        Assert.Equal(2, r.NumbersChecked);
    }

    [Fact]
    public void Empty_or_numberless_drafts_get_a_plain_note()
    {
        Assert.Single(Run("   ").Findings);
        Assert.Contains("Nothing to check", Run("").Findings[0].Text);
        Assert.Contains("No ANDA numbers found", Run("Hello team, thanks for the update.").Findings[0].Text);
    }

    [Fact]
    public void Differences_come_before_notes_and_the_limit_note_is_always_last()
    {
        var r = Run("ANDA 216482 goal date is Oct 5.");
        Assert.Equal(DraftFindingKind.Difference, r.Findings[0].Kind);
        Assert.Equal(DraftFindingKind.Note, r.Findings[^1].Kind);
        Assert.Contains("not the system of record", r.Findings[^1].Text);
    }

    [Fact]
    public void It_is_deterministic_and_never_throws_on_odd_input()
    {
        string odd = "ANDA 216482 goal 13/45/2026 and \u0000 😀 forecast 99/99 ".PadRight(5000, 'x');
        var a = DraftCheck.Check(odd, Repo.Find); var b = DraftCheck.Check(odd, Repo.Find);
        Assert.Equal(a.Findings.Select(f => f.Text), b.Findings.Select(f => f.Text));
    }
}
