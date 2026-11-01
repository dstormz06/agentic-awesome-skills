using Avalonia.Media;

namespace AndaCompanion.Theming;

public enum AppearanceId { A, B, C, D, E }

public enum ProgressVariant { Bar, Ticks, Ring, Hatch, Capsule }

/// <summary>
/// One palette. The product is light-only by direction of the product owner.
/// Every text ink (Ink, Ink2, Ink3) and every status ink on its tint meets WCAG 2.x AA (4.5:1);
/// the contrast tests in AndaCompanion.Tests enforce this for all five appearances.
/// </summary>
public sealed record Palette(
    string Bg, string Ink, string Ink2, string Ink3, string Hair, string Line,
    string Accent, string Accent2, string Good, string GoodBg, string Warn, string WarnBg,
    string Bad, string BadBg, string Track, string Fill, string Mark, string? Grid = null);

/// <summary>
/// An appearance = palette + structural signature. Appearance is preference,
/// never information: anatomy, slot order, and status semantics are identical across all five.
/// Accents are anchored to the U.S. Web Design System federal palette. All light.
/// </summary>
public sealed record Appearance(
    AppearanceId Id, string Name, string Tagline,
    Palette Palette,
    double Radius, ProgressVariant Progress,
    string BodyFont, string NumFont, string DrugFont,
    bool DrugItalic, bool StampSeal, bool MonoLabels);

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
        "Engineer's drawing — federal navy ink (USWDS) on white, graph grid, drafting mono, hatched progress.",
        new("#FFFFFF", "#14181D", "#4E5560", "#6F757F", "#E3E7EC", "#C9CFD8",
            "#1A4480", "#162E51", "#106B3C", "#E5F2EA", "#8A5800", "#FAF1DC",
            "#A03030", "#F8E9E9", "#E6E9EE", "#1A4480", "#1A1A4480", Grid: "#0D1A4480"),
        Radius: 4, Progress: ProgressVariant.Hatch,
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

    public static readonly IReadOnlyList<Appearance> All = new[] { A, B, C, D, E };

    public static Appearance Get(AppearanceId id) => All.First(a => a.Id == id);
}
