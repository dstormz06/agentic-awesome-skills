using Avalonia.Controls;
using Avalonia.Threading;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

/// <summary>Settings: My checklist. The shortcut that turns highlighted notes into checklist items, and what it does when pressed. Saved at once, like the rest of Settings.</summary>
public partial class SettingsWindow
{
    private const string NoShortcut = "None";

    private void BuildMyChecklist()
    {
        ChecklistKeyBox.Text = string.IsNullOrWhiteSpace(_settings.ChecklistHotkey) ? NoShortcut : _settings.ChecklistHotkey;
        ChecklistKeyBox.KeyDown += (_, e) =>
        {
            e.Handled = true;
            if (HotkeyCapture.FromKey(e.Key, e.KeyModifiers) is not { } combo) return;           // a modifier alone: keep waiting
            if (!HotkeyParser.TryParse(combo, out uint _m, out uint _k, out string normalized, out string error)) { ChecklistKeyStatus.Text = $"My checklist: {error}"; return; }
            TrySetChecklistKey(normalized);
        };
        ChecklistKeyClear.Click += (_, _) => TrySetChecklistKey("");
        ChecklistKeyReset.Click += (_, _) => TrySetChecklistKey(Settings.DefaultChecklistHotkey);
        ChecklistKeyStatus.Text = _hooks?.HotkeyStatus() ?? "";

        var modes = new (ChecklistMode Mode, string Label)[]
        {
            (ChecklistMode.Review, "Show me the items first, so I can change them (recommended)"),
            (ChecklistMode.AddAtOnce, "Add them at once (I can press Undo)"),
        };
        foreach (var (mode, label) in modes)
        {
            var rb = new RadioButton { Content = label, GroupName = "checklistmode", IsChecked = mode == _settings.ChecklistMode };
            rb.IsCheckedChanged += (_, _) => { if (!_loading && rb.IsChecked == true) { _settings.ChecklistMode = mode; Apply(); } };
            ChecklistModePanel.Children.Add(rb);
        }

        ChecklistOpen.Click += (_, _) => _hooks?.OpenChecklist?.Invoke();
        ChecklistNotes.Click += (_, _) => _hooks?.OpenNotes?.Invoke();
        ChecklistClear.Click += (_, _) =>
        {
            int n = _hooks?.ClearChecklistFinished?.Invoke() ?? 0;
            ChecklistStatus.Text = n == 0 ? "Nothing finished to clear. Finished items that repeat stay: they come back by themselves." : $"Cleared {n} finished {(n == 1 ? "item" : "items")}.";
        };
    }

    /// <summary>Sets the checklist shortcut ("" means none). A shortcut another feature already uses is refused and nothing changes.</summary>
    private void TrySetChecklistKey(string normalized)
    {
        if (normalized.Length > 0)
        {
            var all = ShortcutsInUse(null).Where(x => x.Name != "My checklist").Append(("My checklist", normalized)).ToArray();
            if (HotkeyParser.ValidateSet(all) is { } clash) { ChecklistKeyStatus.Text = clash; return; }
        }
        _settings.ChecklistHotkey = normalized;
        ChecklistKeyBox.Text = normalized.Length == 0 ? NoShortcut : normalized;
        Apply();
        if (normalized.Length == 0) { ChecklistKeyStatus.Text = "My checklist has no shortcut now. The tray menu still opens it."; return; }
        ChecklistKeyStatus.Text = $"My checklist: {normalized}. Checking with Windows…";
        DispatcherTimer.RunOnce(() => ChecklistKeyStatus.Text = _hooks?.HotkeyStatus() ?? $"My checklist: {normalized}", TimeSpan.FromMilliseconds(500));
    }
}
