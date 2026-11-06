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

/// <summary>Claude via the official Anthropic C# SDK. The model name comes from Settings, so a change needs no rebuild.</summary>
public sealed class AnthropicChatCompleter(string? model = null) : IChatCompleter
{
    private readonly AnthropicClient _client = new();
    private readonly string _model = Settings.IsValidModelName(model) ? model!.Trim() : Settings.DefaultClaudeModel;

    public string ProviderName => "Anthropic · " + _model;

    public async Task<string> CompleteAsync(string system, string user, int maxTokens, CancellationToken ct)
    {
        var response = await _client.Messages.Create(new()
        {
            Model = _model,
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
    public static IChatCompleter? Create(Func<string, string?>? env = null, IElsaMapping? mapping = null, Settings? settings = null)
    {
        env ??= Environment.GetEnvironmentVariable;
        // The person's choice in Settings wins over the environment. Auto (the default) keeps the earlier behavior.
        switch (settings?.Provider)
        {
            case ProviderChoice.Offline: return null;
            case ProviderChoice.MyApi: return CustomApiConfig.CreateCompleter(settings.CustomApi, env);
            case ProviderChoice.Claude: return string.IsNullOrEmpty(env("ANTHROPIC_API_KEY")) ? null : new AnthropicChatCompleter(settings.ClaudeModel);
        }
        string provider = env("ANDA_COMPANION_PROVIDER")?.Trim().ToLowerInvariant() ?? "anthropic";
        return provider switch
        {
            "anthropic" when ConnectionService.HasKey => new AnthropicChatCompleter(settings?.ClaudeModel),
            "elsa" => ElsaChatCompleterOrNull(env, mapping ?? ElsaMapping.Current),
            _ => null, // no key, or an unknown provider → offline mode
        };
    }

    /// <summary>The completer the app really uses: the same choice, wrapped so every real request updates <see cref="ConnectionStatus"/>.</summary>
    public static IChatCompleter? CreateReporting(Settings? settings = null, Func<string, string?>? env = null) =>
        Create(env, null, settings) is { } inner ? new ReportingChatCompleter(inner) : null;

    private static IChatCompleter? ElsaChatCompleterOrNull(Func<string, string?> env, IElsaMapping mapping) =>
        ElsaConfig.Inspect(env, mapping) is { Status: ElsaStatus.Ready, Settings: { } settings }
            ? new ElsaChatCompleter(settings, mapping)
            : null;
}
