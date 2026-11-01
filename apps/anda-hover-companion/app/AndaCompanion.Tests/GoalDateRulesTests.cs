using AndaCompanion.Services;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// The accuracy policy in code: only source-confirmed tiers are encoded, and every other
/// combination must report "not encoded" (null) rather than a guess.
/// </summary>
public class GoalDateRulesTests
{
    [Fact]
    public void Gdufa3_encodes_only_the_confirmed_tier()
    {
        var rules = new Gdufa3Rules();
        Assert.Equal("GDUFA III", rules.Version);
        Assert.Equal(8, rules.AmendmentAssessmentMonths(AmendmentClass.Major, requiresPreapprovalInspection: false));
        Assert.Equal(10, rules.StandardOriginalAndaMonths);
    }

    [Theory]
    [InlineData(AmendmentClass.Major, true)]
    [InlineData(AmendmentClass.Minor, false)]
    [InlineData(AmendmentClass.Minor, true)]
    public void Gdufa3_does_not_guess_unencoded_tiers(AmendmentClass kind, bool inspection) =>
        Assert.Null(new Gdufa3Rules().AmendmentAssessmentMonths(kind, inspection));

    [Fact]
    public void Gdufa3_cites_its_sources() =>
        Assert.Contains("fda.gov", new Gdufa3Rules().Citation);

    [Fact]
    public void Gdufa4_is_a_loud_placeholder_until_the_letter_is_published()
    {
        var rules = new Gdufa4Rules();
        Assert.Contains("pending", rules.Version);
        Assert.Null(rules.AmendmentAssessmentMonths(AmendmentClass.Major, false));
        var ex = Assert.Throws<InvalidOperationException>(() => rules.StandardOriginalAndaMonths);
        Assert.Contains("not yet published", ex.Message);
    }
}
