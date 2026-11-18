using System.IO.Compression;
using System.Text;
using System.Xml;

namespace AndaCompanion.Services;

/// <summary>A document read as plain text. <see cref="Says"/> holds what the file states about itself (who made it, with what); it proves nothing.</summary>
public sealed record ReadDocument(string Name, string Text, IReadOnlyList<string> Says);

/// <summary>
/// Reads a Word document (.docx), a text file or a Markdown file as plain text, for "Compare two files". Read-only and local: the file is opened
/// with sharing allowed and is never written. No Office and no third-party library: a .docx is a zip of XML, read with size limits, with
/// document type definitions and outside entities refused. Only the body text is read: not headers, footnotes, comments or text boxes, and of a
/// tracked change only the inserted text. Nothing here follows a link or runs anything in the file.
/// </summary>
public static class DocumentReader
{
    public const long MaxFileBytes = 25L * 1024 * 1024, MaxXmlBytes = 60L * 1024 * 1024, MaxTextBytes = 5L * 1024 * 1024;
    private const string Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static XmlReaderSettings Safe => new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = false };

    public static bool IsDocument(string path) => Path.GetExtension(path).ToLowerInvariant() is ".docx" or ".txt" or ".md";

    /// <summary>The document, or what went wrong in plain words.</summary>
    public static (ReadDocument? Document, string? Problem) Read(string path)
    {
        string name = Path.GetFileName(path); if (name.Length > 80) name = name[..80];
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length > MaxFileBytes) return (null, $"{name} is larger than 25 MB, which is more than this tool will read.");
            bool docx = path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);
            if (!docx)
            {
                if (fs.Length > MaxTextBytes) return (null, $"{name} is larger than 5 MB, which is more than this tool reads as plain text.");
                using var r = new StreamReader(fs, Encoding.UTF8, true);
                string text = r.ReadToEnd();
                if (text.Contains('\0')) return (null, $"{name} does not look like plain text, so it was not read.");
                return text.Trim().Length == 0 ? (null, $"{name} has no text that could be read.") : (new ReadDocument(name, text, Array.Empty<string>()), null);
            }
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            if (zip.Entries.Sum(e => e.Length) > 150L * 1024 * 1024) return (null, $"{name} expands to an unreasonable size, so it was not opened.");
            var body = zip.GetEntry("word/document.xml");
            if (body is null || body.Length > MaxXmlBytes) return (null, $"{name} does not look like a Word document this tool can read.");
            var (paragraphs, tracked) = Body(body);
            if (paragraphs.Count == 0) return (null, $"{name} has no text that could be read.");
            var says = Properties(zip);
            if (tracked) says.Add("The document has tracked changes. Of each, the inserted text was read and the deleted text was not.");
            return (new ReadDocument(name, string.Join("\n\n", paragraphs), says), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or XmlException)
        { return (null, $"{name} could not be read as a document."); }
    }

    private static (List<string> Paragraphs, bool Tracked) Body(ZipArchiveEntry entry)
    {
        var paragraphs = new List<string>(); var sb = new StringBuilder(); bool tracked = false;
        using var stream = entry.Open(); using var r = XmlReader.Create(stream, Safe);
        r.Read();
        while (!r.EOF)
        {
            if (r.NodeType == XmlNodeType.Element && r.NamespaceURI == Word)
            {
                if (r.LocalName == "t") { sb.Append(r.ReadElementContentAsString()); continue; }      // the reader has already moved on
                if (r.LocalName == "tab") sb.Append(' ');
                else if (r.LocalName is "br" or "cr") sb.Append(' ');
                else if (r.LocalName is "ins" or "del") tracked = true;
            }
            else if (r.NodeType == XmlNodeType.EndElement && r.NamespaceURI == Word && r.LocalName == "p")
            {
                string p = sb.ToString().Trim(); sb.Clear();
                if (p.Length > 0) paragraphs.Add(p);
            }
            r.Read();
        }
        if (sb.ToString().Trim() is { Length: > 0 } last) paragraphs.Add(last);
        return (paragraphs, tracked);
    }

    /// <summary>What the file states about itself. Anyone can change these fields, so they are shown as the file's own statement and nothing more.</summary>
    private static List<string> Properties(ZipArchive zip)
    {
        string? creator = null, lastBy = null, modified = null, application = null;
        void Scan(string entryName, Action<string, string> take)
        {
            if (zip.GetEntry(entryName) is not { } e || e.Length > 1024 * 1024) return;
            try
            {
                using var s = e.Open(); using var r = XmlReader.Create(s, Safe);
                r.Read();
                while (!r.EOF)
                {
                    if (r.NodeType == XmlNodeType.Element && !r.IsEmptyElement && r.Depth == 1) { string n = r.LocalName; take(n, r.ReadElementContentAsString()); continue; }
                    r.Read();
                }
            }
            catch (XmlException) { /* a properties part that cannot be read is simply not quoted */ }
            catch (InvalidOperationException) { /* an element with child elements, where text was expected */ }
        }
        Scan("docProps/core.xml", (n, v) => { if (n == "creator") creator = v; else if (n == "lastModifiedBy") lastBy = v; else if (n == "modified") modified = v; });
        Scan("docProps/app.xml", (n, v) => { if (n == "Application") application = v; });
        static string One(string v) { v = string.Join(" ", v.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)); return FileCompare.Show(new string(v.Where(c => !char.IsControl(c)).ToArray()), 80); }
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(creator)) parts.Add("created by " + One(creator));
        if (!string.IsNullOrWhiteSpace(lastBy)) parts.Add("last saved by " + One(lastBy));
        if (!string.IsNullOrWhiteSpace(modified)) parts.Add("last saved on " + One(modified));
        if (!string.IsNullOrWhiteSpace(application)) parts.Add("written with " + One(application));
        return parts.Count == 0 ? new List<string>() : new List<string> { "The file says: " + string.Join("; ", parts) + ". Anyone can change these fields, so they prove nothing." };
    }
}
