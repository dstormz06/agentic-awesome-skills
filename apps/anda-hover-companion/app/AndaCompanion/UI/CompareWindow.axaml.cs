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
    private ReadDocument? _docA, _docB;  // a side holds a table or a document, never both
    private string _mode = "two";        // "two" files, a report against my "data", or my sheet since last "week"
    private string? _weekProblem;
    private IReadOnlyList<CustomAction> _prompts = Array.Empty<CustomAction>();
    private bool _settingWeek;
    private string? _sheetA, _sheetB;    // for a workbook, the sheet that was read, so the person can see which one was compared
    private bool _painting, _fresh;      // _fresh: a new pair of files is in and has not been counted yet

    public CompareResult? Result { get; private set; }

    /// <summary>What is compared just now: "table", "doc" (two documents), "data" (a report against my data), or null when nothing is.</summary>
    public string? Kind { get; private set; }
    public DocResult? Doc { get; private set; }
    public ReportCheckResult? Checked { get; private set; }
    public string Mode => _mode;

    /// <summary>The person's stored prompts, and how one is shown once it is filled in (the usual preview). Absent in a test or with no hook.</summary>
    public Func<IReadOnlyList<CustomAction>>? Prompts { get; init; }
    public Action<CustomAction, string, string>? RunPrompt { get; init; }
    public Func<DateOnly>? Today { get; init; }

    /// <summary>The goal and forecast dates of the person's own data for an ANDA number, as the card shows them; null when the ANDA is not there.</summary>
    public Func<string, (string Goal, string Forecast)?>? Mine { get; init; }

    /// <summary>The weekly copy of the person's sheet: whether it is on, how it is switched, and the pair to compare (the last kept copy and the sheet now).</summary>
    public Func<bool>? WeeklyOn { get; init; }
    public Action<bool>? SetWeekly { get; init; }
    public Func<(CompareTable? Earlier, CompareTable? Now, string? Problem)>? WeeklyPair { get; init; }

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
        SampleDocsBtn.Click += (_, _) =>
        {
            LoadDocuments(new ReadDocument(DocCompare.SampleNameA, DocCompare.SampleA, Array.Empty<string>()), new ReadDocument(DocCompare.SampleNameB, DocCompare.SampleB, Array.Empty<string>()));
            Say("Two fictional versions of one letter are in. They are compared paragraph by paragraph.");
        };
        SampleReportBtn.Click += (_, _) => { Load(SampleReport(), null); Say("A fictional weekly report is in. It is checked against the data the cards show."); };
        ModeTwoBtn.Click += (_, _) => SetMode("two");
        ModeDataBtn.Click += (_, _) => SetMode("data");
        ModeWeekBtn.Click += (_, _) => SetMode("week");
        WeekBox.IsCheckedChanged += (_, _) =>
        {
            if (_settingWeek) return;
            bool on = WeekBox.IsChecked == true;
            SetWeekly?.Invoke(on);
            LoadWeek(); Paint(null);
            Say(on ? "From now on one copy of your sheet is kept each week, on this PC only." : "No copies are kept, and the copies that were kept are deleted.");
        };
        UseBtn.Click += (_, _) => UsePrompt();
        CheckBtn.Click += (_, _) => CheckAnswer();
        KeyBox.SelectionChanged += async (_, _) =>
        {
            if (_painting) return;
            bool chosen = KeyBox.SelectedIndex > 0;
            if (await RefreshAsync(chosen ? KeyBox.SelectedItem as string : null))
                Say(Result is not null && chosen ? $"Rows are now matched by “{Result.Key}”." : "The suggested column is used again.");
        };
        CopyBtn.Click += async (_, _) => { if (Kind is not null) await CopyAsync(string.Join("\n", FileCompare.ForPaste(Lines(false))), "Summary copied."); };
        CopyAllBtn.Click += async (_, _) => { if (Kind is not null) await CopyAsync(string.Join("\n", FileCompare.ForPaste(Lines(true))), "Full report copied."); };
        SaveBtn.Click += async (_, _) => await SaveAsync();
        AiBtn.Click += (_, _) =>
        {
            if (Kind is null || Narrative is null) return;
            Say("A window with exactly what would be sent is open. Nothing has been sent. Only the summary and the listed changes are in it, not the files.");
            Narrative(Kind == "table" ? FileCompare.Digest(Result!, _a!.Name, _b!.Name) : CompareMaterial.CutText(string.Join("\n", Lines(true)), 1800));
        };
        Opened += (_, _) =>
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen is not null) Body.MaxHeight = Math.Max(240, screen.WorkingArea.Height / screen.Scaling - 52 - 40 - 34 - 14 - 20);
            RefreshPrompts();
        };
        Paint(null);
    }

    private void Say(string text) => Status.Text = text;
    public string StatusText => Status.Text ?? "";

    /// <summary>Puts one or both tables in and compares. Public so the sample files, and a test, can use it.</summary>
    public void Load(CompareTable? earlier, CompareTable? later)
    {
        if (earlier is not null) { _a = earlier; _sheetA = null; _docA = null; }
        if (later is not null) { _b = later; _sheetB = null; _docB = null; }
        _fresh = true;
        Paint(null);
    }

    /// <summary>Puts one or both documents in and compares them paragraph by paragraph.</summary>
    public void LoadDocuments(ReadDocument? earlier, ReadDocument? later)
    {
        if (earlier is not null) { _docA = earlier; _a = null; _sheetA = null; }
        if (later is not null) { _docB = later; _b = null; _sheetB = null; }
        _fresh = true;
        Paint(null);
    }

    /// <summary>Two files, a report against my data, or my sheet since the last kept copy. Changing the way clears what was chosen.</summary>
    public void SetMode(string mode)
    {
        _mode = mode is "data" or "week" ? mode : "two";
        _a = _b = null; _docA = _docB = null; _sheetA = _sheetB = null; _weekProblem = null; AnswerBox.Text = "";
        foreach (var (button, name, on) in new[] { (ModeTwoBtn, "Two files", _mode == "two"), (ModeDataBtn, "A report against my data", _mode == "data"), (ModeWeekBtn, "My sheet since last week", _mode == "week") })
        {
            button.Classes.Set("primary", on);
            AutomationProperties.SetName(button, on ? name + ", selected" : name);
        }
        if (_mode == "week") LoadWeek();
        Paint(null); Say("");
    }

    private void LoadWeek()
    {
        _a = _b = null; _docA = _docB = null; _weekProblem = null;
        if (WeeklyOn?.Invoke() != true || WeeklyPair is null) return;
        var (earlier, now, problem) = WeeklyPair();
        if (earlier is null || now is null) { _weekProblem = problem ?? "There is no earlier copy of your sheet yet."; return; }
        _a = earlier; _b = now; _fresh = true;
    }

    /// <summary>Fills the list of stored prompts: the ones that are switched on and that fill a prompt or ask the AI.</summary>
    public void RefreshPrompts()
    {
        _prompts = (Prompts?.Invoke() ?? Array.Empty<CustomAction>()).Where(a => a.Enabled && a.Kind is ActionKind.FillPrompt or ActionKind.AskAi).ToList();
        PromptBox.ItemsSource = _prompts.Count == 0
            ? new[] { "No stored prompts yet. Add one under Settings." }
            : new[] { "Choose one of your stored prompts…" }.Concat(_prompts.Select(a => $"{a.Name} · {(a.Kind == ActionKind.AskAi ? "ask my AI" : "fill and show")}")).ToList();
        PromptBox.SelectedIndex = 0; PromptBox.IsEnabled = _prompts.Count > 0;
        UseBtn.IsEnabled = Kind is not null && _prompts.Count > 0 && RunPrompt is not null;
    }

    private string NameOfA => _docA?.Name ?? _a?.Name ?? "";
    private string NameOfB => _docB?.Name ?? _b?.Name ?? "";
    private string TextOfA => _docA?.Text ?? (_a is null ? "" : CompareMaterial.TableText(_a));
    private string TextOfB => _docB?.Text ?? (_b is null ? "" : CompareMaterial.TableText(_b));

    /// <summary>The summary, or the whole report, of whatever is compared.</summary>
    public IReadOnlyList<string> Lines(bool full) => Kind switch
    {
        "data" => Checked!.Lines,
        "doc" => full ? DocCompare.Report(Doc!, NameOfA, NameOfB) : DocCompare.Summary(Doc!, NameOfA, NameOfB),
        "table" => full ? FileCompare.Report(Result!, NameOfA, NameOfB) : FileCompare.Summary(Result!, NameOfA, NameOfB),
        _ => Array.Empty<string>(),
    };

    /// <summary>Everything an answer may rightly quote: the report and both files.</summary>
    public string Facts()
    {
        var parts = new List<string> { string.Join("\n", Lines(true)) };
        if (NameOfA.Length > 0) parts.Add(NameOfA + "\n" + TextOfA);
        if (NameOfB.Length > 0) parts.Add(NameOfB + "\n" + TextOfB);
        return string.Join("\n", parts);
    }

    /// <summary>
    /// One of the person's stored prompts, run on what was compared. It is given the differences the rules found; the beginning of each file goes
    /// in only if the person ticks the box. The preview shows every character and the size before anything can be sent.
    /// </summary>
    private void UsePrompt()
    {
        if (Kind is null || RunPrompt is null) return;
        if (PromptBox.SelectedIndex <= 0 || PromptBox.SelectedIndex > _prompts.Count) { Say("Please choose one of your stored prompts first."); return; }
        var action = _prompts[PromptBox.SelectedIndex - 1]; bool whole = WholeBox.IsChecked == true, data = Kind == "data";
        string material = CompareMaterial.For(Lines(true), NameOfA, TextOfA, data ? null : NameOfB, data ? "" : TextOfB, whole);
        string prompt = CompareMaterial.Prompt(action.Template, material, (Today ?? (() => DateOnly.FromDateTime(DateTime.Now)))());
        string holds = whole ? "It holds the differences found by the rules and, because you chose so, the beginning of each file." : "It holds the differences found by the rules, not the files.";
        Say("A window with exactly what the prompt holds is open. Nothing has been sent.");
        RunPrompt(action, prompt, holds);
    }

    /// <summary>Every number, date and name with a digit in the pasted answer is looked up in the comparison and in the two files.</summary>
    private void CheckAnswer()
    {
        if (Kind is null) return;
        var r = AnswerCheck.Check(AnswerBox.Text, Facts());
        CheckOut.Children.Clear();
        for (int i = 0; i < r.Lines.Count; i++)
        {
            bool limit = r.Lines.Count > 1 && i == r.Lines.Count - 1, miss = !limit && r.Missing.Count > 0;
            var text = new TextBlock { Text = r.Lines[i], TextWrapping = TextWrapping.Wrap, FontSize = limit ? 12 : 13, Foreground = Res(limit ? "C.Ink2" : miss ? "C.Warn" : "C.Ink"), Tag = miss ? "miss" : "check" };
            CheckOut.Children.Add(miss ? new Border { Background = Res("C.WarnBg"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8), Child = text, Tag = "missbox" } : text);
        }
    }

    /// <summary>A fictional weekly report made from the data the cards show, with one forecast moved and one ANDA the data does not hold.</summary>
    private CompareTable SampleReport()
    {
        var rows = new List<IReadOnlyList<string>>(); bool moved = false;
        foreach (string id in new[] { "214990", "216482" })
        {
            if (Mine?.Invoke(id) is not { } d) continue;
            string forecast = d.Forecast;
            if (!moved && FileCompare.Day(forecast) is { } day) { forecast = DateOnly.FromDayNumber(day + 14).ToString("M/d/yyyy", System.Globalization.CultureInfo.InvariantCulture); moved = true; }
            rows.Add(new[] { id, d.Goal, forecast });
        }
        rows.Add(new[] { "220017", "4/10/2027", "4/10/2027" });
        return new CompareTable("Weekly report (sample)", new[] { "ANDA", "Goal date", "Forecast date" }, rows);
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
        if (earlier) { _a = table; _sheetA = sheet; _docA = null; } else { _b = table; _sheetB = sheet; _docB = null; }
        _fresh = true;
    }

    /// <summary>Reads a file on this PC, as a table or as a document by its kind, and compares. Returns what went wrong in plain words, or null.</summary>
    public string? LoadFile(string path, bool earlier)
    {
        if (DocumentReader.IsDocument(path))
        {
            var (document, why) = DocumentReader.Read(path);
            if (document is null) return why;
            PutDocument(document, earlier);
            Paint(null);
            return null;
        }
        var (table, sheet, problem) = ReadTable(path);
        if (table is null) return problem;
        Put(table, sheet, earlier);
        Paint(null);
        return null;
    }

    private void PutDocument(ReadDocument document, bool earlier)
    {
        if (earlier) { _docA = document; _a = null; _sheetA = null; } else { _docB = document; _b = null; _sheetB = null; }
        _fresh = true;
    }

    private async Task ChooseAsync(bool earlier)
    {
        string? path;
        if (PickFile is not null) path = await PickFile(earlier);
        else
        {
            var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = _mode == "data" ? "Choose the report" : earlier ? "Choose the earlier file" : "Choose the later file", AllowMultiple = false,
                FileTypeFilter = _mode == "data"
                    ? new[] { new FilePickerFileType("Excel workbook or CSV") { Patterns = new[] { "*.xlsx", "*.csv" } } }
                    : new[] { new FilePickerFileType("Tables and documents") { Patterns = new[] { "*.xlsx", "*.csv", "*.docx", "*.txt", "*.md" } } },
            });
            path = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
        }
        if (string.IsNullOrEmpty(path)) return;
        // A large workbook takes a moment to read. That work is done off the UI thread, so the window stays responsive.
        PickA.IsEnabled = PickB.IsEnabled = false; Say("Reading the file…");
        try
        {
            if (DocumentReader.IsDocument(path))
            {
                var (document, why) = await Task.Run(() => DocumentReader.Read(path));
                if (document is null) { Say(why ?? "The file could not be read as a document."); return; }
                PutDocument(document, earlier);
                if (Paint(null)) Say($"{document.Name} was read on this PC. Nothing was sent.");
                return;
            }
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
        if (_mode == "data" || a is null || b is null || a.Rows.Count + b.Rows.Count <= SmallRows) return Paint(keyName);
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
        if (Kind is null) return;
        string html = Kind == "table" ? FileCompare.HtmlReport(Result!, _a!.Name, _b!.Name)
            : FileCompare.HtmlPage(Kind == "data" ? $"Check against my data: {NameOfA}" : $"Comparison: {NameOfA} and {NameOfB}", Lines(true));
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

    private void AddSummary(IReadOnlyList<string> lines)
    {
        Out.Children.Add(Label("SUMMARY"));
        for (int i = 0; i < lines.Count; i++)
            Out.Children.Add(new TextBlock
            {
                Text = lines[i], TextWrapping = TextWrapping.Wrap, FontSize = i == lines.Count - 1 ? 12 : 13, FontWeight = i == 0 ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = Res(i == lines.Count - 1 ? "C.Ink2" : "C.Ink"), Tag = "summary",
            });
    }

    /// <summary>One bar in parts, each also named with its number. The first part is an outline and the rest are filled, so they differ by more than color.</summary>
    private void AddBar(string label, (string Name, int Count, string Brush, bool Filled)[] parts)
    {
        if (parts.Sum(p => p.Count) <= 0) return;
        Out.Children.Add(Label(label));
        Border Part(string brush, bool filled) => filled
            ? new Border { Background = Res(brush), CornerRadius = new CornerRadius(3) }
            : new Border { BorderBrush = Res(brush), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(3), Tag = "outline" };
        var bar = new Grid { Height = 18 };
        AutomationProperties.SetName(bar, string.Join(", ", parts.Select(p => $"{p.Name}: {N(p.Count)}")));
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

    private void PaintDoc(DocResult r)
    {
        AddSummary(DocCompare.Summary(r, NameOfA, NameOfB));
        AddBar("PARAGRAPHS", new[] { ("Same", r.Same, "C.Ink3", false), ("Changed", r.Changed, "C.Accent", true), ("New", r.Added, "C.Good", true), ("No longer there", r.Removed, "C.Warn", true) });
        if (r.Changes.Count == 0) return;
        Out.Children.Add(Label("WHAT CHANGED"));
        var table = new Grid { ColumnDefinitions = new ColumnDefinitions("74,104,*,*"), ColumnSpacing = 8, RowSpacing = 4 };
        void Cell(string text, int row, int col, bool head = false)
        {
            var t = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, FontWeight = head || col == 0 ? FontWeight.SemiBold : FontWeight.Normal, Foreground = Res(head ? "C.Ink2" : "C.Ink"), Tag = head ? "head" : "cell" };
            Grid.SetRow(t, row); Grid.SetColumn(t, col); table.Children.Add(t);
        }
        table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        string[] heads = { "Paragraph", "What", "Earlier", "Later" };
        for (int c = 0; c < heads.Length; c++) Cell(heads[c], 0, c, head: true);
        int shown = 0;
        foreach (var change in r.Changes)
        {
            shown++; table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Cell(N(change.At), shown, 0); Cell(change.Kind switch { "changed" => "Changed", "added" => "New", _ => "No longer there" }, shown, 1); Cell(change.From, shown, 2); Cell(change.To, shown, 3);
        }
        Out.Children.Add(new Border
        {
            BorderBrush = Res("C.Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8),
            Child = new ScrollViewer { MaxHeight = 220, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = table },
        });
        int all = r.Changed + r.Added + r.Removed;
        if (all > r.Changes.Count) Out.Children.Add(new TextBlock { Classes = { "hint" }, Text = $"The first {r.Changes.Count} of {N(all)} changes are shown." });
    }

    private void PaintData(ReportCheckResult r)
    {
        AddSummary(r.Lines.Where(l => !l.StartsWith("ANDA ", StringComparison.Ordinal) && !l.StartsWith("…and ", StringComparison.Ordinal)).ToList());
        AddBar("ANDAS THE REPORT NAMES", new[] { ("Agree", r.Agree, "C.Ink3", false), ("Differ", r.Differ, "C.Accent", true), ("Not in my data", r.Unknown.Count, "C.Warn", true) });
        if (r.Diffs.Count == 0) return;
        Out.Children.Add(Label("WHERE THE REPORT AND MY DATA DIFFER"));
        foreach (string d in r.Diffs.Take(40)) Out.Children.Add(new TextBlock { Text = d, TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = Res("C.Ink"), Tag = "diff" });
        if (r.Diffs.Count > 40) Out.Children.Add(new TextBlock { Classes = { "hint" }, Text = $"The first 40 of {N(r.Diffs.Count)} differences are shown. “Copy the full report” has the count." });
    }

    private bool Paint(string? keyName, CompareResult? ready = null, bool compare = true)
    {
        _painting = true;
        try
        {
            Out.Children.Clear(); Result = null; Doc = null; Checked = null; Kind = null; CheckOut.Children.Clear();
            static string Sheet(string? sheet) => string.IsNullOrEmpty(sheet) ? "" : $", sheet “{sheet}”";
            string Size(CompareTable? t, ReadDocument? d, string? sheet) => d is not null ? $"{d.Name}: {N(DocCompare.Paras(d.Text).Count)} paragraphs"
                : t is not null ? $"{t.Name}{Sheet(sheet)}: {N(t.Rows.Count)} rows, {t.Headers.Count} columns" : "No file yet.";
            NameA.Text = Size(_a, _docA, _sheetA); NameB.Text = Size(_b, _docB, _sheetB);
            SaysA.Text = string.Join(" ", _docA?.Says ?? Array.Empty<string>()); SaysA.IsVisible = SaysA.Text.Length > 0;
            SaysB.Text = string.Join(" ", _docB?.Says ?? Array.Empty<string>()); SaysB.IsVisible = SaysB.Text.Length > 0;
            bool data = _mode == "data", week = _mode == "week", weekOn = WeeklyOn?.Invoke() == true;
            PickGrid.IsVisible = !week; PickBPanel.IsVisible = !data; LabelA.Text = data ? "REPORT" : "EARLIER FILE"; PickA.Content = data ? "Choose the report…" : "Choose the earlier file…";
            WeekPanel.IsVisible = week; _settingWeek = true; WeekBox.IsChecked = weekOn; WeekBox.IsEnabled = SetWeekly is not null; _settingWeek = false;
            SampleBtn.IsVisible = SampleDocsBtn.IsVisible = _mode == "two"; SampleReportBtn.IsVisible = data && Mine is not null;
            KeyRow.IsVisible = !data && !(_docA is not null && (_docB is not null || _b is null));      // a column is chosen for tables only
            foreach (var b in new[] { CopyBtn, CopyAllBtn, SaveBtn, AiBtn, UseBtn, CheckBtn }) b.IsEnabled = false;
            void KeyOff(string text) { KeyBox.ItemsSource = new[] { text }; KeyBox.SelectedIndex = 0; KeyBox.IsEnabled = false; }
            void Note(string text) => Out.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = Res("C.Ink"), Tag = "say" });
            void Ready(string kind)
            {
                Kind = kind;
                CopyBtn.IsEnabled = CopyAllBtn.IsEnabled = SaveBtn.IsEnabled = CheckBtn.IsEnabled = true;
                AiBtn.IsEnabled = Narrative is not null; UseBtn.IsEnabled = _prompts.Count > 0 && RunPrompt is not null;
                if (Narrative is null) ToolTip.SetTip(AiBtn, "Needs the running app and an AI connection set up in Settings.");
                if (_fresh) { _fresh = false; UsageLog.Record("compare_files"); }      // once for a pair of files, not again for each look at it
            }
            if (week && !weekOn) { KeyOff("Chosen when both files are in"); Note("Nothing is kept yet. When this is switched on, the app keeps one copy of your sheet each week, on this PC only, so it can show what changed since the last copy."); return true; }
            if (week && (_a is null || _b is null)) { KeyOff("Chosen when both files are in"); Note(_weekProblem ?? "There is no earlier copy of your sheet yet."); return true; }
            if (data)
            {
                KeyOff("Rows are matched by the ANDA number");
                if (_docA is not null) { Note("A report is a table. Please choose an Excel workbook or a CSV file."); return true; }
                if (_a is null) return true;
                Checked = ReportCheck.Check(_a, Mine ?? (_ => null));
                PaintData(Checked); Ready("data");
                return true;
            }
            bool hasA = _a is not null || _docA is not null, hasB = _b is not null || _docB is not null;
            if (!hasA || !hasB) { KeyOff("Chosen when both files are in"); return true; }
            if ((_docA is null) != (_docB is null)) { KeyOff("Chosen when both files are in"); Note("One file is a table and the other is a document, so they cannot be compared. Please choose two tables or two documents."); return true; }
            if (_docA is not null)
            {
                KeyOff("Documents are compared paragraph by paragraph");
                Doc = DocCompare.Compare(_docA.Text, _docB!.Text);
                PaintDoc(Doc); Ready("doc");
                return true;
            }
            if (_a is null || _b is null) return true;
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
            Ready("table");
            return true;
        }
        finally { _painting = false; }
    }
}
