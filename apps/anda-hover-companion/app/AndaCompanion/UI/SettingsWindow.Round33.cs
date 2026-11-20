using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

// Round 33 in Settings: text size and spacing, the style of the lookup window, recent ANDAs, and the columns a card shows under "Details".
// Each choice is saved and takes effect when it is made.
public partial class SettingsWindow
{
    private void BuildRound33()
    {
        void Radios(WrapPanel panel, string group, IEnumerable<(string Key, string Label)> choices, Func<string> get, Action<string> set)
        {
            foreach (var (key, label) in choices)
            {
                var rb = new RadioButton { Content = label, GroupName = group, IsChecked = get() == key, Margin = new Thickness(0, 0, 14, 2), Tag = key };
                rb.IsCheckedChanged += (_, _) => { if (_loading || rb.IsChecked != true || get() == key) return; set(key); Apply(); };
                panel.Children.Add(rb);
            }
        }
        Radios(TextSizePanel, "textsize", Display.TextSizes, () => _settings.TextSize, v => _settings.TextSize = v);
        Radios(DensityPanel, "density", Display.Densities, () => _settings.Density, v => _settings.Density = v);
        DisplayHint.Text = Display.Hint;
        Radios(LookupStylePanel, "lookupstyle", new[] { ("box", "Box: a number and Go"), ("bar", "One Bar: also offers Review and Ask") }, () => _settings.LookupStyle, v => _settings.LookupStyle = v);

        RecentsCheck.IsChecked = _settings.RememberRecents;
        RecentsCheck.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.RememberRecents = RecentsCheck.IsChecked == true;
            if (!_settings.RememberRecents) { _settings.Recents = new(); RecentsStatus.Text = Recents.SaidOff; } else RecentsStatus.Text = "";
            Apply();
        };
        RecentsClear.Click += (_, _) => { _settings.Recents = new(); RecentsStatus.Text = Recents.SaidCleared; Apply(); };
        PinsClear.Click += (_, _) => { _settings.Pins = new(); RecentsStatus.Text = Recents.SaidPinsCleared; Apply(); };

        foreach (var (key, label) in CardDetails.Fields)
        {
            var box = new CheckBox { Content = label, IsChecked = _settings.CardFields.Contains(key), Margin = new Thickness(0, 0, 14, 2), Tag = key };
            box.IsCheckedChanged += (_, _) =>
            {
                if (_loading) return;
                var chosen = _settings.CardFields.Where(k => k != key).ToList();
                if (box.IsChecked == true) chosen.Add(key);
                _settings.CardFields = CardDetails.Normalize(chosen);
                CardFieldsStatus.Text = _settings.CardFields.Count == 0 ? "No columns are chosen: a card shows no details." : $"{_settings.CardFields.Count} of {CardDetails.Fields.Count} columns are shown under “Details”.";
                Apply();
            };
            CardFieldsPanel.Children.Add(box);
        }
    }
}
