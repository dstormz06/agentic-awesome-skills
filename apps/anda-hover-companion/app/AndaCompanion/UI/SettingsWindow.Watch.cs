using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

// Round 42 in Settings, How it starts: the notices near the clock (off until switched on), division news from one Word document, at most
// 1, 3 or 5 notices a day, and the limits of the timeline checks, each one of its choices. Nothing here sends anything.
public partial class SettingsWindow
{
    /// <summary>For tests: chooses the news document instead of the file picker.</summary>
    internal Func<Task<string?>>? PickNewsDocument { get; set; }

    private void BuildWatch()
    {
        TimelineHint.Text = Timeline.Hint; WatchHint.Text = Notices.Hint;
        WatchBackBox.IsChecked = _settings.WatchBack;
        NewsPathBox.Text = _settings.NewsDocument;
        NewsBox.IsChecked = _settings.DivisionNews && _settings.NewsDocument.Length > 0;
        AutomationProperties.SetName(NewsPathBox, "Division news document");
        WatchBackBox.IsCheckedChanged += (_, _) => { _settings.WatchBack = WatchBackBox.IsChecked == true; Apply(); WatchStatus.Text = CheckNow(); };
        NewsBox.IsCheckedChanged += (_, _) =>
        {
            if (NewsBox.IsChecked == true && _settings.NewsDocument.Length == 0) { NewsBox.IsChecked = false; WatchStatus.Text = "Please choose the division's news document first."; return; }
            _settings.DivisionNews = NewsBox.IsChecked == true; Apply(); WatchStatus.Text = CheckNow();
        };
        NewsPick.Click += async (_, _) =>
        {
            string? path = PickNewsDocument is { } pick ? await pick() : await PickDocx();
            if (path is null) return;
            if (!path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)) { WatchStatus.Text = "The news document must be a Word document (.docx)."; return; }
            if (DivisionNews.PathProblem(path) is { } bad) { WatchStatus.Text = bad; return; }      // a network path, or longer than is kept (round 42 review)
            _settings.NewsDocument = path; NewsPathBox.Text = path; Apply();
            WatchStatus.Text = "Document chosen. Tick the box above to show its news.";
        };
        NewsClear.Click += (_, _) => { _settings.NewsDocument = ""; _settings.DivisionNews = false; NewsPathBox.Text = ""; NewsBox.IsChecked = false; Apply(); WatchStatus.Text = CheckNow(); };
        foreach (int cap in Notices.Caps)
        {
            var rb = new RadioButton { GroupName = "noticeCap", Content = $"At most {cap} a day", IsChecked = _settings.NoticesPerDay == cap, Margin = new Avalonia.Thickness(0, 0, 12, 0) };
            rb.IsCheckedChanged += (_, _) => { if (rb.IsChecked == true) { _settings.NoticesPerDay = cap; Apply(); } };
            NoticeCapPanel.Children.Add(rb);
        }
        AutomationProperties.SetName(NoticeCapPanel, "Notices a day");
        NoticeStayBox.IsChecked = _settings.NoticesStay;
        NoticeStayBox.IsCheckedChanged += (_, _) => { _settings.NoticesStay = NoticeStayBox.IsChecked == true; Apply(); };
        int row = 0;
        foreach (var (key, choices, label) in TimelineSettings.Limits)
        {
            TimelineGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var text = new TextBlock { Text = label, TextWrapping = Avalonia.Media.TextWrapping.Wrap, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            var box = new ComboBox { ItemsSource = choices, SelectedItem = _settings.Timeline.Get(key), MinWidth = 72 };
            AutomationProperties.SetName(box, label);
            box.SelectionChanged += (_, _) => { if (box.SelectedItem is int v) { _settings.Timeline.Set(key, v); _settings.Timeline = TimelineSettings.Normalize(_settings.Timeline); Apply(); } };
            Grid.SetRow(text, row); Grid.SetRow(box, row); Grid.SetColumn(box, 1);
            TimelineGrid.Children.Add(text); TimelineGrid.Children.Add(box);
            row++;
        }
        WatchNow.Click += (_, _) => WatchStatus.Text = CheckNow();
        NewsOpen.Click += (_, _) => _hooks?.OpenNews?.Invoke();
        NewsOpen.IsEnabled = _hooks?.OpenNews is not null;
    }

    private string CheckNow() => _hooks?.CheckNotices?.Invoke() ?? Notices.SaidNotRunning;

    private async Task<string?> PickDocx()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "The division's news document", AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Word document") { Patterns = new[] { "*.docx" } } },
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
