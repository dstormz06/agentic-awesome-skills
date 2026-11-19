using System.Globalization;
using System.Text.RegularExpressions;

namespace AndaCompanion.Services.Portfolio;

/// <summary>Which spreadsheet column holds what. Matched by header words, and overridable in Settings, so a person's own layout works.</summary>
public static class PortfolioColumns
{
    /// <summary>Field keys in display order. "number" is required; the rest are optional.</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> Fields = new[]
    {
        ("number", "ANDA number"), ("drug", "Drug / product"), ("applicant", "Applicant"), ("goal", "Goal date"),
        ("forecast", "Forecast date"), ("status", "Status"), ("notes", "My notes"), ("cycle", "Cycle"),
        ("submitted", "Date submitted"), ("ingredient", "Active ingredient"), ("form", "Dosage form"), ("strength", "Strength"),
        ("psg", "PSG date (yours)"),
    };

    private static readonly Dictionary<string, string[]> Synonyms = new()
    {
        ["number"] = new[] { "anda", "anda number", "anda no", "anda #", "application", "application number", "application no", "appl no", "app no", "app #" },
        ["drug"] = new[] { "drug", "drug name", "product", "product name", "proprietary name", "established name", "description of product" },
        ["applicant"] = new[] { "applicant", "company", "firm", "sponsor", "applicant name" },
        ["goal"] = new[] { "goal", "goal date", "gdufa goal", "gdufa goal date", "gdufa date", "due date", "target date", "action date" },
        ["forecast"] = new[] { "forecast", "forecast date", "projected", "projected date", "projected action date", "my forecast" },
        ["status"] = new[] { "status", "current status", "review status", "stage", "latest", "latest status" },
        ["notes"] = new[] { "notes", "rpm notes", "my notes", "note", "comments", "project description", "project descriptions", "description" },
        ["cycle"] = new[] { "cycle", "review cycle", "cycle number" },
        ["submitted"] = new[] { "submission date", "submitted", "date submitted", "received", "receipt date", "date received", "received date" },
        ["ingredient"] = new[] { "active ingredient", "ingredient", "api", "established name" },
        ["form"] = new[] { "dosage form", "form" },
        ["strength"] = new[] { "strength", "strengths" },
        ["psg"] = new[] { "psg", "psg date", "psg updated", "psg revised", "psg date updated" },
    };

    public static string Normalize(string header) =>
        Regex.Replace(header.ToLowerInvariant().Replace('_', ' '), @"[^a-z0-9# ]+", " ").Trim() is var s ? Regex.Replace(s, @"\s+", " ") : "";

    public static bool Looks(string header, string key) =>
        Synonyms.TryGetValue(key, out var words) && words.Contains(Normalize(header));

    /// <summary>
    /// The extra columns an expanded card can show under "Details" (round 33), found by their header names. "applicant" and "myNotes" are the
    /// applicant and notes columns above. Kept apart from <see cref="Fields"/>, which the browser extension mirrors column for column.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> DetailSynonyms = new Dictionary<string, string[]>
    {
        ["short"] = new[] { "applicant short name", "short name", "applicant short", "applicant abbreviation" },
        ["established"] = new[] { "established name", "established", "established drug name", "generic name" },
        ["project"] = new[] { "project description", "project", "description" },
        ["agentCo"] = new[] { "us agent", "u s agent", "us agent company", "us agent name", "agent" },
        ["agentEmail"] = new[] { "us agent email", "u s agent email", "agent email", "us agent e mail" },
        ["applicantEmail"] = new[] { "applicant email", "applicant e mail", "applicant contact email", "contact email" },
        ["doc356h"] = new[] { "latest 356h", "356h", "form 356h", "fda 356h", "356h path", "356h link" },
        ["doc32p1"] = new[] { "module 3 2 p 1", "3 2 p 1", "32p1", "m3 2 p 1", "module 3 2 p 1 path" },
        ["teamLead"] = new[] { "team lead", "team leader", "tl" },
        ["division"] = new[] { "division" },
        ["fei"] = new[] { "facility fei", "fei", "fei number", "facility fei number" },
    };

    private static readonly string[] OwnNotes = { "my notes", "notes", "rpm notes", "note", "comments" };

    /// <summary>
    /// The header for each detail column: the first header, not already used for a field above, whose name is one of the column's names.
    /// Two columns can already be in use under another name, and are then shown under both: a column headed "Established name" that serves as
    /// the drug or the active ingredient, and a column headed "Project description" that serves as the notes. In the second case "My notes"
    /// is a separate column of that name when the sheet has one, and otherwise is not in the sheet.
    /// </summary>
    /// <param name="fields">What <see cref="Detect"/> chose for the card's own fields.</param>
    public static Dictionary<string, string> DetectDetails(IReadOnlyList<string> headers, IReadOnlyDictionary<string, string> fields)
    {
        var map = new Dictionary<string, string>();
        var used = new HashSet<string>(fields.Values);
        foreach (var (key, words) in DetailSynonyms)
        {
            var hit = headers.FirstOrDefault(h => !used.Contains(h) && words.Contains(Normalize(h)));
            if (hit is not null) { map[key] = hit; used.Add(hit); }
        }
        bool Named(string? header, string key) => header is not null && DetailSynonyms[key].Contains(Normalize(header));
        if (!map.ContainsKey("established"))
            foreach (var field in new[] { "ingredient", "drug" })
                if (fields.TryGetValue(field, out var h) && Named(h, "established")) { map["established"] = h; break; }
        if (!map.ContainsKey("project") && fields.TryGetValue("notes", out var notes) && Named(notes, "project"))
        {
            map["project"] = notes;
            var own = headers.FirstOrDefault(h => !used.Contains(h) && OwnNotes.Contains(Normalize(h)));
            map["myNotes"] = own ?? "";      // "" : the notes column is the project description, and the sheet has no notes column besides it
        }
        return map;
    }

    /// <summary>The header chosen for each field: the person's override if it exists, otherwise the first header that matches.</summary>
    public static Dictionary<string, string> Detect(IReadOnlyList<string> headers, IReadOnlyDictionary<string, string>? overrides = null)
    {
        var map = new Dictionary<string, string>();
        var used = new HashSet<string>();
        foreach (var (key, _) in Fields)
        {
            if (overrides is not null && overrides.TryGetValue(key, out var chosen) && !string.IsNullOrWhiteSpace(chosen) && headers.Contains(chosen))
            { map[key] = chosen; used.Add(chosen); continue; }
            var hit = headers.FirstOrDefault(h => !used.Contains(h) && Looks(h, key));
            if (hit is not null) { map[key] = hit; used.Add(hit); }
        }
        return map;
    }
}

public static class DateParsing
{
    private static readonly string[] Formats =
    {
        "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "M/d/yyyy", "M/d/yy", "MM/dd/yyyy", "M/d/yyyy h:mm:ss tt", "MMM d, yyyy", "MMMM d, yyyy",
        "d-MMM-yyyy", "d-MMM-yy", "dd-MMM-yyyy", "d MMM yyyy", "yyyy/MM/dd", "MMM d yyyy", "MMMM d yyyy",
    };

    /// <summary>US month-first dates and ISO dates. A date that cannot be read is not guessed.</summary>
    public static bool TryParse(string? text, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (DateTime.TryParseExact(text.Trim(), Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            if (dt.Year < 1990 || dt.Year > 2100) return false;
            date = DateOnly.FromDateTime(dt);
            return true;
        }
        return false;
    }
}
