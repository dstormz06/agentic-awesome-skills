using AndaCompanion.Data;
using AndaCompanion.Models;
using AndaCompanion.Services;
using Xunit;

namespace AndaCompanion.Tests;

public class BusinessCalendarTests
{
    private static readonly BusinessCalendar Cal = new();
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Fact]
    public void Observed_dates_match_the_OPM_list_for_2026()
    {
        // Verified against OPM's published list (opm.gov/policy-data-oversight/pay-leave/federal-holidays): Jan 1 Thu, Jul 3 Fri
        // (Jul 4 is a Saturday), Nov 11 Wed, Dec 25 Fri. The rest follow the statutory rules.
        var observed = BusinessCalendar.HolidaysOf(2026).ToDictionary(h => h.Name, h => h.Observed);
        Assert.Equal(D(2026, 1, 1), observed["New Year's Day"]);
        Assert.Equal(D(2026, 1, 19), observed["Birthday of Martin Luther King, Jr."]);
        Assert.Equal(D(2026, 2, 16), observed["Washington's Birthday"]);
        Assert.Equal(D(2026, 5, 25), observed["Memorial Day"]);
        Assert.Equal(D(2026, 6, 19), observed["Juneteenth National Independence Day"]);
        Assert.Equal(D(2026, 7, 3), observed["Independence Day"]);
        Assert.Equal(D(2026, 9, 7), observed["Labor Day"]);
        Assert.Equal(D(2026, 10, 12), observed["Columbus Day"]);
        Assert.Equal(D(2026, 11, 11), observed["Veterans Day"]);
        Assert.Equal(D(2026, 11, 26), observed["Thanksgiving Day"]);
        Assert.Equal(D(2026, 12, 25), observed["Christmas Day"]);
    }

    [Fact]
    public void Observed_dates_match_the_OPM_list_for_2027()
    {
        // OPM: Jan 1 Fri, Jun 18 Fri (Jun 19 is a Saturday), Jul 5 Mon (Jul 4 is a Sunday), Nov 11 Thu, Dec 24 Fri (Dec 25 is a Saturday).
        var observed = BusinessCalendar.HolidaysOf(2027).ToDictionary(h => h.Name, h => h.Observed);
        Assert.Equal(D(2027, 1, 1), observed["New Year's Day"]);
        Assert.Equal(D(2027, 6, 18), observed["Juneteenth National Independence Day"]);
        Assert.Equal(D(2027, 7, 5), observed["Independence Day"]);
        Assert.Equal(D(2027, 11, 11), observed["Veterans Day"]);
        Assert.Equal(D(2027, 12, 24), observed["Christmas Day"]);
    }

    [Fact]
    public void A_New_Years_Day_on_a_Saturday_is_observed_on_the_previous_December_31()
    {
        // 2022-01-01 was a Saturday
        Assert.Equal(DayKind.Holiday, Cal.KindOf(D(2021, 12, 31)));
        Assert.Equal("New Year's Day", Cal.HolidayName(D(2021, 12, 31)));
    }

    [Fact]
    public void Inauguration_Day_is_regional_and_only_in_the_right_years()
    {
        Assert.Contains(BusinessCalendar.HolidaysOf(2025), h => h.Regional && h.Observed == D(2025, 1, 20));   // a Monday
        Assert.Contains(BusinessCalendar.HolidaysOf(2013), h => h.Regional && h.Observed == D(2013, 1, 21));   // Jan 20, 2013 was a Sunday: observed Monday
        Assert.DoesNotContain(BusinessCalendar.HolidaysOf(2028), h => h.Regional);
        Assert.DoesNotContain(BusinessCalendar.HolidaysOf(2026), h => h.Regional);
    }

    [Theory]
    [InlineData(2026, 10, 3, DayKind.Weekend)]       // Saturday
    [InlineData(2026, 10, 17, DayKind.Weekend)]
    [InlineData(2026, 10, 5, DayKind.Business)]
    [InlineData(2026, 11, 26, DayKind.Holiday)]
    [InlineData(2026, 7, 3, DayKind.Holiday)]
    [InlineData(2026, 7, 4, DayKind.Weekend)]
    public void Kinds(int y, int m, int d, DayKind kind) => Assert.Equal(kind, Cal.KindOf(D(y, m, d)));

    [Fact]
    public void The_demo_goal_dates_really_fall_on_weekends()
    {
        // The bundled fictional records happen to land on Saturdays: Oct 3, Oct 17 and Aug 15, 2026.
        Assert.Equal(DayOfWeek.Saturday, D(2026, 10, 3).DayOfWeek);
        Assert.Equal(DayOfWeek.Saturday, D(2026, 10, 17).DayOfWeek);
        Assert.Equal(DayOfWeek.Saturday, D(2026, 8, 15).DayOfWeek);
    }

    [Fact]
    public void Nearest_business_days_skip_weekends_and_holidays()
    {
        var f = Cal.Evaluate(D(2026, 10, 3));
        Assert.Equal(D(2026, 10, 2), f.PreviousBusinessDay);
        Assert.Equal(D(2026, 10, 5), f.NextBusinessDay);       // Mon Oct 5, but Oct 12 (Columbus) is later, so Monday is a normal day
        var thanks = Cal.Evaluate(D(2026, 11, 25));
        Assert.Equal(D(2026, 11, 27), thanks.NextBusinessDay);  // skips Thanksgiving
        var longWeekend = Cal.Evaluate(D(2026, 7, 3));
        Assert.Equal(D(2026, 7, 2), longWeekend.PreviousBusinessDay);
        Assert.Equal(D(2026, 7, 6), longWeekend.NextBusinessDay);
    }

    [Theory]
    [InlineData(2026, 11, 25, 1, true)]    // Wed before Thanksgiving: 1 business day before
    [InlineData(2026, 11, 24, 2, true)]
    [InlineData(2026, 11, 23, 3, true)]
    [InlineData(2026, 11, 27, 1, false)]   // Fri after
    [InlineData(2026, 11, 30, 2, false)]
    [InlineData(2026, 12, 1, 3, false)]
    public void Business_days_near_a_holiday_are_counted_from_the_nearest_one(int y, int m, int d, int n, bool before)
    {
        var f = Cal.Evaluate(D(y, m, d), leadBusinessDays: 3);
        Assert.Equal(DayKind.Business, f.Kind);
        Assert.Equal(n, f.BusinessDaysFromHoliday);
        Assert.Equal(before, f.Before);
        Assert.Equal("Thanksgiving Day", f.HolidayName);
    }

    [Fact]
    public void The_lead_time_is_adjustable_and_zero_turns_it_off()
    {
        Assert.Null(Cal.Evaluate(D(2026, 11, 23), leadBusinessDays: 2).BusinessDaysFromHoliday);
        Assert.NotNull(Cal.Evaluate(D(2026, 11, 23), leadBusinessDays: 3).BusinessDaysFromHoliday);
        Assert.Null(Cal.Evaluate(D(2026, 11, 25), leadBusinessDays: 0).BusinessDaysFromHoliday);
    }

    [Fact]
    public void An_ordinary_date_is_not_notable()
    {
        Assert.False(Cal.Evaluate(D(2026, 8, 6)).IsNotable);   // Thursday in August
    }

    [Fact]
    public void Business_days_between_excludes_both_ends_and_skips_weekends_and_holidays()
    {
        Assert.Equal(1, Cal.BusinessDaysBetween(D(2026, 11, 24), D(2026, 11, 26)));    // Wed between Tue and Thu
        Assert.Equal(0, Cal.BusinessDaysBetween(D(2026, 11, 25), D(2026, 11, 26)));
        Assert.Equal(0, Cal.BusinessDaysBetween(D(2026, 10, 2), D(2026, 10, 5)));      // Fri to Mon
    }

    [Fact]
    public void Extra_closures_from_a_file_are_honoured_and_a_bad_file_is_ignored()
    {
        string dir = Temp.NewDir(), path = Path.Combine(dir, "closures.txt");
        File.WriteAllText(path, "# executive order closings\n2025-12-24 Executive order closing\nnot a date line\n2025-12-26 Executive order closing\n");
        var cal = BusinessCalendar.FromFile(path);
        Assert.Equal(DayKind.Holiday, cal.KindOf(D(2025, 12, 24)));
        Assert.Equal("Executive order closing", cal.HolidayName(D(2025, 12, 26)));
        Assert.Equal(DayKind.Business, BusinessCalendar.FromFile(Path.Combine(dir, "missing.txt")).KindOf(D(2025, 12, 24)));
        File.WriteAllBytes(path, new byte[] { 0xFF, 0xFE, 0x00 });
        Assert.NotNull(BusinessCalendar.FromFile(path));
    }
}

public class WatchRulesTests
{
    private static readonly BusinessCalendar Cal = new();
    private static readonly MockAndaRepository Repo = new();
    private static readonly DateOnly RealToday = new(2026, 11, 2);

    [Fact]
    public void The_demo_record_flags_a_goal_and_a_forecast_on_a_Saturday_and_ignores_the_real_calendar()
    {
        var flags = WatchRules.Evaluate(Repo.Find("216482")!, RealToday, Cal);
        Assert.Contains(flags, f => f.Code == "goal-date-calendar" && f.Text.Contains("falls on a weekend: Sat Oct 3") && f.Text.Contains("Fri Oct 2") && f.Text.Contains("Mon Oct 5"));
        Assert.Contains(flags, f => f.Code == "forecast-date-calendar" && f.Text.Contains("falls on a weekend"));
        // fictional data is judged as of its extract date, never "overdue" or "stale" because the real date moved on
        Assert.DoesNotContain(flags, f => f.Code is "goal-passed" or "stale-data");
    }

    [Fact]
    public void A_weekday_forecast_gives_no_calendar_flag()
    {
        var flags = WatchRules.Evaluate(Repo.Find("214990")!, RealToday, Cal);   // forecast Thu Aug 6
        Assert.DoesNotContain(flags, f => f.Code == "forecast-date-calendar");
        Assert.Contains(flags, f => f.Code == "goal-date-calendar");              // goal Sat Aug 15
    }

    [Fact]
    public void Flags_carry_evidence_a_reason_and_a_source()
    {
        foreach (var f in WatchRules.Evaluate(Repo.Find("216482")!, RealToday, Cal))
        {
            Assert.False(string.IsNullOrWhiteSpace(f.Text));
            Assert.False(string.IsNullOrWhiteSpace(f.Why));
            Assert.False(string.IsNullOrWhiteSpace(f.Source));
        }
    }

    private static AndaRecord Real(DateOnly? goal, DateOnly? asOf = null, DateOnly? submitted = null, DateOnly? psg = null, DateOnly? forecast = null) => new()
    {
        Number = "123456", Drug = "Test", Meta = "x", GoalDate = goal?.ToString("MMM d, yyyy") ?? AndaRecord.Unknown, GoalDays = "", ProgressPct = -1,
        ForecastDate = forecast?.ToString("MMM d, yyyy") ?? AndaRecord.Unknown, ForecastDelta = "", ForecastLate = false, ForecastConfidence = "", Latest = "",
        Pending = Array.Empty<PendingItem>(), Notes = Array.Empty<NoteItem>(), ForecastDetail = "", Sisters = Array.Empty<SisterRef>(),
        GoalDateValue = goal, ForecastDateValue = forecast, AsOfDate = asOf, SubmittedDate = submitted, PsgDateFromSheet = psg, SourceLabel = "Your portfolio: P.xlsx",
    };

    [Fact]
    public void A_passed_goal_is_the_most_severe_flag()
    {
        var flags = WatchRules.Evaluate(Real(new DateOnly(2026, 10, 28)), RealToday, Cal);   // Wed Oct 28; 5 days ago
        Assert.Equal("goal-passed", flags[0].Code);
        Assert.Equal(WatchSeverity.Bad, flags[0].Severity);
        Assert.Contains("5 days ago", flags[0].Text);
    }

    [Fact]
    public void A_goal_in_a_few_days_is_flagged_and_a_far_one_is_not()
    {
        Assert.Contains(WatchRules.Evaluate(Real(new DateOnly(2026, 11, 4)), RealToday, Cal), f => f.Code == "goal-soon" && f.Text.Contains("in 2 days"));
        Assert.DoesNotContain(WatchRules.Evaluate(Real(new DateOnly(2026, 12, 15)), RealToday, Cal), f => f.Code == "goal-soon");
    }

    [Fact]
    public void Stale_data_is_flagged_for_real_data_only_and_only_after_the_threshold()
    {
        Assert.Contains(WatchRules.Evaluate(Real(new DateOnly(2027, 3, 3), asOf: new DateOnly(2026, 10, 1)), RealToday, Cal), f => f.Code == "stale-data" && f.Text.Contains("32 days"));
        Assert.DoesNotContain(WatchRules.Evaluate(Real(new DateOnly(2027, 3, 3), asOf: new DateOnly(2026, 10, 30)), RealToday, Cal), f => f.Code == "stale-data");
    }

    [Fact]
    public void A_missing_goal_date_is_said_plainly()
    {
        Assert.Contains(WatchRules.Evaluate(Real(null), RealToday, Cal), f => f.Code == "no-goal-date");
    }

    [Fact]
    public void A_PSG_date_after_submission_is_a_prompt_to_check_not_a_conclusion()
    {
        var flags = WatchRules.Evaluate(Real(new DateOnly(2027, 3, 3), submitted: new DateOnly(2026, 6, 3), psg: new DateOnly(2026, 8, 21)), RealToday, Cal);
        var f = Assert.Single(flags, x => x.Code == "psg-after-submission");
        Assert.Contains("check", f.Why, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not judge", f.Why);
        Assert.DoesNotContain(WatchRules.Evaluate(Real(new DateOnly(2027, 3, 3), submitted: new DateOnly(2026, 9, 1), psg: new DateOnly(2026, 8, 21)), RealToday, Cal), x => x.Code == "psg-after-submission");
    }

    [Fact]
    public void Output_is_ordered_by_severity_then_stable()
    {
        var a = WatchRules.Evaluate(Real(new DateOnly(2026, 12, 5), forecast: new DateOnly(2026, 12, 19)), RealToday, Cal);   // both Saturdays
        Assert.Equal(a.OrderByDescending(f => f.Severity).ThenBy(f => WatchRules.Rank(f.Code)).Select(f => f.Code), a.Select(f => f.Code));
        Assert.Equal("goal-date-calendar", a[0].Code);          // the goal comes before the forecast
        Assert.Equal(a.Select(f => f.Text), WatchRules.Evaluate(Real(new DateOnly(2026, 12, 5), forecast: new DateOnly(2026, 12, 19)), RealToday, Cal).Select(f => f.Text));
    }

    [Fact]
    public void Urgency_is_about_attention_a_late_forecast_or_a_passed_goal()
    {
        Assert.True(UrgencyPolicy.IsUrgent(Repo.Find("216482")!, RealToday, Cal));          // forecast +14 days
        Assert.False(UrgencyPolicy.IsUrgent(Repo.Find("214990")!, RealToday, Cal));         // 9 days ahead
        Assert.True(UrgencyPolicy.IsUrgent(Real(new DateOnly(2026, 10, 28)), RealToday, Cal));   // goal passed
        Assert.False(UrgencyPolicy.IsUrgent(Repo.Find("216482")!, RealToday, Cal, lateDays: 30 /* not offered, but harmless */));
        Assert.Contains("14 days", UrgencyPolicy.Reason(Repo.Find("216482")!, RealToday, Cal)!);
        Assert.Equal(new[] { 3, 7, 14 }, UrgencyPolicy.LateDayChoices);
        Assert.Equal(7, UrgencyPolicy.Normalize(5));
        Assert.Equal(14, UrgencyPolicy.Normalize(14));
    }
}

[Collection("Global state")]
public class Phase1SelfTestCheckTests
{
    [Fact]
    public void The_phase_1_self_test_check_passes_against_the_demo_records()
    {
        var r = SelfTestChecks.Phase1Rules(new AndaCompanion.Data.MockAndaRepository(), Temp.NewDir());
        Assert.True(r.Status == CheckStatus.Pass, r.Detail);
    }
}
