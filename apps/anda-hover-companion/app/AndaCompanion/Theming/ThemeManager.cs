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
    public static Appearance Current { get; private set; } = Appearances.Get(Appearances.Default);

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

        // Fluent's own controls (checkbox, radio, slider fill) read SystemAccentColor*; point them at the
        // appearance's accent so nothing in the app shows a stray default Windows blue.
        var accent = Color.Parse(palette.Accent);
        r["SystemAccentColor"] = accent;
        r["SystemAccentColorDark1"] = Mix(accent, Colors.Black, 0.12);
        r["SystemAccentColorDark2"] = Mix(accent, Colors.Black, 0.24);
        r["SystemAccentColorDark3"] = Mix(accent, Colors.Black, 0.36);
        r["SystemAccentColorLight1"] = Mix(accent, Colors.White, 0.18);
        r["SystemAccentColorLight2"] = Mix(accent, Colors.White, 0.36);
        r["SystemAccentColorLight3"] = Mix(accent, Colors.White, 0.54);

        r["C.Radius"] = new CornerRadius(appearance.Radius);
        r["C.Font"] = new FontFamily(appearance.BodyFont);
        r["C.NumFont"] = new FontFamily(appearance.NumFont);
        r["C.DrugFont"] = new FontFamily(appearance.DrugFont);
        r["C.DrugStyle"] = appearance.DrugItalic ? FontStyle.Italic : FontStyle.Normal;
        r["C.LabelFont"] = new FontFamily(appearance.Look.SerifLabels ? appearance.DrugFont
            : appearance.MonoLabels ? appearance.NumFont : appearance.BodyFont);
        r["C.DateSize"] = appearance.Look.DateSize;

        Changed?.Invoke();
    }

    private static Color Mix(Color from, Color to, double amount) => new(
        255,
        (byte)Math.Round(from.R + (to.R - from.R) * amount),
        (byte)Math.Round(from.G + (to.G - from.G) * amount),
        (byte)Math.Round(from.B + (to.B - from.B) * amount));
}
