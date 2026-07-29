using System.Text;
using AndaCompanion.Models;

namespace AndaCompanion.Services;

/// <summary>
/// T1 — the copy-ready status line: the sentence RPMs retype dozens of times a
/// week, composed deterministically from the record (never from the model).
/// Pure function so it is trivially testable and reusable (card chip, future
/// handoff pack, future Huddle Mode).
/// </summary>
public static class StatusLine
{
    public static string Build(AndaRecord r)
    {
        var sb = new StringBuilder();
        sb.Append($"{r.Kind} {r.Number} ({r.Drug}): {r.Meta[(r.Meta.LastIndexOf('·') + 1)..].Trim()}, ");
        sb.Append($"{r.ProgressPct}% through review; goal {r.GoalDate}; ");
        sb.Append($"forecast {r.ForecastDate} ({Clean(r.ForecastDelta)}, {r.ForecastConfidence})");
        if (r.Attention is not null) sb.Append($"; needs action: {r.Attention.Text}");
        sb.Append('.');
        return sb.ToString();
    }

    // The delta glyphs (▲/▼) are card UI; a pasted sentence should be plain text.
    private static string Clean(string delta) => delta.Replace("▲", "").Replace("▼", "").Trim();
}
