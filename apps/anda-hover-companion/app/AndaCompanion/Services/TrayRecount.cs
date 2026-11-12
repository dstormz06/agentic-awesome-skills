namespace AndaCompanion.Services;

/// <summary>
/// When the tray icon is counted again on a schedule, as the person chose in Settings (round 23). The count always happens when the app
/// reads data; this adds a scheduled count, which looks at the portfolio file once, on this PC, and sends nothing anywhere.
/// </summary>
public static class TrayRecount
{
    public const string OnUse = "use";        // only when the person uses the app
    public const string Daily = "daily";      // also once a day, just after midnight (the default)
    public const string Hourly = "hourly";    // also at each hour

    public static readonly IReadOnlyList<(string Id, string Label)> Choices = new[]
    {
        (OnUse, "Only when I use the app"),
        (Daily, "Also once a day, just after midnight"),
        (Hourly, "Also every hour"),
    };

    public static string Normalize(string? mode) => Choices.Any(c => c.Id == mode) ? mode! : Daily;

    /// <summary>
    /// How long to wait, from this local time, until the next scheduled count; null when there is none. The count is taken one minute after
    /// the boundary, so that "today" has certainly changed. Local wall-clock arithmetic is deliberate: a day with a clock change is an hour
    /// longer or shorter, and the timer is set again after each count, so an early or late count corrects itself at the next one.
    /// </summary>
    public static TimeSpan? Next(DateTime localNow, string? mode)
    {
        switch (Normalize(mode))
        {
            case OnUse: return null;
            case Hourly:
            {
                var next = new DateTime(localNow.Year, localNow.Month, localNow.Day, localNow.Hour, 1, 0, localNow.Kind);
                if (next <= localNow) next = next.AddHours(1);
                return next - localNow;
            }
            default:
            {
                var next = localNow.Date.AddMinutes(1);
                if (next <= localNow) next = next.AddDays(1);
                return next - localNow;
            }
        }
    }
}
