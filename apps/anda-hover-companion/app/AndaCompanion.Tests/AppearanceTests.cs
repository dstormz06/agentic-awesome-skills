using AndaCompanion.Theming;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// The product rules about appearance, enforced as numbers: light-only, A is the default,
/// and every text ink meets WCAG 2.x AA (the Section 508 baseline) on every appearance.
/// </summary>
public class AppearanceTests
{
    public static IEnumerable<object[]> AllAppearances() => Appearances.All.Select(a => new object[] { a.Id });

    private static double Channel(int v)
    {
        double c = v / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static double Luminance(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 8) hex = hex[2..];
        int r = Convert.ToInt32(hex[..2], 16), g = Convert.ToInt32(hex[2..4], 16), b = Convert.ToInt32(hex[4..6], 16);
        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    public static double Contrast(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        if (la < lb) (la, lb) = (lb, la);
        return (la + 0.05) / (lb + 0.05);
    }

    [Fact]
    public void There_are_five_appearances_and_A_is_first()
    {
        Assert.Equal(new[] { AppearanceId.A, AppearanceId.B, AppearanceId.C, AppearanceId.D, AppearanceId.E },
            Appearances.All.Select(a => a.Id));
        Assert.Equal(AppearanceId.A, new AndaCompanion.Services.Settings().Appearance);
        Assert.Equal(AppearanceId.A, ThemeManager.Current.Id);
    }

    [Theory, MemberData(nameof(AllAppearances))]
    public void Every_appearance_is_light(AppearanceId id)
    {
        var p = Appearances.Get(id).Palette;
        Assert.True(Luminance(p.Bg) > 0.85, $"{id} surface is not light");
        Assert.True(Luminance(p.Ink) < 0.1, $"{id} ink should be dark text on a light surface");
    }

    [Theory, MemberData(nameof(AllAppearances))]
    public void Text_inks_meet_AA_on_the_surface(AppearanceId id)
    {
        var p = Appearances.Get(id).Palette;
        Assert.True(Contrast(p.Ink, p.Bg) >= 7, $"{id} Ink");
        Assert.True(Contrast(p.Ink2, p.Bg) >= 4.5, $"{id} Ink2 = {Contrast(p.Ink2, p.Bg):0.00}");
        Assert.True(Contrast(p.Ink3, p.Bg) >= 4.5, $"{id} Ink3 = {Contrast(p.Ink3, p.Bg):0.00}");
        Assert.True(Contrast(p.Accent, p.Bg) >= 4.5, $"{id} Accent");
        Assert.True(Contrast(p.Accent2, p.Bg) >= 4.5, $"{id} Accent2");
    }

    [Theory, MemberData(nameof(AllAppearances))]
    public void Status_inks_meet_AA_on_their_tints_and_on_the_surface(AppearanceId id)
    {
        var p = Appearances.Get(id).Palette;
        string goodBg = p.GoodBg.Length == 9 ? p.Bg : p.GoodBg;      // B uses a transparent tint
        Assert.True(Contrast(p.Good, goodBg) >= 4.5, $"{id} Good on tint");
        Assert.True(Contrast(p.Warn, p.WarnBg) >= 4.5, $"{id} Warn on tint");
        Assert.True(Contrast(p.Bad, p.BadBg) >= 4.5, $"{id} Bad on tint");
        Assert.True(Contrast(p.Good, p.Bg) >= 4.5, $"{id} Good");
        Assert.True(Contrast(p.Warn, p.Bg) >= 4.5, $"{id} Warn");
        Assert.True(Contrast(p.Bad, p.Bg) >= 4.5, $"{id} Bad");
    }

    [Theory, MemberData(nameof(AllAppearances))]
    public void White_text_on_the_accent_button_is_readable(AppearanceId id) =>
        Assert.True(Contrast("#FFFFFF", Appearances.Get(id).Palette.Accent) >= 4.5, $"{id} primary button");

    [Fact]
    public void Status_never_depends_on_color_alone_so_the_three_inks_must_be_distinct()
    {
        foreach (var a in Appearances.All)
            Assert.Equal(3, new[] { a.Palette.Good, a.Palette.Warn, a.Palette.Bad }.Distinct().Count());
    }

    [Fact]
    public void Each_appearance_has_its_own_progress_shape()
    {
        Assert.Equal(5, Appearances.All.Select(a => a.Progress).Distinct().Count());
    }

    [Fact]
    public void The_shipped_palettes_contain_no_dark_surfaces_even_in_the_card_tints()
    {
        foreach (var a in Appearances.All)
        {
            var p = a.Palette;
            foreach (var surface in new[] { p.Bg, p.WarnBg, p.BadBg, p.Track, p.Hair, p.Line })
                Assert.True(Luminance(surface) > 0.6, $"{a.Id}: {surface} is too dark for a light-only product");
        }
    }
}
