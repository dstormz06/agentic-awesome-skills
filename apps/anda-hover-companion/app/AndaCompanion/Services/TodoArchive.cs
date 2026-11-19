using System.Globalization;
using System.Text.Json;

namespace AndaCompanion.Services;

/// <summary>
/// Finished items are never deleted by the app on its own. When a list drops them (after the days the person chose, when the list is full, or when the person
/// clears the finished ones), they are first added to an archive file beside the list: one line of JSON for each item, with the day and the
/// reason. The file is only ever added to. If it cannot be written, the items stay in the list. Only the person deletes: one item at a time
/// in the list, or the archive file itself.
/// </summary>
public static class TodoArchive
{
    public static string FileFor(string listPath) =>
        Path.Combine(Path.GetDirectoryName(listPath) ?? ".", Path.GetFileNameWithoutExtension(listPath) + ".archive.jsonl");

    /// <summary>Adds the items to the archive. False when the file could not be written: the caller then keeps the items where they are.</summary>
    public static bool Append<T>(string listPath, IReadOnlyCollection<T> items, DateOnly today, string why)
    {
        if (items.Count == 0) return true;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(listPath) ?? ".");
            string day = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            File.AppendAllLines(FileFor(listPath), items.Select(i => JsonSerializer.Serialize(new { archivedOn = day, why, item = i })));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { return false; }
    }

    /// <summary>Adds the line that says an archived item was put back on its list. Nothing is ever taken out of the archive.</summary>
    public static bool MarkPutBack(string listPath, string id, DateOnly today)
    {
        try
        {
            File.AppendAllLines(FileFor(listPath), new[] { JsonSerializer.Serialize(new { restoredOn = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), id }) });
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { return false; }
    }

    /// <summary>A line longer than this is not one of the archive's: an item is a few hundred characters.</summary>
    private const int MaxLine = 16 * 1024;

    /// <summary>The lines of the archive, oldest first. A line that is not JSON, or is far too long, is passed over. Read line by line.</summary>
    public static IEnumerable<JsonElement> Lines(string listPath)
    {
        string path = FileFor(listPath);
        if (!File.Exists(path)) yield break;
        StreamReader reader;
        try { reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { yield break; }
        using (reader)
        {
            while (true)
            {
                string? line;
                try { line = reader.ReadLine(); } catch (IOException) { yield break; }
                if (line is null) yield break;
                if (line.Length == 0 || line.Length > MaxLine) continue;
                JsonElement element;
                try { using var doc = JsonDocument.Parse(line); element = doc.RootElement.Clone(); } catch (JsonException) { continue; }
                yield return element;
            }
        }
    }

    private static JsonElement? Property(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in e.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;      // the app writes "Id", the simulation "id"
        return null;
    }
    private static string? Text(JsonElement? e) => e is { ValueKind: JsonValueKind.String } s ? s.GetString() : null;
    private static string Shown(JsonElement? e, int max)
    {
        string raw = e switch { { ValueKind: JsonValueKind.String } s => s.GetString() ?? "", { ValueKind: JsonValueKind.Number } n => n.GetRawText(), { ValueKind: JsonValueKind.True } => "true", { ValueKind: JsonValueKind.False } => "false", _ => "" };
        var sb = new System.Text.StringBuilder();
        foreach (char c in raw) sb.Append(char.IsControl(c) ? ' ' : c);
        string t = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        return t.Length > max ? t[..max] : t;
    }

    /// <summary>One archived item as the window shows it. <see cref="RestoredOn"/> is the day it was put back, or null.</summary>
    public sealed record Row(string Id, string Title, string? Number, string ArchivedOn, string Why, string? RestoredOn);

    /// <summary>
    /// The archive as the window shows it: newest first, with what was put back marked as put back; searched by words in the title or digits
    /// of the number, whatever the case of the letters. The same rule as the simulation's archiveRows (mockup/round30-golden.json).
    /// </summary>
    public static (IReadOnlyList<Row> Rows, int Total, int Found) Rows(IEnumerable<JsonElement> lines, string? query, int max = 200)
    {
        var rows = new List<Row>();
        foreach (var l in lines)
        {
            if (l.ValueKind != JsonValueKind.Object) continue;
            if (Property(l, "item") is { ValueKind: JsonValueKind.Object } item && Text(Property(item, "id")) is { Length: > 0 } id)
            {
                string? number = Text(Property(item, "number")) is { } n && System.Text.RegularExpressions.Regex.IsMatch(n, "^[0-9]{6}$") ? n : null;
                rows.Add(new Row(id, Shown(Property(item, "title"), 200), number, Shown(Property(l, "archivedOn"), 10), Shown(Property(l, "why"), 80), null));
            }
            else if (Text(Property(l, "restoredOn")) is { } restoredOn && Text(Property(l, "id")) is { } back)
            {
                for (int k = rows.Count - 1; k >= 0; k--)
                    if (rows[k].Id == back && rows[k].RestoredOn is null) { rows[k] = rows[k] with { RestoredOn = Shown(JsonSerializer.SerializeToElement(restoredOn), 10) }; break; }
            }
        }
        string q = Shown(JsonSerializer.SerializeToElement(query ?? ""), 100).ToLowerInvariant();
        var hit = rows.Where(r => q.Length == 0 || r.Title.ToLowerInvariant().Contains(q, StringComparison.Ordinal) || (r.Number ?? "").Contains(q, StringComparison.Ordinal)).Reverse().ToList();
        return (hit.Take(max > 0 ? max : 200).ToList(), rows.Count, hit.Count);
    }

    /// <summary>The newest archived item with this id that was not put back, or null. Read from the file each time it is asked.</summary>
    public static T? Find<T>(string listPath, string id) where T : class
    {
        JsonElement? found = null;
        foreach (var l in Lines(listPath))
        {
            if (Property(l, "item") is { ValueKind: JsonValueKind.Object } item && Text(Property(item, "id")) == id) found = item;
            else if (Text(Property(l, "restoredOn")) is not null && Text(Property(l, "id")) == id) found = null;
        }
        if (found is not { } e) return null;
        try { return e.Deserialize<T>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch (Exception ex) when (ex is JsonException or NotSupportedException) { return null; }
    }
}
