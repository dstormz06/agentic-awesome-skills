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

    public bool FirstRunDone { get; set; }
}

public static class SettingsService
{
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AndaCompanion", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path)) ?? new Settings();
        }
        catch { /* corrupted settings → defaults */ }
        return new Settings();
    }

    public static void Save(Settings s)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best effort */ }
    }
}
