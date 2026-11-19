using System.Globalization;
using System.Text.Json;

namespace AndaCompanion.Services;

/// <summary>A finished item that is still on its list but no longer shown there, waiting to be archived.</summary>
public sealed record WaitingItem(string Id, string Title, string? Number, DateOnly Finished);

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

    /// <summary>
    /// The lines of the archive, oldest first. A line that is not JSON, or is far too long, is passed over, and a line that is far too long
    /// is never held in memory: the file is read in pieces, so one damaged line cannot take the PC's memory.
    /// </summary>
    public static IEnumerable<JsonElement> Lines(string listPath)
    {
        string path = FileFor(listPath);
        if (!File.Exists(path)) yield break;
        StreamReader reader;
        try { reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { yield break; }
        using (reader)
        {
            var line = new System.Text.StringBuilder(); bool tooLong = false; char[] buffer = new char[8192];
            while (true)
            {
                int read;
                try { read = reader.Read(buffer, 0, buffer.Length); } catch (IOException) { yield break; }
                if (read <= 0) break;
                for (int k = 0; k < read; k++)
                {
                    char c = buffer[k];
                    if (c == '\n')
                    {
                        if (!tooLong && Parse(line) is { } element) yield return element;
                        line.Clear(); tooLong = false;
                    }
                    else if (tooLong) { }
                    else if (line.Length >= MaxLine) { tooLong = true; line.Clear(); }
                    else line.Append(c);
                }
            }
            if (!tooLong && Parse(line) is { } last) yield return last;
        }
    }

    private static JsonElement? Parse(System.Text.StringBuilder line)
    {
        if (line.Length == 0) return null;
        try { using var doc = JsonDocument.Parse(line.ToString()); return doc.RootElement.Clone(); } catch (JsonException) { return null; }
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
        foreach (char c in raw) sb.Append(char.IsControl(c) || c == (char)0xFEFF ? ' ' : c);      // the same characters as the simulation
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
        var (rows, total) = Read(lines);
        var hit = Search(rows, query);
        return (hit.Take(max > 0 ? max : 200).ToList(), total, hit.Count);
    }

    /// <summary>
    /// Every item of the archive, oldest first, read once: the window keeps this and searches it, so that typing in the search box does not
    /// read the file again. An item archived again before it was put back is held once, by its newest line.
    /// </summary>
    public static (IReadOnlyList<Row> Rows, int Total) Read(IEnumerable<JsonElement> lines)
    {
        var rows = new List<Row?>(); var open = new Dictionary<string, int>(StringComparer.Ordinal);      // id -> where its line that was not put back is
        foreach (var l in lines)
        {
            if (l.ValueKind != JsonValueKind.Object) continue;
            if (Property(l, "item") is { ValueKind: JsonValueKind.Object } item && Text(Property(item, "id")) is { Length: > 0 } id)
            {
                string? number = Text(Property(item, "number")) is { } n && System.Text.RegularExpressions.Regex.IsMatch(n, @"^[0-9]{6}\z") ? n : null;
                if (open.TryGetValue(id, out int earlier)) rows[earlier] = null;
                open[id] = rows.Count;
                rows.Add(new Row(id, Shown(Property(item, "title"), 200), number, Shown(Property(l, "archivedOn"), 10), Shown(Property(l, "why"), 80), null));
            }
            else if (Text(Property(l, "restoredOn")) is { } restoredOn && Text(Property(l, "id")) is { } back && open.TryGetValue(back, out int at))
            {
                rows[at] = rows[at]! with { RestoredOn = Shown(JsonSerializer.SerializeToElement(restoredOn), 10) };
                open.Remove(back);
            }
        }
        var kept = rows.Where(r => r is not null).Select(r => r!).ToList();
        return (kept, kept.Count);
    }

    /// <summary>The items that match, newest first: by words in the title or digits of the number, whatever the case of the letters.</summary>
    public static IReadOnlyList<Row> Search(IReadOnlyList<Row> rows, string? query)
    {
        string q = Fold(query);
        var hit = rows.Where(r => q.Length == 0 || r.Title.ToLowerInvariant().Contains(q, StringComparison.Ordinal) || (r.Number ?? "").Contains(q, StringComparison.Ordinal)).ToList();
        hit.Reverse();
        return hit;
    }

    /// <summary>A search text as it is compared: one line, small letters.</summary>
    public static string Fold(string? query) => Shown(JsonSerializer.SerializeToElement(query ?? ""), 100).ToLowerInvariant();

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
