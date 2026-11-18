using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using AndaCompanion.Models;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

/// <summary>
/// "Review a draft". Three views, all by fixed rules on this PC, with no AI and nothing sent: Check everything, Proofread (a short list of
/// spelling, grammar, spacing and punctuation rules, with a corrected draft the person can copy) and Fact-check (the draft against the person's
/// own data, notes and the calendar). Under them: one question before sending, and the person's own notes and open items for recall.
/// The AI actions (Rewrite, Proofread with AI, Fact-check with AI) sit in a folded section; they use an approved AI platform if one is
/// connected, after showing exactly what would be sent. A result is only ever copied by the person; nothing is replaced in their email.
/// </summary>
public partial class DraftWindow : Window
{
    public const string SampleDraft = "Hi all, quick status. ANDA 216482 is on track, with a goal date of Oct 10, 2026 and a forecast of Oct 17, 2026. ANDA 214990 is running late. ANDA 123456 needs a call. The IR responce for ANDA 216482 (750 mg, cycle 3) is due Jul 28.";   // wording-ok (the slip is there to be found)
    public const string NothingFound = "Proofreading found nothing by its rules: spelling of common words and of names in your data, spacing, punctuation, capitals, brackets and date style.";
    private const int MarkLimit = 4000;

    private readonly Func<string, AndaRecord?> _lookup;
    private readonly WritingService _writing;
    private readonly Settings _settings;
    private readonly PromptLibraryService _prompts;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private WritingRequest? _pending;
    private CancellationTokenSource? _cts;

    private readonly Func<DateOnly> _today;
    private readonly BusinessCalendar _calendar;
    private ProofResult _proof = ProofResult.Empty("");

    /// <summary>"all", "proof" (proofreading only) or "facts" (fact check only).</summary>
    public string View { get; private set; } = "all";

    /// <summary>The person's quick notes from the card, the two to-do lists, and how to open a card: set by the app; absent in a test that does not need them.</summary>
    public QuickNotesService? QuickNotes { get; init; }
    public Func<IEnumerable<TodoItem>>? AndaTodo { get; init; }
    public Func<IEnumerable<MyTodoItem>>? MyTodo { get; init; }
    public Action<string>? OpenCard { get; init; }

    /// <summary>Round 27. Adds a follow-up to the ANDA to-do list (title, ANDA number or null, the day): the result says whether it was new.</summary>
    public Func<string, string?, DateOnly, TodoAddResult>? AddFollowUp { get; init; }

    /// <summary>Round 27. Opens the usual AI preview with the second-opinion prompt for the draft. Absent when the app has no way to do that.</summary>
    public Action<string>? SecondOpinion { get; init; }
    private ReplyReminder? _remind;

    public ProofResult Proof => _proof;

    public DraftWindow(string draft, Func<string, AndaRecord?> lookup, WritingService writing, Settings settings, PromptLibraryService prompts,
        Func<DateOnly>? today = null, BusinessCalendar? calendar = null)
    {
        _lookup = lookup; _writing = writing; _settings = settings; _prompts = prompts;
        _today = today ?? (() => DateOnly.FromDateTime(DateTime.Now)); _calendar = calendar ?? new BusinessCalendar();
        InitializeComponent();

        DraftBox.Text = draft;
        DraftBox.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); HidePreview(); };
        _debounce.Tick += (_, _) => { _debounce.Stop(); RunCheck(); };

        ViewAllBtn.Click += (_, _) => SetView("all");
        RemindBtn.Click += (_, _) => AddReminder();
        SecondOpinionBtn.Click += (_, _) => SecondOpinion?.Invoke(DraftBox.Text ?? "");
        ViewProofBtn.Click += (_, _) => SetView("proof");
        ViewFactBtn.Click += (_, _) => SetView("facts");
        SampleBtn.IsVisible = lookup("216482") is { Demo: true };          // the sample draft is about the sample records
        SampleBtn.Click += (_, _) => { DraftBox.Text = SampleDraft; _debounce.Stop(); RunCheck(); };
        CopyFixedBtn.Click += (_, _) => CopyCorrected();
        ApplyFixedBtn.Click += (_, _) => ApplyCorrected();

        AiNote.Text = writing.Available
            ? $"Sends your draft (and, for Fact-check with AI, the matching records) to {writing.ProviderName}. You see exactly what will be sent first, nothing is sent until you press Send, and the result is Copy only."
            : "AI is off in this build (offline mode). The checks above work without it. These buttons turn on when an approved AI platform is connected in Settings.";
        AiHeader.Text = writing.Available ? "AI options · an approved AI platform is connected" : "AI options · off";
        AiExpander.IsExpanded = writing.Available;
        foreach (var b in new[] { ProofBtn, RewriteBtn, FactBtn })
        {
            b.IsEnabled = writing.Available;
            if (!writing.Available) ToolTip.SetTip(b, "Needs an approved AI platform. The checks above work without it.");
        }
        ProofBtn.Click += (_, _) => Prepare(WritingAction.Proofread);
        RewriteBtn.Click += (_, _) => Prepare(WritingAction.Rewrite);
        FactBtn.Click += (_, _) => Prepare(WritingAction.FactCheck);
        CancelBtn.Click += (_, _) => HidePreview();
        SendBtn.Click += async (_, _) => await SendAsync();
        CopyBtn.Click += (_, _) => CopyResult();
        CloseBtn.Click += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Opened += (_, _) => { FitToScreen(); RunCheck(); };
        Closed += (_, _) => { _debounce.Stop(); _cts?.Cancel(); };
    }

    private void FitToScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;
        Body.MaxHeight = Math.Max(240, screen.WorkingArea.Height / screen.Scaling - 52 - 40 - 34 - 14 - 20 - ConnectionBar.Height);
    }

    /// <summary>Check everything, Proofread only, or Fact-check only. The checks are the same; the view chooses what is shown.</summary>
    public void SetView(string view)
    {
        View = view is "proof" or "facts" ? view : "all";
        foreach (var (button, name, on) in new[] { (ViewAllBtn, "Check everything", View == "all"), (ViewProofBtn, "Proofread", View == "proof"), (ViewFactBtn, "Fact-check", View == "facts") })
        {
            button.Classes.Set("primary", on);
            AutomationProperties.SetName(button, on ? name + ", selected" : name);
        }
        RunCheck();
    }

    private IReadOnlyList<DraftNote> NotesOf(AndaRecord r) => DraftNotes.For(r, QuickNotes, _today());

    private static IBrush Res(string key) => (IBrush)Application.Current!.Resources[key]!;

    private static Grid Row(string icon, string iconInk, string text, string textInk)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("22,*") };
        row.Children.Add(new TextBlock { Text = icon, FontSize = 13, FontWeight = FontWeight.Bold, Foreground = Res(iconInk) });
        var t = new TextBlock { Text = text, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = Res(textInk) };
        Grid.SetColumn(t, 1);
        row.Children.Add(t);
        return row;
    }

    /// <summary>Runs every fixed-rule check and shows what the chosen view asks for. Returns the shared review result.</summary>
    public DraftCheckResult RunCheck()
    {
        string text = DraftBox.Text ?? "";
        bool empty = string.IsNullOrWhiteSpace(text);
        var result = DraftReview.Check(text, _lookup, _today(), _calendar);
        var extra = FactExtras.Check(text, _lookup, NotesOf, _today());
        _proof = Proofreader.Check(text, _lookup);

        FactWrap.IsVisible = View != "proof"; ProofWrap.IsVisible = View != "facts";

        // The summary line.
        if (empty)
            SummaryText.Text = $"Nothing was selected. Select text in any window and press {(string.IsNullOrWhiteSpace(_settings.DraftHotkey) ? "Review a draft in the tray menu" : _settings.DraftHotkey)}, or paste a draft above.";
        else
        {
            int facts = result.Findings.Count(f => f.Kind is DraftFindingKind.Difference or DraftFindingKind.UnknownAnda) + extra.Differences.Count, words = _proof.Total;
            string a = facts > 0 ? $"{facts} difference{(facts == 1 ? "" : "s")} from your data" : "No difference from your data";
            string b = words > 0 ? $"{words} wording item{(words == 1 ? "" : "s")}" : "no wording items";
            SummaryText.Text = (View == "proof" ? char.ToUpperInvariant(b[0]) + b[1..] : View == "facts" ? a : a + " · " + b)
                + (_proof.Fixes > 0 && View != "facts" ? $" · {_proof.Fixes} can be fixed for you" : "");
        }

        // Fact check: the shared result, then the extra facts from the person's own data and notes. Its last line says what Proofreading now covers.
        FindingsPanel.Children.Clear();
        void Finding(DraftFindingKind kind, string textOf)
        {
            (string icon, string ink) = kind switch
            {
                DraftFindingKind.Difference => ("⚠", "C.Warn"),
                DraftFindingKind.UnknownAnda => ("?", "C.Bad"),
                DraftFindingKind.Ok => ("✓", "C.Good"),
                _ => ("ℹ", "C.Ink3"),
            };
            FindingsPanel.Children.Add(Row(icon, ink, textOf, kind == DraftFindingKind.Note ? "C.Ink3" : "C.Ink"));
        }
        var receipt = result.Findings.LastOrDefault(f => f.Text == DraftReview.ReceiptNote);
        foreach (var f in result.Findings) if (!ReferenceEquals(f, receipt)) Finding(f.Kind, f.Text);
        foreach (string d in extra.Differences) Finding(DraftFindingKind.Difference, d);
        if (extra.AlsoChecked is { } also) Finding(DraftFindingKind.Ok, also);
        foreach (string n in extra.Notes) Finding(DraftFindingKind.Note, n);
        if (receipt is not null) Finding(DraftFindingKind.Note, DraftReview.ReceiptNoteWithProofreading);

        // Proofreading.
        ProofPanel.Children.Clear();
        FixPanel.IsVisible = false; ProofFine.IsVisible = !empty;
        if (empty) ProofPanel.Children.Add(Row("ℹ", "C.Ink3", "Nothing to proofread yet. Select your draft and press the shortcut, or paste it above.", "C.Ink3"));
        else
        {
            var items = _proof.Items.Where(i => View == "proof" || !i.AlsoInFacts).ToList();
            if (items.Count == 0) ProofPanel.Children.Add(Row("✓", "C.Good", NothingFound, "C.Ink"));
            foreach (var i in items) ProofPanel.Children.Add(Row(i.Fixable ? "✎" : "ℹ", i.Fixable ? "C.Accent" : "C.Ink3", i.Text, i.Fixable ? "C.Ink" : "C.Ink3"));
            if (_proof.More > 0) ProofPanel.Children.Add(Row("ℹ", "C.Ink3", $"{_proof.More} more item{(_proof.More == 1 ? "" : "s")} of the same kind {(_proof.More == 1 ? "is" : "are")} not listed.", "C.Ink3"));
            if (_proof.Fixes > 0)
            {
                PaintMarked(text);
                FixStatus.Text = $"{_proof.Fixes} change{(_proof.Fixes == 1 ? "" : "s")} can be made without guessing.";
                FixPanel.IsVisible = true;
            }
        }

        // One question before sending (not in the Proofread view, which is about wording only).
        var question = View == "proof" ? null : BeforeSend.Ask(text, result, extra);
        SendBox.IsVisible = question is not null;
        if (question is not null)
        {
            SendQuestionText.Text = question.Text;
            SendFine.Text = question.Gap ? BeforeSend.GapNote : BeforeSend.NoGapNote;
            SendBox.BorderBrush = Res(question.Gap ? "C.Warn" : "C.Line");
            SendBox.Background = question.Gap ? Res("C.WarnBg") : null;
        }

        PaintRemind(text); PaintSigns(text);
        PaintRecall(text);
        UsageLog.Record("draft_check");
        return result;
    }

    /// <summary>The draft itself, with each change shown where it is: what goes is struck out, what comes is beside it.</summary>
    private void PaintMarked(string text)
    {
        var inlines = new Avalonia.Controls.Documents.InlineCollection();
        int at = 0;
        foreach (var k in _proof.Marks)
        {
            if (k.Start > MarkLimit) break;
            if (k.Start > at) inlines.Add(new Avalonia.Controls.Documents.Run(text[at..k.Start]));
            if (k.End > k.Start) inlines.Add(new Avalonia.Controls.Documents.Run(text[k.Start..k.End]) { TextDecorations = TextDecorations.Strikethrough, Foreground = Res("C.Bad") });
            if (k.To.Length > 0) inlines.Add(new Avalonia.Controls.Documents.Run(k.To) { TextDecorations = TextDecorations.Underline, FontWeight = FontWeight.SemiBold, Foreground = Res("C.Good") });
            at = k.End;
        }
        int end = Math.Min(text.Length, MarkLimit);
        if (end > at) inlines.Add(new Avalonia.Controls.Documents.Run(text[at..end] + (text.Length > MarkLimit ? " …" : "")));
        MarkedText.Inlines = inlines;
    }

    /// <summary>Copies the corrected text, and says "copied" only once Windows has taken it.</summary>
    private async void CopyCorrected()
    {
        if (_proof.Fixes == 0) return;
        string corrected = _proof.Corrected;
        try
        {
            if (Clipboard is not { } clipboard) throw new InvalidOperationException("no clipboard");
            await clipboard.SetTextAsync(corrected);
            FixStatus.Text = "Corrected draft copied. Your own text was not changed.";
            UsageLog.Record("proofread_copy");
        }
        catch (Exception) { FixStatus.Text = "Windows did not allow copying just now. Please use “Apply here” and copy from the box above."; }
    }

    /// <summary>Puts the corrected text into this window's own box, and checks again. Nothing outside the app is touched.</summary>
    private void ApplyCorrected()
    {
        if (_proof.Fixes == 0) return;
        DraftBox.Text = _proof.Corrected;
        _debounce.Stop();
        RunCheck();
        DraftBox.Focus();
        UsageLog.Record("proofread_apply");
    }

    /// <summary>For recall: for each ANDA the draft names, the person's notes (the ones the draft touches first) and open items, folded.</summary>
    /// <summary>When the draft asks for a reply by a date, the person can put a follow-up on the to-do list for that day. The mailbox is not read.</summary>
    private void PaintRemind(string text)
    {
        _remind = View != "proof" && !string.IsNullOrWhiteSpace(text) && AddFollowUp is not null ? ReplyBy.Find(text, _today()) : null;
        RemindPanel.IsVisible = _remind is not null;
        if (_remind is null) return;
        RemindBtn.IsEnabled = true;
        RemindText.Text = $"Your draft asks for a reply by {_remind.Shown}.";
    }

    private void AddReminder()
    {
        if (_remind is null || AddFollowUp is null) return;
        var day = DateOnly.ParseExact(_remind.Day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var result = AddFollowUp(_remind.Title, _remind.Number, day);
        RemindText.Text = result.Reason ?? (result.Added
            ? $"Added to your ANDA to-do list for {_remind.Shown}. This tool does not read your mail, so it cannot know whether a reply came."
            : "That is already on your to-do list.");
        if (result.Added) RemindBtn.IsEnabled = false;
    }

    /// <summary>Signs worth a second look: things to check, with the reason. Never a statement about who or what wrote the text.</summary>
    private void PaintSigns(string text)
    {
        SignsList.Children.Clear();
        SignsPanel.IsVisible = View != "proof" && !string.IsNullOrWhiteSpace(text);
        if (!SignsPanel.IsVisible) return;
        var r = Services.SecondLook.Check(text);
        SecondOpinionBtn.IsVisible = SecondOpinion is not null;
        if (r.Items.Count == 0) { SignsList.Children.Add(Row("✓", "C.Good", Services.SecondLook.None, "C.Ink")); SignsFine.IsVisible = false; return; }
        foreach (string item in r.Items) SignsList.Children.Add(Row("•", "C.Ink3", item, "C.Ink"));
        SignsList.Children.Add(Row("→", "C.Ink3", string.Join("\n", Services.SecondLook.Response), "C.Ink"));
        SignsFine.Text = Services.SecondLook.NotAVerdict; SignsFine.IsVisible = true;
    }

    private void PaintRecall(string text)
    {
        RecallPanel.Children.Clear();
        var groups = Recall.For(text, _lookup, NotesOf, AndaTodo?.Invoke(), MyTodo?.Invoke());
        RecallPanel.IsVisible = groups.Count > 0;
        if (groups.Count == 0) return;
        RecallPanel.Children.Add(new TextBlock { Classes = { "lbl" }, Text = "FOR YOUR RECALL · YOUR NOTES AND OPEN ITEMS" });
        foreach (var g in groups)
        {
            int open = g.Items.Count;
            string head = $"ANDA {g.Number} · {g.Notes.Count} note{(g.Notes.Count == 1 ? "" : "s")}{(g.Latest is { } latest ? $" (latest {latest})" : "")} · {(open > 0 ? $"{open} open item{(open == 1 ? "" : "s")}" : "no open items")}";
            var body = new StackPanel { Spacing = 5 };
            body.Children.Add(new TextBlock { Classes = { "lbl" }, Text = "NOTES · THE ONES YOUR DRAFT TOUCHES FIRST, THEN THE NEWEST" });
            if (g.Notes.Count == 0) body.Children.Add(new TextBlock { Classes = { "hint" }, Text = "No notes yet for this ANDA. Open the card and press N to add one." });
            foreach (var n in g.Notes.Take(4))
            {
                string who = n.Note.Who + (n.Note.Mine ? " · quick note" : "") + (n.Shared.Count > 0 ? " · also in your draft: " + string.Join(", ", n.Shared.Take(3)) : "");
                body.Children.Add(new TextBlock { Text = who, FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Res(n.Shared.Count > 0 ? "C.Accent" : "C.Ink3") });
                body.Children.Add(new TextBlock { Text = FactExtras.Cut(n.Note.Text, 260), FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Res("C.Ink"), Margin = new Thickness(0, 0, 0, 4) });
            }
            if (g.Notes.Count > 4) body.Children.Add(new TextBlock { Classes = { "hint" }, Text = $"{g.Notes.Count - 4} more on the card." });
            body.Children.Add(new TextBlock { Classes = { "lbl" }, Margin = new Thickness(0, 4, 0, 0), Text = "OPEN ITEMS" });
            if (open == 0) body.Children.Add(new TextBlock { Classes = { "hint" }, Text = "No open items in your data or your to-do lists." });
            foreach (var i in g.Items.Take(5))
            {
                string due = string.Join(" · ", new[] { i.Due, i.Source == "your data" ? "" : i.Source }.Where(x => !string.IsNullOrEmpty(x)));
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                row.Children.Add(new TextBlock { Text = i.Title, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Res("C.Ink") });
                var d = new TextBlock { Text = due, FontSize = 12, Margin = new Thickness(10, 0, 0, 0), FontWeight = i.Urgent ? FontWeight.SemiBold : FontWeight.Normal, Foreground = Res(i.Urgent ? "C.Warn" : "C.Ink3") };
                Grid.SetColumn(d, 1); row.Children.Add(d);
                body.Children.Add(row);
            }
            if (OpenCard is { } openCard)
            {
                string number = g.Number;
                var btn = new Button { Content = "Open the card", Margin = new Thickness(0, 6, 0, 0) };
                AutomationProperties.SetName(btn, "Open the card of ANDA " + number);
                btn.Click += (_, _) => openCard(number);
                body.Children.Add(btn);
            }
            var box = new Expander
            {
                Header = new TextBlock { Text = head, FontSize = 12.5, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = Res("C.Ink") },
                Content = body, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, Tag = g.Number,
            };
            AutomationProperties.SetName(box, head);
            RecallPanel.Children.Add(box);
        }
    }

    private void Prepare(WritingAction action)
    {
        string draft = DraftBox.Text ?? "";
        if (string.IsNullOrWhiteSpace(draft)) { ResultNote.Text = "Please type or paste a draft first."; ResultPanel.IsVisible = true; return; }
        var records = WritingPrompts.RecordsIn(draft, _lookup);
        string? lens = _settings.ActiveLens is { } name ? _prompts.GetText(name) : null;
        _pending = WritingPrompts.Build(action, draft, _settings.WritingInstructions(), lens, records);
        PreviewBox.Text = _pending.Preview();
        PreviewPanel.IsVisible = true;
        ResultPanel.IsVisible = false;
    }

    private void HidePreview() { _pending = null; PreviewPanel.IsVisible = false; }

    private async Task SendAsync()
    {
        if (_pending is not { } req) return;
        PreviewPanel.IsVisible = false;
        ResultPanel.IsVisible = true;
        ResultLabel.Text = "WORKING…";
        ResultBox.Text = ""; ResultNote.Text = "";
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        UsageLog.Record(req.Action switch { WritingAction.Proofread => "ai_proofread", WritingAction.Rewrite => "ai_rewrite", _ => "ai_fact_check" });
        var result = await _writing.RunAsync(req, cts.Token);
        if (cts.IsCancellationRequested) return;
        ResultLabel.Text = result.Ok ? "RESULT · GENERATED, VERIFY BEFORE SENDING" : "NO RESULT";
        ResultBox.Text = result.Ok ? result.Text : result.Message;
        ResultNote.Text = result.Ok ? "Nothing in your email was changed. Copy it if you want it." : "";
        CopyBtn.IsEnabled = result.Ok;
        _pending = null;
    }

    private void CopyResult()
    {
        if (string.IsNullOrEmpty(ResultBox.Text)) return;
        Clipboard?.SetTextAsync(ResultBox.Text);
        ResultNote.Text = "Copied ✓";
        DispatcherTimer.RunOnce(() => ResultNote.Text = "Nothing in your email was changed.", TimeSpan.FromSeconds(1.5));
    }
}
