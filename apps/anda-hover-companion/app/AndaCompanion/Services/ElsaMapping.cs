namespace AndaCompanion.Services;

/// <summary>
/// Round 41: Elsa's two translations come from Settings, not from code. When Elsa's team gives the shape of its request and where the
/// reply text is, the person pastes them in Settings, AI and connections, Elsa (checklist: ELSA-INTEGRATION.md): a request body with the
/// same {{placeholders}} as My API ({{system}}, {{user}}, {{max_tokens}}, {{model}} from ANDA_ELSA_MODEL, {{agent}} from ANDA_ELSA_AGENT),
/// the reply path, and the token's header and prefix. The address and the token stay in the environment variables IT sets. Left blank, the
/// mapping is the placeholder, which fails loudly instead of guessing. No source edit is needed.
/// </summary>
public static class ElsaMapping
{
    public static IElsaMapping For(Settings? settings) =>
        settings is { ElsaRequestTemplate: var t, ElsaReplyPath: var p } && !string.IsNullOrWhiteSpace(t) && !string.IsNullOrWhiteSpace(p)
            ? new SettingsElsaMapping(t.Trim(), p.Trim(), settings.ElsaAuthHeader, settings.ElsaAuthScheme)
            : new UnfilledElsaMapping();

    public const string Hint = "Fill these in only when Elsa's team gives them. The address and the token stay in the environment variables IT sets (ANDA_ELSA_ENDPOINT, ANDA_ELSA_TOKEN, with ANDA_COMPANION_PROVIDER=elsa); nothing here is a secret. The body uses the same placeholders as My API, and {{agent}} for ANDA_ELSA_AGENT. Left blank, Elsa stays offline and says so.";
    public const string SaidBlank = "Left blank: Elsa stays offline until its request body and reply path are filled in.";
    public const string SaidFilled = "Filled in. Elsa is used when IT has set its environment variables; press Test above to try it.";

    /// <summary>What Settings says about the values typed for Elsa, or null when they can be used (or are left blank on purpose).</summary>
    public static string? Problem(Settings s)
    {
        bool template = !string.IsNullOrWhiteSpace(s.ElsaRequestTemplate), path = !string.IsNullOrWhiteSpace(s.ElsaReplyPath);
        if (!template && !path) return null;
        if (!template) return "Please also paste Elsa's request body; until both are filled in, Elsa stays offline.";
        if (!path) return "Please also enter where the reply text is (the reply path); until both are filled in, Elsa stays offline.";
        if (CustomApiMapping.TryFill(s.ElsaRequestTemplate, "system", "user", 16, "model", new Dictionary<string, string> { ["agent"] = "agent" }, out _) is { } templateError)
            return templateError;
        if (!CustomApiMapping.IsValidPath(s.ElsaReplyPath))
            return "The reply path is not valid. Please use names and [numbers], for example choices[0].message.content";
        return ApiTarget.Authorization(s.ElsaAuthHeader, s.ElsaAuthScheme, null).Problem;
    }
}

/// <summary>Elsa's translations as typed in Settings: My API's template and reply path, with Elsa's model and agent from the environment.</summary>
public sealed class SettingsElsaMapping(string template, string path, string header, string scheme) : IElsaMapping
{
    public bool IsFilledIn => true;

    public HttpRequestMessage BuildRequest(ElsaSettings es, string system, string user, int maxTokens) =>
        new CustomApiMapping(Api(es.Model), new Dictionary<string, string> { ["agent"] = es.Agent ?? "" }).BuildRequest(es, system, user, maxTokens);

    public string ReadReply(string responseBody) => new CustomApiMapping(Api(null)).ReadReply(responseBody);

    private CustomApiSettings Api(string? model) =>
        new() { Name = "Elsa", RequestTemplate = template, ResponsePath = path, AuthHeader = header, AuthScheme = scheme, Model = model ?? "" };
}
