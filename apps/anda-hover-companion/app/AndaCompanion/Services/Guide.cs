namespace AndaCompanion.Services;

/// <summary>One step of the guide as it is shown: its place, its title, its lines, its own button if it has one, and the three buttons below.</summary>
public sealed record GuideView(int Index, string Place, string Title, IReadOnlyList<string> Lines, string? Action, bool Back, string Next, string Leave);

/// <summary>
/// First steps: a guide of five short steps. It opens by itself once, at the first start, can be left at any step, and is opened again from
/// the tray menu (Help: first steps). The same words as the simulation's (mockup/round32-golden.json).
/// </summary>
public static class Guide
{
    public const string ShowSample = "Show the sample card", OpenSettings = "Open Settings", OpenConnections = "Open Connections";

    public static readonly IReadOnlyList<(string Title, string? Action, IReadOnlyList<string> Lines)> Steps = new (string, string?, IReadOnlyList<string>)[]
    {
        ("What this is", null, new[]
        {
            "ANDA Companion (Notula) shows a short card for an ANDA number you select in an email or a document: the goal date, the forecast, what needs you, and your notes.",
            "It starts with sample data so that you can try it. Every sample record is fictional, and its card says so.",
            "Nothing leaves this PC unless you switch a connection on, or press a button that says it goes online.",
        }),
        ("Try it on a sample", ShowSample, new[]
        {
            "Select an ANDA number in any window, then press {shortcut}. Or press the button below to open the card of the sample ANDA 216482.",
            "On the card, Enter starts a note of your own and Esc closes the card.",
        }),
        ("Use your own data", OpenSettings, new[]
        {
            "In Settings, under “My files and writing”, choose your portfolio file (Excel or CSV). The card then shows your ANDAs, and says where the data is from and how old it is.",
            "The file is read on this PC. Nothing from it is sent anywhere unless you switch a connection on, and the app copies it only if you ask it to keep a weekly copy.",
        }),
        ("What is switched on", OpenConnections, new[]
        {
            "Settings and the app's other large windows say at their foot what is switched on. At the moment: {connections}",
            "No AI is used until you choose a connection in Settings. “Connections and event log” lists what each connection sends, where to, and what is kept.",
        }),
        ("Where things are", null, new[]
        {
            "Click the tray icon near the clock to open Today: what needs a look, the ANDAs you keep, and the recent ones. Right-click it for the menu: Lookup, Today, Review a draft, Ask, Settings, Help, and More for the rest, such as Connections and event log, and Backups and archive.",
            "This guide is there as well, under “Help: first steps”. Notices and the tray dot appear only while the app is running.",
        }),
    };

    /// <summary>One step as it is shown, with the person's own shortcut and what is switched on written in. A step outside the five is the nearest of them.</summary>
    public static GuideView Step(double? i, string? shortcut, string? connections)
    {
        int n = Steps.Count;
        double asked = i is { } v && !double.IsNaN(v) ? Math.Floor(v) : 0;
        int k = (int)Math.Min(Math.Max(0, asked), n - 1);
        string key = string.IsNullOrWhiteSpace(shortcut) ? "the shortcut you chose in Settings" : shortcut.Trim();
        string on = string.IsNullOrWhiteSpace(connections) ? "Offline · Local only" : connections.Trim();
        if (!(on.EndsWith('.') || on.EndsWith('!') || on.EndsWith('?'))) on += ".";
        var (title, action, lines) = Steps[k];
        // One pass: what is written in is never read for marks itself.
        string Fill(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\{(shortcut|connections)\}", m => m.Groups[1].Value == "shortcut" ? key : on);
        return new GuideView(k, $"Step {k + 1} of {n}", title, lines.Select(Fill).ToList(), action, k > 0, k < n - 1 ? "Next" : "Done", k < n - 1 ? "Skip the guide" : "Close");
    }

    /// <summary>How step 2 names the shortcut. One that another program holds does not work, and the guide must not teach it.</summary>
    public static string Shortcut(string? key, bool? working)
    {
        string k = (key ?? "").Trim();
        return k.Length == 0 ? "" : working == false ? $"your shortcut ({k} is used by another program at the moment: please choose another in Settings)" : k;
    }

    /// <summary>What the guide says after one of its three buttons was pressed, and at a first start that could not be remembered.</summary>
    public const string SaidSample = "The card of the sample ANDA 216482 was opened. It is fictional, and its card says so. Esc closes it.";
    public const string SaidSampleNoNotes = "The card of the sample ANDA 216482 was opened. It is fictional, and its card says so. Notes are switched off on it, because your own data is loaded. Esc closes it.";
    public const string SaidSettings = "Settings was opened at “My files and writing”, in front of this guide.";
    public const string SaidConnections = "“Connections and event log” was opened, in front of this guide.";
    public const string SaidNotRemembered = "This PC did not let the app remember that the guide was shown, so it will open again at the next start. You can also open it from the tray menu: Help: first steps.";

    /// <summary>The guide opens by itself once, at the first start, and never again by itself.</summary>
    public static bool OpensAtStart(bool? seen) => seen != true;
}
