using System.Globalization;
using System.IO.Compression;
using System.Text;
using AndaCompanion.Models;

namespace AndaCompanion.Services;

/// <summary>The two reference lines on a card. Each is either a date with its source, or a plain statement that it is not known.</summary>
public sealed record ReferenceInfo(string? PsgText, string? RldText);

/// <summary>
/// The latest labeling date of a product's reference listed drug (RLD), read from the Drugs@FDA data files the person downloaded
/// themselves (a zip of tab-delimited tables). The app downloads nothing and sends nothing: matching is done locally, so nothing
/// about which ANDAs someone tracks leaves the PC.
///
/// UNVERIFIED: the table and column names below are FDA's as I understand them (Products: ApplNo, Form, Strength, ReferenceDrug,
/// ActiveIngredient; Submissions: ApplNo, SubmissionClassCodeID, SubmissionStatus, SubmissionStatusDate; SubmissionClass_Lookup:
/// SubmissionClassCodeID, SubmissionClassCodeDescription). The FDA site could not be reached from the build environment, so
/// they were not checked against a real download. If a header is missing the result is "not checked", never a guess.
/// </summary>
public sealed class DrugsAtFdaFile
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private sealed record Product(string ApplNo, string Form, string Strength, string Ingredient);

    private readonly List<Product> _rlds = new();
    private readonly Dictionary<string, DateOnly> _latestLabeling = new();

    public DateOnly FileDate { get; }
    public string FileName { get; }
    public int ReferenceProducts => _rlds.Count;

    private DrugsAtFdaFile(DateOnly fileDate, string fileName) { FileDate = fileDate; FileName = fileName; }

    public static DrugsAtFdaFile? TryLoad(string? path, out string message)
    {
        message = "";
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { message = "The Drugs@FDA file was not found. Choose it again in Settings."; return null; }
        try
        {
            var info = new FileInfo(path);
            if (info.Length > 200L * 1024 * 1024) { message = "The Drugs@FDA file is larger than expected, so it was not read."; return null; }
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            var products = Table(zip, "Products.txt");
            var submissions = Table(zip, "Submissions.txt");
            var classes = Table(zip, "SubmissionClass_Lookup.txt");
            if (products is null || submissions is null || classes is null)
            { message = "This is not a Drugs@FDA data file (Products, Submissions and SubmissionClass_Lookup were not all found). Reference dates not checked."; return null; }

            string[] needP = { "ApplNo", "Form", "Strength", "ReferenceDrug", "ActiveIngredient" };
            string[] needS = { "ApplNo", "SubmissionClassCodeID", "SubmissionStatus", "SubmissionStatusDate" };
            string[] needC = { "SubmissionClassCodeID", "SubmissionClassCodeDescription" };
            if (!products.Has(needP) || !submissions.Has(needS) || !classes.Has(needC))
            { message = "The Drugs@FDA file has a layout this version does not recognize, so reference dates were not checked."; return null; }

            var file = new DrugsAtFdaFile(DateOnly.FromDateTime(info.LastWriteTime), info.Name);
            foreach (var r in products.Rows)
                if (r["ReferenceDrug"].Trim() is "1" or "Yes" or "Y")
                    file._rlds.Add(new Product(r["ApplNo"].Trim(), r["Form"].Trim(), r["Strength"].Trim(), r["ActiveIngredient"].Trim()));

            var labelingClasses = classes.Rows
                .Where(r => r["SubmissionClassCodeDescription"].Contains("label", StringComparison.OrdinalIgnoreCase))
                .Select(r => r["SubmissionClassCodeID"].Trim()).ToHashSet();
            foreach (var r in submissions.Rows)
            {
                if (!labelingClasses.Contains(r["SubmissionClassCodeID"].Trim())) continue;
                if (!string.Equals(r["SubmissionStatus"].Trim(), "AP", StringComparison.OrdinalIgnoreCase)) continue;
                if (!DateTime.TryParse(r["SubmissionStatusDate"].Trim(), Inv, DateTimeStyles.None, out var dt)) continue;
                string appl = r["ApplNo"].Trim(); var d = DateOnly.FromDateTime(dt);
                if (!file._latestLabeling.TryGetValue(appl, out var cur) || d > cur) file._latestLabeling[appl] = d;
            }
            message = $"Drugs@FDA file loaded ({file.ReferenceProducts} reference products, file saved {file.FileDate.ToString("MMM d, yyyy", Inv)}).";
            return file;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            message = "The Drugs@FDA file could not be read. Reference dates not checked.";
            return null;
        }
    }

    /// <summary>The RLD's most recent approved labeling submission date, or a plain reason there is none to show.</summary>
    public (DateOnly? Date, string Message) RldLabeling(AndaRecord r)
    {
        if (string.IsNullOrWhiteSpace(r.ActiveIngredient) || string.IsNullOrWhiteSpace(r.DosageForm))
            return (null, "needs the active ingredient and dosage form in your sheet");
        string ing = Norm(r.ActiveIngredient), form = Norm(r.DosageForm), strength = r.Strength is { } s ? Norm(s) : "";
        var matches = _rlds.Where(p => Norm(p.Ingredient) == ing && Norm(p.Form.Split(';')[0]) == form &&
                                       (strength.Length == 0 || Norm(p.Strength) == strength)).ToList();
        var appls = matches.Select(m => m.ApplNo).Distinct().ToList();
        if (appls.Count == 0) return (null, "no reference listed drug matched in the Drugs@FDA file");
        if (appls.Count > 1) return (null, $"{appls.Count} reference listed drugs matched; add the strength to your sheet to pick one");
        return _latestLabeling.TryGetValue(appls[0], out var date) ? (date, "") : (null, "no approved labeling submission found for the reference listed drug");
    }

    public ReferenceInfo Describe(AndaRecord r)
    {
        var (date, message) = RldLabeling(r);
        string rld = date is { } d
            ? $"{d.ToString("MMM d, yyyy", Inv)} · Drugs@FDA, file saved {FileDate.ToString("MMM d", Inv)}"
            : $"not available: {message}";
        return new ReferenceInfo(PsgOf(r), rld);
    }

    public static string? PsgOf(AndaRecord r) =>
        r.PsgDateFromSheet is { } p ? $"{p.ToString("MMM d, yyyy", Inv)} · from your sheet, not an official source" : null;

    private static string Norm(string s) => new string(s.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    // ---- tiny tab-delimited reader ----
    private sealed class Tsv
    {
        public required string[] Headers { get; init; }
        public required List<Dictionary<string, string>> Rows { get; init; }
        public bool Has(params string[] names) => names.All(n => Headers.Contains(n, StringComparer.OrdinalIgnoreCase));
    }

    private static Tsv? Table(ZipArchive zip, string name)
    {
        var entry = zip.Entries.FirstOrDefault(e => string.Equals(Path.GetFileName(e.FullName), name, StringComparison.OrdinalIgnoreCase));
        if (entry is null || entry.Length > 120L * 1024 * 1024) return null;
        using var reader = new StreamReader(entry.Open(), Encoding.Latin1);
        string? headerLine = reader.ReadLine();
        if (headerLine is null) return null;
        var headers = headerLine.Split('\t').Select(h => h.Trim()).ToArray();
        var rows = new List<Dictionary<string, string>>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;
            var cells = line.Split('\t');
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Length; i++) row[headers[i]] = i < cells.Length ? cells[i] : "";
            rows.Add(row);
        }
        return new Tsv { Headers = headers, Rows = rows };
    }
}
