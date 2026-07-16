using Avalonia.Media;

namespace AndaCompanion.Theming;

public enum AppearanceId { A, B, C, D, E }

public enum ThemeMode { System, Light, Dark }

public enum ProgressVariant { Bar, Ticks, Ring, Hatch, Capsule }

/// <summary>One resolved palette (a single theme of one appearance).</summary>
public sealed record Palette(
    string Bg, string Ink, string Ink2, string Ink3, string Hair, string Line,
    string Accent, string Accent2, string Good, string GoodBg, string Warn, string WarnBg,
    string Bad, string BadBg, string Track, string Fill, string Mark, string? Grid = null);

/// <summary>
/// An appearance = palette(s) + structural signature. Appearance is preference,
/// never information: anatomy, slot order, and status semantics are identical across all five.
/// Accents are anchored to the U.S. Web Design System federal palette.
/// </summary>
public sealed record Appearance(
    AppearanceId Id, string Name, string Tagline,
    Palette Light, Palette? Dark,          // Dark == null → single-theme (C commits to dark)
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
        Light: new("#FFFFFF", "#1A1D21", "#5C6370", "#8B9199", "#ECEAE6", "#E2E0DB",
                   "#3D6FA5", "#2D5A8C", "#177245", "#E7F3EC", "#8A5800", "#FBF2DD",
                   "#A33636", "#F9E9E9", "#E9E7E2", "#5D84AD", "#1A3D6FA5"),
        Dark: new("#212429", "#EAECEF", "#A7ADB5", "#7B828B", "#2E3238", "#383D44",
                  "#8FB5DD", "#A9C7E6", "#63CB90", "#1E3128", "#E2B45E", "#352B18",
                  "#E28F8F", "#392224", "#383D44", "#7FA5CC", "#248FB5DD"),
        Radius: 12, Progress: ProgressVariant.Bar,
        BodyFont: Sans, NumFont: Sans, DrugFont: Sans,
        DrugItalic: false, StampSeal: false, MonoLabels: false);

    public static readonly Appearance B = new(
        AppearanceId.B, "B · Docket Ledger",
        "Typeset federal docket — warm paper, archival green, serif title, stamp seal.",
        Light: new("#FBF8F2", "#211D15", "#6E6552", "#9A9078", "#E9E2D3", "#DDD3BF",
                   "#23573F", "#1C4834", "#23573F", "#00FFFFFF", "#8A5200", "#F6ECD8",
                   "#93321F", "#F5E5E0", "#E6DDC9", "#23573F", "#1A23573F"),
        Dark: new("#201D16", "#EBE5D6", "#B0A68E", "#847B65", "#332F24", "#403A2C",
                  "#82C2A1", "#9AD0B3", "#82C2A1", "#00FFFFFF", "#E0B463", "#372D18",
                  "#DD9280", "#3A2620", "#3B3527", "#82C2A1", "#2482C2A1"),
        Radius: 8, Progress: ProgressVariant.Ticks,
        BodyFont: Sans, NumFont: Mono, DrugFont: Serif,
        DrugItalic: true, StampSeal: true, MonoLabels: false);

    public static readonly Appearance C = new(
        AppearanceId.C, "C · Signal",
        "Mission-control dark — deep navy, steel-blue accent, mono numerals, progress ring.",
        Light: new("#151E2B", "#E6EDF5", "#94A4B8", "#66788E", "#247EA8D6", "#387EA8D6",
                   "#7CB0E3", "#A4CBF0", "#5CC28E", "#1F5CC28E", "#E8C46A", "#1AE8C46A",
                   "#E89090", "#1AE89090", "#2E7EA8D6", "#7CB0E3", "#247CB0E3"),
        Dark: null,                              // C is deliberately a single dark world
        Radius: 14, Progress: ProgressVariant.Ring,
        BodyFont: Sans, NumFont: Mono, DrugFont: Sans,
        DrugItalic: false, StampSeal: false, MonoLabels: false);

    public static readonly Appearance D = new(
        AppearanceId.D, "D · Draft",
        "Engineer's drawing — federal navy ink (USWDS) on white, graph grid, drafting mono. Dark is a true blueprint.",
        Light: new("#FFFFFF", "#14181D", "#4E5560", "#848B96", "#E3E7EC", "#C9CFD8",
                   "#1A4480", "#162E51", "#106B3C", "#E5F2EA", "#8A5800", "#FAF1DC",
                   "#A03030", "#F8E9E9", "#E6E9EE", "#1A4480", "#1A1A4480", Grid: "#0D1A4480"),
        Dark: new("#162E51", "#DCE4F5", "#A3B4D4", "#7A8CB0", "#24DCE4F5", "#42DCE4F5",
                  "#9DB9E8", "#BCCFF0", "#7FD3A6", "#247FD3A6", "#ECC36E", "#21ECC36E",
                  "#EB9C9C", "#21EB9C9C", "#2EDCE4F5", "#9DB9E8", "#299DB9E8", Grid: "#12DCE4F5"),
        Radius: 4, Progress: ProgressVariant.Hatch,
        BodyFont: Sans, NumFont: Mono, DrugFont: Sans,
        DrugItalic: false, StampSeal: true, MonoLabels: true);

    public static readonly Appearance E = new(
        AppearanceId.E, "E · Instrument",
        "Medical-device porcelain — slate steel on clinical white, capsule geometry, soft large radii.",
        Light: new("#FCFDFE", "#1F2C33", "#5A6C76", "#8B9BA4", "#E7EDF0", "#D6DFE4",
                   "#3E5D6D", "#2F4855", "#1C7A4D", "#E5F2EB", "#92610A", "#FCF1D9",
                   "#AD3D3D", "#F9E8E8", "#E3EAED", "#567383", "#1A3E5D6D"),
        Dark: new("#182226", "#E3EBEE", "#A5B7BF", "#74868F", "#263238", "#32444C",
                  "#9FB9C6", "#BDD2DC", "#63C495", "#1C332A", "#E4BB64", "#352C17",
                  "#E39292", "#3A2324", "#2A3A41", "#7F9DAB", "#249FB9C6"),
        Radius: 20, Progress: ProgressVariant.Capsule,
        BodyFont: Sans, NumFont: Sans, DrugFont: Sans,
        DrugItalic: false, StampSeal: false, MonoLabels: false);

    public static readonly IReadOnlyList<Appearance> All = new[] { A, B, C, D, E };

    public static Appearance Get(AppearanceId id) => All.First(a => a.Id == id);
}
