using System.Text;
using AndaCompanion.Models;

namespace AndaCompanion.Services;

public interface ICompanionBriefService
{
    Task<CompanionBrief> GetBriefAsync(AndaRecord record, CancellationToken ct = default);
}

/// <summary>
/// Deterministic fallback used when no Claude access is configured (or the call fails).
/// It assembles a grounded brief from the record itself — no generation, no guessing —
/// so the card degrades to a great deterministic tooltip instead of breaking.
/// </summary>
public sealed class OfflineBriefService : ICompanionBriefService
{
    public Task<CompanionBrief> GetBriefAsync(AndaRecord r, CancellationToken ct = default)
    {
        // S4: never restate what the card already shows (goal/forecast dates are two
        // slots above) — lead with what the card does NOT show.
        var sb = new StringBuilder();
        if (r.Attention is not null) sb.Append($"Open now: {r.Attention.Text}. ");
        if (r.Sisters.Count > 0)
            sb.Append($"Related precedent: ANDA {r.Sisters[0].Number} — {r.Sisters[0].Description}. ");
        if (sb.Length == 0) sb.Append("No open items need action. ");
        sb.Append("Forecast drivers and assumptions: expand → Forecast & assumptions.");
        return Task.FromResult(new CompanionBrief(
            sb.ToString().TrimEnd(),
            r.Notes.Select(n => n.Who).ToArray(),
            Generated: false));
    }
}

public static class BriefServiceFactory
{
    /// <summary>
    /// Provider-backed brief when a chat completer is available (Anthropic today;
    /// Elsa or another approved wrapper later via ChatCompleterFactory); offline otherwise.
    /// </summary>
    public static ICompanionBriefService Create(Func<string?>? lensProvider = null)
    {
        var offline = new OfflineBriefService();
        return ChatCompleterFactory.Create() is { } completer
            ? new GeneratedBriefService(completer, offline, lensProvider)
            : offline;
    }
}
