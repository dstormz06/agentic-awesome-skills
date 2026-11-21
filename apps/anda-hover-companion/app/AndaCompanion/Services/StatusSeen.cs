using System.Text.Json;
using System.Text.Json.Serialization;

namespace AndaCompanion.Services;

/// <summary>
/// Round 42: when each ANDA's status was first seen as it is now, for the check "status unchanged". Kept on this PC in status-seen.json:
/// for each ANDA its number, an eight-digit fingerprint of its status (never the status's words) and that day, with which data it was (the sheet's fingerprint or the demo). Never sent, not in a backup;
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

    private sealed record Kept([property: JsonPropertyName("source")] string? Source, [property: JsonPropertyName("items")] Dictionary<string, Seen>? Items);

    /// <summary>
    /// What was kept for this data (round 42 review: the source is the sheet's fingerprint, or "demo", as for the last look; another sheet
    /// starts again). CanWrite is false when the file is there but cannot be read just now (locked), so that it is not written over.
    /// </summary>
    public static (Dictionary<string, Seen>? Items, bool CanWrite) Load(string path, string source)
    {
        if (!File.Exists(path)) return (null, true);
        var (text, _) = SafeFile.Read(path);
        return FromText(text, path, source);
    }

    internal static (Dictionary<string, Seen>? Items, bool CanWrite) FromText(string? text, string path, string source)
    {
        if (text is null) return (null, false);
        try
        {
            if (JsonSerializer.Deserialize<Kept>(text) is { Items: { } d } k)
                return (k.Source == source ? d.Where(kv => kv.Value is { Fp: not null, Since: not null }).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal) : null, true);
        }
        catch (JsonException) { }
        SafeFile.SetAside(path);
        return (null, true);
    }

    public static bool Save(string path, IReadOnlyDictionary<string, Seen> seen, string source) => SafeFile.Write(path, JsonSerializer.Serialize(new Kept(source, new(seen, StringComparer.Ordinal)), Options));
}
