using System.Text.Json;
using AndaCompanion.Theming;

namespace AndaCompanion.Services;

public enum ConsentModel
{
    Full,       // hotkey opens the full card (the hotkey itself is the consent) — DESIGN.md model ①
    PeekFirst,  // hotkey opens the compact peek; a second act expands (no AI call until then)
    Whisper,    // hotkey shows a small invite chip; the card opens only if it is clicked
}

/// <summary>What the to-do shortcut does with highlighted notes.</summary>
public enum MyTodoMode { Review = 0, AddAtOnce = 1 }

public sealed class Settings
{
    public AppearanceId Appearance { get; set; } = Appearances.Default;   // T · Pocket Plain is the product default

    /// <summary>
    /// Hide a card or compact box after this many idle seconds (0 = never). Pointer over it, a pinned card and an
    /// expanded drawer all count as "in use" and are never hidden. See <see cref="UI.AutoHidePolicy"/>.
    /// </summary>
    public int AutoHideSeconds { get; set; } = UI.AutoHidePolicy.Default;

    // Whisper = DESIGN.md consent model ③: a small invite, and the card opens only if it is clicked. It is the default
    // since 2026-11-02 (tester feedback). Urgent ANDAs skip the invite and open a peek (see EscalateUrgent).
    public ConsentModel Consent { get; set; } = ConsentModel.Whisper;

    /// <summary>
    /// In whisper mode, an urgent or at-risk ANDA opens as a peek instead of waiting for a click. Still started by the user's
    /// own selection and hotkey; nothing ever opens unprompted. Peek makes no AI call.
    /// </summary>
    public bool EscalateUrgent { get; set; } = true;

    /// <summary>A forecast at least this many days after the goal counts as at risk (3, 7 or 14).</summary>
    public int UrgentLateDays { get; set; } = UrgencyPolicy.DefaultLateDays;

    /// <summary>Warn about a goal date within this many business days before or after a federal holiday (0 turns it off).</summary>
    public int HolidayLeadDays { get; set; } = 3;
    public static readonly IReadOnlyList<int> HolidayLeadChoices = new[] { 0, 2, 3, 5 };

    // ---- shortcuts (Settings, Triggers) ----
    public string PeekHotkey { get; set; } = "Ctrl+Alt+A";
    public string LookupHotkey { get; set; } = "Ctrl+Alt+Space";
    // Review a draft and My to-do list moved off Ctrl+Alt+D and Ctrl+Alt+L in round 19 for the reason given below (Word uses both). A saved choice is kept.
    public string DraftHotkey { get; set; } = "Ctrl+Alt+Shift+R";

    // ---- ask about a selection: select text or cells, press this, choose a stored prompt or a quick check ----
    // Three modifiers on purpose: Word, Outlook and Excel use many Ctrl+Alt+letter shortcuts (Ctrl+Alt+M is Word's New comment, Ctrl+Alt+D its Insert endnote, Ctrl+Alt+L its list-number field), and a
    // shortcut this app registers is taken from them. Ctrl+Alt+Shift+letter is far less used. It can be changed or removed under Settings.
    public const string DefaultAskHotkey = "Ctrl+Alt+Shift+P";

    /// <summary>Select text or cells, press this, and Ask opens on the selection. Empty means no shortcut (the tray menu still works).</summary>
    public string AskHotkey { get; set; } = DefaultAskHotkey;

    /// <summary>A saved or restored Ask shortcut, made valid: unreadable becomes the default, one that clashes with another shortcut becomes none.</summary>
    public static string RepairAskHotkey(string? saved, IEnumerable<string> others)
    {
        if (saved is not null && saved.Trim().Length == 0) return "";
        string candidate = HotkeyParser.TryParse(saved, out _, out _, out string norm, out _) ? norm : DefaultAskHotkey;
        foreach (var other in others)
            if (HotkeyParser.TryParse(other, out _, out _, out string n, out _) && n == candidate) return "";
        return candidate;
    }

    // ---- my to-do list (Settings, My to-do list): a separate feature for anything, with its own shortcut ----
    public const string DefaultMyTodoHotkey = "Ctrl+Alt+Shift+L";

    /// <summary>Highlight text, press this, and the notes become suggested to-do items. Empty means no shortcut (the tray menu still works).</summary>
    public string MyTodoHotkey { get; set; } = DefaultMyTodoHotkey;

    /// <summary>What the to-do shortcut does with highlighted notes: show the items first (the default), or add them at once.</summary>
    public MyTodoMode MyTodoMode { get; set; } = MyTodoMode.Review;

    /// <summary>A saved or restored to-do shortcut, made valid: unreadable becomes the default, one that clashes with another shortcut becomes none.</summary>
    public static string RepairMyTodoHotkey(string? saved, IEnumerable<string> others)
    {
        if (saved is not null && saved.Trim().Length == 0) return "";
        string candidate = HotkeyParser.TryParse(saved, out _, out _, out string norm, out _) ? norm : DefaultMyTodoHotkey;
        foreach (var other in others)
            if (HotkeyParser.TryParse(other, out _, out _, out string n, out _) && n == candidate) return "";
        return candidate;
    }

    // ---- my data (Settings, My data): the RPM's own files, read-only and local ----
    public string? PortfolioPath { get; set; }
    public Dictionary<string, string> PortfolioColumns { get; set; } = new();
    public string? NotesFolder { get; set; }

    /// <summary>A Drugs@FDA zip the person downloaded themselves. Read locally for the RLD labeling date; the app downloads nothing.</summary>
    public string? ReferenceDataPath { get; set; }

    /// <summary>Standing instructions added to every writing action (proofread, rewrite, fact-check), in the RPM's own words.</summary>
    public string StandingInstructions { get; set; } = "";

    /// <summary>Preferred tone for writing actions: "", Plain, Formal, Warm or Direct. A style preference only; it can never change a fact.</summary>
    public string Tone { get; set; } = "";
    public static readonly IReadOnlyList<string> ToneChoices = new[] { "", "Plain", "Formal", "Warm", "Direct" };

    /// <summary>How the person signs a message, used by the writing actions when they rewrite one. Empty = no sign-off added.</summary>
    public string SignOff { get; set; } = "";

    /// <summary>The Claude model name used when Claude is the connection. Editable in Settings so a change of approved model needs no rebuild.</summary>
    public string ClaudeModel { get; set; } = DefaultClaudeModel;
    public const string DefaultClaudeModel = "claude-opus-4-8";
    public static bool IsValidModelName(string? name) => !string.IsNullOrWhiteSpace(name) && System.Text.RegularExpressions.Regex.IsMatch(name.Trim(), "^[A-Za-z0-9][A-Za-z0-9._:/-]{0,79}$");

    /// <summary>Which connection generated text uses. Auto = decided by the environment, as before.</summary>
    public ProviderChoice Provider { get; set; } = ProviderChoice.Auto;

    /// <summary>The person's own API, described here instead of in code. Used only when Provider is MyApi.</summary>
    public CustomApiSettings CustomApi { get; set; } = new();

    /// <summary>The optional read-only Outlook connection. Off by default; holds no secret (the sign-in token is never saved).</summary>
    public OutlookSettings Outlook { get; set; } = new();

    /// <summary>
    /// Start Select &amp; Peek from a middle-click (the wheel button) on selected text. Off by default: it needs a Windows mouse hook,
    /// which some security software flags. Never swallows the click.
    /// </summary>
    public bool MiddleClickTrigger { get; set; }

    /// <summary>A key that must be held with the middle-click: None, Ctrl, Shift or Alt. Ctrl is recommended: a plain middle-click scrolls and opens links.</summary>
    public string MiddleClickModifier { get; set; } = "Ctrl";
    public static readonly IReadOnlyList<string> MiddleClickModifiers = new[] { "None", "Ctrl", "Shift", "Alt" };

    /// <summary>An https address the person pastes to fetch a newer Drugs@FDA zip when they press Update. Never fetched automatically.</summary>
    public string? ReferenceDataUrl { get; set; }

    /// <summary>The standing instructions plus tone and sign-off, as one block for the writing actions. Facts are never up for change.</summary>
    public string WritingInstructions()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Tone) && ToneChoices.Contains(Tone)) parts.Add($"Tone: {Tone.ToLowerInvariant()}.");
        if (!string.IsNullOrWhiteSpace(StandingInstructions)) parts.Add(StandingInstructions.Trim());
        if (!string.IsNullOrWhiteSpace(SignOff)) parts.Add($"When you write or rewrite a whole message, end it with this sign-off exactly: {SignOff.Trim()}");
        return string.Join("\n", parts);
    }

    /// <summary>A local usage log: date, event and seconds, never an ANDA number or any text. Off by default.</summary>
    public bool UsageLog { get; set; }

    /// <summary>The Edge extension allowed to talk to this app (its 32-letter ID). Defaults to the extension in this repository.</summary>
    public string ExtensionId { get; set; } = NativeHostRegistration.DefaultExtensionId;

    /// <summary>Card surface opacity, from <see cref="MinCardOpacity"/> (40%) to 1.0 (solid). Text always stays opaque.</summary>
    public double CardOpacity { get; set; } = DefaultCardOpacity;

    /// <summary>The lowest card opacity the slider offers: 40%. A saved or restored value outside 40–100% is brought back into range.</summary>
    public const double MinCardOpacity = 0.40;

    /// <summary>What a new install, and "Reset all settings", starts with: 92%, so the card sits lightly on the screen. An install that already saved a choice keeps it.</summary>
    public const double DefaultCardOpacity = 0.92;

    /// <summary>Brings any value into 40–100%. A value that is not a number becomes solid (1.0).</summary>
    public static double NormalizeOpacity(double value) => double.IsNaN(value) || double.IsInfinity(value) ? 1.0 : Math.Clamp(value, MinCardOpacity, 1.0);

    /// <summary>Name of the active Prompt Library lens (null = no lens).</summary>
    public string? ActiveLens { get; set; }

    /// <summary>
    /// Select &amp; Peek reads the selection by sending Ctrl+C to the app in front.
    /// Off: Ctrl+Alt+A opens the lookup box instead and no keystroke is ever sent.
    /// </summary>
    public bool CaptureViaCopy { get; set; } = true;

    // ---- my actions, my agents and my to-do list (Settings, My actions) ----

    /// <summary>The person's own actions: a prompt, an agent, or a to-do item, each reachable from its own shortcut and the tray menu.</summary>
    public List<CustomAction> MyActions { get; set; } = MyActionRules.Starters();

    /// <summary>Agents the person registered (for example Alert Hub, Patent Analysis and Triaging Agent). Each is off until it is switched on and set up.</summary>
    public List<AgentSettings> Agents { get; set; } = AgentRules.Presets();

    /// <summary>Include items that come from the person's own data (at-risk ANDAs, dates, action items) in the to-do list.</summary>
    public bool TodoShowFromData { get; set; } = true;

    /// <summary>While the to-do list is open it refreshes itself this often (0 = only when asked); a changed portfolio file is read at the next refresh.</summary>
    public int TodoRefreshSeconds { get; set; } = 30;
    public static readonly IReadOnlyList<int> TodoRefreshChoices = new[] { 0, 15, 30, 60, 300 };

    public bool FirstRunDone { get; set; }

    // ---- this window arranges itself (Settings, Maintenance) ----

    /// <summary>Settings open the sections that need a look or that the person opens often, and collapse the rest.</summary>
    public bool AdaptiveSettings { get; set; } = true;

    /// <summary>
    /// The product is focused on three things (look up an ANDA, review a draft, ask about a selection). Everything else (the two to-do lists,
    /// agents, the other looks) stays out of sight until this is switched on under Maintenance. Off by default; nothing is lost by leaving it off.
    /// </summary>
    public bool ShowAllTools { get; set; }

    /// <summary>How often each Settings section was opened (section name to count). Nothing else is kept: no text and no numbers.</summary>
    public Dictionary<string, int> SectionUse { get; set; } = new();
}

/// <summary>
/// What the focused product shows. With "Show all tools" off the card is always drawn solid and in the default look, and the shortcut for
/// My to-do list is not taken from other programs; the chosen look and transparency are kept and come back when all tools are shown.
/// </summary>
public static class Edition
{
    public static AppearanceId Appearance(Settings s) => s.ShowAllTools ? s.Appearance : Appearances.Default;
    public static double Opacity(Settings s) => s.ShowAllTools ? s.CardOpacity : 1.0;
}

public static class SettingsService
{
    public static Settings Load(string? path = null)
    {
        path ??= AppPaths.SettingsFile;
        try
        {
            if (File.Exists(path))
            {
                return Normalize(JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings());
            }
        }
        catch { /* corrupted settings → defaults */ }
        return new Settings();
    }

    /// <summary>Repairs anything out of range or unknown to the defaults. Used for a saved file and for a restored backup alike.</summary>
    public static Settings Normalize(Settings loaded)
    {
        loaded.AutoHideSeconds = UI.AutoHidePolicy.Normalize(loaded.AutoHideSeconds);
        loaded.UrgentLateDays = UrgencyPolicy.Normalize(loaded.UrgentLateDays);
        if (!Settings.HolidayLeadChoices.Contains(loaded.HolidayLeadDays)) loaded.HolidayLeadDays = 3;
        if (!NativeHostRegistration.IsValidExtensionId(loaded.ExtensionId)) loaded.ExtensionId = NativeHostRegistration.DefaultExtensionId;
        loaded.PortfolioColumns ??= new();
        loaded.StandingInstructions ??= "";
        loaded.Tone = Settings.ToneChoices.Contains(loaded.Tone ?? "") ? loaded.Tone ?? "" : "";
        loaded.SignOff ??= "";
        loaded.ClaudeModel = Settings.IsValidModelName(loaded.ClaudeModel) ? loaded.ClaudeModel.Trim() : Settings.DefaultClaudeModel;
        loaded.Outlook = OutlookSettings.Normalize(loaded.Outlook);
        loaded.CustomApi ??= new();
        loaded.CustomApi.RequestTemplate = string.IsNullOrWhiteSpace(loaded.CustomApi.RequestTemplate) ? CustomApiSettings.ExampleTemplate : loaded.CustomApi.RequestTemplate;
        if (!Enum.IsDefined(loaded.Provider)) loaded.Provider = ProviderChoice.Auto;
        if (!Settings.MiddleClickModifiers.Contains(loaded.MiddleClickModifier ?? "")) loaded.MiddleClickModifier = "Ctrl";
        if (HotkeyParser.ValidateSet(("Select & Peek", loaded.PeekHotkey), ("Lookup", loaded.LookupHotkey), ("Review a draft", loaded.DraftHotkey)) is not null)
        { loaded.PeekHotkey = "Ctrl+Alt+A"; loaded.LookupHotkey = "Ctrl+Alt+Space"; loaded.DraftHotkey = "Ctrl+Alt+Shift+R"; }
        if (!Enum.IsDefined(loaded.Appearance)) loaded.Appearance = Appearances.Default;
        loaded.CardOpacity = Settings.NormalizeOpacity(loaded.CardOpacity);
        if (!Enum.IsDefined(loaded.MyTodoMode)) loaded.MyTodoMode = MyTodoMode.Review;
        loaded.AskHotkey = Settings.RepairAskHotkey(loaded.AskHotkey, new[] { loaded.PeekHotkey, loaded.LookupHotkey, loaded.DraftHotkey });
        loaded.MyTodoHotkey = Settings.RepairMyTodoHotkey(loaded.MyTodoHotkey, new[] { loaded.PeekHotkey, loaded.LookupHotkey, loaded.DraftHotkey, loaded.AskHotkey });
        loaded.MyActions = MyActionRules.Normalize(loaded.MyActions, new[] { loaded.PeekHotkey, loaded.LookupHotkey, loaded.DraftHotkey, loaded.AskHotkey, loaded.MyTodoHotkey });
        loaded.Agents = AgentRules.Normalize(loaded.Agents);
        if (!Settings.TodoRefreshChoices.Contains(loaded.TodoRefreshSeconds)) loaded.TodoRefreshSeconds = 30;
        loaded.SectionUse = SettingsAdvisor.CleanUse(loaded.SectionUse);
        return loaded;
    }

    public static void Save(Settings s, string? path = null)
    {
        path ??= AppPaths.SettingsFile;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best effort */ }
    }
}
