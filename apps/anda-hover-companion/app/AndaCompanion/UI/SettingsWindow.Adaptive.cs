using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

/// <summary>
/// Settings arrange themselves: each group is a collapsible section, and when the window opens the ones that need a look (or that the person
/// opens often, or that a new user needs first) are open with a short reason beside the heading, and the rest are collapsed. The person can
/// open or close any section, use Expand all / Collapse all / Suggested view, and turn the arranging off. Nothing moves while they work.
/// </summary>
public partial class SettingsWindow
{
    private sealed record Sec(SettingsSection Id, Expander Box, Border Pill, TextBlock PillText);
    private sealed record JumpTarget(string Title, Sec Section, Control? Target);

    private List<Sec> _secs = new();
    private List<JumpTarget> _jump = new();
    private readonly HashSet<SettingsSection> _touched = new();
    private bool _applyingLayout, _useDirty;

    /// <summary>The sections that are open right now, in the order shown.</summary>
    public IReadOnlyList<SettingsSection> OpenSections => _secs.Where(s => s.Box.IsExpanded).Select(s => s.Id).ToList();

    private void BuildAdaptive()
    {
        _secs = SettingsSections.All.Select(x => new Sec(x.Id, this.FindControl<Expander>("Sec" + x.Key)!, this.FindControl<Border>("Pill" + x.Key)!, this.FindControl<TextBlock>("PillText" + x.Key)!)).ToList();
        foreach (var sec in _secs)
        {
            sec.Box.PropertyChanged += (_, e) => { if (e.Property == Expander.IsExpandedProperty && e.NewValue is true && !_applyingLayout) Touch(sec.Id); };
            sec.Box.AddHandler(InputElement.GotFocusEvent, (_, _) => Touch(sec.Id), RoutingStrategies.Bubble);
        }

        // go to a section: the group names and the labels inside them. Choosing one opens its group first.
        _jump = new();
        foreach (var sec in _secs)
        {
            _jump.Add(new JumpTarget(SettingsSections.Title(sec.Id), sec, null));
            if (sec.Box.Content is not StackPanel content) continue;
            foreach (var panel in content.Children.OfType<StackPanel>())
                if (panel.Children.FirstOrDefault() is TextBlock { Text: { Length: > 0 } label } first && first.Classes.Contains("lbl"))
                    _jump.Add(new JumpTarget(label, sec, panel));
        }
        JumpBox.ItemsSource = _jump.Select(j => j.Title).ToList();
        JumpBox.SelectionChanged += (_, _) =>
        {
            if (JumpBox.SelectedIndex < 0) return;
            var target = _jump[JumpBox.SelectedIndex];
            JumpBox.SelectedIndex = -1;
            GoTo(target.Section, target.Target);
        };

        ExpandAllBtn.Click += (_, _) => ExpandAll();
        CollapseAllBtn.Click += (_, _) => CollapseAll();
        SuggestedBtn.Click += (_, _) => ApplySuggested();

        AdaptiveCheck.IsChecked = _settings.AdaptiveSettings;
        AdaptiveCheck.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.AdaptiveSettings = AdaptiveCheck.IsChecked == true; Apply();
            ApplySuggested();
        };
        AdaptiveReset.Click += (_, _) =>
        {
            _settings.SectionUse = new(); _touched.Clear(); Apply();
            AdaptiveStatus.Text = "Forgotten. The window will now start from what needs a look.";
        };
        GsShow.Click += (_, _) => { _settings.GettingStartedDone = false; Apply(); PaintGettingStarted(); AdaptiveStatus.Text = "Getting started is shown at the top again."; ApplySuggested(); };
        GsHide.Click += (_, _) => { _settings.GettingStartedDone = true; Apply(); PaintGettingStarted(); };
        GsFiles.Click += (_, _) => GoTo(_secs.First(s => s.Id == SettingsSection.Files), null);
        GsChecklist.Click += (_, _) => _hooks?.OpenChecklist?.Invoke();
        PaintGettingStarted();

        ApplySuggested();
        Closed += (_, _) => { if (_useDirty) SettingsService.Save(_settings); };
    }

    private void PaintGettingStarted()
    {
        GettingStarted.IsVisible = !_settings.GettingStartedDone;
        bool chosen = !string.IsNullOrWhiteSpace(_settings.PortfolioPath);
        string notesKey = string.IsNullOrWhiteSpace(_settings.ChecklistHotkey) ? "the My checklist item in the tray menu" : _settings.ChecklistHotkey;
        GsStep1.Text = $"1. Select an ANDA number in any program (an email, a spreadsheet, a web page), then press {_settings.PeekHotkey}. A small card opens.";
        GsStep2.Text = chosen
            ? "2. Done: your portfolio file is chosen, so the cards use your own ANDAs."
            : "2. Choose your portfolio file under “My files and writing”, so the cards use your own ANDAs. Until then the app shows fictional demo records.";
        GsStep3.Text = $"3. Select notes anywhere (a message, an email, a scribbled list) and press {notesKey}. They become a checklist you can tick off.";
        GsFiles.IsVisible = !chosen;
    }

    // ---------- what is open, and why ----------

    private SettingsFacts CurrentFacts()
    {
        var problems = new HashSet<SettingsSection>();
        foreach (var h in _hooks?.Hotkeys?.Invoke() ?? Array.Empty<HotkeyStatus>())
            if (!h.Registered)
                problems.Add(h.Binding.Id == HotkeyService.IdChecklist ? SettingsSection.Checklist : h.Binding.Id >= HotkeyService.IdActionBase ? SettingsSection.Actions : SettingsSection.Start);

        string? portfolio = !string.IsNullOrWhiteSpace(_settings.PortfolioPath) && _hooks?.Portfolio() is { } repo && !repo.Status.Ok ? "your portfolio file could not be read" : null;
        var conn = ConnectionStatus.Current;
        string? connection = conn.State switch
        {
            ConnectionState.Failed => $"{conn.Provider} did not answer",
            ConnectionState.SetUp => $"{conn.Provider} is set up but not tested",
            _ => null,
        };
        string? actions = null;
        foreach (var agent in _settings.Agents.Where(a => a.Enabled))
            if (_hooks?.AgentProblem?.Invoke(agent) is not null) { actions = $"the agent “{agent.Name}” is switched on but not ready"; break; }
        if (actions is null)
            foreach (var action in _settings.MyActions.Where(a => a.Enabled))
                if (MyActionRules.Validate(action.Clone(), Array.Empty<(string Name, string Hotkey)>()) is not null) { actions = $"the action “{action.Name}” is not ready"; break; }
        return new SettingsFacts(problems, portfolio, connection, actions, !string.IsNullOrWhiteSpace(_settings.PortfolioPath));
    }

    /// <summary>Opens what needs a look, what a new user needs first and what is opened often; collapses the rest; and says why beside each heading.</summary>
    public void ApplySuggested()
    {
        var advice = SettingsAdvisor.Advise(_settings, CurrentFacts());
        _applyingLayout = true;
        try
        {
            foreach (var sec in _secs)
            {
                var a = advice.First(x => x.Section == sec.Id);
                sec.Box.IsExpanded = a.Open;
                sec.Pill.IsVisible = a.Reason is not null;
                sec.PillText.Text = a.Reason ?? "";
                sec.PillText.Foreground = (IBrush)Avalonia.Application.Current!.Resources[a.Attention ? "C.Warn" : "C.Accent"]!;
                sec.Pill.Background = (IBrush)Avalonia.Application.Current!.Resources[a.Attention ? "C.WarnBg" : "C.Mark"]!;
            }
        }
        finally { _applyingLayout = false; }

        var opened = advice.Where(a => a.Open).Select(a => SettingsSections.Title(a.Section)).ToList();
        AdaptiveText.Text = !_settings.AdaptiveSettings
            ? "Sections stay as you leave them. To have the ones you need opened for you, turn on “Open the sections that need a look” under Maintenance."
            : opened.Count == 0
                ? "Everything is in order, so every section is collapsed. Click a heading to open it, or use “Go to a section” above."
                : $"Opened for you: {string.Join(", ", opened)}. The other sections are collapsed; click a heading to open one.";
    }

    public void ExpandAll()
    {
        _applyingLayout = true;
        try { foreach (var sec in _secs) sec.Box.IsExpanded = true; }
        finally { _applyingLayout = false; }
        AdaptiveText.Text = "Every section is open. Use “Suggested view” to go back to what matters now, or “Collapse all” to close them.";
    }

    public void CollapseAll()
    {
        _applyingLayout = true;
        try { foreach (var sec in _secs) sec.Box.IsExpanded = false; }
        finally { _applyingLayout = false; }
        AdaptiveText.Text = "Every section is collapsed. Click a heading to open one, use “Go to a section” above, or press “Suggested view”.";
    }

    /// <summary>Opens a section (if it is collapsed) and scrolls to it. The scroll waits for the section to be drawn.</summary>
    private void GoTo(Sec sec, Control? target)
    {
        if (!sec.Box.IsExpanded) sec.Box.IsExpanded = true;
        Dispatcher.UIThread.Post(() => (target ?? sec.Box).BringIntoView(), DispatcherPriority.Loaded);
    }

    // ---------- what is used often ----------

    /// <summary>Counts one opening of a section, once per time Settings is open. Only a count is kept (no text, no values); nothing is kept if the arranging is off.</summary>
    private void Touch(SettingsSection section)
    {
        if (_loading || !_settings.AdaptiveSettings || !_touched.Add(section)) return;
        SettingsAdvisor.Count(_settings.SectionUse, section);
        _useDirty = true;
    }
}
