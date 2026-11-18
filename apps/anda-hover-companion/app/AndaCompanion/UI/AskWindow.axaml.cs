using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

/// <summary>What the Ask window needs from the running app. Everything is a callback, so the window can be tested on its own.</summary>
public sealed record AskHooks(
    Func<IReadOnlyList<CustomAction>> Prompts, Action<CustomAction, string> RunPrompt, Action<string> ReviewDraft, Action<string> Lookup)
{
    /// <summary>Round 26, all optional: which ANDAs of the person's data a text names; adding a note to one of them; today's date; saving an invitation file.</summary>
    public Func<string, IReadOnlyList<string>>? KnownAndas { get; init; }
    public Func<string, string, (bool Ok, string Message)>? AddNote { get; init; }
    public Func<DateOnly>? Today { get; init; }
    public Func<string, Task<bool>>? SaveInvitation { get; init; }
}

/// <summary>
/// "Ask about a selection": the person selects text or spreadsheet cells and presses the Ask shortcut. The window shows what was selected, says in
/// words what it looks like and which tool suits it, offers quick checks that run on this PC with fixed rules (no AI, nothing sent), and offers the
/// person's stored prompts or an instruction typed here. A prompt is filled in and shown first; nothing is sent unless the person presses Send in
/// the window that opens, and only to the connection set up in Settings. A suggestion is only ever a button: nothing runs by itself.
/// </summary>
public partial class AskWindow : Window
{
    private readonly AskHooks _hooks;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private List<CustomAction> _prompts = new();
    private Action? _suggested;
    private string _analysedText = "";      // the text the window last read; a change notice for the same text is not a change

    public AskWindow(AskHooks hooks, string initialText)
    {
        _hooks = hooks;
        InitializeComponent();

        string text = initialText ?? "";
        if (text.Length > SelectionHelpers.MaxChars) text = text[..SelectionHelpers.MaxChars];
        SelectionBox.Text = text;
        PrivacyNote.Text = "Quick checks run on this PC. A prompt is filled in and shown to you first. Nothing is sent unless you press “Ask my AI…” and then Send on the preview, and only to the connection set up in Settings.";

        _prompts = hooks.Prompts().Where(a => a.Enabled && a.Kind is ActionKind.FillPrompt or ActionKind.AskAi).ToList();
        PromptBox.ItemsSource = _prompts.Select(a => $"{a.Name} · {(a.Kind == ActionKind.AskAi ? "ask my AI" : "fill and show")}").ToList();
        if (_prompts.Count == 0)
        {
            PromptBox.IsEnabled = false; UsePromptBtn.IsEnabled = false;
            PromptBox.PlaceholderText = "No stored prompts yet. Add one under Settings.";
        }

        CloseBtn.Click += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        // The text box can announce a change after the window is shown even though the text is the same (seen on a real Windows machine).
        // Reading the selection again then would wipe a result the person has just asked for, so only a real change starts the pause.
        SelectionBox.TextChanged += (_, _) => { if ((SelectionBox.Text ?? "") == _analysedText) return; _debounce.Stop(); _debounce.Start(); };
        _debounce.Tick += (_, _) => { _debounce.Stop(); if ((SelectionBox.Text ?? "") != _analysedText) Analyse(); };
        SuggestBtn.Click += (_, _) => _suggested?.Invoke();
        DescribeBtn.Click += (_, _) => Show(SelectionHelpers.Describe(SelectionBox.Text));
        DupBtn.Click += (_, _) => Show(SelectionHelpers.FindDuplicates(SelectionBox.Text));
        BlankBtn.Click += (_, _) => Show(SelectionHelpers.FindBlanks(SelectionBox.Text));
        TotalBtn.Click += (_, _) => Show(SelectionHelpers.Totals(SelectionBox.Text));
        CompareBtn.Click += (_, _) =>
        {
            if (!int.TryParse(ColA.Text, out int a) || !int.TryParse(ColB.Text, out int b)) { Show(new[] { "Please type two column numbers, for example 1 and 2." }); return; }
            Show(SelectionHelpers.CompareColumns(SelectionBox.Text, a, b));
        };
        CopyResultBtn.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard && ResultBox.Text is { Length: > 0 } result) { await clipboard.SetTextAsync(result); Status.Text = "Copied."; }
        };
        UsePromptBtn.Click += (_, _) =>
        {
            if (PromptBox.SelectedIndex < 0 || PromptBox.SelectedIndex >= _prompts.Count) { Status.Text = "Please choose a stored prompt first."; return; }
            var chosen = _prompts[PromptBox.SelectedIndex].Clone();
            // Ask is about the selection, so a stored prompt that has no {selection} gets it added at the end (the preview shows exactly that)
            if (WithSelection(chosen.Template) is not { } template) { Status.Text = "That stored prompt is too long to add your selection to."; return; }
            chosen.Template = template;
            RunPrompt(chosen);
        };
        foreach (var (name, template) in ReadingPrompts.All)
        {
            var b = new Button { Content = name, Margin = new Thickness(0, 0, 8, 6) };
            b.Click += (_, _) => { UsageLog.Record("reading_prompt"); RunPrompt(new CustomAction { Name = name, Kind = ActionKind.AskAi, Template = template, Enabled = true }); };
            ReadButtons.Children.Add(b);
        }
        FillOwnBtn.Click += (_, _) => RunOwn(ActionKind.FillPrompt);
        AskOwnBtn.Click += (_, _) => RunOwn(ActionKind.AskAi);
        Opened += (_, _) => { FitToScreen(); if (string.IsNullOrWhiteSpace(SelectionBox.Text)) SelectionBox.Focus(); UsageLog.Record("ask_open"); };
        Closed += (_, _) => _debounce.Stop();
        Analyse();
    }

    private void FitToScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;
        Body.MaxHeight = Math.Max(240, screen.WorkingArea.Height / screen.Scaling - 52 - 40 - 34 - 14 - 20);
    }

    public SelectionShape Shape { get; private set; } = SelectionHelpers.Classify("");

    /// <summary>The text of the last quick check, or an empty string. Public so a test (or a paste) can ask for it.</summary>
    public string Result => ResultBox.IsVisible ? ResultBox.Text ?? "" : "";

    /// <summary>Reads the selection again and redraws what is offered. Public so a test can ask for it at once.</summary>
    public void Analyse()
    {
        _debounce.Stop();
        _analysedText = SelectionBox.Text ?? "";
        var shape = Shape = SelectionHelpers.Classify(SelectionBox.Text);
        ShapeText.Text = shape.Label;
        SuggestText.Text = shape.Suggestion;
        ResultBox.IsVisible = CopyResultBtn.IsVisible = false; ResultBox.Text = "";
        bool data = shape.Kind is SelectionKind.Table or SelectionKind.List;
        QuickPanel.IsVisible = data;
        CompareBtn.IsEnabled = ColA.IsEnabled = ColB.IsEnabled = shape.Kind == SelectionKind.Table;
        string text = SelectionBox.Text ?? "";
        switch (shape.Kind)
        {
            case SelectionKind.Table or SelectionKind.List:
                SuggestBtn.Content = "Find duplicates"; _suggested = () => Show(SelectionHelpers.FindDuplicates(SelectionBox.Text)); break;
            case SelectionKind.Anda:
                SuggestBtn.Content = "Look it up"; _suggested = () => { if (AndaCompanion.Data.AndaValidator.Extract(text) is { } n) _hooks.Lookup(n); }; break;
            case SelectionKind.Text:
                SuggestBtn.Content = "Review as a draft"; _suggested = () => _hooks.ReviewDraft(SelectionBox.Text ?? ""); break;
            default: _suggested = null; break;
        }
        SuggestBtn.IsVisible = _suggested is not null;
        Status.Text = "";
        PaintMore(text);
    }

    private DateOnly Today() => _hooks.Today?.Invoke() ?? DateOnly.FromDateTime(DateTime.Now);

    /// <summary>What else can be done with this selection by fixed rules, shown only when it applies: a calendar invitation, and a note on an ANDA it names.</summary>
    private void PaintMore(string text)
    {
        MoreButtons.Children.Clear();
        var invitation = string.IsNullOrWhiteSpace(text) ? null : Invitations.For(text, Today(), "", "");
        var ids = (_hooks.AddNote is null ? null : _hooks.KnownAndas?.Invoke(text))?.Take(3).ToList() ?? new List<string>();
        MorePanel.IsVisible = invitation is not null || ids.Count > 0;
        InviteHint.IsVisible = invitation is not null;
        if (invitation is not null)
        {
            InviteHint.Text = $"A date was found: {invitation.Shown}. The invitation would read “{invitation.Title}”.";
            var b = new Button { Content = "Save a calendar invitation…", Margin = new Thickness(0, 0, 8, 6), Tag = "invitation" };
            b.Click += async (_, _) => await SaveInvitationAsync();
            MoreButtons.Children.Add(b);
        }
        foreach (string id in ids)
        {
            var b = new Button { Content = $"Add to the notes of ANDA {id}", Margin = new Thickness(0, 0, 8, 6), Tag = "note:" + id };
            b.Click += (_, _) =>
            {
                string raw = SelectionBox.Text ?? "";
                var (ok, message) = _hooks.AddNote!(id, raw);
                if (ok) UsageLog.Record("note_added");
                Status.Text = ok
                    ? $"Added to your notes on ANDA {id}." + (raw.Trim().Length > QuickNotesService.MaxLength ? $" Only the first {QuickNotesService.MaxLength} characters were kept." : "") + " Open the card and press N to see it."
                    : message == "Type a note first." ? "There is nothing to add yet." : message;
            };
            MoreButtons.Children.Add(b);
        }
    }

    /// <summary>Makes the invitation file and lets the person save it. Nothing is sent, and nothing is added to a calendar for them.</summary>
    public async Task SaveInvitationAsync()
    {
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var made = Invitations.For(SelectionBox.Text, Today(), stamp, Guid.NewGuid().ToString("N") + "@notula");
        if (made is null) { Status.Text = "No date was found in the selection."; return; }
        try
        {
            bool saved;
            if (_hooks.SaveInvitation is not null) saved = await _hooks.SaveInvitation(made.Ics);
            else
            {
                var file = await StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
                {
                    Title = "Save the calendar invitation", SuggestedFileName = "invitation.ics", DefaultExtension = "ics",
                    FileTypeChoices = new[] { new Avalonia.Platform.Storage.FilePickerFileType("Calendar invitation") { Patterns = new[] { "*.ics" } } },
                });
                if (file is null) return;
                await using var stream = await file.OpenWriteAsync();
                await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
                await writer.WriteAsync(made.Ics);
                saved = true;
            }
            if (saved) UsageLog.Record("invitation_saved");
            Status.Text = saved
                ? $"An all-day event for {made.Shown} was saved. Open the file to add it to your calendar. Nothing was sent, and nothing was added for you."
                : "The invitation was not saved.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { Status.Text = "The invitation could not be saved there. Please choose another folder."; }
    }

    private void Show(IReadOnlyList<string> lines)
    {
        ResultBox.Text = string.Join("\n", lines);
        ResultBox.IsVisible = CopyResultBtn.IsVisible = true;
        QuickPanel.IsVisible = true;
    }

    private void RunPrompt(CustomAction action)
    {
        if (string.IsNullOrWhiteSpace(SelectionBox.Text)) { Status.Text = "Please select or paste something first."; return; }
        Status.Text = action.Kind == ActionKind.AskAi
            ? "A window with exactly what would be sent is open. Nothing has been sent."
            : "Your prompt is filled in and shown in a new window. Nothing was sent.";
        if (SelectionHelpers.Classify(SelectionBox.Text).Kind is SelectionKind.Table or SelectionKind.List && UsesRecordDetails(action.Template))
            Status.Text += " A table holds many ANDAs, so details of one ANDA, such as {number}, are left empty.";
        _hooks.RunPrompt(action, SelectionBox.Text!);
    }

    /// <summary>The prompt with the selection in it: as it is when it already has {selection}, otherwise with the selection added at the end. Null when that is too long.</summary>
    public static string? WithSelection(string? template)
    {
        string t = template ?? "";
        if (!t.Contains("{selection}", StringComparison.Ordinal)) t += "\n\nSelected text:\n{selection}";
        return t.Length > MyActionRules.MaxTemplateChars ? null : t;
    }

    /// <summary>True when the prompt asks for details of one ANDA ({number}, {drug}, {goal} and so on) rather than only {selection} or {today}.</summary>
    public static bool UsesRecordDetails(string? template) =>
        System.Text.RegularExpressions.Regex.Matches(template ?? "", @"\{([A-Za-z_]+)\}").Any(m => m.Groups[1].Value is not ("selection" or "today"));

    private void RunOwn(ActionKind kind)
    {
        string own = (OwnBox.Text ?? "").Trim();
        if (own.Length == 0) { Status.Text = "Please type your instruction first."; return; }
        if (WithSelection(own) is not { } template) { Status.Text = "That instruction is too long."; return; }
        if (MyActionRules.UnknownPlaceholders(template) is { Count: > 0 } unknown) { Status.Text = $"“{{{unknown[0]}}}” is not something Ask can fill in. Please remove it."; return; }
        RunPrompt(new CustomAction { Name = "Ask about a selection", Kind = kind, Template = template, Enabled = true });
    }
}
