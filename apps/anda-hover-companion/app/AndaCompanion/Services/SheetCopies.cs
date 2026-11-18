using System.Globalization;
using System.Text.Json;

namespace AndaCompanion.Services;

/// <summary>
/// "My sheet since last week": when the person switches it on, the app keeps one copy of their portfolio sheet each week, in its own folder on
/// this PC, so that Compare can show what changed since the last copy. Off until switched on; switching it off keeps the copies that exist.
/// The copies hold what the sheet holds, so they are as sensitive as the sheet: they stay on this PC, are never sent, and are not part of a
/// backup. <b>The app never deletes a copy by itself</b>, however many there are: they may be records, so only the person deletes them
/// (<see cref="DeleteAll"/>, behind a second press).
/// </summary>
public sealed class SheetCopies
{
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
            var days = Days().ToList();
            // A week counts as kept only when its copy can be read: a damaged copy is set aside, so that the week can be kept again.
            foreach (var d in days.Where(d => WeekStart(d) == WeekStart(today) && d <= today).ToList())
                if (Read(d) is null) { SafeFile.SetAside(FileFor(d)); days.Remove(d); }
            if (days.Any(d => WeekStart(d) == WeekStart(today) && d <= today)) return false;
            try
            {
                Directory.CreateDirectory(_dir);
                string json = JsonSerializer.Serialize(new Stored(today.ToString("yyyy-MM-dd", Inv), table.Headers.ToList(), table.Rows.Select(r => r.ToList()).ToList()));
                if (json.Length > MaxBytes) return false;
                string path = FileFor(today), temp = path + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, path, overwrite: true);
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
                if (Read(day) is { } table) return (table, day);      // a copy that cannot be read is passed over
            return null;
        }
    }

    /// <summary>The copy kept on that day, or null when there is none or it cannot be read.</summary>
    private CompareTable? Read(DateOnly day)
    {
        try
        {
            string path = FileFor(day);
            if (!File.Exists(path) || new FileInfo(path).Length > MaxBytes) return null;
            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(path));
            if (stored?.Headers is null || stored.Rows is null) return null;
            var rows = stored.Rows.Select(r => (IReadOnlyList<string>)(r ?? new List<string>()).Select(c => c ?? "").ToList()).ToList();
            return new CompareTable("My sheet, copy kept " + day.ToString("MMM d", Inv), stored.Headers.Select(h => h ?? "").ToList(), rows);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
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
