using System.Text.Json;
using AndaCompanion.Theming;

namespace AndaCompanion.Services;

public enum ConsentModel
{
    Full,       // hotkey opens the full card (the hotkey itself is the consent) — DESIGN.md model ①
    PeekFirst,  // hotkey opens the compact peek; a second act expands (no AI call until then)
    Whisper,    // hotkey shows a small invite chip; the card opens only if it is clicked
}

public sealed class Settings
{
    public AppearanceId Appearance { get; set; } = Appearances.Default;   // K · Graphite Quiet is the product default

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
    public string DraftHotkey { get; set; } = "Ctrl+Alt+D";

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

    /// <summary>Which connection generated text uses. Auto = decided by the environment, as before.</summary>
    public ProviderChoice Provider { get; set; } = ProviderChoice.Auto;

    /// <summary>The person's own API, described here instead of in code. Used only when Provider is MyApi.</summary>
    public CustomApiSettings CustomApi { get; set; } = new();

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

    /// <summary>Card surface transparency, 0.85–1.0. Text always stays opaque.</summary>
    public double CardOpacity { get; set; } = 1.0;

    /// <summary>Name of the active Prompt Library lens (null = no lens).</summary>
    public string? ActiveLens { get; set; }

    /// <summary>
    /// Select &amp; Peek reads the selection by sending Ctrl+C to the app in front.
    /// Off: Ctrl+Alt+A opens the lookup box instead and no keystroke is ever sent.
    /// </summary>
    public bool CaptureViaCopy { get; set; } = true;

    public bool FirstRunDone { get; set; }
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
                var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings();
                loaded.AutoHideSeconds = UI.AutoHidePolicy.Normalize(loaded.AutoHideSeconds);
                loaded.UrgentLateDays = UrgencyPolicy.Normalize(loaded.UrgentLateDays);
                if (!Settings.HolidayLeadChoices.Contains(loaded.HolidayLeadDays)) loaded.HolidayLeadDays = 3;
                if (!NativeHostRegistration.IsValidExtensionId(loaded.ExtensionId)) loaded.ExtensionId = NativeHostRegistration.DefaultExtensionId;
                loaded.PortfolioColumns ??= new();
                loaded.StandingInstructions ??= "";
                loaded.Tone = Settings.ToneChoices.Contains(loaded.Tone ?? "") ? loaded.Tone ?? "" : "";
                loaded.SignOff ??= "";
                loaded.CustomApi ??= new();
                loaded.CustomApi.RequestTemplate = string.IsNullOrWhiteSpace(loaded.CustomApi.RequestTemplate) ? CustomApiSettings.ExampleTemplate : loaded.CustomApi.RequestTemplate;
                if (!Enum.IsDefined(loaded.Provider)) loaded.Provider = ProviderChoice.Auto;
                if (!Settings.MiddleClickModifiers.Contains(loaded.MiddleClickModifier ?? "")) loaded.MiddleClickModifier = "Ctrl";
                if (HotkeyParser.ValidateSet(("Select & Peek", loaded.PeekHotkey), ("Lookup", loaded.LookupHotkey), ("Check a draft", loaded.DraftHotkey)) is not null)
                { loaded.PeekHotkey = "Ctrl+Alt+A"; loaded.LookupHotkey = "Ctrl+Alt+Space"; loaded.DraftHotkey = "Ctrl+Alt+D"; }
                if (!Enum.IsDefined(loaded.Appearance)) loaded.Appearance = Appearances.Default;
                return loaded;
            }
        }
        catch { /* corrupted settings → defaults */ }
        return new Settings();
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
