using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

/// <summary>One list's archive, for the window: its lines, and how an item is put back on that list.</summary>
public sealed record ArchiveSource(string ListName, Func<IEnumerable<JsonElement>> Lines, Func<string, (bool Ok, string Message)> PutBack);

/// <summary>
/// Backups and archive. For each of the person's lists: the copies that are kept, and one press to put a copy back (the list as it was a
/// moment before stays among the copies, so that can be undone). How many copies are kept is the person's choice; older copies are removed
/// by the app and the window says so. Below: the archive of finished to-do items, which can be searched, and from which an item can be put
/// back on its list. Nothing here deletes a list or the archive, and nothing is sent anywhere.
/// </summary>
public sealed class BackupsWindow : Window
{
    internal ComboBox KeepBox { get; }
    internal ComboBox DaysBox { get; }
    internal StackPanel Lists { get; }
    internal TextBlock Status { get; }
    internal TextBox FindBox { get; }
    internal StackPanel Archive { get; }
    internal TextBlock Count { get; }

    private readonly Backups _backups;
    private readonly Func<int> _keep, _days;
    private readonly Action<int> _setKeep, _setDays;
    private readonly IReadOnlyList<ArchiveSource> _archives;
    private readonly TimeZoneInfo _zone;
    private readonly ScrollViewer _scroll;
    private bool _painting;

    public BackupsWindow(Backups backups, Func<int> keep, Action<int> setKeep, Func<int> archiveDays, Action<int> setArchiveDays, IReadOnlyList<ArchiveSource> archives, TimeZoneInfo? zone = null)
    {
        _backups = backups; _keep = keep; _setKeep = setKeep; _days = archiveDays; _setDays = setArchiveDays; _archives = archives; _zone = zone ?? TimeZoneInfo.Local;
        Title = "ANDA Companion (Notula) — Backups and archive"; Width = 680; SizeToContent = SizeToContent.Height; CanResize = false; Topmost = true; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Res("C.Bg");
        TextBlock Hint(string text) => new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Res("C.Ink2"), Text = text };
        TextBlock Label(string text) => new() { FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Res("C.Ink2"), Text = text, Margin = new Thickness(0, 8, 0, 0) };
        TextBlock Plain(string text) => new() { FontSize = 13, Foreground = Res("C.Ink"), Text = text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };

        KeepBox = new ComboBox { ItemsSource = Backups.KeepChoices.Select(n => n.ToString(CultureInfo.InvariantCulture)).ToList(), MinWidth = 80 };
        AutomationProperties.SetName(KeepBox, "How many backup copies of each list are kept");
        DaysBox = new ComboBox { ItemsSource = Backups.ArchiveDaysChoices.Select(n => n.ToString(CultureInfo.InvariantCulture)).ToList(), MinWidth = 80 };
        AutomationProperties.SetName(DaysBox, "After how many days a finished item is archived");
        Lists = new StackPanel { Spacing = 6 };
        Status = Hint(""); AutomationProperties.SetLiveSetting(Status, AutomationLiveSetting.Polite);
        FindBox = new TextBox { Watermark = "Search the archive", MaxLength = 100 }; AutomationProperties.SetName(FindBox, "Search the archive");
        Archive = new StackPanel { Spacing = 2 };
        Count = Hint(""); AutomationProperties.SetLiveSetting(Count, AutomationLiveSetting.Polite);

        KeepBox.SelectionChanged += (_, _) =>
        {
            if (_painting || KeepBox.SelectedIndex < 0) return;
            _setKeep(Backups.KeepChoices[KeepBox.SelectedIndex]);
            int gone = _backups.Prune();
            Paint();
            Status.Text = gone > 0
                ? $"{gone:#,0} older backup {(gone == 1 ? "copy was" : "copies were")} removed. Your lists and the archive were not changed."
                : $"From now on {_keep().ToString(CultureInfo.InvariantCulture)} copies of each list are kept. Nothing was removed.";
        };
        DaysBox.SelectionChanged += (_, _) =>
        {
            if (_painting || DaysBox.SelectedIndex < 0) return;
            _setDays(Backups.ArchiveDaysChoices[DaysBox.SelectedIndex]);
            Paint();
            Status.Text = $"From now on a finished item is archived after {_days().ToString(CultureInfo.InvariantCulture)} days. Nothing is deleted.";
        };
        FindBox.TextChanged += (_, _) => { if (!_painting) PaintArchive(); };
        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        StackPanel Row(params Control[] parts) { var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; p.Children.AddRange(parts); return p; }
        var body = new StackPanel { Spacing = 8, Margin = new Thickness(20) };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        head.Children.Add(new TextBlock { Text = "Backups and archive", FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Res("C.Ink"), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(close, 1); head.Children.Add(close);
        body.Children.Add(head);
        body.Children.Add(Hint("Every change to one of your lists keeps a copy of it, in the app's own folder on this PC. Nothing here is sent anywhere, and the copies are not part of a settings backup."));
        body.Children.Add(Row(Plain("Keep"), KeepBox, Plain("copies of each list, and one copy for each of that many days.")));
        body.Children.Add(Hint("Older backup copies are removed by the app. Your lists and the archive never are."));
        body.Children.Add(Lists);
        body.Children.Add(Status);
        body.Children.Add(Label("ARCHIVE · FINISHED TO-DO ITEMS"));
        body.Children.Add(Row(Plain("A finished item leaves its list after"), DaysBox, Plain("days and is kept here.")));
        body.Children.Add(FindBox);
        body.Children.Add(Archive);
        body.Children.Add(Count);
        body.Children.Add(Hint("The archive is only ever added to: putting an item back adds a line, it does not remove one. It is a plain file beside each list (todo.archive.jsonl, mytodo.archive.jsonl); to delete it, delete that file by hand."));
        // The whole page scrolls inside the window, so that it never grows past the screen.
        _scroll = new ScrollViewer { Content = body, MaxHeight = 640, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Opened += (_, _) => { if (Screens.ScreenFromWindow(this) is { } screen) _scroll.MaxHeight = Math.Max(300, screen.WorkingArea.Height / screen.Scaling - 52 - 40 - ConnectionBar.Height); };
        Content = _scroll;
        Paint();
    }

    private static IBrush Res(string key) => (IBrush)Application.Current!.Resources[key]!;
    private string When(DateTime utc) => EventLog.Local(utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture), _zone);
    private static string Day(string iso) => DateOnly.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("MMM d", CultureInfo.InvariantCulture) : iso;

    /// <summary>Shows the lists, their copies and the archive as they are now.</summary>
    public void Paint()
    {
        _painting = true;
        try
        {
            KeepBox.SelectedIndex = Math.Max(0, Backups.KeepChoices.ToList().IndexOf(_keep()));
            DaysBox.SelectedIndex = Math.Max(0, Backups.ArchiveDaysChoices.ToList().IndexOf(_days()));
            Lists.Children.Clear();
            foreach (var list in _backups.Lists)
            {
                var copies = _backups.Copies(list.Id);
                var pick = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = copies.Select((c, k) => $"{When(c.Utc)} · {_backups.Size(list, c)}{(k == 0 ? " (as it is now)" : "")}").ToList(), Tag = "copies" };
                AutomationProperties.SetName(pick, $"Copies of the {list.Name}, newest first");
                var go = new Button { Content = "Restore this copy", Tag = "restore" };
                AutomationProperties.SetName(go, $"Restore this copy of the {list.Name}");
                void Sync() => go.IsEnabled = copies.Count >= 2 && pick.SelectedIndex > 0;
                pick.SelectionChanged += (_, _) => Sync();
                go.Click += (_, _) =>
                {
                    if (pick.SelectedIndex < 0 || pick.SelectedIndex >= copies.Count) return;
                    var (_, message) = _backups.Restore(list.Id, copies[pick.SelectedIndex].Utc, When);
                    Paint(); Status.Text = message;
                };
                if (copies.Count > 0) pick.SelectedIndex = 0;
                Sync();
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                row.Children.Add(pick); Grid.SetColumn(go, 1); go.Margin = new Thickness(8, 0, 0, 0); row.Children.Add(go);
                var box = new StackPanel { Spacing = 3 };
                box.Children.Add(new TextBlock { Text = list.Name, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Res("C.Ink") });
                box.Children.Add(new TextBlock { Text = copies.Count == 0 ? "No copy yet." : $"{copies.Count:#,0} {(copies.Count == 1 ? "copy" : "copies")} · newest {When(copies[0].Utc)}", FontSize = 12, Foreground = Res("C.Ink2"), Tag = "sub" });
                box.Children.Add(row);
                Lists.Children.Add(new Border { BorderBrush = Res("C.Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8), Child = box });
            }
        }
        finally { _painting = false; }
        PaintArchive();
    }

    private void PaintArchive()
    {
        string query = FindBox.Text ?? "";
        var all = new List<(ArchiveSource Source, TodoArchive.Row Row)>(); int total = 0, found = 0;
        foreach (var source in _archives)
        {
            var (rows, t, f) = TodoArchive.Rows(source.Lines(), query);
            total += t; found += f; all.AddRange(rows.Select(r => (source, r)));
        }
        var shown = all.OrderByDescending(x => x.Row.ArchivedOn, StringComparer.Ordinal).Take(200).ToList();
        Archive.Children.Clear();
        foreach (var (source, r) in shown)
        {
            var text = new StackPanel { Spacing = 1 };
            text.Children.Add(new TextBlock { Text = (r.Number is null ? "" : $"ANDA {r.Number}: ") + r.Title, FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = Res("C.Ink") });
            text.Children.Add(new TextBlock { Text = $"{source.ListName} · archived {Day(r.ArchivedOn)} · {r.Why}{(r.RestoredOn is null ? "" : " · put back " + Day(r.RestoredOn))}", FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Res("C.Ink2") });
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 3) };
            row.Children.Add(text);
            if (r.RestoredOn is null)
            {
                var put = new Button { Content = "Put back on the list", Tag = "putback", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                AutomationProperties.SetName(put, "Put back on the list: " + r.Title);
                put.Click += (_, _) =>
                {
                    var (ok, message) = source.PutBack(r.Id);
                    if (ok) EventLog.Record("file", "a to-do item was put back from the archive");
                    Paint(); Status.Text = message;
                };
                Grid.SetColumn(put, 1); row.Children.Add(put);
            }
            Archive.Children.Add(row);
        }
        string days = _days().ToString(CultureInfo.InvariantCulture);
        Count.Text = total == 0 ? $"Nothing is archived yet. A finished item is moved here after {days} days, or when you clear the finished items of a list."
            : query.Trim().Length > 0 ? $"{found:#,0} of {total:#,0} archived {(total == 1 ? "item" : "items")} {(found == 1 ? "matches" : "match")}." + (found > shown.Count ? $" The newest {shown.Count:#,0} are shown." : "")
            : $"{total:#,0} archived {(total == 1 ? "item" : "items")}." + (total > shown.Count ? $" The newest {shown.Count:#,0} are shown; search to find an older one." : "");
    }
}
