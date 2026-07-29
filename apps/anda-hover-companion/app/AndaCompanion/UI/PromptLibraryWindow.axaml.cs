using Avalonia.Controls;
using Avalonia.Input;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

public partial class PromptLibraryWindow : Window
{
    private readonly PromptLibraryService _prompts;
    private readonly Settings _settings;
    private readonly Action _onChanged;

    public PromptLibraryWindow(PromptLibraryService prompts, Settings settings, Action onChanged)
    {
        _prompts = prompts;
        _settings = settings;
        _onChanged = onChanged;
        InitializeComponent();

        LensList.SelectionChanged += (_, _) =>
        {
            if (LensList.SelectedItem is string name && _prompts.GetText(name) is { } text)
            {
                NameBox.Text = name;
                TextBox1.Text = text;
                StatusText.Text = name == _settings.ActiveLens ? "This lens is active." : "";
            }
        };
        NewBtn.Click += (_, _) => { LensList.SelectedItem = null; NameBox.Text = ""; TextBox1.Text = ""; StatusText.Text = ""; };
        SaveBtn.Click += (_, _) =>
        {
            var name = NameBox.Text?.Trim();
            var text = TextBox1.Text?.Trim();
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(text))
            {
                StatusText.Text = "A lens needs both a name and a prompt.";
                return;
            }
            _prompts.Upsert(new LensPrompt(name, text));
            Reload(select: name);
            StatusText.Text = "Saved.";
            _onChanged();
        };
        ActivateBtn.Click += (_, _) =>
        {
            if (NameBox.Text?.Trim() is { Length: > 0 } name && _prompts.GetText(name) is not null)
            {
                _settings.ActiveLens = name;
                SettingsService.Save(_settings);
                StatusText.Text = $"“{name}” is now the active lens.";
                _onChanged();
            }
        };
        DeleteBtn.Click += (_, _) =>
        {
            if (NameBox.Text?.Trim() is { Length: > 0 } name)
            {
                _prompts.Delete(name);
                if (_settings.ActiveLens == name) { _settings.ActiveLens = null; SettingsService.Save(_settings); }
                Reload(select: null);
                NameBox.Text = ""; TextBox1.Text = "";
                StatusText.Text = "Deleted.";
                _onChanged();
            }
        };
        CloseBtn.Click += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        Reload(select: _settings.ActiveLens);
    }

    private void Reload(string? select)
    {
        LensList.ItemsSource = _prompts.Prompts.Select(p => p.Name).ToList();
        if (select is not null) LensList.SelectedItem = select;
    }
}
