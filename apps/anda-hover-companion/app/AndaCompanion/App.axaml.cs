using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using AndaCompanion.Data;
using AndaCompanion.Models;
using AndaCompanion.Services;
using AndaCompanion.Theming;
using AndaCompanion.UI;

namespace AndaCompanion;

public partial class App : Application
{
    private readonly IAndaRepository _repository = new MockAndaRepository();
    private ICompanionBriefService _briefs = new OfflineBriefService();
    private Settings _settings = new();
    private HotkeyService? _hotkeys;
    private TrayIcon? _tray;
    private CardWindow? _card;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        _settings = SettingsService.Load();
        ThemeManager.Apply(_settings.Appearance, _settings.Theme);
        _briefs = BriefServiceFactory.Create();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            _tray = BuildTrayIcon(desktop);
            _hotkeys = new HotkeyService(id => Dispatcher.UIThread.Post(() => OnHotkey(id)));
            _hotkeys.Start();
            desktop.Exit += (_, _) => { _hotkeys.Dispose(); _tray?.Dispose(); };
        }

        base.OnFrameworkInitializationCompleted();
    }

    // ---------- hotkey / consent flow ----------

    private void OnHotkey(int id)
    {
        if (id == HotkeyService.IdLookup) { OpenLookup(); return; }

        var cursor = TextCaptureService.GetCursorPosition();
        var near = new PixelPoint(cursor.X, cursor.Y);
        string? number = AndaValidator.Extract(TextCaptureService.TryCaptureSelectedText());
        if (number is null) { OpenLookup(); return; }

        var record = _repository.Find(number);
        if (record is null)
        {
            new ChipWindow().ShowNotice($"{number} is not a recognized ANDA", near);
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

    public void OpenCard(AndaRecord record, PixelPoint near, bool mini)
    {
        _card?.Close();
        _card = new CardWindow(_briefs, _repository.Find);
        _card.Closed += (_, _) => { if (ReferenceEquals(_card, null)) return; };
        _card.Show(record, near, mini);
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
            value => { _settings.Appearance = (AppearanceId)value; ApplySettings(); }));

        menu.Add(BuildRadioSubmenu("Theme", new[]
            {
                ("System", (object)ThemeMode.System), ("Light", (object)ThemeMode.Light), ("Dark", (object)ThemeMode.Dark),
            },
            () => (object)_settings.Theme,
            value => { _settings.Theme = (ThemeMode)value; ApplySettings(); }));

        menu.Add(BuildRadioSubmenu("Consent model", new[]
            {
                ("① Full card on hotkey", (object)ConsentModel.Full),
                ("② Peek first, expand to full", (object)ConsentModel.PeekFirst),
                ("③ Whisper invite, open on click", (object)ConsentModel.Whisper),
            },
            () => (object)_settings.Consent,
            value => { _settings.Consent = (ConsentModel)value; SettingsService.Save(_settings); }));

        menu.Add(new NativeMenuItemSeparator());
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

    private NativeMenuItem BuildRadioSubmenu(
        string title, IEnumerable<(string Label, object Value)> options,
        Func<object> current, Action<object> select)
    {
        var parent = new NativeMenuItem(title) { Menu = new NativeMenu() };
        foreach (var (label, value) in options)
        {
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
        ThemeManager.Apply(_settings.Appearance, _settings.Theme);
        _card?.Close(); // appearance changes take effect on the next card
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
            var accent = ((ISolidColorBrush)Current!.Resources["C.Accent"]!).Color;
            ctx.DrawRectangle(new SolidColorBrush(accent), null, new RoundedRect(new Rect(2, 2, 28, 28), 8));
            ctx.DrawEllipse(Brushes.White, null, new Point(16, 16), 5, 5);
        }
        return rtb;
    }
}
