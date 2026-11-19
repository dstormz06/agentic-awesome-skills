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

/// <summary>One list's archive, for the window: its lines, how an item is put back on that list, and its finished items that wait to be archived.</summary>
/// <param name="ListName">"ANDA to-do list", as a label.</param>
/// <param name="OnList">"the ANDA to-do list" or "My to-do list", as it stands in a sentence.</param>
public sealed record ArchiveSource(string ListName, string OnList, Func<IEnumerable<JsonElement>> Lines, Func<string, (bool Ok, string Message)> PutBack,
    Func<IReadOnlyList<WaitingItem>>? Waiting = null, Func<string, bool>? OpenAgain = null);

/// <summary>
/// Backups and archive. For each of the person's lists: the copies that are kept, and one press to put a copy back (what was there a moment
/// before stays among the copies, so that can be undone). How many copies are kept is the person's choice; a smaller number would remove
/// copies, so it is asked about first. Below: the archive of finished to-do items, which can be searched, and from which an item can be put
/// back on its list; and the finished items that a list no longer shows but that are not archived yet, so that nothing is ever out of sight.
/// Nothing here deletes a list or the archive, and nothing is sent anywhere.
/// </summary>
public sealed class BackupsWindow : Window
{
    internal ComboBox KeepBox { get; }
    internal Button KeepGo { get; }
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
    private readonly TimeZoneInfo? _zone;      // a test may fix it; otherwise the zone the tool writes in, asked each time
    private readonly ScrollViewer _scroll;
    private readonly Dictionary<string, DateTime> _picked = new();      // list id -> the copy the person chose, kept while the window repaints
    private List<(ArchiveSource Source, IReadOnlyList<TodoArchive.Row> Rows, int Total)> _read = new();      // the archives, read once for each paint
    private (int Keep, int Gone)? _pending;                             // a smaller number that was chosen and not yet agreed to
    private bool _painting;

    public BackupsWindow(Backups backups, Func<int> keep, Action<int> setKeep, Func<int> archiveDays, Action<int> setArchiveDays, IReadOnlyList<ArchiveSource> archives, TimeZoneInfo? zone = null)
    {
        _backups = backups; _keep = keep; _setKeep = setKeep; _days = archiveDays; _setDays = setArchiveDays; _archives = archives; _zone = zone;
        Title = "ANDA Companion (Notula) — Backups and archive"; Width = 680; SizeToContent = SizeToContent.Height; CanResize = false; Topmost = true; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Res("C.Bg");
        TextBlock Hint(string text) => new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Res("C.Ink2"), Text = text };
        TextBlock Label(string text) => new() { FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Res("C.Ink2"), Text = text, Margin = new Thickness(0, 8, 0, 0) };
        TextBlock Plain(string text) => new() { FontSize = 13, Foreground = Res("C.Ink"), Text = text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };

        KeepBox = new ComboBox { ItemsSource = Backups.KeepChoices.Select(n => n.ToString(CultureInfo.InvariantCulture)).ToList(), MinWidth = 80 };
        AutomationProperties.SetName(KeepBox, "How many backup copies of each list are kept");
        KeepGo = new Button { IsVisible = false, HorizontalAlignment = HorizontalAlignment.Left };
        DaysBox = new ComboBox { ItemsSource = Backups.ArchiveDaysChoices.Select(n => n.ToString(CultureInfo.InvariantCulture)).ToList(), MinWidth = 80 };
        AutomationProperties.SetName(DaysBox, "After how many days a finished item is archived");
        Lists = new StackPanel { Spacing = 6 };
        Status = Hint(""); AutomationProperties.SetLiveSetting(Status, AutomationLiveSetting.Polite);
        FindBox = new TextBox { Watermark = "Search the archive", MaxLength = 100 }; AutomationProperties.SetName(FindBox, "Search the archive");
        Archive = new StackPanel { Spacing = 2 };
        Count = Hint(""); AutomationProperties.SetLiveSetting(Count, AutomationLiveSetting.Polite);

        // A smaller number would remove copies, and removing cannot be undone: so it is asked about first. An arrow key on the box removes nothing.
        KeepBox.SelectionChanged += (_, _) =>
        {
            if (_painting || KeepBox.SelectedIndex < 0) return;
            int keepNow = Backups.KeepChoices[KeepBox.SelectedIndex], gone = _backups.WouldRemove(keepNow);
            if (gone == 0)
            {
                _pending = null; _setKeep(keepNow); Paint();
                Status.Text = $"From now on {keepNow.ToString(CultureInfo.InvariantCulture)} copies of each list are kept. Nothing was removed.";
                KeepBox.Focus(); return;
            }
            _pending = (keepNow, gone); Paint();
            Status.Text = $"Keeping {keepNow.ToString(CultureInfo.InvariantCulture)} would remove {gone:#,0} older backup {(gone == 1 ? "copy" : "copies")} now, and that cannot be undone. Nothing has been removed. Press “{KeepGo.Content}” to go ahead, or choose a larger number.";
            KeepBox.Focus();
        };
        KeepGo.Click += (_, _) =>
        {
            if (_pending is not { } p) return;
            _pending = null; _setKeep(p.Keep);
            int gone = _backups.Prune();
            Paint();
            Status.Text = $"{gone:#,0} older backup {(gone == 1 ? "copy was" : "copies were")} removed. Your lists and the archive were not changed.";
            KeepBox.Focus();
        };
        DaysBox.SelectionChanged += (_, _) =>
        {
            if (_painting || DaysBox.SelectedIndex < 0) return;
            _setDays(Backups.ArchiveDaysChoices[DaysBox.SelectedIndex]);
            Paint();
            Status.Text = $"From now on a finished item is archived after {_days().ToString(CultureInfo.InvariantCulture)} days. Nothing is deleted.";
            DaysBox.Focus();
        };
        FindBox.TextChanged += (_, _) => { if (!_painting) PaintArchive(); };      // searches what was read; the files are not read again for each key
        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closed += (_, _) => _pending = null;

        StackPanel Row(params Control[] parts) { var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; p.Children.AddRange(parts); return p; }
        var body = new StackPanel { Spacing = 8, Margin = new Thickness(20) };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        head.Children.Add(new TextBlock { Text = "Backups and archive", FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Res("C.Ink"), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(close, 1); head.Children.Add(close);
        body.Children.Add(head);
        body.Children.Add(Hint("Every change to one of your lists keeps a copy of it, in the app's own folder on this PC. Nothing here is sent anywhere, and the copies are not part of a settings backup."));
        body.Children.Add(Row(Plain("Keep"), KeepBox, Plain("copies of each list, and one copy for each of that many days.")));
        body.Children.Add(KeepGo);
        body.Children.Add(Hint("Older backup copies are removed by the app, and a smaller number here is asked about first. Your lists and the archive are never removed."));
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
        Opened += (_, _) =>
        {
            if (Screens.ScreenFromWindow(this) is { } screen) _scroll.MaxHeight = Math.Max(300, screen.WorkingArea.Height / screen.Scaling - 52 - 40 - ConnectionBar.Height);
            KeepBox.Focus();                                        // the keyboard starts on the first control, never on nothing
        };
        Content = _scroll;
        Paint();
    }

    private static IBrush Res(string key) => (IBrush)Application.Current!.Resources[key]!;

    /// <summary>The time of a copy in the person's own time zone, with the year: "Nov 18, 2026, 3:12:00 PM EST".</summary>
    internal string When(DateTime utc)
    {
        utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return Clock.Label(Clock.Ms(DateTime.SpecifyKind(utc, DateTimeKind.Utc)), _zone ?? Clock.Zone, true);
    }
    private static string Day(string iso) => DateOnly.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("MMM d", CultureInfo.InvariantCulture) : iso;
    private ComboBox? PickFor(string id) => Lists.Children.OfType<Border>().FirstOrDefault(b => (b.Tag as string) == id)?.Child is StackPanel box ? box.Children.OfType<Grid>().FirstOrDefault()?.Children.OfType<ComboBox>().FirstOrDefault() : null;

    /// <summary>Shows the lists, their copies and the archive as they are now. The copy chosen for each list stays chosen.</summary>
    public void Paint()
    {
        _painting = true;
        try
        {
            KeepBox.SelectedIndex = Math.Max(0, Backups.KeepChoices.ToList().IndexOf(_pending?.Keep ?? _keep()));
            DaysBox.SelectedIndex = Math.Max(0, Backups.ArchiveDaysChoices.ToList().IndexOf(_days()));
            KeepGo.IsVisible = _pending is not null;
            KeepGo.Content = _pending is { } p ? $"Keep {p.Keep.ToString(CultureInfo.InvariantCulture)} and remove {p.Gone:#,0} older {(p.Gone == 1 ? "copy" : "copies")}" : "";
            Lists.Children.Clear();
            foreach (var list in _backups.Lists)
            {
                var copies = _backups.Copies(list.Id); string? now = _backups.Now(list);
                var texts = copies.Select(c => _backups.Read(c)).ToList();
                var pick = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "copies",
                    ItemsSource = copies.Select((c, k) => $"{When(c.Utc)} · {_backups.Size(list, c)}{(now is not null && texts[k] == now ? " (as it is now)" : "")}").ToList() };      // said only of a copy that holds what the list holds now
                AutomationProperties.SetName(pick, $"Copies of {list.Name}, newest first");
                var go = new Button { Content = "Restore this copy", Tag = "restore" };
                AutomationProperties.SetName(go, $"Restore this copy of {list.Name}");
                void Sync() => go.IsEnabled = pick.SelectedIndex >= 0 && pick.SelectedIndex < copies.Count && !(now is not null && texts[pick.SelectedIndex] == now);
                pick.SelectionChanged += (_, _) => { if (!_painting && pick.SelectedIndex >= 0 && pick.SelectedIndex < copies.Count) _picked[list.Id] = copies[pick.SelectedIndex].Utc; Sync(); };
                go.Click += (_, _) =>
                {
                    if (pick.SelectedIndex < 0 || pick.SelectedIndex >= copies.Count) return;
                    var (ok, message) = _backups.Restore(list.Id, copies[pick.SelectedIndex].Utc, When);
                    if (ok) _picked.Remove(list.Id);
                    Paint(); Status.Text = message;
                    PickFor(list.Id)?.Focus();                      // the keyboard stays on this list's copies, not on nothing
                };
                int chosen = _picked.TryGetValue(list.Id, out var utc) ? copies.ToList().FindIndex(c => c.Utc == utc) : -1;
                if (chosen < 0) _picked.Remove(list.Id);
                if (copies.Count > 0) pick.SelectedIndex = Math.Max(0, chosen);
                Sync();
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                row.Children.Add(pick); Grid.SetColumn(go, 1); go.Margin = new Thickness(8, 0, 0, 0); row.Children.Add(go);
                var box = new StackPanel { Spacing = 3 };
                box.Children.Add(new TextBlock { Text = list.Name, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Res("C.Ink") });
                box.Children.Add(new TextBlock { Text = copies.Count == 0 ? "No copy yet." : $"{copies.Count:#,0} {(copies.Count == 1 ? "copy" : "copies")} · newest {When(copies[0].Utc)}", FontSize = 12, Foreground = Res("C.Ink2"), Tag = "sub" });
                box.Children.Add(row);
                Lists.Children.Add(new Border { BorderBrush = Res("C.Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8), Child = box, Tag = list.Id });
            }
            _read = _archives.Select(a => { var (rows, total) = TodoArchive.Read(a.Lines()); return (a, rows, total); }).ToList();
        }
        finally { _painting = false; }
        PaintArchive();
    }

    private void PaintArchive()
    {
        string query = FindBox.Text ?? "", needle = TodoArchive.Fold(query);
        Archive.Children.Clear();
        // First, what is finished and still on a list but no longer shown there: found here until it is archived.
        var waiting = _archives.SelectMany(a => (a.Waiting?.Invoke() ?? Array.Empty<WaitingItem>()).Select(w => (Source: a, Item: w))).OrderByDescending(x => x.Item.Finished).ToList();
        var waitingHit = waiting.Where(x => needle.Length == 0 || x.Item.Title.ToLowerInvariant().Contains(needle, StringComparison.Ordinal) || (x.Item.Number ?? "").Contains(needle, StringComparison.Ordinal)).ToList();
        string days = _days().ToString(CultureInfo.InvariantCulture);
        foreach (var (source, w) in waitingHit.Take(200))
        {
            var again = new Button { Content = "Open it again", Tag = "openagain", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(again, "Open it again: " + w.Title);
            again.Click += (_, _) =>
            {
                bool ok = source.OpenAgain?.Invoke(w.Id) == true;
                Paint(); Status.Text = ok ? $"“{w.Title}” is open again on {source.OnList}." : "That item is open already.";
                FindBox.Focus();
            };
            Archive.Children.Add(RowOf((w.Number is null ? "" : $"ANDA {w.Number}: ") + w.Title,
                $"{source.ListName} · finished {w.Finished.ToString("MMM d", CultureInfo.InvariantCulture)} · still on its list, no longer shown there · archived after {days} days", again));
        }
        var all = new List<(ArchiveSource Source, TodoArchive.Row Row)>(); int total = 0, found = 0;
        foreach (var (source, rows, t) in _read)
        {
            var hit = TodoArchive.Search(rows, query);
            total += t; found += hit.Count; all.AddRange(hit.Take(200).Select(r => (source, r)));
        }
        var shown = all.OrderByDescending(x => x.Row.ArchivedOn, StringComparer.Ordinal).Take(200).ToList();
        foreach (var (source, r) in shown)
        {
            Button? put = null;
            if (r.RestoredOn is null)
            {
                put = new Button { Content = "Put back on the list", Tag = "putback", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                AutomationProperties.SetName(put, "Put back on the list: " + r.Title);
                put.Click += (_, _) =>
                {
                    var (ok, message) = source.PutBack(r.Id);
                    if (ok) EventLog.Record("file", "a to-do item was put back from the archive");
                    Paint(); Status.Text = message;
                    FindBox.Focus();                                // the button is gone; the keyboard stays in the archive
                };
            }
            Archive.Children.Add(RowOf((r.Number is null ? "" : $"ANDA {r.Number}: ") + r.Title,
                $"{source.ListName} · archived {Day(r.ArchivedOn)} · {r.Why}{(r.RestoredOn is null ? "" : " · put back " + Day(r.RestoredOn))}", put));
        }
        int n = waiting.Count;
        string still = n == 0 ? "" : $" {n:#,0} finished {(n == 1 ? "item is still on its list" : "items are still on their lists")}, waiting to be archived" + (needle.Length > 0 ? $"; {waitingHit.Count:#,0} {(waitingHit.Count == 1 ? "matches" : "match")}." : ".");
        Count.Text = (total == 0 ? $"Nothing is archived yet. A finished item is moved here after {days} days, or when you clear the finished items of a list."
            : needle.Length > 0 ? $"{found:#,0} of {total:#,0} archived {(total == 1 ? "item" : "items")} {(found == 1 ? "matches" : "match")}." + (found > shown.Count ? $" The newest {shown.Count:#,0} are shown." : "")
            : $"{total:#,0} archived {(total == 1 ? "item" : "items")}." + (total > shown.Count ? $" The newest {shown.Count:#,0} are shown; search to find an older one." : "")) + still;
    }

    private static Grid RowOf(string title, string detail, Button? action)
    {
        var text = new StackPanel { Spacing = 1 };
        text.Children.Add(new TextBlock { Text = title, FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = Res("C.Ink") });
        text.Children.Add(new TextBlock { Text = detail, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Res("C.Ink2") });
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 3) };
        row.Children.Add(text);
        if (action is not null) { Grid.SetColumn(action, 1); row.Children.Add(action); }
        return row;
    }
}
