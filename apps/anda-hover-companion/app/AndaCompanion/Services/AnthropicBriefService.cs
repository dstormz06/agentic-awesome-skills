using System.Text.Json;
using AndaCompanion.Models;
using Anthropic;

namespace AndaCompanion.Services;

/// <summary>
/// Companion brief via the official Anthropic C# SDK (claude-opus-4-8).
/// The brief is the ONLY thing Claude writes — facts and the forecast number are
/// rendered from the data layer. Grounded-tone rules are enforced by the prompt;
/// any failure falls back to the deterministic offline brief.
/// </summary>
public sealed class AnthropicBriefService : ICompanionBriefService
{
    private const string SystemPrompt =
        "You are the companion brief inside an FDA regulatory project manager's ANDA quick-look card. " +
        "You write 2-4 sentences, nothing else: no preamble, no hedging filler, no restating what the card " +
        "already shows (goal date, forecast date, progress). Your job is to watch the RPM's back: flag " +
        "untested assumptions, applicant patterns, cross-ANDA signals, conflicts between notes and the plan, " +
        "gaps, and what will land on them next. Quantify claims where the record allows. Every claim must be " +
        "supported by the structured record provided; if the record is insufficient, say what is missing " +
        "rather than inferring. Cut the fluff; stay professionally grounded.";

    private readonly ICompanionBriefService _fallback;
    private readonly AnthropicClient _client = new();

    public AnthropicBriefService(ICompanionBriefService fallback) => _fallback = fallback;

    public async Task<CompanionBrief> GetBriefAsync(AndaRecord record, CancellationToken ct = default)
    {
        try
        {
            var recordJson = JsonSerializer.Serialize(record);
            var response = await _client.Messages.Create(new()
            {
                Model = "claude-opus-4-8",
                MaxTokens = 1024,
                System = SystemPrompt,
                Messages =
                [
                    new()
                    {
                        Role = "user",
                        Content = "Structured record for ANDA " + record.Number + ":\n" + recordJson +
                                  "\n\nWrite the companion brief.",
                    },
                ],
            }, cancellationToken: ct);

            var text = string.Concat(response.Content
                .Select(block => block.TryPickText(out var textBlock) ? textBlock.Text : string.Empty));
            if (string.IsNullOrWhiteSpace(text)) return await _fallback.GetBriefAsync(record, ct);
            return new CompanionBrief(text.Trim(), record.Notes.Select(n => n.Who).ToArray(), Generated: true);
        }
        catch
        {
            // Rate limit, network, refusal, missing key — the card must still work.
            return await _fallback.GetBriefAsync(record, ct);
        }
    }
}
