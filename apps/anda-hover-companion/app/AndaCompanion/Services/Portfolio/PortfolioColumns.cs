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
