namespace AndaCompanion.Services;

/// <summary>
/// Text size and spacing (round 18 in the simulation, round 33 here). Text size applies to the card and to this app's windows; spacing
/// applies to the card. Neither changes other programs. The factors are the simulation's.
/// </summary>
public static class Display
{
    public static readonly IReadOnlyList<(string Key, string Label)> TextSizes = new[] { ("standard", "Standard text"), ("large", "Larger text"), ("xl", "Largest text") };
    public static readonly IReadOnlyList<(string Key, string Label)> Densities = new[] { ("comfortable", "Comfortable spacing"), ("compact", "Compact spacing") };
    public const string Hint = "Text size applies to the card and to this app's windows; spacing applies to the card. Neither changes other programs.";

    public static string TextSize(string? v) => TextSizes.Any(t => t.Key == v) ? v! : "standard";
    public static string Density(string? v) => Densities.Any(t => t.Key == v) ? v! : "comfortable";

    /// <summary>How much larger text is drawn: 1, 1.15 or 1.3.</summary>
    public static double TextScale(string? textSize) => TextSize(textSize) switch { "large" => 1.15, "xl" => 1.3, _ => 1.0 };

    /// <summary>How much of the card's vertical spacing is kept: 1, or 0.72 when compact.</summary>
    public static double Spacing(string? density) => Density(density) == "compact" ? 0.72 : 1.0;
}
