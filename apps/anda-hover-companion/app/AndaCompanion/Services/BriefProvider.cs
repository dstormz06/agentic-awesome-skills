using Anthropic;

namespace AndaCompanion.Services;

/// <summary>
/// The provider seam that future-proofs the synthesis layer. The companion's
/// grounding rules, fencing, fallback, and UI never depend on which model
/// service answers — they depend only on this interface.
///
/// Implementations: <see cref="AnthropicChatCompleter"/> (today, official SDK).
/// When FDA's Elsa (or any approved multi-model chat wrapper) publishes an API,
/// implement this interface against its chat endpoint and register it in
/// <see cref="ChatCompleterFactory"/> — nothing else in the app changes.
/// </summary>
public interface IChatCompleter
{
    /// <summary>A human-readable provider name for the connection panel.</summary>
    string ProviderName { get; }

    Task<string> CompleteAsync(string system, string user, int maxTokens, CancellationToken ct);
}

/// <summary>Claude via the official Anthropic C# SDK (claude-opus-4-8).</summary>
public sealed class AnthropicChatCompleter : IChatCompleter
{
    private readonly AnthropicClient _client = new();

    public string ProviderName => "Anthropic · claude-opus-4-8";

    public async Task<string> CompleteAsync(string system, string user, int maxTokens, CancellationToken ct)
    {
        var response = await _client.Messages.Create(new()
        {
            Model = "claude-opus-4-8",
            MaxTokens = maxTokens,
            System = system,
            Messages = [new() { Role = "user", Content = user }],
        }, cancellationToken: ct);
        return string.Concat(response.Content
            .Select(block => block.TryPickText(out var textBlock) ? textBlock.Text : string.Empty));
    }
}

public static class ChatCompleterFactory
{
    /// <summary>
    /// Provider selection via ANDA_COMPANION_PROVIDER (default "anthropic").
    /// "elsa" is a reserved name: FDA's Elsa has no published API yet, so it —
    /// like any unknown provider — resolves to null and the app runs offline,
    /// loudly rather than wrongly. To integrate Elsa later: implement
    /// IChatCompleter against its endpoint and add a case here.
    /// </summary>
    public static IChatCompleter? Create()
    {
        string provider = Environment.GetEnvironmentVariable("ANDA_COMPANION_PROVIDER")?.ToLowerInvariant() ?? "anthropic";
        return provider switch
        {
            "anthropic" when ConnectionService.HasKey => new AnthropicChatCompleter(),
            _ => null, // no key, or a provider (e.g. "elsa") not yet available → offline mode
        };
    }
}
