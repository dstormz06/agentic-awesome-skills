using Anthropic;

namespace AndaCompanion.Services;

/// <summary>
/// The provider seam that future-proofs the synthesis layer. The companion's
/// grounding rules, fencing, fallback, and UI never depend on which model
/// service answers — they depend only on this interface.
///
/// Implementations: <see cref="AnthropicChatCompleter"/> (official SDK) and
/// <see cref="ElsaChatCompleter"/> (placeholder: everything is wired, the two request/response
/// translations in ElsaMapping.cs are the only part still to write — see ELSA-INTEGRATION.md).
/// Any other approved chat wrapper plugs in the same way: implement this interface and add a case to
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
    /// Provider selection via ANDA_COMPANION_PROVIDER (default "anthropic"). "elsa" returns a completer only when
    /// the endpoint is configured safely AND the mapping has been written; otherwise — like any unknown provider —
    /// it resolves to null and the app runs offline, loudly (Settings says exactly why) rather than wrongly.
    /// </summary>
    public static IChatCompleter? Create(Func<string, string?>? env = null, IElsaMapping? mapping = null)
    {
        env ??= Environment.GetEnvironmentVariable;
        string provider = env("ANDA_COMPANION_PROVIDER")?.Trim().ToLowerInvariant() ?? "anthropic";
        return provider switch
        {
            "anthropic" when ConnectionService.HasKey => new AnthropicChatCompleter(),
            "elsa" => ElsaChatCompleterOrNull(env, mapping ?? ElsaMapping.Current),
            _ => null, // no key, or an unknown provider → offline mode
        };
    }

    private static IChatCompleter? ElsaChatCompleterOrNull(Func<string, string?> env, IElsaMapping mapping) =>
        ElsaConfig.Inspect(env, mapping) is { Status: ElsaStatus.Ready, Settings: { } settings }
            ? new ElsaChatCompleter(settings, mapping)
            : null;
}
