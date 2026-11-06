using AndaCompanion.Data;
using Xunit;

namespace AndaCompanion.Tests;

public class RecognizerTests
{
    [Theory]
    [InlineData("216482", "216482")]
    [InlineData("ANDA 216482", "216482")]
    [InlineData("anda-216482", "216482")]
    [InlineData("ANDA#216482", "216482")]
    [InlineData("see ANDA 214990, then the labeling", "214990")]
    [InlineData("(216482)", "216482")]
    [InlineData("Ref: 216482.", "216482")]
    [InlineData("a\n216482\nb", "216482")]
    public void Recognizes_a_six_digit_number(string text, string expected) =>
        Assert.Equal(expected, AndaValidator.Extract(text));

    [Theory]
    [InlineData("12345")]            // five digits
    [InlineData("1234567")]          // seven digits: a longer number must not be clipped to six
    [InlineData("21648a")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Rejects_everything_else(string? text) => Assert.Null(AndaValidator.Extract(text));

    [Fact]
    public void Digit_boundaries_are_enforced_on_both_sides()
    {
        Assert.Null(AndaValidator.Extract("9216482"));    // digit before
        Assert.Null(AndaValidator.Extract("2164829"));    // digit after
        Assert.Equal("216482", AndaValidator.Extract("x216482y"));
    }

    [Theory]
    [InlineData("NDA 021234")]
    [InlineData("NDA021234")]
    [InlineData("NDA: 021234")]
    [InlineData("NDA No. 021234")]
    [InlineData("nda #021234")]
    [InlineData("DMF 012345")]
    [InlineData("IND 123456")]
    [InlineData("BLA 125000")]
    [InlineData("MF-012345")]
    [InlineData("ANDA/NDA 216482")]      // ambiguous: the label right before the digits says NDA, so decline rather than guess
    public void A_number_labelled_as_another_application_type_is_never_taken_for_an_ANDA(string text) =>
        Assert.Null(AndaValidator.Extract(text));

    [Theory]
    [InlineData("NDA 021234 and ANDA 216482", "216482")]      // the labeled one is skipped, the real one found
    [InlineData("ANDA 021234", "021234")]                      // old-style ANDA numbers still work
    [InlineData("IND 123456; 214990", "214990")]
    public void Labels_only_exclude_the_number_they_label(string text, string expected) =>
        Assert.Equal(expected, AndaValidator.Extract(text));

    [Fact]
    public void Unregistered_kinds_are_never_recognized_by_accident()
    {
        // The registry is opt-in: widening scope must be a deliberate registry entry.
        Assert.Single(EntityKinds.Registered);
        Assert.Null(EntityKinds.Recognize("NDA 021234"));
        Assert.Equal("ANDA", EntityKinds.Recognize("ANDA 216482")!.Value.Kind.Label);
    }

    [Fact]
    public void Repository_resolves_known_numbers_only()
    {
        var repo = new MockAndaRepository();
        Assert.NotNull(repo.Find("216482"));
        Assert.NotNull(repo.Find("214990"));
        Assert.Null(repo.Find("999999"));
    }

    [Fact]
    public void Sister_references_resolve_to_real_records()
    {
        var repo = new MockAndaRepository();
        foreach (var number in new[] { "216482", "214990" })
            foreach (var sister in repo.Find(number)!.Sisters)
                Assert.NotNull(repo.Find(sister.Number));
    }
}
