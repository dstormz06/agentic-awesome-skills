using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using AndaCompanion.Services;
using AndaCompanion.Services.Portfolio;

namespace AndaCompanion.UI;

/// <summary>
/// "Compare two files": the person chooses two versions of the same report (Excel or CSV). The window reads both on this PC, matches rows by the
/// column that identifies them, and shows a short summary, two simple charts and what changed. The summary and the report can be copied or saved.
/// A written narrative is offered through the person's AI connection, after the usual preview, and is given only the summary and the listed
/// changes. Nothing here uses the network by itself, and file contents are only ever shown as text.
/// </summary>
public partial class CompareWindow : Window
{
    private CompareTable? _a, _b;
    private string? _sheetA, _sheetB;    // for a workbook, the sheet that was read, so the person can see which one was compared
    private bool _painting, _fresh;      // _fresh: a new pair of files is in and has not been counted yet

    public CompareResult? Result { get; private set; }

    /// <summary>Opens the AI preview with the digest of the comparison. Absent when the app has no way to do that (a test, or no hook).</summary>
    public Action<string>? Narrative { get; init; }

    /// <summary>How a file is chosen and how the report is saved. The app uses the system's own dialogs; a test may put its own in place.</summary>
    public Func<bool, Task<string?>>? PickFile { get; set; }
    public Func<string, Task<bool>>? SaveReport { get; set; }
    public Func<string, Task>? WriteClipboard { get; set; }

    public CompareWindow()
    {
        InitializeComponent();
        CloseBtn.Click += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        PickA.Click += async (_, _) => await ChooseAsync(true);
        PickB.Click += async (_, _) => await ChooseAsync(false);
        SampleBtn.Click += (_, _) => { Load(FileCompare.SampleA, FileCompare.SampleB); Say("Two fictional versions of one report are in. Change the column that matches rows if you like."); };
        KeyBox.SelectionChanged += async (_, _) =>
        {
            if (_painting) return;
            bool chosen = KeyBox.SelectedIndex > 0;
            if (await RefreshAsync(chosen ? KeyBox.SelectedItem as string : null))
                Say(Result is not null && chosen ? $"Rows are now matched by “{Result.Key}”." : "The suggested column is used again.");
        };
        CopyBtn.Click += async (_, _) => { if (Result is not null) await CopyAsync(string.Join("\n", FileCompare.ForPaste(FileCompare.Summary(Result, _a!.Name, _b!.Name))), "Summary copied."); };
        CopyAllBtn.Click += async (_, _) => { if (Result is not null) await CopyAsync(string.Join("\n", FileCompare.ForPaste(FileCompare.Report(Result, _a!.Name, _b!.Name))), "Full report copied."); };
        SaveBtn.Click += async (_, _) => await SaveAsync();
        AiBtn.Click += (_, _) =>
        {
            if (Result is null || Narrative is null) return;
            Say("A window with exactly what would be sent is open. Nothing has been sent. Only the summary and the listed changes are in it, not the files.");
            Narrative(FileCompare.Digest(Result, _a!.Name, _b!.Name));
        };
        Opened += (_, _) =>
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen is not null) Body.MaxHeight = Math.Max(240, screen.WorkingArea.Height / screen.Scaling - 52 - 40 - 34 - 14 - 20);
        };
        Paint(null);
    }

    private void Say(string text) => Status.Text = text;
    public string StatusText => Status.Text ?? "";

    /// <summary>Puts one or both tables in and compares. Public so the sample files, and a test, can use it.</summary>
    public void Load(CompareTable? earlier, CompareTable? later)
    {
        if (earlier is not null) { _a = earlier; _sheetA = null; }
        if (later is not null) { _b = later; _sheetB = null; }
        _fresh = true;
        Paint(null);
    }

    /// <summary>Reads a file on this PC as a table: the table, or what went wrong in plain words. Touches no control, so it can run off the UI thread.</summary>
    private static (CompareTable? Table, string? Sheet, string? Problem) ReadTable(string path)
    {
        string name = System.IO.Path.GetFileName(path); if (name.Length > 80) name = name[..80];
        try
        {
            var raw = TableReader.Read(path);
            bool workbook = path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);
            return (new CompareTable(name, raw.Headers, raw.Rows), workbook ? FileCompare.Show(raw.SheetName, FileCompare.MaxHead) : null, null);
        }
        catch (PortfolioException ex) { return (null, null, $"{name}: {ex.Message}"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or System.Xml.XmlException)
        { return (null, null, $"{name} could not be read as a table."); }
    }

    private void Put(CompareTable table, string? sheet, bool earlier)
    {
        if (earlier) { _a = table; _sheetA = sheet; } else { _b = table; _sheetB = sheet; }
        _fresh = true;
    }

    /// <summary>Reads a file on this PC as a table and compares. Returns what went wrong in plain words, or null.</summary>
    public string? LoadFile(string path, bool earlier)
    {
        var (table, sheet, problem) = ReadTable(path);
        if (table is null) return problem;
        Put(table, sheet, earlier);
        Paint(null);
        return null;
    }

    private async Task ChooseAsync(bool earlier)
    {
        string? path;
        if (PickFile is not null) path = await PickFile(earlier);
        else
        {
            var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = earlier ? "Choose the earlier file" : "Choose the later file", AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Excel workbook or CSV") { Patterns = new[] { "*.xlsx", "*.csv" } } },
            });
            path = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
        }
        if (string.IsNullOrEmpty(path)) return;
        // A large workbook takes a moment to read. That work is done off the UI thread, so the window stays responsive.
        PickA.IsEnabled = PickB.IsEnabled = false; Say("Reading the file…");
        try
        {
            var (table, sheet, problem) = await Task.Run(() => ReadTable(path));
            if (table is null) { Say(problem ?? "The file could not be read as a table."); return; }
            Put(table, sheet, earlier);
            if (await RefreshAsync(null)) Say($"{table.Name} was read on this PC. Nothing was sent.");
        }
        finally { PickA.IsEnabled = PickB.IsEnabled = true; }
    }

    /// <summary>Above this many rows in the two files together, the comparison is done off the UI thread so the window stays responsive.</summary>
    public const int SmallRows = 2000;
    private const string TooLong = "These files took too long to compare, so the comparison was stopped. Nothing was sent.";

    /// <summary>Compares and paints. True when a result is shown (or there is nothing to compare yet); false when the comparison was stopped.</summary>
    public async Task<bool> RefreshAsync(string? keyName)
    {
        var (a, b) = (_a, _b);
        if (a is null || b is null || a.Rows.Count + b.Rows.Count <= SmallRows) return Paint(keyName);
        bool sample = SampleBtn.IsEnabled; SampleBtn.IsEnabled = false; KeyBox.IsEnabled = false; Say("Comparing…");
        try
        {
            var ready = await Task.Run(() => FileCompare.Compare(a, b, keyName));
            return ReferenceEquals(a, _a) && ReferenceEquals(b, _b) && Paint(keyName, ready);
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { Paint(null, null, compare: false); Say(TooLong); return false; }
        finally { SampleBtn.IsEnabled = sample; }
    }

    private async Task CopyAsync(string text, string done)
    {
        try
        {
            if (WriteClipboard is not null) await WriteClipboard(text);
            else if (Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
            else throw new InvalidOperationException("no clipboard");
            Say(done);
        }
        catch (Exception) { Say("Windows did not allow copying just now. Please use “Save a report…” instead."); }
    }

    private async Task SaveAsync()
    {
        if (Result is null) return;
        string html = FileCompare.HtmlReport(Result, _a!.Name, _b!.Name);
        try
        {
            bool saved;
            if (SaveReport is not null) saved = await SaveReport(html);
            else
            {
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save the comparison report", SuggestedFileName = "comparison-report.html", DefaultExtension = "html",
                    FileTypeChoices = new[] { new FilePickerFileType("Web page") { Patterns = new[] { "*.html" } } },
                });
                if (file is null) return;
                await using var stream = await file.OpenWriteAsync();
                await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
                await writer.WriteAsync(html);
                saved = true;
            }
            Say(saved ? "The report was saved. Nothing was sent." : "The report was not saved.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { Say("The report could not be saved there. Please choose another folder."); }
    }

    private static IBrush Res(string key) => (IBrush)Application.Current!.Resources[key]!;
    private static TextBlock Label(string text) => new() { Classes = { "lbl" }, Text = text, Margin = new Thickness(0, 6, 0, 0) };
    private static string N(int n) => n.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture);

    private bool Paint(string? keyName, CompareResult? ready = null, bool compare = true)
    {
        _painting = true;
        try
        {
            Out.Children.Clear(); Result = null;
            static string Sheet(string? sheet) => string.IsNullOrEmpty(sheet) ? "" : $", sheet “{sheet}”";
            NameA.Text = _a is null ? "No file yet." : $"{_a.Name}{Sheet(_sheetA)}: {N(_a.Rows.Count)} rows, {_a.Headers.Count} columns";
            NameB.Text = _b is null ? "No file yet." : $"{_b.Name}{Sheet(_sheetB)}: {N(_b.Rows.Count)} rows, {_b.Headers.Count} columns";
            foreach (var b in new[] { CopyBtn, CopyAllBtn, SaveBtn, AiBtn }) b.IsEnabled = false;
            if (_a is null || _b is null) { KeyBox.ItemsSource = new[] { "Chosen when both files are in" }; KeyBox.SelectedIndex = 0; KeyBox.IsEnabled = false; return true; }
            if (!compare) return false;

            CompareResult r;
            try { r = ready ?? FileCompare.Compare(_a, _b, keyName); }
            catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { Say(TooLong); return false; }
            Result = r;
            KeyBox.ItemsSource = new[] { "Suggested for you" }.Concat(r.Shared).ToList();
            KeyBox.SelectedIndex = keyName is not null && r.Key is not null ? r.Shared.ToList().IndexOf(r.Key) + 1 : 0;
            KeyBox.IsEnabled = r.Shared.Count > 0;

            // The summary.
            Out.Children.Add(Label("SUMMARY"));
            var lines = FileCompare.Summary(r, _a.Name, _b.Name);
            for (int i = 0; i < lines.Count; i++)
                Out.Children.Add(new TextBlock
                {
                    Text = lines[i], TextWrapping = TextWrapping.Wrap, FontSize = i == lines.Count - 1 ? 12 : 13, FontWeight = i == 0 ? FontWeight.SemiBold : FontWeight.Normal,
                    Foreground = Res(i == lines.Count - 1 ? "C.Ink2" : "C.Ink"), Tag = "summary",
                });

            // Rows: one bar in parts. Each part is also named with its number, so nothing is said by color alone.
            // "Same" is an outline and the three kinds of change are filled, so the parts differ by more than color. Every part is drawn at
            // full strength and stands at least 3 to 1 against the window in every look (a test holds this).
            var parts = new[] { ("Same", r.Same, "C.Ink3", false), ("Changed", r.ChangedRows, "C.Accent", true), ("New", r.AddedCount, "C.Good", true), ("No longer there", r.RemovedCount, "C.Warn", true) };
            Border Part(string brush, bool filled) => filled
                ? new Border { Background = Res(brush), CornerRadius = new CornerRadius(3) }
                : new Border { BorderBrush = Res(brush), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(3), Tag = "outline" };
            if (parts.Sum(p => p.Item2) > 0)
            {
                Out.Children.Add(Label("ROWS"));
                var bar = new Grid { Height = 18 };
                AutomationProperties.SetName(bar, string.Join(", ", parts.Select(p => $"{p.Item1}: {N(p.Item2)}")));
                var legend = new WrapPanel();
                foreach (var (name, n, brush, filled) in parts)
                {
                    if (n > 0)
                    {
                        bar.ColumnDefinitions.Add(new ColumnDefinition(n, GridUnitType.Star) { MinWidth = 6 });
                        var piece = Part(brush, filled); piece.Margin = new Thickness(0, 0, 3, 0);
                        Grid.SetColumn(piece, bar.ColumnDefinitions.Count - 1); bar.Children.Add(piece);
                    }
                    var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 16, 0) };
                    var swatch = Part(brush, filled); swatch.Width = swatch.Height = 10; swatch.VerticalAlignment = VerticalAlignment.Center;
                    item.Children.Add(swatch);
                    item.Children.Add(new TextBlock { Text = $"{name} {N(n)}", FontSize = 12, Foreground = Res("C.Ink") });
                    legend.Children.Add(item);
                }
                Out.Children.Add(bar); Out.Children.Add(legend);
            }

            // Changes by column.
            if (r.ByColumn.Count > 0)
            {
                Out.Children.Add(Label("CHANGES BY COLUMN"));
                int max = r.ByColumn[0].Count;
                foreach (var (column, count) in r.ByColumn.Take(8))
                {
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*,40"), ColumnSpacing = 10 };
                    row.Children.Add(new TextBlock { Text = column, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Res("C.Ink") });
                    var track = new Grid { Height = 10, VerticalAlignment = VerticalAlignment.Center };
                    track.ColumnDefinitions.Add(new ColumnDefinition(count, GridUnitType.Star) { MinWidth = 4 });
                    track.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(0, max - count), GridUnitType.Star));
                    track.Children.Add(new Border { Background = Res("C.Track"), CornerRadius = new CornerRadius(5), [Grid.ColumnSpanProperty] = 2 });
                    track.Children.Add(new Border { Background = Res("C.Accent"), CornerRadius = new CornerRadius(5) });
                    Grid.SetColumn(track, 1); row.Children.Add(track);
                    var n = new TextBlock { Text = N(count), FontSize = 12, TextAlignment = TextAlignment.Right, Foreground = Res("C.Ink") };
                    Grid.SetColumn(n, 2); row.Children.Add(n);
                    AutomationProperties.SetName(row, $"{column}: {N(count)} changes");
                    Out.Children.Add(row);
                }
                if (r.ByColumn.Count > 8) Out.Children.Add(new TextBlock { Classes = { "hint" }, Text = $"{r.ByColumn.Count - 8} more columns changed; the full report lists every change." });
            }

            // What changed.
            if (r.Changes.Count > 0)
            {
                Out.Children.Add(Label("WHAT CHANGED"));
                var table = new Grid { ColumnDefinitions = new ColumnDefinitions("92,120,*,*,104"), ColumnSpacing = 8, RowSpacing = 4 };
                void Cell(string text, int row, int col, bool head = false)
                {
                    var t = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, FontWeight = head || col == 0 ? FontWeight.SemiBold : FontWeight.Normal, Foreground = Res(head ? "C.Ink2" : "C.Ink") };
                    Grid.SetRow(t, row); Grid.SetColumn(t, col); table.Children.Add(t);
                }
                table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                string[] heads = { r.Key ?? "Row", "Column", "Earlier", "Later", "Change" };
                for (int c = 0; c < heads.Length; c++) Cell(heads[c], 0, c, head: true);
                int shown = 0;
                foreach (var change in r.Changes)
                    foreach (var x in change.Cells)
                    {
                        if (shown >= 60) break;
                        shown++; table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                        Cell(change.Row, shown, 0); Cell(x.Column, shown, 1); Cell(x.From.Length == 0 ? "(empty)" : x.From, shown, 2); Cell(x.To.Length == 0 ? "(empty)" : x.To, shown, 3); Cell(x.Note, shown, 4);
                    }
                Out.Children.Add(new Border
                {
                    BorderBrush = Res("C.Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8),
                    Child = new ScrollViewer { MaxHeight = 220, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = table },
                });
                if (r.ChangedCells > 60) Out.Children.Add(new TextBlock { Classes = { "hint" }, Text = $"The first 60 of {N(r.ChangedCells)} changed cells are shown. “Copy the full report” has more." });
            }
            if (r.AddedCount > 0)
            {
                Out.Children.Add(Label("NEW IN THE LATER FILE"));
                Out.Children.Add(new TextBlock { Text = string.Join(", ", r.Added) + (r.AddedCount > r.Added.Count ? $", and {N(r.AddedCount - r.Added.Count)} more" : ""), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = Res("C.Ink") });
            }
            if (r.RemovedCount > 0)
            {
                Out.Children.Add(Label("NO LONGER IN THE LATER FILE"));
                Out.Children.Add(new TextBlock { Text = string.Join(", ", r.Removed) + (r.RemovedCount > r.Removed.Count ? $", and {N(r.RemovedCount - r.Removed.Count)} more" : ""), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = Res("C.Ink") });
            }
            CopyBtn.IsEnabled = CopyAllBtn.IsEnabled = SaveBtn.IsEnabled = true;
            AiBtn.IsEnabled = Narrative is not null;
            if (Narrative is null) ToolTip.SetTip(AiBtn, "Needs the running app and an AI connection set up in Settings.");
            if (_fresh) { _fresh = false; UsageLog.Record("compare_files"); }      // once for a pair of files, not again for each look at it
            return true;
        }
        finally { _painting = false; }
    }
}
