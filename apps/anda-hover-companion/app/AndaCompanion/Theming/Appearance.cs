using Avalonia.Media;

namespace AndaCompanion.Theming;

/// <summary>
/// Nineteen looks. A–K and P–S are full cards (K is the product default); L–O are the compact forms, which
/// borrow a sibling's palette and expand into that sibling's full card. Saved settings store the numeric
/// value, so new members are only ever appended. No look draws a pattern, texture or grid behind its content.
/// </summary>
public enum AppearanceId { A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S }

public enum ProgressVariant { Bar, Ticks, Ring, Solid, Capsule, Line, Dots, Step, Timeline, Gauge }

/// <summary>How the card's blocks are arranged. Standard is the common anatomy; the others rearrange the same
/// blocks (the facts and their order of importance never change).</summary>
public enum CardLayout { Standard, Gauge, Folder, Rail, Tag }

/// <summary>Full = the regular quick-look card. The rest are the compact boxes (see CompactCardWindow).</summary>
public enum CardForm { Full, Strip, Tile, Ticket, Badge }

public enum AttentionShape { Tint, Pill, Outline }

public enum ChipStyle { Outline, Tint, Underline }

public enum SealStyle { Default, Quiet, Tinted }

/// <summary>
/// Structural signature bits that a palette alone cannot express. Everything defaults to the plain card,
/// so a look only states what makes it different. Appearance is preference, never information.
/// </summary>
public sealed record Structure
{
    public string? HeaderTint { get; init; }            // tinted identity band (F, H)
    public bool StatusSpine { get; init; }              // 5 px left edge coloured by status (G)
    public string? TopRule { get; init; }               // coloured rule across the top (I, J)
    public double TopRuleHeight { get; init; }
    public bool TopRuleDouble { get; init; }            // two hairlines instead of one band (I)
    public AttentionShape Attention { get; init; } = AttentionShape.Tint;
    public ChipStyle Chips { get; init; } = ChipStyle.Outline;
    public string? ChipFill { get; init; }              // Tint chips
    public string? ChipLine { get; init; }
    public string? ChipInk { get; init; }
    public string? LinkFill { get; init; }              // the "Open in system of record" chip is filled (F)
    public SealStyle Seal { get; init; } = SealStyle.Default;
    public string? SealFill { get; init; }
    public string? SealInk { get; init; }
    public bool SerifLabels { get; init; }
    public double SlotPadV { get; init; } = 10;
    public double SlotPadH { get; init; } = 16;
    public double DateSize { get; init; } = 13.5;
    public CardLayout Layout { get; init; } = CardLayout.Standard;
    public string? RailWash { get; init; }              // Rail: the tinted column that carries the days
}

/// <summary>
/// One palette. The product is light-only by direction of the product owner.
/// Every text ink (Ink, Ink2, Ink3) and every status ink on its tint meets WCAG 2.x AA (4.5:1);
/// the contrast tests in AndaCompanion.Tests enforce this for every appearance.
/// </summary>
public sealed record Palette(
    string Bg, string Ink, string Ink2, string Ink3, string Hair, string Line,
    string Accent, string Accent2, string Good, string GoodBg, string Warn, string WarnBg,
    string Bad, string BadBg, string Track, string Fill, string Mark);

/// <summary>
/// An appearance = palette + structural signature. Appearance is preference,
/// never information: anatomy, slot order, and status semantics are identical across every look.
/// Accents are anchored to the U.S. Web Design System federal palette. All light.
/// </summary>
public sealed record Appearance(
    AppearanceId Id, string Name, string Tagline,
    Palette Palette,
    double Radius, ProgressVariant Progress,
    string BodyFont, string NumFont, string DrugFont,
    bool DrugItalic, bool StampSeal, bool MonoLabels)
{
    public Structure Look { get; init; } = new();
    public CardForm Form { get; init; } = CardForm.Full;

    /// <summary>For a compact look: the full look it expands into. Null for full looks.</summary>
    public AppearanceId? ExpandsAs { get; init; }

    /// <summary>The full look whose structure and palette this appearance uses (itself, if it is full).</summary>
    public AppearanceId Basis => ExpandsAs ?? Id;
    public bool IsCompact => Form != CardForm.Full;
}

public static class Appearances
{
    private const string Sans = "Segoe UI Variable Text, Segoe UI, Inter, sans-serif";
    private const string Mono = "Cascadia Mono, Consolas, DejaVu Sans Mono, monospace";
    private const string Serif = "Iowan Old Style, Palatino Linotype, Georgia, serif";

    public static readonly Appearance A = new(
        AppearanceId.A, "A · Quiet Fluent",
        "Native Windows calm — white surface, steel-blue accent, system type.",
        new("#FFFFFF", "#1A1D21", "#5C6370", "#70757C", "#ECEAE6", "#E2E0DB",
            "#3D6FA5", "#2D5A8C", "#177245", "#E7F3EC", "#8A5800", "#FBF2DD",
            "#A33636", "#F9E9E9", "#E9E7E2", "#5D84AD", "#1A3D6FA5"),
        Radius: 12, Progress: ProgressVariant.Bar,
        BodyFont: Sans, NumFont: Sans, DrugFont: Sans,
        DrugItalic: false, StampSeal: false, MonoLabels: false);

    public static readonly Appearance B = new(
        AppearanceId.B, "B · Docket Ledger",
        "Typeset federal docket — warm paper, archival green, serif title, stamp seal.",
        new("#FBF8F2", "#211D15", "#6E6552", "#78705C", "#E9E2D3", "#DDD3BF",
            "#23573F", "#1C4834", "#23573F", "#00FFFFFF", "#8A5200", "#F6ECD8",
            "#93321F", "#F5E5E0", "#E6DDC9", "#23573F", "#1A23573F"),
        Radius: 8, Progress: ProgressVariant.Ticks,
        BodyFont: Sans, NumFont: Mono, DrugFont: Serif,
        DrugItalic: true, StampSeal: true, MonoLabels: false);

    public static readonly Appearance C = new(
        AppearanceId.C, "C · Signal",
        "Light steel command panel — cool sky surface, federal blue, mono numerals, progress ring.",
        new("#F7FAFD", "#16232E", "#51667A", "#617484", "#E2EAF2", "#C9D8E5",
            "#005EA2", "#0B4778", "#177245", "#E3F1EA", "#8A5800", "#FBF2DD",
            "#A33636", "#F9E9E9", "#DDE7F1", "#2E7BBF", "#1A005EA2"),
        Radius: 14, Progress: ProgressVariant.Ring,
        BodyFont: Sans, NumFont: Mono, DrugFont: Sans,
        DrugItalic: false, StampSeal: false, MonoLabels: false);

    public static readonly Appearance D = new(
        AppearanceId.D, "D · Draft",
        "Engineer's drawing — federal navy ink (USWDS) on plain white paper, drafting mono, square corners, a solid progress bar.",
        new("#FFFFFF", "#14181D", "#4E5560", "#6F757F", "#E3E7EC", "#C9CFD8",
            "#1A4480", "#162E51", "#106B3C", "#E5F2EA", "#8A5800", "#FAF1DC",
            "#A03030", "#F8E9E9", "#E6E9EE", "#1A4480", "#1A1A4480"),
        Radius: 4, Progress: ProgressVariant.Solid,
        BodyFont: Sans, NumFont: Mono, DrugFont: Sans,
        DrugItalic: false, StampSeal: true, MonoLabels: true);

    public static readonly Appearance E = new(
        AppearanceId.E, "E · Instrument",
        "Medical-device porcelain — slate steel on clinical white, capsule geometry, soft large radii.",
        new("#FCFDFE", "#1F2C33", "#5A6C76", "#67767F", "#E7EDF0", "#D6DFE4",
            "#3E5D6D", "#2F4855", "#1C7A4D", "#E5F2EB", "#92610A", "#FCF1D9",
            "#AD3D3D", "#F9E8E8", "#E3EAED", "#567383", "#1A3E5D6D"),
        Radius: 20, Progress: ProgressVariant.Capsule,
        BodyFont: Sans, NumFont: Sans, DrugFont: Sans,
        DrugItalic: false, StampSeal: false, MonoLabels: false);

    public static readonly Appearance F = new(
        AppearanceId.F, "F · Agency Bronze",
        "Warm greige and bronze, a solid bronze band across the top and filled tag chips — the familiar agency-tool feel.",
        new("#FFFFFF", "#25201A", "#584F43", "#6B6153", "#EEE9DF", "#E0D8C9",
            "#7A5A2E", "#5D4620", "#1F6B43", "#E6F2EA", "#8F4A00", "#FDEEDB",
            "#A33636", "#F9E9E9", "#E9E2D3", "#9C7539", "#1A7A5A2E"),
        Radius: 12, Progress: ProgressVariant.Bar,
        BodyFont: Sans, NumFont: Sans, DrugFont: Sans,
        DrugItalic: false, StampSeal: false, MonoLabels: false)
    {
        Look = new()
        {
            TopRule = "#7A5A2E", TopRuleHeight = 5,
            Seal = SealStyle.Tinted, SealFill = "#F1E7D6", SealInk = "#5D4620",
            Attention = AttentionShape.Pill,
            Chips = ChipStyle.Tint, ChipFill = "#F6F0E4", ChipLine = "#E6DCC7", ChipInk = "#5D4620",
            LinkFill = "#7A5A2E",
        },
    };

    public static readonly Appearance G = new(
        AppearanceId.G, "G · Harbor Teal",
        "Cool teal with a status spine down the left edge — late or on track at a glance. Compact spacing.",
        new("#FCFEFE", "#12262B", "#42606A", "#557078", "#E3EEEE", "#CFE0E1",
            "#0B6E73", "#08565A", "#1B7A4B", "#E2F2EA", "#8A5800", "#FBF1DB",
            "#A33636", "#F9E9E9", "#DCEAEB", "#288A8F", "#1A0B6E73"),
        Radius: 10, Progress: ProgressVariant.Bar,
        BodyFont: Sans, NumFont: Mono, DrugFont: Sans,
        DrugItalic: false, StampSeal: true, MonoLabels: false)
    {
        Look = new() { StatusSpine = true, SlotPadV = 8, SlotPadH = 14 },
    };

    public static readonly Appearance H = new(
        AppearanceId.H, "H · Sage Clinical",
        "Soft sage band, generous radii and spacing, ten-dot progress. The calmest of the set.",
        new("#FBFCFA", "#1F2A24", "#4A5A50", "#5B6B61", "#E6EDE7", "#D3DED6",
            "#3A6653", "#2D5142", "#246A45", "#E1F0E6", "#8A5800", "#FAF0D9",
            "#A33636", "#F9E9E9", "#E1E9E3", "#578A71", "#1A3A6653"),
        Radius: 20, Progress: ProgressVariant.Dots,
        BodyFont: Sans, NumFont: Sans, DrugFont: Sans,
        DrugItalic: false, StampSeal: false, MonoLabels: false)
    {
        Look = new() { HeaderTint = "#EEF4F0", Attention = AttentionShape.Pill, SlotPadV = 12, SlotPadH = 18 },
    };

    public static readonly Appearance I = new(
        AppearanceId.I, "I · Indigo Dossier",
        "A formal case file — double rule, serif name and labels, a five-step stepper.",
        new("#FDFCFB", "#1D1C26", "#4D4B5E", "#5F5D72", "#E9E7EF", "#D8D5E3",
            "#3B3F8C", "#2D3070", "#23694A", "#E4F1EA", "#8A5200", "#F8EED9",
            "#A33636", "#F9E9E9", "#E4E2EE", "#5A5FB0", "#1A3B3F8C"),
        Radius: 3, Progress: ProgressVariant.Step,
        BodyFont: Sans, NumFont: Mono, DrugFont: Serif,
        DrugItalic: false, StampSeal: true, MonoLabels: false)
    {
        Look = new() { TopRule = "#3B3F8C", TopRuleHeight = 5, TopRuleDouble = true, SerifLabels = true },
    };

    public static readonly Appearance J = new(
        AppearanceId.J, "J · Cobalt Timeline",
        "Draws goal against forecast on one timeline, so the gap is the picture. Gold rule on top.",
        new("#FFFFFF", "#18212E", "#46556A", "#5B697D", "#E8EDF3", "#D4DCE7",
            "#1F4FB4", "#173D8C", "#157047", "#E2F1EA", "#8A5800", "#FBF1DB",
            "#A33636", "#F9E9E9", "#E1E8F1", "#587FC5", "#1A1F4FB4"),
        Radius: 6, Progress: ProgressVariant.Timeline,
        BodyFont: Sans, NumFont: Sans, DrugFont: Sans,
        DrugItalic: false, StampSeal: true, MonoLabels: false)
    {
        Look = new() { TopRule = "#C99A1D", TopRuleHeight = 3 },
    };

    public static readonly Appearance K = new(
        AppearanceId.K, "K · Graphite Quiet",
        "Monochrome in soft slate. Colour appears only where status needs it. Quiet in a meeting, sharp on a projector. The default.",
        new("#FDFDFD", "#2A2E33", "#4A4E53", "#5C6167", "#ECECEC", "#DEDEDE",
            "#56626E", "#48535E", "#1A6B43", "#E6F1EA", "#8A5800", "#FAF1DC",
            "#A33636", "#F9E9E9", "#E6E6E6", "#6E7A86", "#1A56626E"),
        Radius: 8, Progress: ProgressVariant.Line,
        BodyFont: Sans, NumFont: Sans, DrugFont: Sans,
        DrugItalic: false, StampSeal: false, MonoLabels: false)
    {
        Look = new()
        {
            Seal = SealStyle.Quiet,
            Attention = AttentionShape.Outline,
            Chips = ChipStyle.Underline,
            DateSize = 15,
        },
    };

    // ---- Four more Bronze looks (P–S): same palette family as F, each with its own shape. ----

    private static readonly Palette Bronze = new("#FFFFFF", "#25201A", "#584F43", "#6B6153", "#EEE9DF", "#E0D8C9",
        "#7A5A2E", "#5D4620", "#1F6B43", "#E6F2EA", "#8F4A00", "#FDEEDB",
        "#A33636", "#F9E9E9", "#E9E2D3", "#9C7539", "#1A7A5A2E");

    public static readonly Appearance P = new(
        AppearanceId.P, "P · Bronze Gauge",
        "A half dial carries the progress. Soft round corners and pill-shaped warnings on the bronze palette.",
        Bronze, Radius: 18, Progress: ProgressVariant.Gauge,
        BodyFont: Sans, NumFont: Sans, DrugFont: Sans,
        DrugItalic: false, StampSeal: false, MonoLabels: false)
    {
        Look = new()
        {
            HeaderTint = "#F8F2E6", Layout = CardLayout.Gauge,
            Seal = SealStyle.Tinted, SealFill = "#F1E7D6", SealInk = "#5D4620",
            Attention = AttentionShape.Pill,
            Chips = ChipStyle.Tint, ChipFill = "#FBF7EE", ChipLine = "#D9CDB4", ChipInk = "#5D4620",
        },
    };

    public static readonly Appearance Q = new(
        AppearanceId.Q, "Q · Bronze Folder",
        "A case-file folder: the number on a bronze tab, a plain edge and a thin line for progress.",
        Bronze with { Bg = "#FBF8F1", Hair = "#ECE5D6" }, Radius: 14, Progress: ProgressVariant.Line,
        BodyFont: Sans, NumFont: Sans, DrugFont: Sans,
        DrugItalic: false, StampSeal: true, MonoLabels: false)
    {
        Look = new() { Layout = CardLayout.Folder, Chips = ChipStyle.Underline },
    };

    public static readonly Appearance R = new(
        AppearanceId.R, "R · Bronze Rail",
        "A tinted column carries the days to goal in large figures; the detail reads down the right. Squarer corners.",
        Bronze, Radius: 6, Progress: ProgressVariant.Solid,
        BodyFont: Sans, NumFont: Sans, DrugFont: Sans,
        DrugItalic: false, StampSeal: false, MonoLabels: false)
    {
        Look = new()
        {
            Layout = CardLayout.Rail, RailWash = "#F6EFE2",
            Seal = SealStyle.Tinted, SealFill = "#F1E7D6", SealInk = "#5D4620",
        },
    };

    public static readonly Appearance S = new(
        AppearanceId.S, "S · Bronze Tag",
        "An ID tag: a centred head, goal and forecast in two columns, and a plain bar for progress.",
        Bronze, Radius: 18, Progress: ProgressVariant.Bar,
        BodyFont: Sans, NumFont: Sans, DrugFont: Sans,
        DrugItalic: false, StampSeal: false, MonoLabels: false)
    {
        Look = new()
        {
            Layout = CardLayout.Tag, DateSize = 15,
            Seal = SealStyle.Tinted, SealFill = "#F1E7D6", SealInk = "#5D4620",
            Chips = ChipStyle.Tint, ChipFill = "#F6EFE2", ChipLine = "#E6DCC7", ChipInk = "#5D4620",
        },
    };

    // ---- Compact forms: small boxes that stay out of the way of an open email. They borrow a sibling's
    // palette, make no AI call, and expand into that sibling's full card on click (or the E key). ----

    public static readonly Appearance L = K with
    {
        Id = AppearanceId.L, Name = "L · Graphite Strip",
        Tagline = "A one-glance strip: number, goal, forecast, status edge. About 340 × 63–81 px. Graphite palette.",
        Form = CardForm.Strip, ExpandsAs = AppearanceId.K,
    };

    public static readonly Appearance M = A with
    {
        Id = AppearanceId.M, Name = "M · Fluent Tile",
        Tagline = "A small tile: the forecast gap in large type, one attention line. About 196 × 123–145 px. Quiet Fluent palette.",
        Form = CardForm.Tile, ExpandsAs = AppearanceId.A,
    };

    public static readonly Appearance N = F with
    {
        Id = AppearanceId.N, Name = "N · Bronze Ticket",
        Tagline = "A slim ticket with a stub: number left, forecast right. About 300 × 64–68 px. Agency Bronze palette.",
        Form = CardForm.Ticket, ExpandsAs = AppearanceId.F,
    };

    public static readonly Appearance O = G with
    {
        Id = AppearanceId.O, Name = "O · Teal Badge",
        Tagline = "The smallest: a status pill with the number and the gap. About 150–185 × 38 px. Harbor Teal palette.",
        Form = CardForm.Badge, ExpandsAs = AppearanceId.G,
    };

    public static readonly IReadOnlyList<Appearance> All = new[] { A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S };

    /// <summary>The product default, by direction of the product owner (replaced A on 2026-11-02).</summary>
    public static readonly AppearanceId Default = AppearanceId.K;

    public static Appearance Get(AppearanceId id) => All.First(a => a.Id == id);
}
