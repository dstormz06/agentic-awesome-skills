using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AndaCompanion.Services;

/// <summary>Which connection the app uses for generated text. <c>Auto</c> keeps the earlier behavior (the environment decides).</summary>
public enum ProviderChoice { Auto, Offline, Claude, MyApi }

/// <summary>
/// The person's own API, described in Settings instead of in code. Nothing here is a secret: the token is never stored.
/// <see cref="SecretEnvVar"/> only NAMES the environment variable that holds it, and it is read at the moment of a request.
/// </summary>
public sealed class CustomApiSettings
{
    public const string ExampleTemplate =
        "{\"model\":\"{{model}}\",\"max_tokens\":{{max_tokens}},\"messages\":[{\"role\":\"system\",\"content\":\"{{system}}\"},{\"role\":\"user\",\"content\":\"{{user}}\"}]}";

    public string Name { get; set; } = "My API";

    /// <summary>https:// only (http:// is accepted for localhost, for a local test server).</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>The request header that carries the token, for example Authorization or x-api-key. Empty = no token header.</summary>
    public string AuthHeader { get; set; } = "Authorization";

    /// <summary>Text put before the token in that header, for example "Bearer". Empty = the token alone.</summary>
    public string AuthScheme { get; set; } = "Bearer";

    /// <summary>The NAME of an environment variable that holds the token. The token itself is never saved by this app.</summary>
    public string SecretEnvVar { get; set; } = "";

    public string Model { get; set; } = "";

    /// <summary>The JSON request body. Placeholders: {{system}}, {{user}}, {{max_tokens}}, {{model}}.</summary>
    public string RequestTemplate { get; set; } = ExampleTemplate;

    /// <summary>Where the reply text is in the JSON answer, for example choices[0].message.content</summary>
    public string ResponsePath { get; set; } = "choices[0].message.content";

    public int TimeoutSeconds { get; set; } = 30;
    public static readonly IReadOnlyList<int> TimeoutChoices = new[] { 10, 30, 60, 120 };
}

public sealed record CustomApiInspection(bool Ok, ElsaSettings? Settings, string Message);

/// <summary>
/// Round 40: My API filled in for a common kind of service with one press: the header, its prefix, the body and the reply path. The address
/// stays the person's own; Hint is only shown as its example. The same three as the simulation (mockup/round40-golden.json).
/// </summary>
public sealed record CustomApiPreset(string Id, string Label, string Header, string Scheme, string Template, string Path, string Hint)
{
    public static readonly IReadOnlyList<CustomApiPreset> All = new CustomApiPreset[]
    {
        new("openai", "OpenAI-compatible", "Authorization", "Bearer", CustomApiSettings.ExampleTemplate, "choices[0].message.content", "https://…/v1/chat/completions"),
        new("azure", "Azure OpenAI", "api-key", "",
            "{\"max_tokens\":{{max_tokens}},\"messages\":[{\"role\":\"system\",\"content\":\"{{system}}\"},{\"role\":\"user\",\"content\":\"{{user}}\"}]}",
            "choices[0].message.content", "https://<resource>.openai.azure.com/openai/deployments/<deployment>/chat/completions?api-version=<version>"),
        new("ollama", "Ollama on this PC", "", "",
            "{\"model\":\"{{model}}\",\"stream\":false,\"messages\":[{\"role\":\"system\",\"content\":\"{{system}}\"},{\"role\":\"user\",\"content\":\"{{user}}\"}]}",
            "message.content", "http://localhost:11434/api/chat"),
    };

    public void FillIn(CustomApiSettings api) { api.AuthHeader = Header; api.AuthScheme = Scheme; api.RequestTemplate = Template; api.ResponsePath = Path; }
}

/// <summary>
/// Round 40: where a request may go and with which token, checked the same way for My API, the agents and the MCP servers before anything
/// is sent. The address: https:// only (http:// for this PC only), no user name or password in it. The token: read from the environment
/// variable the person names, never stored. Each check gives either its value or a sentence for the person, never both.
/// </summary>
public static class ApiTarget
{
    public static (Uri? Uri, string? Problem) Address(string? endpoint)
    {
        string address = (endpoint ?? "").Trim();
        if (address.Length == 0) return (null, "Please enter the endpoint address of your API (https://…).");
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            !(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            return (null, "The endpoint must be an https:// address (http:// is accepted only for localhost). Nothing was sent.");
        if (!string.IsNullOrEmpty(uri.UserInfo))
            return (null, "Please do not put a user name or password in the address; use the environment variable for the token instead. Nothing was sent.");
        return (uri, null);
    }

    /// <summary>No variable named: no token and no problem.</summary>
    public static (string? Token, string? Problem) Token(string? secretEnvVar, Func<string, string?> env)
    {
        string var = (secretEnvVar ?? "").Trim();
        if (var.Length == 0) return (null, null);
        if (!System.Text.RegularExpressions.Regex.IsMatch(var, "^[A-Za-z_][A-Za-z0-9_]{0,127}$"))
            return (null, "That does not look like an environment variable name. Please enter the NAME of the variable, never the token itself.");
        string? token = env(var)?.Trim();
        return string.IsNullOrEmpty(token)
            ? (null, $"The environment variable {var} is not set on this PC, so no token can be sent. Set it through your approved channel and press Test again.")
            : (token, null);
    }
}

public static class CustomApiConfig
{
    /// <summary>Checks everything that can be checked without sending a request. Values are never logged.</summary>
    public static CustomApiInspection Inspect(CustomApiSettings s, Func<string, string?>? env = null, IReadOnlyDictionary<string, string>? extras = null)
    {
        env ??= Environment.GetEnvironmentVariable;
        // round 40: the address and token checks are shared with the MCP servers (ApiTarget), in the same order as before
        var (uri, addressProblem) = ApiTarget.Address(s.Endpoint);
        if (uri is null) return new(false, null, addressProblem!);
        string endpoint = s.Endpoint!.Trim();
        if (CustomApiMapping.TryFill(s.RequestTemplate, "system", "user", 16, s.Model, extras, out _) is { } templateError)
            return new(false, null, templateError);
        if (!CustomApiMapping.IsValidPath(s.ResponsePath))
            return new(false, null, "The reply path is not valid. Please use names and [numbers], for example choices[0].message.content");
        var (token, tokenProblem) = ApiTarget.Token(s.SecretEnvVar, env);
        if (tokenProblem is not null) return new(false, null, tokenProblem);
        return new(true, new ElsaSettings(endpoint, token, string.IsNullOrWhiteSpace(s.Model) ? null : s.Model.Trim(), null),
            $"{s.Name} configured · {uri.Host}");
    }

    public static IChatCompleter? CreateCompleter(CustomApiSettings s, Func<string, string?>? env = null, HttpMessageHandler? handler = null,
        IReadOnlyDictionary<string, string>? extras = null)
    {
        var inspection = Inspect(s, env, extras);
        return inspection is { Ok: true, Settings: { } es }
            ? new ElsaChatCompleter(es, new CustomApiMapping(s, extras), handler, displayName: string.IsNullOrWhiteSpace(s.Name) ? "My API" : s.Name.Trim(),
                timeoutSeconds: CustomApiSettings.TimeoutChoices.Contains(s.TimeoutSeconds) ? s.TimeoutSeconds : 30)
            : null;
    }
}

/// <summary>The settings-driven version of the two Elsa translations: build the request from a template, read the text out of the answer.</summary>
public sealed class CustomApiMapping(CustomApiSettings settings, IReadOnlyDictionary<string, string>? extras = null) : IElsaMapping
{
    public bool IsFilledIn => true;

    public HttpRequestMessage BuildRequest(ElsaSettings es, string system, string user, int maxTokens)
    {
        if (TryFill(settings.RequestTemplate, system, user, maxTokens, settings.Model, extras, out string body) is { } error)
            throw new ElsaException(error);
        var request = new HttpRequestMessage(HttpMethod.Post, es.Endpoint) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        string header = (settings.AuthHeader ?? "").Trim();
        if (es.Token is { Length: > 0 } token && header.Length > 0)
        {
            string scheme = (settings.AuthScheme ?? "").Trim();
            // TryAddWithoutValidation: a token or scheme with unusual characters must not throw a message that echoes it.
            if (!request.Headers.TryAddWithoutValidation(header, scheme.Length > 0 ? $"{scheme} {token}" : token))
                throw new ElsaException("The authentication header name is not valid.");
        }
        return request;
    }

    public string ReadReply(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var node = doc.RootElement;
            foreach (var step in Steps(settings.ResponsePath))
            {
                if (step.Index is { } i)
                {
                    if (node.ValueKind != JsonValueKind.Array || i >= node.GetArrayLength()) throw new KeyNotFoundException();
                    node = node[i];
                }
                else
                {
                    if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(step.Name!, out node)) throw new KeyNotFoundException();
                }
            }
            if (node.ValueKind != JsonValueKind.String) throw new KeyNotFoundException();
            return node.GetString() ?? "";
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException)
        {
            // Never echo the body: it can contain anything the service returned.
            throw new ElsaException($"The answer did not have text at {settings.ResponsePath}. Check the reply path in Settings.");
        }
    }

    // ---- template ----

    /// <summary>
    /// Replaces the placeholders with JSON-escaped values (so a quote or a newline in a draft can never break out of the string)
    /// and checks the result is valid JSON. Returns an error sentence, or null when <paramref name="body"/> is ready.
    /// </summary>
    public static string? TryFill(string? template, string system, string user, int maxTokens, string? model, out string body) =>
        TryFill(template, system, user, maxTokens, model, null, out body);

    private static readonly System.Text.RegularExpressions.Regex Slot = new(@"\{\{([a-z_]+)\}\}", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// As above, with optional named fields (for example number or goal) that a template can place anywhere, JSON-escaped like the rest.
    /// The template is read once, left to right, so a value that happens to contain {{something}} is never read as a placeholder.
    /// </summary>
    public static string? TryFill(string? template, string system, string user, int maxTokens, string? model,
        IReadOnlyDictionary<string, string>? extras, out string body)
    {
        body = "";
        if (string.IsNullOrWhiteSpace(template)) return "The request body is empty.";
        string Esc(string s) => JsonEncodedText.Encode(s).ToString();
        var values = new Dictionary<string, string>();
        if (extras is not null) foreach (var (k, v) in extras) values[k] = Esc(v);
        values["system"] = Esc(system); values["user"] = Esc(user); values["model"] = Esc(model ?? "");
        values["max_tokens"] = maxTokens.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string filled = Slot.Replace(template, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
        try { using var _ = JsonDocument.Parse(filled); }
        catch (JsonException) { return "The request body is not valid JSON once the placeholders are filled in."; }
        body = filled;
        return null;
    }

    // ---- reply path: names and [numbers] only, e.g. choices[0].message.content ----

    private readonly record struct Step(string? Name, int? Index);

    public static bool IsValidPath(string? path) => !string.IsNullOrWhiteSpace(path) && TrySteps(path, out _);

    private static IEnumerable<Step> Steps(string path) => TrySteps(path, out var steps) ? steps : Array.Empty<Step>();

    private static bool TrySteps(string path, out List<Step> steps)
    {
        steps = new();
        foreach (var part in path.Trim().Split('.'))
        {
            if (part.Length == 0) return false;
            int br = part.IndexOf('[');
            string name = br < 0 ? part : part[..br];
            if (name.Length > 0)
            {
                if (!name.All(c => char.IsLetterOrDigit(c) || c is '_' or '-')) return false;
                steps.Add(new Step(name, null));
            }
            else if (br != 0) return false;
            while (br >= 0)
            {
                int close = part.IndexOf(']', br);
                if (close < 0 || !int.TryParse(part[(br + 1)..close], System.Globalization.NumberStyles.None, null, out int idx)) return false;
                steps.Add(new Step(null, idx));
                br = close + 1 < part.Length ? (part[close + 1] == '[' ? close + 1 : -2) : -1;
                if (br == -2) return false;
            }
        }
        return steps.Count > 0;
    }
}
