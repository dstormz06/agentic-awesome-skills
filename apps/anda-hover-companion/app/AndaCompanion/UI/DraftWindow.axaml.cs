using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using AndaCompanion.Models;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

/// <summary>
/// "Review a draft": the review (deterministic, no AI) runs as soon as there is text; Proofread, Rewrite and Fact-check use an
/// approved AI platform if one is connected, after showing exactly what would be sent. A result is only ever copied by the person;
/// nothing is replaced in their email.
/// </summary>
public partial class DraftWindow : Window
{
    private readonly Func<string, AndaRecord?> _lookup;
    private readonly WritingService _writing;
    private readonly Settings _settings;
    private readonly PromptLibraryService _prompts;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private WritingRequest? _pending;
    private CancellationTokenSource? _cts;

    private readonly Func<DateOnly> _today;
    private readonly BusinessCalendar _calendar;

    public DraftWindow(string draft, Func<string, AndaRecord?> lookup, WritingService writing, Settings settings, PromptLibraryService prompts,
        Func<DateOnly>? today = null, BusinessCalendar? calendar = null)
    {
        _lookup = lookup; _writing = writing; _settings = settings; _prompts = prompts;
        _today = today ?? (() => DateOnly.FromDateTime(DateTime.Now)); _calendar = calendar ?? new BusinessCalendar();
        InitializeComponent();

        DraftBox.Text = draft;
        DraftBox.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); HidePreview(); };
        _debounce.Tick += (_, _) => { _debounce.Stop(); RunCheck(); };

        AiNote.Text = writing.Available
            ? $"Sends your draft (and, for Fact-check, the matching records) to {writing.ProviderName}. You see exactly what will be sent first."
            : "AI is off in this build (offline mode). The check above works without it. These buttons turn on when an approved AI platform is connected in Settings.";
        foreach (var b in new[] { ProofBtn, RewriteBtn, FactBtn })
        {
            b.IsEnabled = writing.Available;
            if (!writing.Available) ToolTip.SetTip(b, "Needs an approved AI platform. The draft check works without it.");
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
        Body.MaxHeight = Math.Max(240, screen.WorkingArea.Height / screen.Scaling - 52 - 40 - 34 - 14 - 20);
    }

    /// <summary>Runs the deterministic check and lists exactly what was and was not checked.</summary>
    public DraftCheckResult RunCheck()
    {
        var result = DraftReview.Check(DraftBox.Text, _lookup, _today(), _calendar);
        FindingsPanel.Children.Clear();
        foreach (var f in result.Findings)
        {
            (string icon, string ink) = f.Kind switch
            {
                DraftFindingKind.Difference => ("⚠", "C.Warn"),
                DraftFindingKind.UnknownAnda => ("?", "C.Bad"),
                DraftFindingKind.Ok => ("✓", "C.Good"),
                _ => ("ℹ", "C.Ink3"),
            };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("22,*") };
            row.Children.Add(new TextBlock { Text = icon, FontSize = 13, FontWeight = FontWeight.Bold, Foreground = (IBrush)Application.Current!.Resources[ink]! });
            var t = new TextBlock { Text = f.Text, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = (IBrush)Application.Current!.Resources[f.Kind == DraftFindingKind.Note ? "C.Ink3" : "C.Ink"]! };
            Grid.SetColumn(t, 1);
            row.Children.Add(t);
            FindingsPanel.Children.Add(row);
        }
        UsageLog.Record("draft_check");
        return result;
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
