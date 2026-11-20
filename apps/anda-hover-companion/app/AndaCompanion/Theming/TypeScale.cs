namespace AndaCompanion.Theming;

/// <summary>
/// Round 35: one type scale of six named sizes for every window, in place of sixteen sizes with half steps (9 to 38). Weight carries
/// emphasis, not half a point. Larger text (Settings, Look and feel) enlarges these six together. A test reads every window's source and
/// fails on any other size; the only exception is a hidden live region (size 1, not drawn) that only a screen reader reads.
/// </summary>
public static class TypeScale
{
    public const double Caption = 11, Footnote = 12, Body = 13, Callout = 15, Title = 20, Display = 34;
    public static readonly IReadOnlyList<(string Name, double Size)> All = new[]
    {
        ("Caption", Caption), ("Footnote", Footnote), ("Body", Body), ("Callout", Callout), ("Title", Title), ("Display", Display),
    };

    /// <summary>Where an older size went: up to 11 Caption, to 12 Footnote, to 13.5 Body, to 16 Callout, to 24 Title, above it Display.</summary>
    public static double Of(double old) => old <= 11 ? Caption : old <= 12 ? Footnote : old <= 13.5 ? Body : old <= 16 ? Callout : old <= 24 ? Title : Display;
}
