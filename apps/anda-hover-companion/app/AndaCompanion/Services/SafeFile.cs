using System.Globalization;
using System.Text;

namespace AndaCompanion.Services;

/// <summary>
/// How the app treats the files that hold the person's own things (settings, to-do lists, prompts). Two rules, the same for every file:
/// a file is replaced only by a complete new one (written beside it, then moved over it), and a file that cannot be read is never
/// overwritten: it is set aside under a name that says when, so whatever is still in it can be recovered by hand.
/// </summary>
public static class SafeFile
{
    /// <summary>The largest file of the person's own things that is read. A larger one is set aside, like one that cannot be read.</summary>
    public const long MaxBytes = 4L * 1024 * 1024;

    /// <summary>Writes the whole text beside the file and moves it into place, so a stop half-way can never leave half a file.</summary>
    public static bool Write(string path, string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            string temp = path + ".tmp";
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { return false; }
    }

    /// <summary>
    /// Sets a file aside, or keeps a copy of it, under "name.{why}-yyyyMMdd-HHmmss.ext" (UTC), never over an earlier one.
    /// Returns the new file's name, or null when it could not be done. <paramref name="copy"/> leaves the original where it is.
    /// </summary>
    public static string? SetAside(string path, string why = "unreadable", bool copy = false, DateTime? utcNow = null)
    {
        try
        {
            if (!File.Exists(path)) return null;
            string dir = Path.GetDirectoryName(path) ?? ".", stem = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
            string stamp = (utcNow ?? DateTime.UtcNow).ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            for (int n = 0; n < 100; n++)
            {
                string name = $"{stem}.{why}-{stamp}{(n == 0 ? "" : "-" + n.ToString(CultureInfo.InvariantCulture))}{ext}", target = Path.Combine(dir, name);
                if (File.Exists(target)) continue;
                if (copy) File.Copy(path, target); else File.Move(path, target);
                return name;
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { return null; }
    }

    /// <summary>The text of a file of the person's own things, or null with the reason when it is missing, too large or cannot be read.</summary>
    public static (string? Text, string? Why) Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return (null, null);
            if (new FileInfo(path).Length > MaxBytes) return (null, "it is larger than 4 MB");
            return (File.ReadAllText(path), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { return (null, ex.Message); }
    }

    /// <summary>The sentence shown when a file was set aside. <paramref name="what"/> is "settings file", "ANDA to-do list" and the like.</summary>
    public static string Note(string what, string? aside, string then) =>
        aside is null
            ? $"The {what} could not be read and could not be set aside. {then}"
            : $"The {what} could not be read. It was set aside as {aside}, so nothing in it is lost. {then}";
}
