using System.Text.Json;
using System.Text.Json.Serialization;

namespace AndaCompanion.Services;

/// <summary>
/// Round 42: when each ANDA's status was first seen as it is now, for the check "status unchanged". Kept on this PC in status-seen.json:
/// for each ANDA its number, an eight-digit fingerprint of its status (never the status's words) and that day. Never sent, not in a backup;
/// an ANDA that leaves the sheet is forgotten. A file that cannot be read is set aside, and the days start again from today.
/// </summary>
public static class StatusSeen
{
    public sealed record Seen([property: JsonPropertyName("fp")] string Fp, [property: JsonPropertyName("since")] string Since);
    public const int MaxEntries = 20_000;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>The status of each ANDA now, against what was kept: a changed or new status starts today; the rest keep their day.</summary>
    public static Dictionary<string, Seen> Update(IReadOnlyDictionary<string, Seen>? kept, IEnumerable<(string Number, string? Status)> now, string today)
    {
        var next = new Dictionary<string, Seen>(StringComparer.Ordinal);
        foreach (var (number, status) in now)
        {
            if (!Timeline.IsNumber(number) || next.ContainsKey(number) || next.Count >= MaxEntries) continue;
            string fp = Timeline.Hash(status ?? "");
            next[number] = kept is not null && kept.TryGetValue(number, out var s) && s.Fp == fp && Timeline.Iso(s.Since) is { } d && d <= Timeline.Iso(today)
                ? s : new Seen(fp, today);
        }
        return next;
    }

    public static IReadOnlyDictionary<string, string> Since(IReadOnlyDictionary<string, Seen> seen) => seen.ToDictionary(kv => kv.Key, kv => kv.Value.Since, StringComparer.Ordinal);

    public static Dictionary<string, Seen>? Load(string path)
    {
        if (!File.Exists(path)) return null;
        var (text, _) = SafeFile.Read(path);
        if (text is null) return null;
        try { if (JsonSerializer.Deserialize<Dictionary<string, Seen>>(text) is { } d) return d.Where(kv => kv.Value is { Fp: not null, Since: not null }).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal); }
        catch (JsonException) { }
        SafeFile.SetAside(path);
        return null;
    }

    public static bool Save(string path, IReadOnlyDictionary<string, Seen> seen) => SafeFile.Write(path, JsonSerializer.Serialize(seen, Options));
}
