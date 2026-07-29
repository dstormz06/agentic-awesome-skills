using Avalonia;
using Avalonia.Media;

namespace AndaCompanion.Theming;

/// <summary>
/// Publishes the active appearance's tokens into Application.Resources as
/// "C.*" dynamic resources. Light-only by product decision; the card surface
/// honors the user's transparency setting (text and status inks stay opaque).
/// </summary>
public static class ThemeManager
{
    public static Appearance Current { get; private set; } = Appearances.A;

    public static event Action? Changed;

    public static void Apply(AppearanceId id, double cardOpacity = 1.0)
    {
        var appearance = Appearances.Get(id);
        var palette = appearance.Palette;
        Current = appearance;

        var r = Application.Current!.Resources;
        void Brush(string key, string hex) => r[key] = new SolidColorBrush(Color.Parse(hex));

        // Card surface carries the transparency preference (clamped to stay readable).
        var bg = Color.Parse(palette.Bg);
        double opacity = Math.Clamp(cardOpacity, 0.85, 1.0);
        r["C.Bg"] = new SolidColorBrush(new Color((byte)(bg.A * opacity), bg.R, bg.G, bg.B));

        Brush("C.Ink", palette.Ink);
        Brush("C.Ink2", palette.Ink2); Brush("C.Ink3", palette.Ink3);
        Brush("C.Hair", palette.Hair); Brush("C.Line", palette.Line);
        Brush("C.Accent", palette.Accent); Brush("C.Accent2", palette.Accent2);
        Brush("C.Good", palette.Good); Brush("C.GoodBg", palette.GoodBg);
        Brush("C.Warn", palette.Warn); Brush("C.WarnBg", palette.WarnBg);
        Brush("C.Bad", palette.Bad); Brush("C.BadBg", palette.BadBg);
        Brush("C.Track", palette.Track); Brush("C.Fill", palette.Fill);
        Brush("C.Mark", palette.Mark);
        Brush("C.Grid", palette.Grid ?? "#00FFFFFF");

        r["C.Radius"] = new CornerRadius(appearance.Radius);
        r["C.Font"] = new FontFamily(appearance.BodyFont);
        r["C.NumFont"] = new FontFamily(appearance.NumFont);
        r["C.DrugFont"] = new FontFamily(appearance.DrugFont);
        r["C.DrugStyle"] = appearance.DrugItalic ? FontStyle.Italic : FontStyle.Normal;
        r["C.LabelFont"] = new FontFamily(appearance.MonoLabels ? appearance.NumFont : appearance.BodyFont);

        Changed?.Invoke();
    }
}
