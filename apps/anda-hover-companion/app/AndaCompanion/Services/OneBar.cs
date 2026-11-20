using System.Text.RegularExpressions;
using AndaCompanion.Data;
using AndaCompanion.Models;

namespace AndaCompanion.Services;

/// <summary>One row of One Bar: what it shows, what pressing it opens ("card", "draft" or "ask"), and the text it would open with.</summary>
public sealed record BarRow(string Icon, string Title, string Sub, string Act, string Text);

/// <summary>
/// One Bar (round 18 in the simulation, round 33 here): the lookup window that also offers Review and Ask. It suggests what to open from what
/// is typed and what was selected. Every suggestion is a row to press; nothing here opens by itself.
/// </summary>
public static class OneBar
{
    public static readonly IReadOnlyList<string> Styles = new[] { "box", "bar" };

    /// <summary>The answer in one sentence, as the card's own headline says it. A record without a forecast says so (the simulation has none).</summary>
    public static string Hero(AndaRecord r)
    {
        string needs = r.Attention is { } a ? $" · needs you: {a.Text.Split(" — ")[0]}" : "";
        if (r.ForecastDeltaDays is not { } delta) return "No forecast in your data" + needs;
        int n = Math.Abs(delta);
        return (delta == 0 ? "On the goal date" : $"{n} day{(n == 1 ? "" : "s")} {(delta > 0 ? "later than" : "ahead of")} the goal") + needs;
    }

    /// <summary>What was typed is nearly an ANDA number (an NDA number, six digits with a space) and is nothing else.</summary>
    public static bool NearNumber(string raw) =>
        Regex.IsMatch(raw, @"^N-?[0-9]{6}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
        || Regex.IsMatch(raw, @"^(NDA|BLA|IND|DMF|MF|NDC)[\s#:.-]*(No\.?\s*)?[0-9-]{5,11}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
        || Regex.IsMatch(raw, @"^[0-9]{3}[\s-][0-9]{3}$");

    public static List<BarRow> Rows(string? typed, string? selection, Func<string, AndaRecord?> find)
    {
        string raw = (typed ?? "").Trim(), sel = selection ?? "";
        string? n = AndaValidator.Extract(raw);
        var rows = new List<BarRow>();
        if (n is null && NearNumber(raw)) rows.Add(new BarRow("!", "Not an ANDA number", LookupError(raw), "note", raw));      // Enter says why, instead of opening a review of it
        if (n is not null)
        {
            var r = find(n);
            rows.Add(new BarRow("6", r is not null ? $"ANDA {n} · {r.Drug}" : $"ANDA {n}", r is not null ? Hero(r) : "Not in your data", "card", n));
        }
        string typedText = raw.Length > 0 && n is null ? raw : "", draftText = typedText.Length > 0 ? typedText : sel;
        rows.Add(new BarRow("¶", typedText.Length > 0 ? "Review what you typed as a draft" : sel.Length > 0 ? "Review the selected text as a draft" : "Review a draft",
            "Checks it against the record, with no AI", "draft", draftText));
        bool table = sel.Length > 0 && SelectionHelpers.Classify(sel).Kind == SelectionKind.Table;
        rows.Add(new BarRow("▦", table ? "Ask about the selected cells" : sel.Length > 0 ? "Ask about the selected text" : "Ask about a selection",
            table ? "Duplicates, blanks, totals, compare two columns" : "Quick checks and your stored prompts", "ask", sel));
        return rows;
    }

    /// <summary>What the lookup window says when what was typed is not an ANDA number.</summary>
    public static string LookupError(string? typed)
    {
        string raw = (typed ?? "").Trim();
        if (Regex.IsMatch(raw, @"^N-?[0-9]{6}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return $"{raw.ToUpperInvariant()} is an NDA number (N is for NDA, A is for ANDA). Please enter a six-digit ANDA number.";
        if (Regex.IsMatch(raw, @"^(NDA|BLA|IND|DMF|MF|NDC)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return "NDA, BLA, IND and similar numbers are not ANDA numbers. Please enter a six-digit ANDA number.";
        if (Regex.IsMatch(raw, @"^[0-9]{3}[\s-][0-9]{3}$")) return "Please enter the six digits without spaces or dashes.";
        return "Please enter a six-digit ANDA number.";
    }
}
