using System.Diagnostics;

namespace AndaCompanion.Services;

/// <summary>
/// The Elsa placeholder. Elsa (the agency's AI platform) is expected to be the production brain; its API is not
/// available to this project yet, and no request or response shape is guessed here. Everything AROUND the call is
/// already built and tested — provider selection, configuration from the environment, an https-only guard, a
/// timeout, error handling, offline fallback, the Settings status line and the Test button. What remains is two
/// small methods in <c>ElsaMapping.cs</c> (see <c>ELSA-INTEGRATION.md</c>).
/// </summary>
public sealed record ElsaSettings(string Endpoint, string? Token, string? Model, string? Agent)
{
    /// <summary>A record prints every member by default, which would put the token in any log line or exception
    /// that formats this object. The token is redacted here, so there is no path by which it is ever printed.</summary>
    public override string ToString() =>
        $"ElsaSettings {{ Endpoint = {Endpoint}, Token = {(Token is null ? "none" : "***")}, Model = {Model ?? "none"}, Agent = {Agent ?? "none"} }}";
}

public enum ElsaStatus { NotSelected, MissingEndpoint, InsecureEndpoint, MappingNotFilled, Ready }

public sealed record ElsaInspection(ElsaStatus Status, ElsaSettings? Settings, string Message);

/// <summary>Raised when Elsa answers with an error status. Carries the status only — never a body or the token.</summary>
public sealed class ElsaException(string message) : Exception(message);

/// <summary>Raised by the placeholder mapping: the app is wired for Elsa, the two mapping methods are not written yet.</summary>
public sealed class ElsaNotMappedException() : NotImplementedException(
    "Elsa is selected but its request mapping has not been written yet. See ELSA-INTEGRATION.md, step 3.");

/// <summary>
/// The only Elsa-specific code. Two translations, nothing else: our provider-neutral request → Elsa's HTTP request,
/// and Elsa's response body → the assistant's text. Everything else (timeouts, status handling, fallback) is shared.
/// </summary>
public interface IElsaMapping
{
    /// <summary>False for the placeholder; true once real methods replace it.</summary>
    bool IsFilledIn { get; }

    /// <summary>Build Elsa's request: method, URL (usually settings.Endpoint), auth header, JSON body.</summary>
    HttpRequestMessage BuildRequest(ElsaSettings settings, string system, string user, int maxTokens);

    /// <summary>Pull the assistant's text out of the response body. Throw if the shape is not what you expect.</summary>
    string ReadReply(string responseBody);

    /// <summary>Override only if Elsa needs something special (integrated Windows auth, a client certificate, a proxy).</summary>
    HttpMessageHandler CreateHandler() => new HttpClientHandler();
}

/// <summary>The stand-in until the real mapping exists. Fails loudly and specifically rather than guessing.</summary>
public sealed class UnfilledElsaMapping : IElsaMapping
{
    public bool IsFilledIn => false;
    public HttpRequestMessage BuildRequest(ElsaSettings settings, string system, string user, int maxTokens) =>
        throw new ElsaNotMappedException();
    public string ReadReply(string responseBody) => throw new ElsaNotMappedException();
}

/// <summary>Reads and checks the Elsa configuration. Values come from the environment and are never stored or logged.</summary>
public static class ElsaConfig
{
    public const string EndpointVar = "ANDA_ELSA_ENDPOINT";
    public const string TokenVar = "ANDA_ELSA_TOKEN";
    public const string ModelVar = "ANDA_ELSA_MODEL";
    public const string AgentVar = "ANDA_ELSA_AGENT";

    /// <summary>True when ANDA_COMPANION_PROVIDER names Elsa.</summary>
    public static bool IsSelected(Func<string, string?>? env = null) =>
        string.Equals((env ?? Environment.GetEnvironmentVariable)("ANDA_COMPANION_PROVIDER")?.Trim(), "elsa",
            StringComparison.OrdinalIgnoreCase);

    public static ElsaInspection Inspect(Func<string, string?>? env = null, IElsaMapping? mapping = null)
    {
        env ??= Environment.GetEnvironmentVariable;
        mapping ??= ElsaMapping.Current;
        if (!IsSelected(env))
            return new(ElsaStatus.NotSelected, null, "Elsa is not selected (ANDA_COMPANION_PROVIDER is not \"elsa\").");

        string? endpoint = env(EndpointVar)?.Trim();
        if (string.IsNullOrEmpty(endpoint))
            return new(ElsaStatus.MissingEndpoint, null,
                $"Elsa is selected but {EndpointVar} is not set. Set it to the endpoint URL Elsa's team gives you.");

        // A token must never travel in clear text. https only; plain http is allowed solely for a local test server.
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            !(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            return new(ElsaStatus.InsecureEndpoint, null,
                $"{EndpointVar} must be an https:// URL (http:// is accepted only for localhost). Nothing was sent.");

        string? token = env(TokenVar)?.Trim();
        var settings = new ElsaSettings(endpoint,
            string.IsNullOrEmpty(token) ? null : token,
            NullIfBlank(env(ModelVar)), NullIfBlank(env(AgentVar)));

        return mapping.IsFilledIn
            ? new(ElsaStatus.Ready, settings, $"Elsa configured · {uri.Host}")
            : new(ElsaStatus.MappingNotFilled, settings,
                $"Elsa endpoint set ({uri.Host}), but the request mapping is still the placeholder. Briefs stay offline " +
                "until it is written — see ELSA-INTEGRATION.md, step 3.");
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>An <see cref="IChatCompleter"/> for Elsa. Shared plumbing only; the Elsa-specific parts live in the mapping.</summary>
public sealed class ElsaChatCompleter : IChatCompleter, IDisposable
{
    private readonly ElsaSettings _settings;
    private readonly IElsaMapping _mapping;
    private readonly HttpClient _http;

    public ElsaChatCompleter(ElsaSettings settings, IElsaMapping mapping, HttpMessageHandler? handler = null)
    {
        _settings = settings;
        _mapping = mapping;
        _http = new HttpClient(handler ?? mapping.CreateHandler()) { Timeout = TimeSpan.FromSeconds(30) };
    }

    public string ProviderName => string.IsNullOrWhiteSpace(_settings.Model) ? "Elsa" : $"Elsa · {_settings.Model}";

    public async Task<string> CompleteAsync(string system, string user, int maxTokens, CancellationToken ct)
    {
        using var request = _mapping.BuildRequest(_settings, system, user, maxTokens);
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new ElsaException($"Elsa answered {(int)response.StatusCode} {response.ReasonPhrase}.");
        return _mapping.ReadReply(await response.Content.ReadAsStringAsync(ct));
    }

    public void Dispose() => _http.Dispose();
}
