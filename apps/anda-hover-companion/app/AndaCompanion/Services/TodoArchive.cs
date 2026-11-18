using System.Globalization;
using System.Text.Json;

namespace AndaCompanion.Services;

/// <summary>
/// Finished items are never deleted by the app on its own. When a list drops them (after 30 days, when the list is full, or when the person
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
}
