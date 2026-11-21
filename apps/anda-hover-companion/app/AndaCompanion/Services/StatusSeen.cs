using System.Text.Json;
using System.Text.Json.Serialization;

namespace AndaCompanion.Services;

/// <summary>
/// Round 42: when each ANDA's status was first seen as it is now, for the check "status unchanged". Kept on this PC in status-seen.json:
/// for each ANDA its number, an eight-digit fingerprint of its status (never the status's words) and that day, with which data it was (the sheet's fingerprint or the demo); up to five sources (round 42, hardened). Never sent, not in a backup;
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

    /// <summary>Round 42, hardened: the days kept for each data source (up to five sheets or the demo), each with the day it was last used.</summary>
    private sealed record Kept([property: JsonPropertyName("sources")] Dictionary<string, Slot>? Sources);
    private sealed record Slot([property: JsonPropertyName("used")] string? Used, [property: JsonPropertyName("items")] Dictionary<string, Seen>? Items);
    /// <summary>The file of round 42, one source only: still read.</summary>
    private sealed record KeptOne([property: JsonPropertyName("source")] string? Source, [property: JsonPropertyName("items")] Dictionary<string, Seen>? Items);
    public const int MaxSources = 5;

    /// <summary>
    /// What was kept for this data (the source is the sheet's fingerprint, or "demo", as for the last look). Switching to another sheet and
    /// back keeps each sheet's days (round 42, hardened). CanWrite is false when the file is there but cannot be read just now (locked), so that it is
    /// not written over.
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
        var all = AllFrom(text, path);
        return (all.TryGetValue(source, out var slot) ? Clean(slot.Items) : null, true);
    }

    private static Dictionary<string, Seen>? Clean(Dictionary<string, Seen>? d) =>
        d?.Where(kv => kv.Value is { Fp: not null, Since: not null }).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

    /// <summary>Every source kept; a file that cannot be understood is set aside and nothing is kept.</summary>
    private static Dictionary<string, Slot> AllFrom(string text, string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("sources", out _))
                return (JsonSerializer.Deserialize<Kept>(text)?.Sources ?? new()).Where(kv => kv.Value is not null)       // a null entry is dropped, never read (review)
                    .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            if (JsonSerializer.Deserialize<KeptOne>(text) is { Source: { } src, Items: { } items }) return new(StringComparer.Ordinal) { [src] = new Slot(null, items) };
        }
        catch (JsonException) { }
        SafeFile.SetAside(path);
        return new(StringComparer.Ordinal);
    }

    /// <summary>Keeps this source's days with the others; the five used most lately are kept, the rest let go.</summary>
    public static bool Save(string path, IReadOnlyDictionary<string, Seen> seen, string source, string? today = null)
    {
        var all = new Dictionary<string, Slot>(StringComparer.Ordinal);
        if (File.Exists(path))
        {
            var (text, _) = SafeFile.Read(path);
            if (text is null) return false;                         // locked just now: not written over
            all = AllFrom(text, path);
        }
        all[source] = new Slot(today ?? DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), new(seen, StringComparer.Ordinal));
        var keep = all.OrderByDescending(kv => kv.Key == source).ThenByDescending(kv => kv.Value.Used ?? "", StringComparer.Ordinal).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(MaxSources).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        return SafeFile.Write(path, JsonSerializer.Serialize(new Kept(keep), Options));
    }
}
