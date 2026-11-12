namespace AndaCompanion.Services;

/// <summary>
/// The days to the goal date, as look X shows them beside the answer (round 24). The style follows the distance and the words say the
/// same, so color is never the only signal. The simulation has the same rule; mockup/countdown-golden.json holds the cases both must
/// answer alike. The thresholds (30 and 7 days) were proposed in round 23 and accepted with the look.
/// </summary>
public static class GoalCountdown
{
    public enum Level { Calm, Near, Soon, Today, Passed }

    public static Level LevelOf(int days) => days < 0 ? Level.Passed : days == 0 ? Level.Today : days <= 7 ? Level.Soon : days <= 30 ? Level.Near : Level.Calm;

    private static string Plural(long n, string word) => $"{n.ToString(System.Globalization.CultureInfo.InvariantCulture)} {word}{(n == 1 ? "" : "s")}";

    /// <summary>What a screen reader hears, and what the picture means: "79 days to the goal date".</summary>
    public static string Words(int days) => days < 0 ? $"The goal date passed {Plural(-(long)days, "day")} ago"
        : days == 0 ? "The goal date is today" : $"{Plural(days, "day")} to the goal date";

    /// <summary>The large figure: the number of days either way, or "Today".</summary>
    public static string Figure(int days) => days == 0 ? "Today" : Math.Abs((long)days).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The small line under the figure.</summary>
    public static string Caption(int days) => days > 0 ? (days == 1 ? "DAY TO GOAL" : "DAYS TO GOAL") : days == 0 ? "IS THE GOAL DATE" : (days == -1 ? "DAY PAST GOAL" : "DAYS PAST GOAL");

    /// <summary>Ink, fill and edge for a level, as hex colors; null fill means none. White on the two solid fills and the amber inks on their
    /// grounds all meet 4.5:1 (the tests measure them).</summary>
    public static (string Ink, string? Fill, string Edge, bool Heavy) Style(Level level, string calmInk, string calmEdge) => level switch
    {
        Level.Near => ("#8A5800", null, "#C9A45C", false),
        Level.Soon => ("#7A4D00", "#FAF1DC", "#8A5800", true),
        Level.Today => ("#FFFFFF", "#8A5800", "#8A5800", true),
        Level.Passed => ("#FFFFFF", "#A33636", "#A33636", true),
        _ => (calmInk, null, calmEdge, false),
    };
}
