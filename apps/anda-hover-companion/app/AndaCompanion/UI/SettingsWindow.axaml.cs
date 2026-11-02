using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using AndaCompanion.Services;
using AndaCompanion.Services.Portfolio;
using AndaCompanion.Theming;

namespace AndaCompanion.UI;

/// <summary>What Settings needs to ask the running app, so the window can show live status (shortcut registration, the loaded portfolio).</summary>
public sealed record SettingsHooks(Func<string> HotkeyStatus, Func<PortfolioRepository?> Portfolio, Func<string> ReferenceStatus);

public partial class SettingsWindow : Window
{
    private readonly Settings _settings;
    private readonly PromptLibraryService _prompts;
    private readonly Action _onChanged;
    private bool _loading = true;

    private readonly SettingsHooks? _hooks;

    public SettingsWindow(Settings settings, PromptLibraryService prompts, Action onChanged, SettingsHooks? hooks = null)
    {
        _hooks = hooks;
        _settings = settings;
        _prompts = prompts;
        _onChanged = onChanged;
        InitializeComponent();

        foreach (var a in Appearances.All)
        {
            var rb = new RadioButton
            {
                Content = a.Name, GroupName = "appearance",
                IsChecked = a.Id == _settings.Appearance,
                Margin = new Avalonia.Thickness(0, 0, 12, 2),
            };
            ToolTip.SetTip(rb, a.Tagline);
            rb.IsCheckedChanged += (_, _) =>
            {
                if (!_loading && rb.IsChecked == true) { _settings.Appearance = a.Id; Apply(); }
            };
            (a.IsCompact ? CompactPanel : AppearancePanel).Children.Add(rb);
        }

        foreach (int seconds in AutoHidePolicy.Choices)
        {
            var rb = new RadioButton
            {
                Content = AutoHidePolicy.Label(seconds), GroupName = "autohide",
                IsChecked = seconds == _settings.AutoHideSeconds,
                Margin = new Avalonia.Thickness(0, 0, 14, 2),
            };
            rb.IsCheckedChanged += (_, _) =>
            {
                if (!_loading && rb.IsChecked == true) { _settings.AutoHideSeconds = seconds; Apply(); }
            };
            AutoHidePanel.Children.Add(rb);
        }

        var consents = new (ConsentModel Model, string Label)[]
        {
            (ConsentModel.Full, "① Full card — the hotkey press is the consent"),
            (ConsentModel.PeekFirst, "② Peek first — expand for the full brief"),
            (ConsentModel.Whisper, "③ Whisper invite — opens only if clicked"),
        };
        foreach (var (model, label) in consents)
        {
            var rb = new RadioButton
            {
                Content = label, GroupName = "consent",
                IsChecked = model == _settings.Consent,
            };
            rb.IsCheckedChanged += (_, _) =>
            {
                if (!_loading && rb.IsChecked == true) { _settings.Consent = model; Apply(); }
            };
            ConsentPanel.Children.Add(rb);
        }

        CaptureCheck.IsChecked = _settings.CaptureViaCopy;
        CaptureCheck.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.CaptureViaCopy = CaptureCheck.IsChecked == true;
            Apply();
        };

        OpacitySlider.Value = Math.Round(_settings.CardOpacity * 100);
        OpacityLabel.Text = $"{OpacitySlider.Value:0}%";
        OpacitySlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty && !_loading)
            {
                _settings.CardOpacity = OpacitySlider.Value / 100.0;
                OpacityLabel.Text = $"{OpacitySlider.Value:0}%";
                Apply();
            }
        };

        BuildNewSections();
        ReloadLenses();
        LensCombo.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.ActiveLens = LensCombo.SelectedIndex <= 0
                ? null
                : (LensCombo.SelectedItem as string);
            Apply();
        };
        ManagePromptsBtn.Click += (_, _) =>
        {
            var win = new PromptLibraryWindow(_prompts, _settings, () => { ReloadLenses(); Apply(); });
            win.Show(this);
        };

        ConnStatus.Text = ConnectionService.Describe();
        TestBtn.Click += async (_, _) =>
        {
            TestBtn.IsEnabled = false;
            ConnStatus.Text = "Testing…";
            ConnStatus.Text = await ConnectionService.TestAsync();
            TestBtn.IsEnabled = true;
        };

        CloseBtn.Click += (_, _) => Close();
        Opened += (_, _) => FitToScreen();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        _loading = false;
    }

    // ---------------- urgent, shortcuts, calendar, my data, writing, usage log ----------------

    private void BuildNewSections()
    {
        EscalateCheck.IsChecked = _settings.EscalateUrgent;
        EscalateCheck.IsCheckedChanged += (_, _) => { if (_loading) return; _settings.EscalateUrgent = EscalateCheck.IsChecked == true; Apply(); };
        foreach (int days in UrgencyPolicy.LateDayChoices)
        {
            var rb = new RadioButton { Content = $"{days}+ days late", GroupName = "late", IsChecked = days == _settings.UrgentLateDays, Margin = new Avalonia.Thickness(0, 0, 14, 2) };
            rb.IsCheckedChanged += (_, _) => { if (!_loading && rb.IsChecked == true) { _settings.UrgentLateDays = days; Apply(); } };
            LatePanel.Children.Add(rb);
        }
        foreach (int days in Settings.HolidayLeadChoices)
        {
            var rb = new RadioButton { Content = days == 0 ? "Off" : $"{days} business days", GroupName = "holiday", IsChecked = days == _settings.HolidayLeadDays, Margin = new Avalonia.Thickness(0, 0, 14, 2) };
            rb.IsCheckedChanged += (_, _) => { if (!_loading && rb.IsChecked == true) { _settings.HolidayLeadDays = days; Apply(); } };
            HolidayPanel.Children.Add(rb);
        }

        // shortcuts: click a box, press the keys
        BindKey(PeekKeyBox, PeekKeyReset, "Select & Peek", () => _settings.PeekHotkey, v => _settings.PeekHotkey = v, "Ctrl+Alt+A");
        BindKey(LookupKeyBox, LookupKeyReset, "Lookup box", () => _settings.LookupHotkey, v => _settings.LookupHotkey = v, "Ctrl+Alt+Space");
        BindKey(DraftKeyBox, DraftKeyReset, "Check a draft", () => _settings.DraftHotkey, v => _settings.DraftHotkey = v, "Ctrl+Alt+D");
        KeyStatus.Text = _hooks?.HotkeyStatus() ?? "";

        // my data
        PortfolioBox.Text = _settings.PortfolioPath ?? "";
        NotesBox.Text = _settings.NotesFolder ?? "";
        ReferenceBox.Text = _settings.ReferenceDataPath ?? "";
        PortfolioChoose.Click += async (_, _) =>
        {
            var path = await PickFileAsync("Choose your ANDA portfolio", new[] { "*.xlsx", "*.xlsm", "*.csv" }, "Excel or CSV");
            if (path is null) return;
            _settings.PortfolioPath = path; _settings.PortfolioColumns = new(); PortfolioBox.Text = path; Apply(); RefreshPortfolioStatus();
        };
        PortfolioClear.Click += (_, _) => { _settings.PortfolioPath = null; _settings.PortfolioColumns = new(); PortfolioBox.Text = ""; Apply(); RefreshPortfolioStatus(); };
        NotesChoose.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose your notes folder", AllowMultiple = false });
            if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } path) return;
            _settings.NotesFolder = path; NotesBox.Text = path; Apply(); RefreshPortfolioStatus();
        };
        NotesClear.Click += (_, _) => { _settings.NotesFolder = null; NotesBox.Text = ""; Apply(); RefreshPortfolioStatus(); };
        ReferenceChoose.Click += async (_, _) =>
        {
            var path = await PickFileAsync("Choose the Drugs@FDA data file you downloaded", new[] { "*.zip" }, "Zip file");
            if (path is null) return;
            _settings.ReferenceDataPath = path; ReferenceBox.Text = path; Apply(); RefreshPortfolioStatus();
        };
        ReferenceClear.Click += (_, _) => { _settings.ReferenceDataPath = null; ReferenceBox.Text = ""; Apply(); RefreshPortfolioStatus(); };
        RefreshPortfolioStatus();

        InstructionsBox.Text = _settings.StandingInstructions;
        InstructionsBox.LostFocus += (_, _) =>
        {
            if (_loading || InstructionsBox.Text == _settings.StandingInstructions) return;
            _settings.StandingInstructions = (InstructionsBox.Text ?? "").Trim();
            Apply();
        };

        UsageCheck.IsChecked = _settings.UsageLog;
        UsageCheck.IsCheckedChanged += (_, _) => { if (_loading) return; _settings.UsageLog = UsageCheck.IsChecked == true; Apply(); };
        UsageHint.Text += $" File: {AppPaths.UsageFile}";
    }

    private async Task<string?> PickFileAsync(string title, string[] patterns, string typeName)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title, AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(typeName) { Patterns = patterns } },
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private void BindKey(TextBox box, Button reset, string name, Func<string> get, Action<string> set, string fallback)
    {
        box.Text = get();
        box.KeyDown += (_, e) =>
        {
            if (HotkeyCapture.FromKey(e.Key, e.KeyModifiers) is not { } combo) { e.Handled = true; return; }   // a modifier alone: keep waiting
            e.Handled = true;
            if (!HotkeyParser.TryParse(combo, out uint _m, out uint _k, out string normalized, out string error)) { KeyStatus.Text = $"{name}: {error}"; return; }
            var others = new[]
            {
                ("Select & Peek", ReferenceEquals(box, PeekKeyBox) ? normalized : _settings.PeekHotkey),
                ("Lookup box", ReferenceEquals(box, LookupKeyBox) ? normalized : _settings.LookupHotkey),
                ("Check a draft", ReferenceEquals(box, DraftKeyBox) ? normalized : _settings.DraftHotkey),
            };
            if (HotkeyParser.ValidateSet(others) is { } clash) { KeyStatus.Text = clash; return; }
            set(normalized); box.Text = normalized; Apply();
            KeyStatus.Text = $"{name}: {normalized}. Checking with Windows…";
            Avalonia.Threading.DispatcherTimer.RunOnce(() => KeyStatus.Text = _hooks?.HotkeyStatus() ?? $"{name}: {normalized}", TimeSpan.FromMilliseconds(500));
        };
        reset.Click += (_, _) =>
        {
            set(fallback); box.Text = fallback;
            if (HotkeyParser.ValidateSet(("Select & Peek", _settings.PeekHotkey), ("Lookup box", _settings.LookupHotkey), ("Check a draft", _settings.DraftHotkey)) is { } clash)
            { KeyStatus.Text = clash; return; }
            Apply();
            Avalonia.Threading.DispatcherTimer.RunOnce(() => KeyStatus.Text = _hooks?.HotkeyStatus() ?? "", TimeSpan.FromMilliseconds(500));
        };
    }

    private void RefreshPortfolioStatus()
    {
        var repo = _hooks?.Portfolio();
        PortfolioStatus.Text = _settings.PortfolioPath is null ? "No portfolio chosen: the fictional demo records are used." : repo?.Status.Message ?? "";
        ReferenceStatus.Text = _hooks?.ReferenceStatus() ?? "";
        ColumnsPanel.Children.Clear();
        ColumnsExpander.IsVisible = repo is { Headers.Count: > 0 };
        if (repo is null) return;
        foreach (var (key, label) in PortfolioColumns.Fields)
        {
            var combo = new ComboBox { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, MinHeight = 28 };
            var items = new List<string> { "(not in my sheet)" }; items.AddRange(repo.Headers.Where(h => h.Length > 0));
            combo.ItemsSource = items;
            combo.SelectedIndex = repo.Mapping.TryGetValue(key, out var chosen) ? Math.Max(0, items.IndexOf(chosen)) : 0;
            combo.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                if (combo.SelectedIndex <= 0) _settings.PortfolioColumns.Remove(key);
                else _settings.PortfolioColumns[key] = (string)combo.SelectedItem!;
                Apply();
                PortfolioStatus.Text = _hooks?.Portfolio()?.Status.Message ?? "";
            };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("140,*") };
            row.Children.Add(new TextBlock { Text = label, FontSize = 12, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            Grid.SetColumn(combo, 1);
            row.Children.Add(combo);
            ColumnsPanel.Children.Add(row);
        }
    }

    /// <summary>On a short screen the sections scroll instead of the window running off the bottom.</summary>
    private void FitToScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;
        // window chrome around the scroller: outer margins 52, card padding 40, header row ~34 + spacing, safety.
        double available = screen.WorkingArea.Height / screen.Scaling - 52 - 40 - 34 - 14 - 20;
        Body.MaxHeight = Math.Max(240, available);
    }

    private void ReloadLenses()
    {
        _loading = true;
        var items = new List<string> { "No lens (default brief)" };
        items.AddRange(_prompts.Prompts.Select(p => p.Name));
        LensCombo.ItemsSource = items;
        LensCombo.SelectedIndex = _settings.ActiveLens is null
            ? 0
            : Math.Max(0, items.FindIndex(n => n == _settings.ActiveLens));
        _loading = false;
    }

    private void Apply()
    {
        SettingsService.Save(_settings);
        _onChanged();
    }
}
