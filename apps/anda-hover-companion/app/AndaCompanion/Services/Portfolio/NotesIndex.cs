using System.IO.Compression;
using System.Text;
using System.Xml;
using AndaCompanion.Data;

namespace AndaCompanion.Services.Portfolio;

public sealed record NoteSnippet(string File, DateOnly Date, string Text);

/// <summary>
/// Finds a person's own notes about an ANDA in a folder they point to: .txt, .md and .docx. Read-only, local, limited in size
/// and number so a large folder cannot hang the app. Only lines that contain the ANDA number are used, trimmed to a short
/// snippet. Nothing here is sent anywhere.
/// </summary>
public static class NotesIndex
{
    public const int MaxFiles = 300, MaxFileBytes = 2 * 1024 * 1024, MaxDepth = 2, MaxPerFile = 4, MaxSnippetChars = 200;

    public static Dictionary<string, List<NoteSnippet>> Build(string? folder)
    {
        var index = new Dictionary<string, List<NoteSnippet>>();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return index;
        int files = 0;
        foreach (var path in Enumerate(folder, 0).OrderByDescending(p => SafeWrite(p)))
        {
            if (++files > MaxFiles) break;
            try
            {
                var info = new FileInfo(path);
                if (info.Length > MaxFileBytes) continue;
                var when = DateOnly.FromDateTime(info.LastWriteTime);
                var perFile = new Dictionary<string, int>();
                foreach (var line in Lines(path))
                {
                    foreach (var n in EntityKinds.Anda.Compiled.Matches(line).Select(m => m.Groups[1].Value).Distinct())
                    {
                        if (perFile.GetValueOrDefault(n) >= MaxPerFile) continue;
                        perFile[n] = perFile.GetValueOrDefault(n) + 1;
                        string text = line.Trim();
                        if (text.Length > MaxSnippetChars) text = text[..(MaxSnippetChars - 1)] + "…";
                        if (!index.TryGetValue(n, out var list)) index[n] = list = new();
                        list.Add(new NoteSnippet(Path.GetFileName(path), when, text));
                    }
                }
            }
            catch { /* a file that cannot be read is skipped, never fatal */ }
        }
        return index;
    }

    private static DateTime SafeWrite(string p) { try { return File.GetLastWriteTime(p); } catch { return DateTime.MinValue; } }

    private static IEnumerable<string> Enumerate(string dir, int depth)
    {
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(dir); } catch { yield break; }
        foreach (var f in files)
        {
            string ext = Path.GetExtension(f).ToLowerInvariant();
            if (ext is ".txt" or ".md" or ".docx") yield return f;
        }
        if (depth >= MaxDepth) yield break;
        IEnumerable<string> subs;
        try { subs = Directory.EnumerateDirectories(dir); } catch { yield break; }
        foreach (var d in subs)
            foreach (var f in Enumerate(d, depth + 1)) yield return f;
    }

    private static IEnumerable<string> Lines(string path)
    {
        if (path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)) return DocxLines(path);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var r = new StreamReader(fs, Encoding.UTF8, true);
        return r.ReadToEnd().Split('\n');
    }

    private static IEnumerable<string> DocxLines(string path)
    {
        var lines = new List<string>();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/document.xml");
        if (entry is null || entry.Length > 20 * 1024 * 1024) return lines;
        using var r = XmlReader.Create(entry.Open(), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var sb = new StringBuilder();
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "t") sb.Append(r.ReadElementContentAsString());
            else if (r.NodeType == XmlNodeType.Element && r.LocalName is "tab") sb.Append(' ');
            else if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "p") { lines.Add(sb.ToString()); sb.Clear(); }
        }
        if (sb.Length > 0) lines.Add(sb.ToString());
        return lines;
    }
}
