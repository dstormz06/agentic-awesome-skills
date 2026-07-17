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
        var sb = new StringBuilder();
        sb.Append(r.ForecastLate
            ? $"Forecast {r.ForecastDate} runs past the {r.GoalDate} goal ({r.ForecastConfidence}). "
            : $"Forecast {r.ForecastDate} is ahead of the {r.GoalDate} goal ({r.ForecastConfidence}). ");
        if (r.Attention is not null) sb.Append($"Open now: {r.Attention.Text}. ");
        if (r.Sisters.Count > 0) sb.Append($"Related: ANDA {r.Sisters[0].Number} — {r.Sisters[0].Description}.");
        return Task.FromResult(new CompanionBrief(
            sb.ToString().TrimEnd(),
            r.Notes.Select(n => n.Who).ToArray(),
            Generated: false));
    }
}

public static class BriefServiceFactory
{
    /// <summary>Claude-backed brief when ANTHROPIC_API_KEY is configured; offline otherwise.</summary>
    public static ICompanionBriefService Create(Func<string?>? lensProvider = null) =>
        ConnectionService.HasKey
            ? new AnthropicBriefService(new OfflineBriefService(), lensProvider)
            : new OfflineBriefService();
}
