using System.Globalization;
using AndaCompanion.Data;
using AndaCompanion.Models;

namespace AndaCompanion.Services.Portfolio;

public sealed record PortfolioStatus(bool Ok, int Count, string Message);

/// <summary>
/// An RPM's own spreadsheet as the data source. It shows only what the sheet contains: a forecast or progress that is not in
/// the sheet is shown as unknown, never estimated. Read-only. The file is re-read when it changes, so an edit in Excel shows on
/// the next card.
/// </summary>
public sealed class PortfolioRepository : IAndaRepository
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private sealed record Row(string Number, string Drug, string Applicant, DateOnly? Goal, DateOnly? Forecast, string Status, string Notes, string Cycle,
        DateOnly? Submitted, string Ingredient, string Form, string Strength, DateOnly? Psg);

    private readonly string _path;
    private readonly IReadOnlyDictionary<string, string>? _overrides;
    private readonly string? _notesFolder;
    private readonly Func<DateOnly> _today;
    private Dictionary<string, Row> _rows = new();
    private Dictionary<string, List<Row>> _byApplicant = new(StringComparer.OrdinalIgnoreCase);   // so a card's sister ANDAs are found without scanning every row
    private Dictionary<string, List<NoteSnippet>> _notes = new();
    private DateTime _loadedWrite;
    private long _loadedLength;
    private DateOnly _asOf;

    public PortfolioStatus Status { get; private set; } = new(false, 0, "Not loaded.");
    public IReadOnlyList<string> Headers { get; private set; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> Mapping { get; private set; } = new Dictionary<string, string>();
    public string FileName => Path.GetFileName(_path);

    public PortfolioRepository(string path, IReadOnlyDictionary<string, string>? overrides = null, string? notesFolder = null, Func<DateOnly>? today = null)
    {
        _path = path;
        _overrides = overrides;
        _notesFolder = notesFolder;
        _today = today ?? (() => DateOnly.FromDateTime(DateTime.Now));
        Reload();
    }

    public void Reload()
    {
        try
        {
            var info = new FileInfo(_path);
            var table = TableReader.Read(_path);
            var map = PortfolioColumns.Detect(table.Headers, _overrides);
            if (!map.ContainsKey("number"))
            {
                Headers = table.Headers; Mapping = map; _rows = new(); _byApplicant = new(StringComparer.OrdinalIgnoreCase);
                Status = new(false, 0, "No column for the ANDA number was found. Choose which column holds it in Settings.");
                return;
            }
            int Col(string key) => map.TryGetValue(key, out var h) ? table.Headers.ToList().IndexOf(h) : -1;
            string Cell(IReadOnlyList<string> r, int i) => i >= 0 && i < r.Count ? r[i].Trim() : "";
            DateOnly? Date(IReadOnlyList<string> r, int i) => DateParsing.TryParse(Cell(r, i), out var d) ? d : null;

            int cNum = Col("number"), cDrug = Col("drug"), cApp = Col("applicant"), cGoal = Col("goal"), cFc = Col("forecast"), cStatus = Col("status"),
                cNotes = Col("notes"), cCycle = Col("cycle"), cSub = Col("submitted"), cIng = Col("ingredient"), cForm = Col("form"), cStr = Col("strength"), cPsg = Col("psg");

            var rows = new Dictionary<string, Row>();
            int skipped = 0;
            foreach (var r in table.Rows)
            {
                string? number = AndaValidator.Extract(Cell(r, cNum));
                if (number is null) { skipped++; continue; }
                rows[number] = new Row(number, Cell(r, cDrug), Cell(r, cApp), Date(r, cGoal), Date(r, cFc), Cell(r, cStatus), Cell(r, cNotes), Cell(r, cCycle),
                    Date(r, cSub), Cell(r, cIng), Cell(r, cForm), Cell(r, cStr), Date(r, cPsg));
            }
            Headers = table.Headers; Mapping = map; _rows = rows;
            _byApplicant = rows.Values.Where(r => r.Applicant.Length > 0).GroupBy(r => r.Applicant, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            _loadedWrite = info.LastWriteTime; _loadedLength = info.Length;
            _asOf = DateOnly.FromDateTime(info.LastWriteTime);
            _notes = NotesIndex.Build(_notesFolder);
            string missing = string.Join(", ", new[] { ("goal", "goal date"), ("forecast", "forecast"), ("drug", "drug") }.Where(x => !map.ContainsKey(x.Item1)).Select(x => x.Item2));
            Status = new(true, rows.Count,
                $"Loaded {rows.Count} ANDA{(rows.Count == 1 ? "" : "s")} from {Path.GetFileName(_path)} (file saved {info.LastWriteTime.ToString("MMM d, HH:mm", Inv)})." +
                (skipped > 0 ? $" {skipped} row{(skipped == 1 ? "" : "s")} without a readable ANDA number skipped." : "") +
                (missing.Length > 0 ? $" Not found in your sheet: {missing}." : ""));
        }
        catch (PortfolioException ex) { _rows = new(); Status = new(false, 0, ex.Message); }
    }

    /// <summary>Re-reads the file if it changed on disk since the last load.</summary>
    private void RefreshIfChanged()
    {
        try
        {
            var info = new FileInfo(_path);
            if (info.Exists && (info.LastWriteTime != _loadedWrite || info.Length != _loadedLength)) Reload();
        }
        catch { /* keep what is loaded */ }
    }

    public AndaRecord? Find(string number)
    {
        RefreshIfChanged();
        if (!_rows.TryGetValue(number, out var row)) return null;
        var today = _today();
        string drug = row.Drug.Length > 0 ? row.Drug : AndaRecord.Unknown;

        string goalText = row.Goal is { } g ? g.ToString("MMM d, yyyy", Inv) : AndaRecord.Unknown;
        int? daysToGoal = row.Goal is { } g2 ? g2.DayNumber - today.DayNumber : null;
        string goalDays = daysToGoal switch
        {
            null => "", 0 => "· today", 1 => "· 1 day", -1 => "· 1 day ago", > 1 => $"· {daysToGoal} days", _ => $"· {-daysToGoal} days ago",
        };

        string forecastText = AndaRecord.Unknown, delta = "no forecast in your sheet"; bool late = false; int? deltaDays = null;
        if (row.Forecast is { } f)
        {
            forecastText = f.ToString("MMM d, yyyy", Inv);
            if (row.Goal is { } gg)
            {
                int d = f.DayNumber - gg.DayNumber; deltaDays = d; late = d > 0;
                delta = d > 0 ? $"▲ +{d} d vs goal" : d < 0 ? $"▼ {-d} d ahead of goal" : "on goal";
            }
            else delta = "no goal date to compare";
        }

        var notes = new List<NoteItem>();
        if (row.Notes.Length > 0) notes.Add(new NoteItem("Your sheet", row.Notes));
        if (_notes.TryGetValue(number, out var snippets))
            foreach (var s in snippets.OrderByDescending(x => x.Date).Take(3))
                notes.Add(new NoteItem($"{s.File} · {s.Date.ToString("MMM d", Inv)}", s.Text));

        var sisters = (row.Applicant.Length > 0 && _byApplicant.TryGetValue(row.Applicant, out var sameApplicant) ? sameApplicant : new List<Row>())
            .Where(x => x.Number != number && SameProduct(x, row))
            .Take(3).Select(x => new SisterRef(x.Number, $"{(x.Drug.Length > 0 ? x.Drug : "same product")} — same applicant")).ToList();

        string meta = string.Join(" · ", new[] { row.Applicant, row.Cycle.Length > 0 ? $"Cycle {row.Cycle}" : "" }.Where(x => x.Length > 0));
        return new AndaRecord
        {
            Number = number, Drug = drug, Meta = meta.Length > 0 ? meta : "From your portfolio",
            GoalDate = goalText, GoalDays = goalDays, DaysToGoal = daysToGoal, ProgressPct = -1,
            ForecastDate = forecastText, ForecastDelta = delta, ForecastDeltaDays = deltaDays, ForecastLate = late,
            ForecastConfidence = row.Forecast is null ? "" : "as entered in your sheet",
            Attention = null,
            Latest = row.Status.Length > 0 ? row.Status : AndaRecord.Unknown,
            DataAsOf = $"your portfolio file, saved {_asOf.ToString("MMM d", Inv)} · {FileName}",
            Ahead = null, Pending = Array.Empty<PendingItem>(), Notes = notes,
            ForecastDetail = "Dates and notes come from your portfolio file, as you entered them. This tool does not compute a forecast for your data.",
            Sisters = sisters,
            GoalDateValue = row.Goal, ForecastDateValue = row.Forecast, SubmittedDate = row.Submitted, AsOfDate = _asOf,
            Demo = false, SourceLabel = $"Your portfolio: {FileName}",
            ActiveIngredient = row.Ingredient.Length > 0 ? row.Ingredient : null, DosageForm = row.Form.Length > 0 ? row.Form : null,
            Strength = row.Strength.Length > 0 ? row.Strength : null, PsgDateFromSheet = row.Psg,
        };
    }

    private static bool SameProduct(Row a, Row b)
    {
        if (a.Ingredient.Length > 0 && b.Ingredient.Length > 0) return string.Equals(a.Ingredient, b.Ingredient, StringComparison.OrdinalIgnoreCase);
        string First(string s) => s.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return First(a.Drug).Length > 0 && string.Equals(First(a.Drug), First(b.Drug), StringComparison.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> Numbers => _rows.Keys;
}
