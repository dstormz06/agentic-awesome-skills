namespace AndaCompanion.Services;

/// <summary>
/// Turns "Ctrl+Alt+A" into Windows modifier flags and a virtual-key code, and back. A custom shortcut must have at least two
/// modifiers, one of them Ctrl or Alt, so it cannot hijack everyday keys such as Ctrl+C. Pure, so it is tested everywhere.
/// </summary>
public static class HotkeyParser
{
    public const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModNoRepeat = 0x4000;

    public static bool TryParse(string? combo, out uint modifiers, out uint virtualKey, out string normalized, out string error)
    {
        modifiers = 0; virtualKey = 0; normalized = ""; error = "";
        if (string.IsNullOrWhiteSpace(combo)) { error = "Please press the keys you would like to use."; return false; }

        uint mods = 0; string? key = null;
        foreach (var raw in combo.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= ModControl; break;
                case "alt": mods |= ModAlt; break;
                case "shift": mods |= ModShift; break;
                case "win" or "windows" or "meta": error = "The Windows key is reserved by Windows. Use Ctrl, Alt or Shift."; return false;
                default:
                    if (key is not null) { error = "Please use one key plus modifiers, for example Ctrl+Alt+D."; return false; }
                    key = raw;
                    break;
            }
        }
        if (key is null) { error = "Please add a key after the modifiers, for example Ctrl+Alt+D."; return false; }
        int modCount = ((mods & ModControl) != 0 ? 1 : 0) + ((mods & ModAlt) != 0 ? 1 : 0) + ((mods & ModShift) != 0 ? 1 : 0);
        if ((mods & (ModControl | ModAlt)) == 0 || modCount < 2)
        { error = "Please use at least two modifiers, one of them Ctrl or Alt (for example Ctrl+Alt+D), so everyday shortcuts such as Ctrl+C keep working."; return false; }

        string k = key.ToUpperInvariant(); uint vk;
        if (k.Length == 1 && k[0] is >= 'A' and <= 'Z') vk = k[0];
        else if (k.Length == 1 && k[0] is >= '0' and <= '9') vk = k[0];
        else if (k == "SPACE") vk = 0x20;
        else if (k.Length is 2 or 3 && k[0] == 'F' && int.TryParse(k[1..], out int f) && f is >= 1 and <= 12) vk = (uint)(0x70 + f - 1);
        else { error = "Please use a letter, a digit, a function key (F1 to F12) or Space."; return false; }

        modifiers = mods; virtualKey = vk; normalized = Format(mods, k == "SPACE" ? "Space" : k);
        return true;
    }

    public static string Format(uint mods, string key) =>
        string.Join("+", new[] { (mods & ModControl) != 0 ? "Ctrl" : null, (mods & ModAlt) != 0 ? "Alt" : null, (mods & ModShift) != 0 ? "Shift" : null, key }.Where(x => x is not null));

    /// <summary>Checks all three together: each must be valid and none may be the same as another.</summary>
    public static string? ValidateSet(params (string Name, string Combo)[] items)
    {
        var seen = new Dictionary<string, string>();
        foreach (var (name, combo) in items)
        {
            if (!TryParse(combo, out _, out _, out var norm, out var error)) return $"{name}: {error}";
            if (seen.TryGetValue(norm, out var other)) return $"{name} and {other} use the same shortcut ({norm}).";
            seen[norm] = name;
        }
        return null;
    }
}
