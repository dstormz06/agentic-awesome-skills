using Avalonia.Input;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

/// <summary>Turns a key press in Settings into a shortcut string such as "Ctrl+Alt+Shift+R". Modifier-only presses are ignored.</summary>
public static class HotkeyCapture
{
    public static string? FromKey(Key key, KeyModifiers modifiers)
    {
        string? name = key switch
        {
            >= Key.A and <= Key.Z => key.ToString(),
            >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
            >= Key.F1 and <= Key.F12 => key.ToString(),
            Key.Space => "Space",
            _ => null,
        };
        if (name is null) return null;
        uint mods = (modifiers.HasFlag(KeyModifiers.Control) ? HotkeyParser.ModControl : 0) |
                    (modifiers.HasFlag(KeyModifiers.Alt) ? HotkeyParser.ModAlt : 0) |
                    (modifiers.HasFlag(KeyModifiers.Shift) ? HotkeyParser.ModShift : 0);
        return HotkeyParser.Format(mods, name);
    }
}
