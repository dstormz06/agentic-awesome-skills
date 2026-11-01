using System.Text.Json;
using AndaCompanion.Theming;

namespace AndaCompanion.Services;

public enum ConsentModel
{
    Full,       // hotkey opens the full card (the hotkey itself is the consent) — DESIGN.md model ①
    PeekFirst,  // hotkey opens the compact peek; a second act expands (no AI call until then)
    Whisper,    // hotkey shows a small invite chip; the card opens only if it is clicked
}

public sealed class Settings
{
    public AppearanceId Appearance { get; set; } = AppearanceId.A;   // A is the product default

    // Full = DESIGN.md consent model ① (the deliberate hotkey press IS the second stage).
    public ConsentModel Consent { get; set; } = ConsentModel.Full;

    /// <summary>Card surface transparency, 0.85–1.0. Text always stays opaque.</summary>
    public double CardOpacity { get; set; } = 1.0;

    /// <summary>Name of the active Prompt Library lens (null = no lens).</summary>
    public string? ActiveLens { get; set; }

    /// <summary>
    /// Select &amp; Peek reads the selection by sending Ctrl+C to the app in front.
    /// Off: Ctrl+Alt+A opens the lookup box instead and no keystroke is ever sent.
    /// </summary>
    public bool CaptureViaCopy { get; set; } = true;

    public bool FirstRunDone { get; set; }
}

public static class SettingsService
{
    public static Settings Load(string? path = null)
    {
        path ??= AppPaths.SettingsFile;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings();
        }
        catch { /* corrupted settings → defaults */ }
        return new Settings();
    }

    public static void Save(Settings s, string? path = null)
    {
        path ??= AppPaths.SettingsFile;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best effort */ }
    }
}
