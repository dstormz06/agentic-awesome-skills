using System.Text.Json;
using AndaCompanion.Theming;

namespace AndaCompanion.Services;

public enum ConsentModel
{
    Full,       // hotkey opens the full card (the hotkey itself is the consent)
    PeekFirst,  // hotkey opens the compact peek; a second act expands (no AI call until then)
    Whisper,    // hotkey shows a small invite chip; the card opens only if it is clicked
}

public sealed class Settings
{
    public AppearanceId Appearance { get; set; } = AppearanceId.A;   // A is the product default
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public ConsentModel Consent { get; set; } = ConsentModel.PeekFirst;
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
