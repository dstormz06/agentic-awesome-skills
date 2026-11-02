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
using AndaCompanion.Theming;
using AndaCompanion.UI;

namespace AndaCompanion;

public partial class App : Application
{
    /// <summary>Set by Program.Main when launched with --selftest (a headless-friendly diagnostic run).</summary>
    public static SelfTestOptions? SelfTestRun { get; set; }

    /// <summary>Raised on the hotkey thread for every hotkey press while a self-test is running.</summary>
    internal static event Action<int>? HotkeyObserved;

    private readonly IAndaRepository _repository = new MockAndaRepository();
    private readonly PromptLibraryService _prompts = new();
    private int _captureBusy;
    private string? _trayError;
    private ICompanionBriefService _briefs = new OfflineBriefService();
    private Settings _settings = new();
    private HotkeyService? _hotkeys;
    private TrayIcon? _tray;
    private CardWindow? _card;
    private CompactCardWindow? _compact;
    private SettingsWindow? _settingsWindow;
    private (AndaRecord Record, PixelPoint Near, bool Mini)? _lastCard;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        _settings = SettingsService.Load();
        ThemeManager.Apply(_settings.Appearance, _settings.CardOpacity);
        _briefs = BriefServiceFactory.Create(() => _prompts.GetText(_settings.ActiveLens));

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;

            // Hotkeys first, so the tray menu can state truthfully which ones are live.
            _hotkeys = new HotkeyService(
                onHotkey: id => _ = OnHotkeyAsync(id),
                onProblem: message => Dispatcher.UIThread.Post(() => ShowNotice(message)));
            _hotkeys.Start();
            _hotkeys.WaitReady(TimeSpan.FromMilliseconds(1500));

            // A tray failure must be reported, not fatal: the hotkeys still work without it.
            try { _tray = BuildTrayIcon(desktop); }
            catch (Exception ex) { _trayError = ex.Message; }
            desktop.Exit += (_, _) => { _hotkeys.Dispose(); _tray?.Dispose(); };

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
                OpenSettings();          // 5-minute setup: pick appearance + consent, test connection
                DemoCard("216482");      // and see the product immediately
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
            ShowNotice($"{number} is not a recognized ANDA", near);
            return;
        }

        // A compact box never takes focus, so the keyboard way to open it is to press the hotkey again on the
        // same number: that is the explicit second act, whatever the consent model says.
        if (_compact is { IsVisible: true } open && open.Number == record.Number)
        {
            OpenFullCard(record, near, mini: false);
            return;
        }

        // Two-stage consent: the hotkey is stage one; what happens next depends on the model.
        switch (_settings.Consent)
        {
            case ConsentModel.Whisper:
                new ChipWindow().ShowInvite($"ANDA {record.Number}", near, () => OpenCard(record, near, mini: false));
                break;
            case ConsentModel.PeekFirst:
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
            if (record is null) new ChipWindow().ShowNotice($"{number} is not a recognized ANDA", near);
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
        var card = new CardWindow(_briefs, _repository.Find) { AutoHideSeconds = _settings.AutoHideSeconds };
        card.Closed += (_, _) => { if (ReferenceEquals(_card, card)) _card = null; };
        _card = card;
        card.Show(record, near, mini);
    }

    private void CloseOpenCards()
    {
        _card?.Close();
        _compact?.Close();
    }

    private void OpenSettings()
    {
        if (_settingsWindow is not null) { _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsWindow(_settings, _prompts, ApplySettings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    // ---------- tray ----------

    private TrayIcon BuildTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var menu = new NativeMenu();

        var lookup = new NativeMenuItem("Lookup ANDA…\tCtrl+Alt+Space");
        lookup.Click += (_, _) => OpenLookup();
        menu.Add(lookup);

        var demoRisk = new NativeMenuItem("Demo: at-risk card");
        demoRisk.Click += (_, _) => DemoCard("216482");
        var demoHealthy = new NativeMenuItem("Demo: healthy card");
        demoHealthy.Click += (_, _) => DemoCard("214990");
        menu.Add(demoRisk);
        menu.Add(demoHealthy);
        menu.Add(new NativeMenuItemSeparator());

        menu.Add(BuildRadioSubmenu("Appearance", Appearances.All.Select(a => (a.Name, (object)a.Id)),
            () => (object)_settings.Appearance,
            value => { _settings.Appearance = (AppearanceId)value; ApplySettings(); },
            separatorBefore: Appearances.L.Name));
        menu.Add(BuildRadioSubmenu("Auto-hide", AutoHidePolicy.Choices.Select(c => (AutoHidePolicy.Label(c), (object)c)),
            () => (object)_settings.AutoHideSeconds,
            value => { _settings.AutoHideSeconds = (int)value; ApplySettings(); }));

        var settings = new NativeMenuItem("Settings…");
        settings.Click += (_, _) => OpenSettings();
        menu.Add(settings);

        var prompts = new NativeMenuItem("Prompt library…");
        prompts.Click += (_, _) =>
            new PromptLibraryWindow(_prompts, _settings, ApplySettings).Show();
        menu.Add(prompts);

        menu.Add(new NativeMenuItemSeparator());
        menu.Add(new NativeMenuItem(HotkeyStatusLine()) { IsEnabled = false });
        var exit = new NativeMenuItem("Exit");
        exit.Click += (_, _) => desktop.Shutdown();
        menu.Add(exit);

        var tray = new TrayIcon
        {
            Icon = new WindowIcon(RenderTrayBitmap()),
            ToolTipText = "ANDA Hover Companion (fictional demo data)",
            Menu = menu,
        };
        tray.Clicked += (_, _) => OpenLookup();
        tray.IsVisible = true;
        return tray;
    }

    /// <summary>One honest line in the tray menu: which hotkeys are live, and which are taken.</summary>
    private string HotkeyStatusLine()
    {
        var status = _hotkeys?.Status;
        if (status is null || status.Count == 0) return "Hotkeys: starting…";
        return "Hotkeys: " + string.Join("   ", status.Select(h => $"{h.Binding.Combo} {(h.Registered ? "✓" : "✗ in use")}"));
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
        ThemeManager.Apply(_settings.Appearance, _settings.CardOpacity);
        // Live preview: if a card is open, rebuild it in place with the new look.
        if ((_card is not null || _compact is not null) && _lastCard is { } last)
            OpenCard(last.Record, last.Near, last.Mini);
    }

    private void DemoCard(string number)
    {
        if (_repository.Find(number) is { } record)
        {
            var cursor = TextCaptureService.GetCursorPosition();
            OpenCard(record, new PixelPoint(cursor.X, cursor.Y), mini: false);
        }
    }

    /// <summary>Tray glyph drawn at runtime — no asset pipeline needed.</summary>
    private static Bitmap RenderTrayBitmap()
    {
        var rtb = new RenderTargetBitmap(new PixelSize(32, 32), new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext())
        {
            // A fixed federal blue (USWDS primary), not the look's accent: with K the accent is graphite, which
            // would make a dark tile on a dark Windows taskbar. The glyph must read in both taskbar modes.
            ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#005EA2")), null, new RoundedRect(new Rect(2, 2, 28, 28), 8));
            ctx.DrawEllipse(Brushes.White, null, new Point(16, 16), 5, 5);
        }
        return rtb;
    }
}
