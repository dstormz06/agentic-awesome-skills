using Avalonia;
using Avalonia.Media;

namespace AndaCompanion.Theming;

/// <summary>
/// Publishes the active appearance's tokens into Application.Resources as
/// "C.*" dynamic resources, mirroring the design studio's CSS custom properties.
/// </summary>
public static class ThemeManager
{
    public static Appearance Current { get; private set; } = Appearances.A;
    public static bool IsDark { get; private set; }

    public static event Action? Changed;

    public static void Apply(AppearanceId id, ThemeMode mode)
    {
        var appearance = Appearances.Get(id);
        bool dark = mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => SystemPrefersDark(),
        };
        var palette = dark ? appearance.Dark ?? appearance.Light : appearance.Light;
        // C carries a single committed palette; treat it as dark for chrome purposes.
        if (appearance.Dark is null) dark = true;

        Current = appearance;
        IsDark = dark;

        var r = Application.Current!.Resources;
        void Brush(string key, string hex) => r[key] = new SolidColorBrush(Color.Parse(hex));

        Brush("C.Bg", palette.Bg); Brush("C.Ink", palette.Ink);
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

    private static bool SystemPrefersDark()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                // HKCU\...\Themes\Personalize\AppsUseLightTheme == 0 → dark
                using var proc = new System.Diagnostics.Process
                {
                    StartInfo = new()
                    {
                        FileName = "reg",
                        Arguments = @"query HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize /v AppsUseLightTheme",
                        RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true,
                    },
                };
                proc.Start();
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(1500);
                return output.Contains("0x0");
            }
            catch { /* fall through to light */ }
        }
        return false;
    }
}
