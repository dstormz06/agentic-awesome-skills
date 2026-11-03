using System.Text.Json;

namespace AndaCompanion.Services;

public sealed record LensPrompt(string Name, string Text);

/// <summary>
/// The Prompt Library: user-authored "lenses" that steer the companion brief
/// ("focus on facility risks", "write it for my division director", …).
/// Lenses are guidance, never authority — the grounding rules in the system
/// prompt always outrank them (see SECURITY.md). Stored as plain JSON in
/// AppData so users can review exactly what the agent is being told.
/// </summary>
public sealed class PromptLibraryService
{
    private readonly string _path;
    private List<LensPrompt> _prompts = new();

    public IReadOnlyList<LensPrompt> Prompts => _prompts;

    public PromptLibraryService(string? path = null)
    {
        _path = path ?? AppPaths.PromptsFile;
        try
        {
            if (File.Exists(_path))
                _prompts = JsonSerializer.Deserialize<List<LensPrompt>>(File.ReadAllText(_path)) ?? new();
        }
        catch { _prompts = new(); }

        if (_prompts.Count == 0)
        {
            _prompts = new()
            {
                new("Facility focus",
                    "Weight facility and inspection signals heaviest: shared sites, unresolved findings, " +
                    "inspection assumptions. Name the facility linkage explicitly when one exists."),
                new("Director summary",
                    "Write for a division director skimming between meetings: lead with the forecast delta " +
                    "and the single decision or risk that needs their awareness. No review-discipline jargon."),
                new("First-cycle watch",
                    "Emphasize anything that threatens first-cycle approval: response quality history, " +
                    "unresolved deficiencies, and assumptions that have not been pressure-tested."),
            };
            Save();
        }
    }

    public const int MaxLenses = 100, MaxNameChars = 80, MaxTextChars = 4000;

    /// <summary>The three lenses a new install starts with.</summary>
    public static IReadOnlyList<LensPrompt> Starters { get; } = new LensPrompt[]
    {
        new("Facility focus",
            "Weight facility and inspection signals heaviest: shared sites, unresolved findings, " +
            "inspection assumptions. Name the facility linkage explicitly when one exists."),
        new("Director summary",
            "Write for a division director skimming between meetings: lead with the forecast delta " +
            "and the single decision or risk that needs their awareness. No review-discipline jargon."),
        new("First-cycle watch",
            "Emphasize anything that threatens first-cycle approval: response quality history, " +
            "unresolved deficiencies, and assumptions that have not been pressure-tested."),
    };

    /// <summary>Replaces every lens (a restored backup, or a reset). Entries without a name or over the limits are dropped.</summary>
    public void ReplaceAll(IEnumerable<LensPrompt> prompts)
    {
        _prompts = prompts.Where(p => !string.IsNullOrWhiteSpace(p?.Name) && p.Text is not null && p.Name.Length <= MaxNameChars && p.Text.Length <= MaxTextChars)
            .GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase).Select(g => new LensPrompt(g.Key, g.Last().Text)).Take(MaxLenses).ToList();
        Save();
    }

    public void Upsert(LensPrompt prompt)
    {
        _prompts.RemoveAll(p => p.Name.Equals(prompt.Name, StringComparison.OrdinalIgnoreCase));
        _prompts.Add(prompt);
        Save();
    }

    public void Delete(string name)
    {
        _prompts.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    public string? GetText(string? name) =>
        name is null ? null
        : _prompts.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Text;

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_prompts, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best effort */ }
    }
}
