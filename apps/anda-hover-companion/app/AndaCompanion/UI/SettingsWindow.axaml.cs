using Avalonia.Controls;
using Avalonia.Input;
using AndaCompanion.Services;
using AndaCompanion.Theming;

namespace AndaCompanion.UI;

public partial class SettingsWindow : Window
{
    private readonly Settings _settings;
    private readonly PromptLibraryService _prompts;
    private readonly Action _onChanged;
    private bool _loading = true;

    public SettingsWindow(Settings settings, PromptLibraryService prompts, Action onChanged)
    {
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
            rb.IsCheckedChanged += (_, _) =>
            {
                if (!_loading && rb.IsChecked == true) { _settings.Appearance = a.Id; Apply(); }
            };
            AppearancePanel.Children.Add(rb);
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

        ConnStatus.Text = ConnectionService.HasKey
            ? "Key detected — press Test to verify the channel."
            : "Offline mode — deterministic briefs. Ready for API access when approved.";
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
