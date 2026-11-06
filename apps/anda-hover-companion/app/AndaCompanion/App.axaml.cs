using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AndaCompanion.Data;
using AndaCompanion.Models;
using AndaCompanion.Services;
using AndaCompanion.Services.Portfolio;
using AndaCompanion.Theming;
using AndaCompanion.UI;

namespace AndaCompanion;

public partial class App : Application
{
    /// <summary>Set by Program.Main when launched with --selftest (a headless-friendly diagnostic run).</summary>
    public static SelfTestOptions? SelfTestRun { get; set; }

    /// <summary>Raised on the hotkey thread for every hotkey press while a self-test is running.</summary>
    internal static event Action<int>? HotkeyObserved;

    private readonly MockAndaRepository _demo = new();
    private readonly SwitchableRepository _repository;
    private PortfolioRepository? _portfolio;
    private DrugsAtFdaFile? _drugs;
    private string _drugsMessage = "";
    private bool _drugsLoading;
    private string? _drugsLoadedFrom;
    private BusinessCalendar _calendar = new();
    private DraftWindow? _draftWindow;
    private readonly PromptLibraryService _prompts = new();
    private int _captureBusy;
    private string? _trayError;
    private ICompanionBriefService _briefs = new OfflineBriefService();
    private readonly QuickReplyService _replies = new();
    private OutlookService? _outlook;
    private MouseTriggerService? _mouse;
    private string? _mouseProblem;
    private string _providerFingerprint = "";
    private Settings _settings = new();
    private HotkeyService? _hotkeys;
    private TrayIcon? _tray;
    private CardWindow? _card;
    private CompactCardWindow? _compact;
    private SettingsWindow? _settingsWindow;
    private (AndaRecord Record, PixelPoint Near, bool Mini)? _lastCard;

    private readonly TodoService _todo = new();
    private readonly AgentService _agents;
    private volatile IReadOnlyList<CustomAction> _boundActions = Array.Empty<CustomAction>();
    private string _agentsFingerprint = "";
    private ActionRunner? _runner;
    private ActionWindow? _actionWindow;
    private TodoWindow? _todoWindow;
    private NativeMenuItem? _actionsMenu;

    public App()
    {
        _repository = new SwitchableRepository(_demo);
        _agents = new AgentService(() => _settings.Agents);
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        _settings = SettingsService.Load();
        ThemeManager.Apply(Edition.Appearance(_settings), Edition.Opacity(_settings));
        _calendar = BusinessCalendar.FromFile(AppPaths.ClosuresFile);
        UsageLog.Configure(_settings.UsageLog && SelfTestRun is null);
        ApplyData();
        _briefs = BriefServiceFactory.Create(() => _prompts.GetText(_settings.ActiveLens), _settings);
        _providerFingerprint = ProviderFingerprint();
        _agentsFingerprint = AgentsFingerprint();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;

            // Hotkeys first, so the tray menu can state truthfully which ones are live.
            _hotkeys = new HotkeyService(
                onHotkey: id => _ = OnHotkeyAsync(id),
                onProblem: message => Dispatcher.UIThread.Post(() => ShowNotice(message)),
                bindings: HotkeyBindings());
            _hotkeys.Start();
            _hotkeys.WaitReady(TimeSpan.FromMilliseconds(1500));

            // A tray failure must be reported, not fatal: the hotkeys still work without it.
            try { _tray = BuildTrayIcon(desktop); }
            catch (Exception ex) { _trayError = ex.Message; }
            desktop.Exit += (_, _) => { _hotkeys.Dispose(); _mouse?.Dispose(); _outlook?.Dispose(); _tray?.Dispose(); };
            ConnectionStatus.Changed += () => Dispatcher.UIThread.Post(UpdateTrayTip);
            UpdateTrayTip();
            ApplyMouseTrigger();

            if (SelfTestRun is { } selfTest)
            {
                // Diagnostic run: render everything, report, exit. Never opens first-run windows.
                new Thread(() => { Thread.Sleep(TimeSpan.FromSeconds(150)); Environment.Exit(2); })
                    { IsBackground = true, Name = "AndaCompanion.SelfTestWatchdog" }.Start();
                Dispatcher.UIThread.Post(async () =>
                {
                    int code = await SelfTest.RunAsync(selfTest, _hotkeys.Status, _tray is not null, _trayError, _repository);
                    desktop.Shutdown(code);
                }, DispatcherPriority.Background);
            }
            else if (!_settings.FirstRunDone)
            {
                _settings.FirstRunDone = true;
                SettingsService.Save(_settings);
                DemoCard("216482");      // see the product immediately; nothing has to be set up first
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    // ---------- hotkey / consent flow ----------

    /// <summary>
    /// Runs on the hotkey thread. Capture touches the clipboard and keyboard state and polls, so it
    /// is pushed to the thread pool; only the resulting UI work is marshalled to the UI thread.
    /// A second press while a capture is in flight is ignored.
    /// </summary>
    private async Task OnHotkeyAsync(int id)
    {
        // Self-test: only report that the key arrived. No capture, no windows, no side effects.
        if (SelfTestRun is not null) { HotkeyObserved?.Invoke(id); return; }

        if (id == HotkeyService.IdLookup) { Dispatcher.UIThread.Post(OpenLookup); return; }
        if (id == HotkeyService.IdDraft) { await OnDraftHotkeyAsync(); return; }
        if (id == HotkeyService.IdAsk) { await OnAskHotkeyAsync(); return; }
        if (id == HotkeyService.IdMyTodo) { await OnMyTodoHotkeyAsync(); return; }
        if (id >= HotkeyService.IdActionBase) { await OnActionHotkeyAsync(id - HotkeyService.IdActionBase); return; }

        if (Interlocked.Exchange(ref _captureBusy, 1) == 1) return;
        try
        {
            var cursor = TextCaptureService.GetCursorPosition();
            var near = new PixelPoint(cursor.X, cursor.Y);

            if (!_settings.CaptureViaCopy) { Dispatcher.UIThread.Post(OpenLookup); return; }

            var result = await Task.Run(TextCaptureService.CaptureSelection);
            Dispatcher.UIThread.Post(() => HandleCapture(result, near));
        }
        finally { Interlocked.Exchange(ref _captureBusy, 0); }
    }

    private void HandleCapture(CaptureResult result, PixelPoint near)
    {
        // Something the user should hear about (terminal in front, modifiers held, clipboard busy):
        // say so, then still offer the lookup box so the press is never a dead end.
        if (result.Notice is { } notice) { ShowNotice(notice, near); OpenLookup(); return; }

        string? number = result.Outcome == CaptureOutcome.Captured ? AndaValidator.Extract(result.Text) : null;
        if (number is null) { OpenLookup(); return; }

        var record = _repository.Find(number);
        if (record is null)
        {
            ShowNotice(NotFoundMessage(number), near);
            return;
        }

        // A compact box never takes focus, so the keyboard way to open it is to press the hotkey again on the
        // same number: that is the explicit second act, whatever the consent model says.
        if (_compact is { IsVisible: true } open && open.Number == record.Number)
        {
            OpenFullCard(record, near, mini: false);
            return;
        }

        // Two-stage consent: the hotkey is stage one; what happens next depends on the model. In whisper mode an urgent or at-risk
        // ANDA skips the invite and opens a peek (no AI call). The person's own selection and shortcut still started it.
        bool urgent = _settings.EscalateUrgent && UrgencyPolicy.IsUrgent(record, Today(), _calendar, _settings.UrgentLateDays);
        switch (UrgencyPolicy.Decide(_settings.Consent, _settings.EscalateUrgent, urgent))
        {
            case FirstStep.Invite:
                new ChipWindow().ShowInvite($"ANDA {record.Number}", near, () => OpenCard(record, near, mini: false));
                break;
            case FirstStep.Peek:
                if (_settings.Consent == ConsentModel.Whisper) UsageLog.Record("peek_auto_open");
                OpenCard(record, near, mini: true);
                break;
            default:
                OpenCard(record, near, mini: false);
                break;
        }
    }

    private static void ShowNotice(string message, PixelPoint? near = null)
    {
        var at = near ?? new PixelPoint(TextCaptureService.GetCursorPosition().X, TextCaptureService.GetCursorPosition().Y);
        new ChipWindow().ShowNotice(message, at);
    }

    private void OpenLookup()
    {
        var cursor = TextCaptureService.GetCursorPosition();
        new LookupWindow(number =>
        {
            var record = _repository.Find(number);
            var near = new PixelPoint(cursor.X, cursor.Y);
            if (record is null) new ChipWindow().ShowNotice(NotFoundMessage(number), near);
            else OpenCard(record, near, _settings.Consent == ConsentModel.PeekFirst);
        }).Show();
    }

    /// <summary>
    /// Opens the quick-look for a record in the chosen appearance. A compact appearance (L–O) opens its small box first —
    /// no AI call — and a click expands it into the sibling look's full card; every other appearance opens the card.
    /// </summary>
    public void OpenCard(AndaRecord record, PixelPoint near, bool mini)
    {
        _lastCard = (record, near, mini);
        if (ThemeManager.Current.IsCompact)
        {
            CloseOpenCards();
            var compact = new CompactCardWindow(record, () => OpenFullCard(record, near, mini: false))
                { AutoHideSeconds = _settings.AutoHideSeconds };
            compact.Closed += (_, _) => { if (ReferenceEquals(_compact, compact)) _compact = null; };
            _compact = compact;
            compact.ShowNear(near);
            return;
        }
        OpenFullCard(record, near, mini);
    }

    private void OpenFullCard(AndaRecord record, PixelPoint near, bool mini)
    {
        CloseOpenCards();
        var card = new CardWindow(_briefs, _repository.Find)
        {
            AutoHideSeconds = _settings.AutoHideSeconds,
            Calendar = _calendar, Watch = new WatchSettings(HolidayLeadBusinessDays: _settings.HolidayLeadDays),
            Today = Today, Reference = ReferenceFor, QuickReplies = () => _replies.Replies,
            Outlook = Outlook,
            Agents = _agents, AtRisk = IsAtRisk, AddTodos = AddAgentTodos,
        };
        card.Closed += (_, _) => { if (ReferenceEquals(_card, card)) _card = null; };
        _card = card;
        card.Show(record, near, mini);
    }

    private void CloseOpenCards()
    {
        _card?.Close();
        _compact?.Close();
    }

    /// <summary>Created on first use, so an app that never turns Outlook on never builds it.</summary>
    private OutlookService Outlook => _outlook ??= new OutlookService(() => _settings.Outlook);

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

    private string NotFoundMessage(string number) =>
        _portfolio is { Status.Ok: true } p ? $"{number} is not in your portfolio ({p.FileName})" : $"{number} is not in the available records.";

    // ---------- my data: the fictional demo records, or the person's own portfolio ----------

    private void ApplyData()
    {
        if (!string.IsNullOrWhiteSpace(_settings.PortfolioPath) && SelfTestRun is null)
        {
            var repo = new PortfolioRepository(_settings.PortfolioPath!, _settings.PortfolioColumns, _settings.NotesFolder);
            _portfolio = repo;
            _repository.Current = repo.Status.Ok ? repo : _demo;          // an unreadable file falls back to the demo records, and Settings says why
        }
        else { _portfolio = null; _repository.Current = _demo; }

        // The stamp includes the file's time, so a file replaced by Update now (same path) is read again.
        string? stamp = _settings.ReferenceDataPath is { Length: > 0 } p0 ? p0 + "|" + (File.Exists(p0) ? File.GetLastWriteTimeUtc(p0).Ticks : 0) : null;
        if (_settings.ReferenceDataPath is { Length: > 0 } path && stamp != _drugsLoadedFrom && !_drugsLoading && SelfTestRun is null)
        {
            _drugsLoading = true; _drugsMessage = "Reading the Drugs@FDA file…"; _drugsLoadedFrom = stamp;
            Task.Run(() => DrugsAtFdaFile.TryLoad(path, out var msg) is { } f ? (f, msg) : (null, msg)).ContinueWith(t =>
                Dispatcher.UIThread.Post(() => { _drugs = t.Result.Item1; _drugsMessage = t.Result.msg; _drugsLoading = false; }));
        }
        else if (string.IsNullOrWhiteSpace(_settings.ReferenceDataPath)) { _drugs = null; _drugsMessage = ""; _drugsLoadedFrom = null; }
    }

    private ReferenceInfo? ReferenceFor(AndaRecord record) =>
        ReferenceResolver.For(record, _drugs, _drugsMessage, _settings.ReferenceDataPath, _drugsLoading);

    // ---------- review a draft ----------

    private async Task OnDraftHotkeyAsync()
    {
        if (Interlocked.Exchange(ref _captureBusy, 1) == 1) return;
        try
        {
            var cursor = TextCaptureService.GetCursorPosition();
            var near = new PixelPoint(cursor.X, cursor.Y);
            string text = "";
            if (_settings.CaptureViaCopy)
            {
                var result = await Task.Run(TextCaptureService.CaptureSelection);
                if (result.Notice is { } notice) Dispatcher.UIThread.Post(() => ShowNotice(notice, near));
                else if (result.Outcome == CaptureOutcome.Captured) text = result.Text ?? "";
            }
            Dispatcher.UIThread.Post(() => OpenDraft(text));
        }
        finally { Interlocked.Exchange(ref _captureBusy, 0); }
    }

    private void OpenDraft(string text)
    {
        if (_draftWindow is not null) { _draftWindow.Close(); _draftWindow = null; }
        var win = new DraftWindow(text, _repository.Find, new WritingService(ChatCompleterFactory.CreateReporting(_settings)), _settings, _prompts, Today, _calendar);
        win.Closed += (_, _) => { if (ReferenceEquals(_draftWindow, win)) _draftWindow = null; };
        _draftWindow = win;
        win.Show();
    }

    private WatchListWindow? _watchWindow;

    private CardContext CardCtx() => new(Today(), _calendar, _settings.HolidayLeadDays, _settings.UrgentLateDays, ReferenceFor);

    private void OpenWatchList()
    {
        if (_watchWindow is not null) { _watchWindow.Reload(); _watchWindow.Activate(); return; }
        var win = new WatchListWindow(() => WatchList.Build(_repository, CardCtx()), (number, near) =>
        {
            if (_repository.Find(number) is { } record) OpenCard(record, near, mini: false);
        });
        win.Closed += (_, _) => _watchWindow = null;
        _watchWindow = win;
        win.Show();
    }

    private void OpenSettings()
    {
        if (_settingsWindow is not null) { _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsWindow(_settings, _prompts, ApplySettings,
            new SettingsHooks(() => HotkeyService.Report(_hotkeys?.Status ?? Array.Empty<HotkeyStatus>()), () => _portfolio, () => _drugsMessage, MouseStatusLine, Outlook,
                OpenTodo: OpenTodoWindow, RunAction: RunActionFromMenu, AgentProblem: _agents.SetupProblem, ClearFinished: _todo.ClearCompleted,
                OpenMyTodo: OpenMyTodoWindow, OpenNotes: () => OpenNotes(""), ClearMyTodoFinished: _myTodo.ClearFinished, Hotkeys: () => _hotkeys?.Status ?? Array.Empty<HotkeyStatus>()), _replies);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    // ---------- tray ----------

    private TrayIcon BuildTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var menu = new NativeMenu();

        var lookup = _lookupItem = new NativeMenuItem($"Lookup ANDA…\t{_settings.LookupHotkey}");
        lookup.Click += (_, _) => OpenLookup();
        menu.Add(lookup);
        var draft = _draftItem = new NativeMenuItem($"Review a draft…\t{_settings.DraftHotkey}");
        draft.Click += (_, _) => OpenDraft("");
        menu.Add(draft);
        var ask = _askItem = new NativeMenuItem(AskMenuLabel());
        ask.Click += (_, _) => OpenAsk("");
        menu.Add(ask);
        var watch = new NativeMenuItem("Watch list…");
        watch.Click += (_, _) => OpenWatchList();
        menu.Add(watch);
        var todo = new NativeMenuItem("ANDA to-do list…");
        todo.Click += (_, _) => OpenTodoWindow();
        menu.Add(Full(todo));
        var todoItem = _myTodoItem = new NativeMenuItem(MyTodoMenuLabel());
        todoItem.Click += (_, _) => OpenMyTodoWindow();
        menu.Add(Full(todoItem));
        var notes = new NativeMenuItem("Notes to my to-do list…");
        notes.Click += (_, _) => OpenNotes("");
        menu.Add(Full(notes));
        _actionsMenu = new NativeMenuItem("My actions") { Menu = new NativeMenu() };
        RebuildActionsMenu();
        menu.Add(_actionsMenu);

        var demoRisk = new NativeMenuItem("Demo: at-risk card");
        demoRisk.Click += (_, _) => DemoCard("216482");
        var demoHealthy = new NativeMenuItem("Demo: healthy card");
        demoHealthy.Click += (_, _) => DemoCard("214990");
        menu.Add(demoRisk);
        menu.Add(demoHealthy);
        menu.Add(new NativeMenuItemSeparator());

        menu.Add(Full(BuildRadioSubmenu("Appearance", Appearances.All.Select(a => (a.Name, (object)a.Id)),
            () => (object)_settings.Appearance,
            value => { _settings.Appearance = (AppearanceId)value; ApplySettings(); },
            separatorBefore: Appearances.L.Name)));
        menu.Add(BuildRadioSubmenu("Auto-hide", AutoHidePolicy.Choices.Select(c => (AutoHidePolicy.Label(c), (object)c)),
            () => (object)_settings.AutoHideSeconds,
            value => { _settings.AutoHideSeconds = (int)value; ApplySettings(); }));

        var settings = new NativeMenuItem("Settings…");
        settings.Click += (_, _) => OpenSettings();
        menu.Add(settings);

        var prompts = new NativeMenuItem("Prompt library…");
        prompts.Click += (_, _) =>
            new PromptLibraryWindow(_prompts, _settings, ApplySettings).Show();
        menu.Add(Full(prompts));

        menu.Add(new NativeMenuItemSeparator());
        menu.Add(_hotkeyLine = new NativeMenuItem(HotkeyStatusLine()) { IsEnabled = false });
        var exit = new NativeMenuItem("Exit");
        exit.Click += (_, _) => desktop.Shutdown();
        menu.Add(exit);

        var tray = new TrayIcon
        {
            Icon = new WindowIcon(RenderTrayBitmap()),
            ToolTipText = "ANDA Companion (Notula) · fictional demo data",
            Menu = menu,
        };
        tray.Clicked += (_, _) => OpenLookup();
        tray.IsVisible = true;
        ShowToolsInTray();
        return tray;
    }

    private readonly List<NativeMenuItem> _fullOnly = new();

    /// <summary>Marks a tray item as one that only "Show all tools" shows. The focused menu is Lookup, Review a draft, Ask, the watch list and Settings.</summary>
    private T Full<T>(T item) where T : NativeMenuItem { _fullOnly.Add(item); return item; }

    private void ShowToolsInTray() { foreach (var item in _fullOnly) item.IsVisible = _settings.ShowAllTools; }

    /// <summary>One honest line in the tray menu: which hotkeys are live, and which are taken.</summary>
    private string HotkeyStatusLine()
    {
        var status = _hotkeys?.Status;
        if (status is null || status.Count == 0) return "Hotkeys: starting…";
        return "Hotkeys: " + string.Join("   ", status.Select(h => $"{h.Binding.Combo} {(h.Registered ? "✓" : "✗ in use")}"));
    }

    private NativeMenuItem? _lookupItem, _draftItem, _askItem, _myTodoItem, _hotkeyLine;

    private string MyTodoMenuLabel() => string.IsNullOrWhiteSpace(_settings.MyTodoHotkey) ? "My to-do list…" : $"My to-do list…\t{_settings.MyTodoHotkey}";

    /// <summary>The tray menu names the shortcuts as they are now, not as they were when the app started. The live line waits a moment for Windows to answer.</summary>
    private void RefreshTrayLabels()
    {
        if (_lookupItem is not null) _lookupItem.Header = $"Lookup ANDA…\t{_settings.LookupHotkey}";
        if (_draftItem is not null) _draftItem.Header = $"Review a draft…\t{_settings.DraftHotkey}";
        if (_askItem is not null) _askItem.Header = AskMenuLabel();
        if (_myTodoItem is not null) _myTodoItem.Header = MyTodoMenuLabel();
        ShowToolsInTray();
        Avalonia.Threading.DispatcherTimer.RunOnce(() => { if (_hotkeyLine is not null) _hotkeyLine.Header = HotkeyStatusLine(); }, TimeSpan.FromMilliseconds(600));
    }

    private NativeMenuItem BuildRadioSubmenu(
        string title, IEnumerable<(string Label, object Value)> options,
        Func<object> current, Action<object> select, string? separatorBefore = null)
    {
        var parent = new NativeMenuItem(title) { Menu = new NativeMenu() };
        foreach (var (label, value) in options)
        {
            if (label == separatorBefore) parent.Menu.Add(new NativeMenuItemSeparator());
            var item = new NativeMenuItem(label)
            {
                ToggleType = NativeMenuItemToggleType.Radio,
                IsChecked = Equals(current(), value),
            };
            item.Click += (_, _) =>
            {
                select(value);
                foreach (var sibling in parent.Menu!.Items.OfType<NativeMenuItem>())
                    sibling.IsChecked = ReferenceEquals(sibling, item);
            };
            parent.Menu.Add(item);
        }
        return parent;
    }

    private void ApplySettings()
    {
        SettingsService.Save(_settings);
        ThemeManager.Apply(Edition.Appearance(_settings), Edition.Opacity(_settings));
        UsageLog.Configure(_settings.UsageLog && SelfTestRun is null);
        _hotkeys?.Reconfigure(HotkeyBindings());
        RefreshTrayLabels();
        // A new brief service only when the connection itself changed: changing a look must not reset "connected" to "not tested".
        if (ProviderFingerprint() != _providerFingerprint)
        {
            _providerFingerprint = ProviderFingerprint();
            _briefs = BriefServiceFactory.Create(() => _prompts.GetText(_settings.ActiveLens), _settings);
        }
        ApplyMouseTrigger();
        ApplyData();
        // Findings from an agent that has since been changed or switched off are not shown any more.
        if (AgentsFingerprint() != _agentsFingerprint) { _agentsFingerprint = AgentsFingerprint(); _agents.ClearCache(); }
        RebuildActionsMenu();
        if (_todoWindow is { } todoWindow) { todoWindow.ApplyTimer(); todoWindow.Reload(force: true); }
        // Live preview: if a card is open, rebuild it in place with the new look.
        if ((_card is not null || _compact is not null) && _lastCard is { } last)
            OpenCard(last.Record, last.Near, last.Mini);
    }

    /// <summary>Which connection is chosen and how it is described, never a secret (the token is only ever an environment variable's name).</summary>
    private string ProviderFingerprint() => _settings.Provider + "|" + System.Text.Json.JsonSerializer.Serialize(_settings.CustomApi);

    private void UpdateTrayTip()
    {
        if (_tray is null) return;
        _tray.ToolTipText = $"ANDA Companion (Notula) · {ConnectionStatus.Current.Label}";
    }

    /// <summary>Starts or stops the middle-click trigger to match Settings. The hook exists only while the setting is on.</summary>
    private void ApplyMouseTrigger()
    {
        bool want = _settings.MiddleClickTrigger && SelfTestRun is null && OperatingSystem.IsWindows();
        if (!want) { _mouse?.Dispose(); _mouse = null; _mouseProblem = null; return; }
        if (_mouse is not null) return;                       // the chosen key is read live on every click
        var service = new MouseTriggerService(() => _settings.MiddleClickModifier, () => _ = OnHotkeyAsync(HotkeyService.IdPeek));
        if (service.Start()) { _mouse = service; _mouseProblem = null; }
        else { _mouseProblem = service.Problem; service.Dispose(); ShowNotice(_mouseProblem ?? "The middle-click trigger could not start."); }
    }

    private string? MouseStatusLine() =>
        _mouse is not null
            ? $"On: {(_settings.MiddleClickModifier == "None" ? "" : _settings.MiddleClickModifier + " + ")}middle-click on selected text."
            : _mouseProblem ?? "Not running.";

    private void DemoCard(string number)
    {
        if (_demo.Find(number) is { } record)
        {
            var cursor = TextCaptureService.GetCursorPosition();
            OpenCard(record, new PixelPoint(cursor.X, cursor.Y), mini: false);
        }
    }

    /// <summary>The Loupe icon for the tray. <paramref name="needsLook"/> adds the small amber dot (the picture is ready; it is not switched on until the new design is ported). If the picture cannot be read, a plain drawn tile is used so the tray never has no icon.</summary>
    private static Bitmap RenderTrayBitmap(bool needsLook = false)
    {
        try
        {
            using var stream = Avalonia.Platform.AssetLoader.Open(new Uri(needsLook ? "avares://AndaCompanion/Assets/tray-32-alert.png" : "avares://AndaCompanion/Assets/tray-32.png"));
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            var rtb = new RenderTargetBitmap(new PixelSize(32, 32), new Vector(96, 96));
            using (var ctx = rtb.CreateDrawingContext())
            {
                // A fixed indigo tile with a white ring, so the glyph reads on a light and on a dark Windows taskbar.
                ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#3A3F8F")), null, new RoundedRect(new Rect(2, 2, 28, 28), 8));
                ctx.DrawEllipse(null, new Pen(Brushes.White, 3), new Point(14, 14), 7, 7);
            }
            return rtb;
        }
    }
}
