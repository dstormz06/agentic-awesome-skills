using Avalonia.Controls;
using Avalonia.Threading;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

/// <summary>Settings: My to-do list. The shortcut that turns highlighted notes into to-do items, and what it does when pressed. Saved at once, like the rest of Settings.</summary>
public partial class SettingsWindow
{
    private const string NoShortcut = "None";

    private void BuildMyMyTodo()
    {
        MyTodoKeyBox.Text = string.IsNullOrWhiteSpace(_settings.MyTodoHotkey) ? NoShortcut : _settings.MyTodoHotkey;
        MyTodoKeyBox.KeyDown += (_, e) =>
        {
            e.Handled = true;
            if (HotkeyCapture.FromKey(e.Key, e.KeyModifiers) is not { } combo) return;           // a modifier alone: keep waiting
            if (!HotkeyParser.TryParse(combo, out uint _m, out uint _k, out string normalized, out string error)) { MyTodoKeyStatus.Text = $"My to-do list: {error}"; return; }
            TrySetMyTodoKey(normalized);
        };
        MyTodoKeyClear.Click += (_, _) => TrySetMyTodoKey("");
        MyTodoKeyReset.Click += (_, _) => TrySetMyTodoKey(Settings.DefaultMyTodoHotkey);
        MyTodoKeyStatus.Text = _hooks?.HotkeyStatus() ?? "";

        var modes = new (MyTodoMode Mode, string Label)[]
        {
            (MyTodoMode.Review, "Show me the items first, so I can change them (recommended)"),
            (MyTodoMode.AddAtOnce, "Add them at once (I can press Undo)"),
        };
        foreach (var (mode, label) in modes)
        {
            var rb = new RadioButton { Content = label, GroupName = "mytodomode", IsChecked = mode == _settings.MyTodoMode };
            rb.IsCheckedChanged += (_, _) => { if (!_loading && rb.IsChecked == true) { _settings.MyTodoMode = mode; Apply(); } };
            MyTodoModePanel.Children.Add(rb);
        }

        MyTodoOpen.Click += (_, _) => _hooks?.OpenMyTodo?.Invoke();
        MyTodoNotes.Click += (_, _) => _hooks?.OpenNotes?.Invoke();
        MyTodoClear.Click += (_, _) =>
        {
            int n = _hooks?.ClearMyTodoFinished?.Invoke() ?? 0;
            MyTodoStatus.Text = n == 0 ? "Nothing finished to clear. Finished items that repeat stay: they come back by themselves." : $"Cleared {n} finished {(n == 1 ? "item" : "items")}.";
        };
    }

    /// <summary>Sets the to-do shortcut ("" means none). A shortcut another feature already uses is refused and nothing changes.</summary>
    private void TrySetMyTodoKey(string normalized)
    {
        if (normalized.Length > 0)
        {
            var all = ShortcutsInUse(null).Where(x => x.Name != "My to-do list").Append(("My to-do list", normalized)).ToArray();
            if (HotkeyParser.ValidateSet(all) is { } clash) { MyTodoKeyStatus.Text = clash; return; }
        }
        _settings.MyTodoHotkey = normalized;
        MyTodoKeyBox.Text = normalized.Length == 0 ? NoShortcut : normalized;
        Apply();
        if (normalized.Length == 0) { MyTodoKeyStatus.Text = "My to-do list has no shortcut now. The tray menu still opens it."; return; }
        MyTodoKeyStatus.Text = $"My to-do list: {normalized}. Checking with Windows…";
        DispatcherTimer.RunOnce(() => MyTodoKeyStatus.Text = _hooks?.HotkeyStatus() ?? $"My to-do list: {normalized}", TimeSpan.FromMilliseconds(500));
    }
}
