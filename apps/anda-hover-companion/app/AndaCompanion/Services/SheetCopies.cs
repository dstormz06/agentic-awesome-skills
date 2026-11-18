using System.Globalization;
using System.Text.Json;

namespace AndaCompanion.Services;

/// <summary>
/// "My sheet since last week": when the person switches it on, the app keeps one copy of their portfolio sheet each week, in its own folder on
/// this PC, so that Compare can show what changed since the last copy. Off until switched on; switching it off deletes the copies. The copies
/// hold what the sheet holds, so they are as sensitive as the sheet: they stay on this PC, are never sent, and are not part of a backup.
/// At most <see cref="Keep"/> copies are kept; the oldest goes first.
/// </summary>
public sealed class SheetCopies
{
    public const int Keep = 5;
    private const long MaxBytes = 40L * 1024 * 1024;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly string _dir;
    private readonly object _gate = new();

    public SheetCopies(string? dir = null) => _dir = dir ?? Path.Combine(AppPaths.Root, "sheet-copies");

    private sealed record Stored(string Day, List<string> Headers, List<List<string>> Rows);

    private static DateOnly WeekStart(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));      // Monday
    private string FileFor(DateOnly day) => Path.Combine(_dir, "sheet-" + day.ToString("yyyy-MM-dd", Inv) + ".json");

    /// <summary>The days of the copies that exist, oldest first. A file whose name is not one of ours is ignored.</summary>
    public IReadOnlyList<DateOnly> Days()
    {
        lock (_gate)
        {
            if (!Directory.Exists(_dir)) return Array.Empty<DateOnly>();
            var days = new List<DateOnly>();
            foreach (string f in Directory.EnumerateFiles(_dir, "sheet-*.json"))
                if (DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(f)["sheet-".Length..], "yyyy-MM-dd", Inv, DateTimeStyles.None, out var d)) days.Add(d);
            days.Sort();
            return days;
        }
    }

    /// <summary>Keeps a copy for this week if none exists yet. True when a copy was written.</summary>
    public bool Capture(CompareTable table, DateOnly today)
    {
        lock (_gate)
        {
            var days = Days();
            if (days.Any(d => WeekStart(d) == WeekStart(today))) return false;
            try
            {
                Directory.CreateDirectory(_dir);
                string json = JsonSerializer.Serialize(new Stored(today.ToString("yyyy-MM-dd", Inv), table.Headers.ToList(), table.Rows.Select(r => r.ToList()).ToList()));
                if (json.Length > MaxBytes) return false;
                string path = FileFor(today), temp = path + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, path, overwrite: true);
                foreach (var old in Days().Reverse().Skip(Keep)) File.Delete(FileFor(old));
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        }
    }

    /// <summary>The newest copy made before <paramref name="today"/>, or null.</summary>
    public (CompareTable Table, DateOnly Day)? LatestBefore(DateOnly today)
    {
        lock (_gate)
        {
            foreach (var day in Days().Reverse().Where(d => d < today))
            {
                try
                {
                    string path = FileFor(day);
                    if (new FileInfo(path).Length > MaxBytes) continue;
                    var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(path));
                    if (stored?.Headers is null || stored.Rows is null) continue;
                    var rows = stored.Rows.Select(r => (IReadOnlyList<string>)(r ?? new List<string>()).Select(c => c ?? "").ToList()).ToList();
                    return (new CompareTable("My sheet, copy kept " + day.ToString("MMM d", Inv), stored.Headers.Select(h => h ?? "").ToList(), rows), day);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { /* a copy that cannot be read is passed over */ }
            }
            return null;
        }
    }

    /// <summary>Deletes every copy. Returns how many were deleted.</summary>
    public int DeleteAll()
    {
        lock (_gate)
        {
            int n = 0;
            foreach (var day in Days()) { try { File.Delete(FileFor(day)); n++; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
            return n;
        }
    }
}
