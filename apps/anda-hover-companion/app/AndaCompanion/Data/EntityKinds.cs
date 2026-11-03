using System.Text.RegularExpressions;

namespace AndaCompanion.Data;

/// <summary>
/// One recognizable identifier type. The app is deliberately NOT hard-wired to
/// ANDAs: the card's anatomy (identity → clock → attention → latest → ahead →
/// brief) fits any tracked submission with a goal date, so the recognizer is a
/// registry rather than a constant.
///
/// To serve a new domain, add an <see cref="EntityKind"/> and point the repository
/// at that data — the UI, consent flow, forecast rules, and companion grounding
/// are all kind-agnostic.
/// </summary>
public sealed record EntityKind(string Label, string Pattern, string Description)
{
    public Regex Compiled { get; } = new(Pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
}

public static class EntityKinds
{
    /// <summary>
    /// Six digits, optionally prefixed "ANDA" — the default domain. A bare six-digit number is
    /// accepted (people select just the number), BUT a number that is explicitly labelled as
    /// another kind of application or file — NDA, BLA, IND, DMF, MF, NDC — is rejected: NDA and
    /// DMF numbers are also six digits and can coincide with a real ANDA number, and showing the
    /// wrong application's card is worse than showing none.
    /// </summary>
    public static readonly EntityKind Anda =
        new("ANDA",
            @"(?:ANDA[-\s#]?)?(?<![0-9])(?<!\b(?:NDA|BLA|IND|DMF|NDC|MF)(?:[-\s#:.]|No(?![A-Za-z])|Number(?![A-Za-z]))*)([0-9]{6})(?![0-9])",
            "Abbreviated New Drug Application");

    /// <summary>
    /// Additional kinds are registered here as the tool expands. Each is opt-in:
    /// an unregistered pattern is never recognized, so broadening scope can never
    /// silently increase false positives.
    /// </summary>
    public static readonly IReadOnlyList<EntityKind> Registered = new[] { Anda };

    /// <summary>
    /// Scans captured text for the first match among registered kinds, in registration
    /// order. Returns the kind and the bare identifier, or null when nothing matches.
    /// </summary>
    public static (EntityKind Kind, string Id)? Recognize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        foreach (var kind in Registered)
        {
            var m = kind.Compiled.Match(text);
            if (m.Success) return (kind, m.Groups[1].Value);
        }
        return null;
    }
}
