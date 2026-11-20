using AndaCompanion.Models;

namespace AndaCompanion.Services;

/// <summary>One row of a card's details: a column of the person's own sheet, its value or "not in your sheet", and whether it can be copied.</summary>
public sealed record DetailRow(string Key, string Label, string? Value, string Text, bool Copy, string CopyName);

/// <summary>What the details of a card show, already decided.</summary>
public sealed record DetailView(IReadOnlyList<DetailRow> Rows, int Have, string Count, string Empty, bool CopyAll, string Block);

/// <summary>
/// Card details (round 19 in the simulation, round 33 here): columns of the person's own sheet, shown when a card is expanded, each with Copy.
/// Nothing is looked up anywhere. The rules are the simulation's; mockup/round33-golden.json holds both programs to the same answers.
/// </summary>
public static class CardDetails
{
    /// <summary>The columns that can be shown, in the order they are shown.</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> Fields = new[]
    {
        ("applicant", "Applicant"), ("established", "Established name"), ("project", "Project description"), ("agentCo", "US agent"), ("agentEmail", "US agent email"),
        ("applicantEmail", "Applicant email"), ("myNotes", "My notes"), ("doc356h", "Latest 356h"), ("doc32p1", "Module 3.2.P.1"), ("teamLead", "Team lead"), ("division", "Division"), ("fei", "Facility FEI"),
    };

    /// <summary>The columns shown until the person chooses otherwise.</summary>
    public static readonly IReadOnlyList<string> DefaultFields = new[] { "applicant", "established", "project", "agentCo", "agentEmail", "applicantEmail", "myNotes", "doc356h", "doc32p1" };

    public const string NotInSheet = "not in your sheet";
    public const string NoneChosen = "No columns are chosen. Choose them in Settings, My files and writing.";

    /// <summary>The chosen columns as they are kept: only known keys, each once, in the fixed order.</summary>
    public static List<string> Normalize(IEnumerable<string>? chosen)
    {
        var set = new HashSet<string>(chosen ?? Array.Empty<string>(), StringComparer.Ordinal);
        return Fields.Where(f => set.Contains(f.Key)).Select(f => f.Key).ToList();
    }

    /// <summary>The value of one column for one record, or null when the sheet does not hold it. The applicant's short name comes from the sheet, never from a search.</summary>
    public static string? Value(AndaRecord r, string key)
    {
        var d = r.Details;
        if (d is null || !d.TryGetValue(key, out var v) || string.IsNullOrEmpty(v)) return null;
        return key == "applicant" && d.TryGetValue("short", out var s) && !string.IsNullOrEmpty(s) ? $"{v} ({s})" : v;
    }

    public static DetailView Rows(AndaRecord r, IEnumerable<string>? chosen)
    {
        var set = new HashSet<string>(chosen ?? Array.Empty<string>(), StringComparer.Ordinal);
        var rows = Fields.Where(f => set.Contains(f.Key)).Select(f =>
        {
            string? v = Value(r, f.Key);
            return new DetailRow(f.Key, f.Label, v, v ?? NotInSheet, v is not null, "Copy " + Name(f.Label));
        }).ToList();
        int have = rows.Count(x => x.Copy);
        string block = string.Join("\n", new[] { $"ANDA {r.Number}" }.Concat(rows.Where(x => x.Copy).Select(x => $"{x.Label}: {x.Value}")));
        return new DetailView(rows, have, rows.Count > 0 ? $"· {have} of {rows.Count} in your sheet" : "", rows.Count > 0 ? "" : NoneChosen, have > 0, block);
    }

    public static string Copied(string what) => $"Copied: {what}.";

    /// <summary>
    /// A column's name inside a sentence: "Copy US agent email", "Copied: applicant email." An ordinary first word is lowered; a word in
    /// capitals (US, FEI) and the rest of the name are kept, so a screen reader says "U S", not "us".
    /// </summary>
    public static string Name(string label) =>
        label.Length > 1 && char.IsUpper(label[0]) && char.IsLower(label[1]) ? char.ToLowerInvariant(label[0]) + label[1..] : label;

    /// <summary>Where the details are from, said under them.</summary>
    public static string Source(AndaRecord r, string? fileName) => r.Demo || string.IsNullOrEmpty(fileName) ? "From the fictional demo records." : $"From {fileName}.";
}
