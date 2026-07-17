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
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AndaCompanion", "prompts.json");

    private List<LensPrompt> _prompts = new();

    public IReadOnlyList<LensPrompt> Prompts => _prompts;

    public PromptLibraryService()
    {
        try
        {
            if (File.Exists(Path))
                _prompts = JsonSerializer.Deserialize<List<LensPrompt>>(File.ReadAllText(Path)) ?? new();
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
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(_prompts, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best effort */ }
    }
}
