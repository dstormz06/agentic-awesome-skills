namespace AndaCompanion.Services;

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
