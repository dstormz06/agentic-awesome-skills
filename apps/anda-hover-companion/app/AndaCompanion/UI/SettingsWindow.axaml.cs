using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using AndaCompanion.Services;
using AndaCompanion.Services.Portfolio;
using AndaCompanion.Theming;

namespace AndaCompanion.UI;

/// <summary>What Settings needs to ask the running app, so the window can show live status (shortcut registration, the loaded portfolio).</summary>
public sealed record SettingsHooks(Func<string> HotkeyStatus, Func<PortfolioRepository?> Portfolio, Func<string> ReferenceStatus,
    Func<string?>? MouseStatus = null, OutlookService? Outlook = null,
    Action? OpenTodo = null, Action<CustomAction>? RunAction = null, Func<AgentSettings, string?>? AgentProblem = null, Func<int>? ClearFinished = null,
    Action? OpenChecklist = null, Action? OpenNotes = null, Func<int>? ClearChecklistFinished = null, Func<IReadOnlyList<HotkeyStatus>>? Hotkeys = null);

public partial class SettingsWindow : Window
{
    private readonly Settings _settings;
    private readonly PromptLibraryService _prompts;
    private readonly Action _onChanged;
    private bool _loading = true;

    private readonly SettingsHooks? _hooks;
    private readonly QuickReplyService _replies;

    public SettingsWindow(Settings settings, PromptLibraryService prompts, Action onChanged, SettingsHooks? hooks = null, QuickReplyService? replies = null)
    {
        _hooks = hooks;
        _replies = replies ?? new QuickReplyService();
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

        OpacitySlider.Minimum = Settings.MinCardOpacity * 100;      // the XAML says 40 too; one named value keeps them equal (a test checks)
        OpacitySlider.Value = Math.Round(Settings.NormalizeOpacity(_settings.CardOpacity) * 100);
        OpacityLabel.Text = $"{OpacitySlider.Value:0}%";
        OpacitySlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty && !_loading)
            {
                _settings.CardOpacity = Settings.NormalizeOpacity(OpacitySlider.Value / 100.0);
                OpacityLabel.Text = $"{OpacitySlider.Value:0}%";
                Apply();
            }
        };

        BuildNewSections();
        BuildRound10Sections();
        BuildActionSections();
        BuildOutlookSection();
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

        CloseBtn.Click += (_, _) => Close();
        Opened += (_, _) => FitToScreen();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        _loading = false;
    }

    // ---------------- Outlook: optional, read-only ----------------

    private void BuildOutlookSection()
    {
        var o = _settings.Outlook;
        var service = _hooks?.Outlook;
        OutlookOn.IsChecked = o.Enabled;
        OutlookClientId.Text = o.ClientId;
        OutlookTenant.Text = o.TenantId;
        foreach (var (cloud, label) in new[] { (OutlookCloud.Commercial, "Microsoft 365"), (OutlookCloud.UsGovernment, "Microsoft 365 US Government") })
        {
            var rb = new RadioButton { Content = label, GroupName = "outlookcloud", IsChecked = cloud == o.Cloud, Margin = new Avalonia.Thickness(0, 0, 14, 2) };
            rb.IsCheckedChanged += (_, _) => { if (!_loading && rb.IsChecked == true) { o.Cloud = cloud; Apply(); PaintOutlook(); } };
            OutlookCloudPanel.Children.Add(rb);
        }
        foreach (int n in OutlookSettings.MaxItemChoices)
        {
            var rb = new RadioButton { Content = $"Show {n} messages", GroupName = "outlookmax", IsChecked = n == o.MaxItems, Margin = new Avalonia.Thickness(0, 0, 12, 2) };
            rb.IsCheckedChanged += (_, _) => { if (!_loading && rb.IsChecked == true) { o.MaxItems = n; Apply(); } };
            OutlookMaxPanel.Children.Add(rb);
        }

        void Save()
        {
            if (_loading) return;
            o.Enabled = OutlookOn.IsChecked == true;
            o.ClientId = (OutlookClientId.Text ?? "").Trim();
            o.TenantId = string.IsNullOrWhiteSpace(OutlookTenant.Text) ? "organizations" : OutlookTenant.Text!.Trim();
            if (!o.Enabled) service?.SignOut();      // turning it off also forgets the sign-in
            Apply();
            PaintOutlook();
        }
        OutlookOn.IsCheckedChanged += (_, _) => Save();
        OutlookClientId.LostFocus += (_, _) => Save();
        OutlookTenant.LostFocus += (_, _) => Save();

        void PaintOutlook()
        {
            var snap = service?.Snapshot ?? new OutlookSnapshot(o.Enabled ? OutlookState.NotSetUp : OutlookState.Off, o.Enabled ? "Outlook is not available in this window." : "Outlook is off.");
            OutlookFields.IsEnabled = o.Enabled;
            OutlookStatus.Text = (snap.State == OutlookState.SignedIn ? "● " : "○ ") + snap.Message;
            OutlookStatus.Foreground = Res(snap.State switch { OutlookState.SignedIn => "C.Good", OutlookState.Error => "C.Bad", OutlookState.SigningIn => "C.Warn", _ => "C.Ink2" });
            bool code = snap.State == OutlookState.SigningIn && !string.IsNullOrEmpty(snap.UserCode);
            OutlookCopy.IsVisible = code;
            OutlookOpen.IsVisible = code && OutlookService.IsMicrosoftAddress(snap.VerificationUrl);
            OutlookSignIn.IsEnabled = service is not null && snap.State is OutlookState.SignedOut or OutlookState.Error or OutlookState.NotSetUp or OutlookState.SignedIn && OutlookService.ValidateSetup(o) is null;
            OutlookSignOut.IsEnabled = service is not null && snap.State is OutlookState.SignedIn or OutlookState.SigningIn;
            OutlookSignIn.Content = snap.State == OutlookState.SignedIn ? "Sign in again" : "Sign in";
        }
        void OnChanged() => Avalonia.Threading.Dispatcher.UIThread.Post(PaintOutlook);
        if (service is not null) { service.Changed += OnChanged; Closed += (_, _) => service.Changed -= OnChanged; }
        PaintOutlook();

        OutlookSignIn.Click += async (_, _) => { Save(); if (service is not null) await service.SignInAsync(); };
        OutlookSignOut.Click += (_, _) => service?.SignOut();
        OutlookCopy.Click += async (_, _) =>
        {
            if (service?.Snapshot.UserCode is { Length: > 0 } c && Clipboard is { } clip) { await clip.SetTextAsync(c); OutlookCopy.Content = "Copied ✓"; Avalonia.Threading.DispatcherTimer.RunOnce(() => OutlookCopy.Content = "Copy code", TimeSpan.FromSeconds(1.4)); }
        };
        OutlookOpen.Click += async (_, _) =>
        {
            var url = service?.Snapshot.VerificationUrl;
            if (OutlookService.IsMicrosoftAddress(url) && TopLevel.GetTopLevel(this)?.Launcher is { } launcher) await launcher.LaunchUriAsync(new Uri(url!));
        };
    }

    // ---------------- round 10: connection, my API, writing, triggers, reference update ----------------

    private static Avalonia.Media.IBrush Res(string key) => (Avalonia.Media.IBrush)Avalonia.Application.Current!.Resources[key]!;

    private void PaintConnection()
    {
        var c = ConnectionStatus.Current;
        string brush = c.State switch { ConnectionState.Connected => "C.Good", ConnectionState.Failed => "C.Bad", ConnectionState.SetUp => "C.Warn", _ => "C.Ink3" };
        string back = c.State switch { ConnectionState.Connected => "C.GoodBg", ConnectionState.Failed => "C.BadBg", ConnectionState.SetUp => "C.WarnBg", _ => "C.Mark" };
        ConnBannerLabel.Text = c.Label; ConnBannerText.Text = c.Sentence;
        ConnBannerLabel.Foreground = Res(brush);
        ConnBannerDot.Stroke = Res(brush);
        ConnBannerDot.Fill = c.State == ConnectionState.Connected ? Res(brush) : Avalonia.Media.Brushes.Transparent;   // filled only when really connected
        ConnBanner.Background = Res(back);
    }

    private void BuildRound10Sections()
    {
        // ---- the connection banner, always true ----
        PaintConnection();
        void OnConn() => Avalonia.Threading.Dispatcher.UIThread.Post(PaintConnection);
        ConnectionStatus.Changed += OnConn;
        Closed += (_, _) => ConnectionStatus.Changed -= OnConn;

        // ---- which connection, and my own API ----
        var choices = new (ProviderChoice Value, string Label)[]
        {
            (ProviderChoice.Auto, "Auto"), (ProviderChoice.Offline, "Offline only"), (ProviderChoice.Claude, "Claude"), (ProviderChoice.MyApi, "My API"),
        };
        foreach (var (value, label) in choices)
        {
            var rb = new RadioButton { Content = label, GroupName = "provider", IsChecked = value == _settings.Provider, Margin = new Avalonia.Thickness(0, 0, 14, 2) };
            rb.IsCheckedChanged += (_, _) =>
            {
                if (_loading || rb.IsChecked != true) return;
                _settings.Provider = value;
                MyApiExpander.IsExpanded = value == ProviderChoice.MyApi;
                Apply();
                ConnStatus.Text = ConnectionService.DescribeFor(_settings);
            };
            ProviderPanel.Children.Add(rb);
        }
        var api = _settings.CustomApi;
        ApiName.Text = api.Name; ApiEndpoint.Text = api.Endpoint; ApiHeader.Text = api.AuthHeader; ApiScheme.Text = api.AuthScheme;
        ApiEnvVar.Text = api.SecretEnvVar; ApiModel.Text = api.Model; ApiTemplate.Text = api.RequestTemplate; ApiPath.Text = api.ResponsePath;
        MyApiExpander.IsExpanded = _settings.Provider == ProviderChoice.MyApi;
        void SaveApi()
        {
            if (_loading) return;
            api.Name = (ApiName.Text ?? "").Trim(); api.Endpoint = (ApiEndpoint.Text ?? "").Trim(); api.AuthHeader = (ApiHeader.Text ?? "").Trim();
            api.AuthScheme = (ApiScheme.Text ?? "").Trim(); api.SecretEnvVar = (ApiEnvVar.Text ?? "").Trim(); api.Model = (ApiModel.Text ?? "").Trim();
            api.RequestTemplate = string.IsNullOrWhiteSpace(ApiTemplate.Text) ? CustomApiSettings.ExampleTemplate : ApiTemplate.Text!;
            api.ResponsePath = (ApiPath.Text ?? "").Trim();
            Apply();
            ConnStatus.Text = ConnectionService.DescribeFor(_settings);
        }
        foreach (var box in new[] { ApiName, ApiEndpoint, ApiHeader, ApiScheme, ApiEnvVar, ApiModel, ApiTemplate, ApiPath }) box.LostFocus += (_, _) => SaveApi();
        foreach (int seconds in CustomApiSettings.TimeoutChoices)
        {
            var rb = new RadioButton { Content = $"{seconds} s", GroupName = "apitimeout", IsChecked = seconds == api.TimeoutSeconds, Margin = new Avalonia.Thickness(0, 0, 12, 2) };
            rb.IsCheckedChanged += (_, _) => { if (!_loading && rb.IsChecked == true) { api.TimeoutSeconds = seconds; Apply(); } };
            ApiTimeoutPanel.Children.Add(rb);
        }
        ConnStatus.Text = ConnectionService.DescribeFor(_settings);
        TestBtn.Click += async (_, _) =>
        {
            SaveApi();
            TestBtn.IsEnabled = false;
            ConnStatus.Text = "Testing…";
            ConnStatus.Text = await ConnectionService.TestForAsync(_settings);
            TestBtn.IsEnabled = true;
        };

        // ---- my writing: tone, style, sign-off, quick replies ----
        foreach (string tone in Settings.ToneChoices)
        {
            var rb = new RadioButton { Content = tone.Length == 0 ? "No tone preference" : tone, GroupName = "tone", IsChecked = tone == _settings.Tone, Margin = new Avalonia.Thickness(0, 0, 12, 2) };
            rb.IsCheckedChanged += (_, _) => { if (!_loading && rb.IsChecked == true) { _settings.Tone = tone; Apply(); } };
            TonePanel.Children.Add(rb);
        }
        SignOffBox.Text = _settings.SignOff;
        SignOffBox.LostFocus += (_, _) =>
        {
            if (_loading || SignOffBox.Text == _settings.SignOff) return;
            _settings.SignOff = (SignOffBox.Text ?? "").Trim(); Apply();
        };
        void PaintReplies() => RepliesSummary.Text = _replies.Replies.Count == 1 ? "1 quick reply" : $"{_replies.Replies.Count} quick replies";
        PaintReplies();
        RepliesManage.Click += (_, _) => new QuickRepliesWindow(_replies, () => { PaintReplies(); _onChanged(); }).Show(this);

        // ---- the Claude model name: change it here, no rebuild ----
        ClaudeModelBox.Text = _settings.ClaudeModel;
        ClaudeModelBox.LostFocus += (_, _) =>
        {
            if (_loading) return;
            string text = (ClaudeModelBox.Text ?? "").Trim();
            _settings.ClaudeModel = Settings.IsValidModelName(text) ? text : Settings.DefaultClaudeModel;
            ClaudeModelBox.Text = _settings.ClaudeModel;
            Apply();
            ConnStatus.Text = ConnectionService.DescribeFor(_settings);
        };

        BuildAdaptive();       // sections that arrange themselves, the section jump list and the Getting started card

        // ---- back up, restore, reset ----
        BackupSave.Click += async (_, _) =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save a backup of my settings", SuggestedFileName = $"anda-companion-backup-{DateTime.Now:yyyy-MM-dd}.json",
                DefaultExtension = "json", FileTypeChoices = new[] { new FilePickerFileType("Backup") { Patterns = new[] { "*.json" } } },
            });
            if (file?.TryGetLocalPath() is not { } path) return;
            try { File.WriteAllText(path, SettingsBackup.Export(_settings, _prompts, _replies)); BackupStatus.Text = "Saved. It holds no secret and no file location."; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { BackupStatus.Text = "The backup could not be saved there. Please choose another folder."; }
        };
        BackupRestore.Click += async (_, _) =>
        {
            string? path = await PickFileAsync("Choose a backup to restore", new[] { "*.json" }, "Backup");
            if (path is null) return;
            string text;
            try { text = new FileInfo(path).Length > SettingsBackup.MaxBytes ? "" : File.ReadAllText(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { BackupStatus.Text = "That file could not be read."; return; }
            var (ok, message) = SettingsBackup.Import(text, _settings, _prompts, _replies);
            BackupStatus.Text = message;
            if (!ok) return;
            Apply();
            RebuildActions(); RebuildAgents();           // the editors now hold the restored lists, not the old ones
            BackupStatus.Text = message + " Please open Settings again to see the new values.";
            Avalonia.Threading.DispatcherTimer.RunOnce(Close, TimeSpan.FromSeconds(1.2));
        };
        bool armed = false;
        SettingsReset.Click += (_, _) =>
        {
            if (!armed)
            {
                armed = true; SettingsReset.Content = "Click again to reset";
                BackupStatus.Text = "This puts every setting, lens and quick reply back to the starting values. File locations are kept.";
                Avalonia.Threading.DispatcherTimer.RunOnce(() => { armed = false; SettingsReset.Content = "Reset all settings…"; }, TimeSpan.FromSeconds(6));
                return;
            }
            SettingsBackup.Reset(_settings, _prompts, _replies);
            Apply();
            RebuildActions(); RebuildAgents();
            BackupStatus.Text = "Everything is back to the starting values. Please open Settings again.";
            Avalonia.Threading.DispatcherTimer.RunOnce(Close, TimeSpan.FromSeconds(1.2));
        };

        // ---- the middle-click trigger (off by default) ----
        MiddleCheck.IsChecked = _settings.MiddleClickTrigger;
        void PaintMouse() => Avalonia.Threading.DispatcherTimer.RunOnce(() => MiddleStatus.Text = _settings.MiddleClickTrigger ? _hooks?.MouseStatus?.Invoke() ?? "" : "Off.", TimeSpan.FromMilliseconds(400));
        MiddleCheck.IsCheckedChanged += (_, _) => { if (_loading) return; _settings.MiddleClickTrigger = MiddleCheck.IsChecked == true; Apply(); PaintMouse(); };
        foreach (string mod in Settings.MiddleClickModifiers)
        {
            var rb = new RadioButton { Content = mod == "None" ? "no key" : mod, GroupName = "middlemod", IsChecked = mod == _settings.MiddleClickModifier, Margin = new Avalonia.Thickness(0, 0, 10, 2) };
            rb.IsCheckedChanged += (_, _) => { if (!_loading && rb.IsChecked == true) { _settings.MiddleClickModifier = mod; Apply(); } };
            MiddleModPanel.Children.Add(rb);
        }
        MiddleStatus.Text = _settings.MiddleClickTrigger ? _hooks?.MouseStatus?.Invoke() ?? "" : "Off.";

        // ---- live update of the Drugs@FDA file: only when pressed ----
        ReferenceUrlBox.Text = _settings.ReferenceDataUrl ?? "";
        ReferenceUpdate.Click += async (_, _) =>
        {
            string url = (ReferenceUrlBox.Text ?? "").Trim();
            _settings.ReferenceDataUrl = url.Length == 0 ? null : url; Apply();
            ReferenceUpdate.IsEnabled = false;
            ReferenceUpdateStatus.Text = "Fetching… (this can take a minute)";
            var (ok, message) = await ReferenceDownloader.UpdateAsync(url, AppPaths.ReferenceDownloadFile);
            if (ok) { _settings.ReferenceDataPath = AppPaths.ReferenceDownloadFile; ReferenceBox.Text = AppPaths.ReferenceDownloadFile; Apply(); }
            ReferenceUpdateStatus.Text = message;
            ReferenceUpdate.IsEnabled = true;
            Avalonia.Threading.DispatcherTimer.RunOnce(RefreshPortfolioStatus, TimeSpan.FromMilliseconds(900));
        };
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

        ExtIdBox.Text = _settings.ExtensionId;
        void PaintExt(string? extra = null)
        {
            var st = NativeHostRegistration.Status();
            ExtStatus.Text = (extra is null ? "" : extra + " ") + st.Message;
        }
        PaintExt();
        ExtConnect.Click += (_, _) =>
        {
            string id = (ExtIdBox.Text ?? "").Trim();
            if (!NativeHostRegistration.IsValidExtensionId(id)) { PaintExt("That is not an extension ID (32 letters from a to p; copy it from the extension's Settings page)."); return; }
            string exe = Environment.ProcessPath ?? "";
            if (!Path.GetFileNameWithoutExtension(exe).Equals("AndaCompanion", StringComparison.OrdinalIgnoreCase))
            { PaintExt("Connect from the installed AndaCompanion.exe. This copy was started through a development tool, so the browser could not start it."); return; }
            _settings.ExtensionId = id; Apply();
            var (_, message) = NativeHostRegistration.Install(exe, id);
            PaintExt(message);
        };
        ExtDisconnect.Click += (_, _) => { var (_, message) = NativeHostRegistration.Uninstall(); PaintExt(message); };

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
            var others = new List<(string, string)>
            {
                ("Select & Peek", ReferenceEquals(box, PeekKeyBox) ? normalized : _settings.PeekHotkey),
                ("Lookup box", ReferenceEquals(box, LookupKeyBox) ? normalized : _settings.LookupHotkey),
                ("Check a draft", ReferenceEquals(box, DraftKeyBox) ? normalized : _settings.DraftHotkey),
            };
            if (!string.IsNullOrWhiteSpace(_settings.ChecklistHotkey)) others.Add(("My checklist", _settings.ChecklistHotkey));
            foreach (var a in _settings.MyActions.Where(a => !string.IsNullOrWhiteSpace(a.Hotkey))) others.Add((a.Name, a.Hotkey));
            if (HotkeyParser.ValidateSet(others.ToArray()) is { } clash) { KeyStatus.Text = clash; return; }
            set(normalized); box.Text = normalized; Apply();
            KeyStatus.Text = $"{name}: {normalized}. Checking with Windows…";
            Avalonia.Threading.DispatcherTimer.RunOnce(() => KeyStatus.Text = _hooks?.HotkeyStatus() ?? $"{name}: {normalized}", TimeSpan.FromMilliseconds(500));
        };
        reset.Click += (_, _) =>
        {
            string before = get();
            set(fallback);
            var all = new List<(string, string)> { ("Select & Peek", _settings.PeekHotkey), ("Lookup box", _settings.LookupHotkey), ("Check a draft", _settings.DraftHotkey) };
            if (!string.IsNullOrWhiteSpace(_settings.ChecklistHotkey)) all.Add(("My checklist", _settings.ChecklistHotkey));
            foreach (var a in _settings.MyActions.Where(a => !string.IsNullOrWhiteSpace(a.Hotkey))) all.Add((a.Name, a.Hotkey));
            if (HotkeyParser.ValidateSet(all.ToArray()) is { } clash)
            { set(before); box.Text = before; KeyStatus.Text = clash; return; }          // refused: keep what was there, not a clashing pair
            box.Text = fallback;
            Apply();
            Avalonia.Threading.DispatcherTimer.RunOnce(() => KeyStatus.Text = _hooks?.HotkeyStatus() ?? "", TimeSpan.FromMilliseconds(500));
        };
    }

    private void RefreshPortfolioStatus()
    {
        PaintGettingStarted();
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
