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
    public Task<CompanionBrief> GetBriefAsync(AndaRecord r, CancellationToken ct = default) =>
        Task.FromResult(new CompanionBrief(Compose(r), r.Notes.Select(n => n.Who).ToArray(), Generated: false));

    /// <summary>The offline brief text on its own, so other surfaces (the browser extension's host) say exactly the same thing.</summary>
    public static string Compose(AndaRecord r)
    {
        // S4: never restate what the card already shows (goal/forecast dates are two
        // slots above) — lead with what the card does NOT show.
        var sb = new StringBuilder();
        if (r.Attention is not null) sb.Append($"Open now: {r.Attention.Text}. ");
        if (r.Sisters.Count > 0)
            sb.Append($"Related precedent: ANDA {r.Sisters[0].Number} — {r.Sisters[0].Description}. ");
        if (sb.Length == 0) sb.Append("No open items need action. ");
        sb.Append("Forecast drivers and assumptions: expand → Forecast & assumptions.");
        return sb.ToString().TrimEnd();
    }
}

public static class BriefServiceFactory
{
    /// <summary>
    /// Provider-backed brief when a chat completer is available (Anthropic today;
    /// Elsa or another approved wrapper later via ChatCompleterFactory); offline otherwise.
    /// </summary>
    public static ICompanionBriefService Create(Func<string?>? lensProvider = null, Settings? settings = null)
    {
        var offline = new OfflineBriefService();
        var completer = ChatCompleterFactory.CreateReporting(settings);
        ConnectionStatus.Configure(completer);   // set up but not tested, or offline: never "connected" until a request succeeds
        return completer is not null
            ? new GeneratedBriefService(completer, offline, lensProvider)
            : offline;
    }
}
