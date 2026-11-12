using AndaCompanion.Theming;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// The product rules about appearance, enforced as numbers: light-only, K is the default,
/// and every text ink meets WCAG 2.x AA (the Section 508 baseline) on every appearance —
/// including on the tinted header bands, seals and chips some looks add.
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
    public void Twenty_three_looks_are_defined_eight_are_offered_and_W_is_the_default()
    {
        Assert.Equal(new[]
            {
                AppearanceId.A, AppearanceId.B, AppearanceId.C, AppearanceId.D, AppearanceId.E, AppearanceId.F, AppearanceId.G,
                AppearanceId.H, AppearanceId.I, AppearanceId.J, AppearanceId.K, AppearanceId.L, AppearanceId.M, AppearanceId.N, AppearanceId.O,
                AppearanceId.P, AppearanceId.Q, AppearanceId.R, AppearanceId.S, AppearanceId.T, AppearanceId.U, AppearanceId.V, AppearanceId.W,
            },
            Appearances.All.Select(a => a.Id));
        Assert.Equal(19, Appearances.All.Count(a => !a.IsCompact));
        Assert.Equal(new[] { AppearanceId.W, AppearanceId.V, AppearanceId.U, AppearanceId.T, AppearanceId.K, AppearanceId.P, AppearanceId.L, AppearanceId.O }, Appearances.Offered.Select(a => a.Id));   // round 23: the eight a person can choose
        Assert.Equal(4, Appearances.All.Count(a => a.IsCompact));
        Assert.Equal(AppearanceId.W, Appearances.Default);      // W · Graphite Gauge replaced T as the default in round 23 (T replaced K in round 19)
        Assert.Equal(AppearanceId.W, new AndaCompanion.Services.Settings().Appearance);
        Assert.Equal(AppearanceId.W, Appearances.Get(Appearances.Default).Id);
        Assert.Equal(22, (int)AppearanceId.W);
        Assert.Equal(19, (int)AppearanceId.T);                  // appended: every number saved in an earlier settings file still means the same look
    }

    [Fact]
    public void Saved_settings_keep_their_numeric_meaning_so_old_choices_still_load()
    {
        // The settings file stores the enum as a number. A–E must keep 0–4 or an existing tester's choice would change.
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, new[] { AppearanceId.A, AppearanceId.B, AppearanceId.C, AppearanceId.D, AppearanceId.E }.Select(i => (int)i));
        Assert.Equal(AppearanceId.K, (AppearanceId)10);
        Assert.Equal(new[] { 15, 16, 17, 18 }, new[] { AppearanceId.P, AppearanceId.Q, AppearanceId.R, AppearanceId.S }.Select(i => (int)i));
    }

    [Fact]
    public void Compact_looks_borrow_a_sibling_and_expand_into_its_full_card()
    {
        var map = Appearances.All.Where(a => a.IsCompact).ToDictionary(a => a.Id, a => a.ExpandsAs);
        Assert.Equal(AppearanceId.K, map[AppearanceId.L]);
        Assert.Equal(AppearanceId.A, map[AppearanceId.M]);
        Assert.Equal(AppearanceId.F, map[AppearanceId.N]);
        Assert.Equal(AppearanceId.G, map[AppearanceId.O]);
        Assert.Equal(new[] { CardForm.Strip, CardForm.Tile, CardForm.Ticket, CardForm.Badge },
            Appearances.All.Where(a => a.IsCompact).Select(a => a.Form));
        foreach (var c in Appearances.All.Where(a => a.IsCompact))
        {
            var sibling = Appearances.Get(c.ExpandsAs!.Value);
            Assert.False(sibling.IsCompact, $"{c.Id} must expand into a FULL look");
            Assert.Equal(sibling.Palette, c.Palette);
            Assert.Equal(sibling.Id, c.Basis);
            Assert.Equal(sibling.Look, c.Look);
        }
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
    public void Each_full_look_family_has_its_own_progress_shape()
    {
        // Ten distinct forms across the fifteen full looks (A, F, G and S share the thin bar; D and R share the solid bar; K and Q the line).
        Assert.Equal(10, Appearances.All.Where(a => !a.IsCompact).Select(a => a.Progress).Distinct().Count());
        Assert.Equal(ProgressVariant.Line, Appearances.K.Progress);
        Assert.Equal(ProgressVariant.Timeline, Appearances.J.Progress);
    }

    [Fact]
    public void Added_surfaces_keep_their_text_readable()
    {
        foreach (var a in Appearances.All.Where(x => !x.IsCompact))
        {
            var p = a.Palette;
            var look = a.Look;
            if (look.HeaderTint is { } tint)
            {
                Assert.True(Contrast(p.Ink, tint) >= 7, $"{a.Id} Ink on header band");
                Assert.True(Contrast(p.Ink2, tint) >= 4.5, $"{a.Id} Ink2 on header band");
                Assert.True(Contrast(p.Ink3, tint) >= 4.5, $"{a.Id} Ink3 on header band = {Contrast(p.Ink3, tint):0.00}");
            }
            if (look.Seal == SealStyle.Tinted)
                Assert.True(Contrast(look.SealInk!, look.SealFill!) >= 4.5, $"{a.Id} seal text");
            if (look.Seal == SealStyle.Quiet)
                Assert.True(Contrast(p.Ink2, p.Bg) >= 4.5, $"{a.Id} quiet seal text");
            if (look.Chips == ChipStyle.Tint)
                Assert.True(Contrast(look.ChipInk!, look.ChipFill!) >= 4.5, $"{a.Id} chip text");
            if (look.LinkFill is { } fill)
            {
                Assert.True(Contrast("#FFFFFF", fill) >= 4.5, $"{a.Id} white text on the filled chip");
                Assert.True(Contrast("#FFFFFF", p.Accent2) >= 4.5, $"{a.Id} white text on the chip's hover fill");
            }
            // attention text sits on the tint (or the surface, for the outlined shape)
            Assert.True(Contrast(p.Warn, look.Attention == AttentionShape.Outline ? p.Bg : p.WarnBg) >= 4.5, $"{a.Id} attention text");
        }
    }

    [Fact]
    public void Structure_values_are_usable()
    {
        foreach (var a in Appearances.All)
        {
            Assert.InRange(a.Look.SlotPadV, 6, 16);
            Assert.InRange(a.Look.SlotPadH, 12, 22);
            Assert.InRange(a.Look.DateSize, 12, 18);
            if (a.Look.TopRule is not null) Assert.True(a.Look.TopRuleHeight > 0, $"{a.Id} has a top rule with no height");
        }
    }

    [Fact]
    public void Accents_and_fills_are_never_near_black()
    {
        // "Don't make anything too dark" (product owner, 2026-11-02). Text must stay dark enough to read, so this
        // applies to the colors used for buttons, checkboxes, progress marks and fills, not to the text inks.
        foreach (var a in Appearances.All)
        {
            Assert.True(Luminance(a.Palette.Accent) >= 0.05, $"{a.Id} accent {a.Palette.Accent} is too dark ({Luminance(a.Palette.Accent):0.000})");
            Assert.True(Luminance(a.Palette.Fill) >= 0.05, $"{a.Id} fill {a.Palette.Fill} is too dark ({Luminance(a.Palette.Fill):0.000})");
        }
        // K is the default: its accent is a soft slate, not graphite
        Assert.True(Luminance(Appearances.K.Palette.Accent) >= 0.10);
        Assert.True(Luminance(Appearances.K.Palette.Ink) >= 0.02, "K text ink is softened from near-black");
    }

    [Fact]
    public void Graphs_stay_visible_against_their_track()
    {
        // non-text contrast: a progress fill must be distinguishable from its track (WCAG 1.4.11, 3:1)
        foreach (var a in Appearances.All)
            Assert.True(Contrast(a.Palette.Fill, a.Palette.Track) >= 3.0, $"{a.Id} fill vs track = {Contrast(a.Palette.Fill, a.Palette.Track):0.00}");
    }

    [Fact]
    public void The_shipped_palettes_contain_no_dark_surfaces_even_in_the_card_tints()
    {
        foreach (var a in Appearances.All)
        {
            var p = a.Palette;
            foreach (var surface in new[] { p.Bg, p.WarnBg, p.BadBg, p.Track, p.Hair, p.Line, a.Look.HeaderTint ?? p.Bg, a.Look.ChipFill ?? p.Bg, a.Look.SealFill ?? p.Bg })
                Assert.True(Luminance(surface) > 0.6, $"{a.Id}: {surface} is too dark for a light-only product");
        }
    }
}
