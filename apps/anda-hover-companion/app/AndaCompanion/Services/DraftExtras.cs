using System.Globalization;
using System.Text.RegularExpressions;
using AndaCompanion.Models;

namespace AndaCompanion.Services;

/// <summary>A note as the draft tools see it: whose it is, its text, and a time to sort by (newest first).</summary>
public sealed record DraftNote(string Who, string Text, bool Mine, long T)
{
    /// <summary>"Jul 8" from "You · Jul 8"; the whole label when it carries no date.</summary>
    public string When => Who.LastIndexOf("· ", StringComparison.Ordinal) is var i and >= 0 ? Who[(i + 2)..] : Who;
    public bool Dated => Who.Contains("· ", StringComparison.Ordinal);
}

/// <summary>The notes of one ANDA: the person's quick notes from the card and the notes of the record, newest first.</summary>
public static class DraftNotes
{
    private static readonly Regex Day = JsRegex.Make(@"([A-Z][a-z]{2}) (\d{1,2})(?:,|\b)");

    public static IReadOnlyList<DraftNote> For(AndaRecord record, QuickNotesService? quick, DateOnly today)
    {
        var all = new List<DraftNote>();
        if (quick is not null)
            foreach (var n in quick.For(record.Number))
                all.Add(new DraftNote("You · " + Clock.Label(Clock.Ms(n.When), Clock.Zone, false), n.Text, true, Ms(Clock.Wall(n.When))));      // ordered by the day and hour it was for the person, as the record's own notes are
        for (int i = 0; i < record.Notes.Count; i++)
            all.Add(new DraftNote(record.Notes[i].Who, record.Notes[i].Text, false, NoteTime(record.Notes[i].Who, today) - i));
        return all.OrderByDescending(n => n.T).ToList();
    }

    private static long Ms(DateTime t) => (long)(DateTime.SpecifyKind(t, DateTimeKind.Utc) - DateTime.UnixEpoch).TotalMilliseconds;

    /// <summary>"You · Jul 8" gives that day in this year, or last year when the day is still ahead: a note has no year, so it is the most recent such day. A label with no date sorts last.</summary>
    internal static long NoteTime(string? who, DateOnly today)
    {
        var m = Day.Match(who ?? "");
        int mon = m.Success ? Array.IndexOf(DraftText.MonthShort, m.Groups[1].Value) : -1;
        if (mon < 0) return 0;
        int day = DraftText.Int(m.Groups[2].Value);
        DateTime At(int year) => new DateTime(year, mon + 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(day - 1);
        long t = Ms(At(today.Year)), now = Ms(today.ToDateTime(TimeOnly.MinValue));
        return t > now + 86400000L ? Ms(At(today.Year - 1)) : t;
    }
}

public sealed record FactExtrasResult(IReadOnlyList<string> Differences, IReadOnlyList<string> Notes, int Strengths, int Cycles, int DueDates, IReadOnlyList<string> Ids)
{
    /// <summary>One line that says what else was compared, shown only when none of it differs.</summary>
    public string? AlsoChecked
    {
        get
        {
            var parts = new List<string>();
            if (Strengths > 0) parts.Add($"{Strengths} strength{(Strengths == 1 ? "" : "s")}");
            if (Cycles > 0) parts.Add($"{Cycles} review cycle{(Cycles == 1 ? "" : "s")}");
            if (DueDates > 0) parts.Add($"{DueDates} due date{(DueDates == 1 ? "" : "s")}");
            if (parts.Count == 0 || Differences.Count > 0) return null;
            string joined = parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];
            return $"Also checked {joined} against your sheet: no difference.";
        }
    }
}

/// <summary>
/// More facts from the person's own data and notes, by fixed rules on this PC: a date that does not exist, the strength, the review cycle, the due date
/// of the open action item, and dates that also stand in a note. A port of the simulation's factExtras(); the shared cases are in
/// mockup/proofread-golden.json. In the app a due date is known only where the record carries an action item with a date (the sample records do;
/// a portfolio file has no such column), and the review cycle only where the record's second line says "Cycle N".
/// </summary>
public static class FactExtras
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const string MonthWords = "Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|June?|July?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?";

    private static readonly Regex
        WrittenDate = JsRegex.Make(@"\b(" + MonthWords + @")\.? (\d{1,2})(?:st|nd|rd|th)?(?:,? (\d{4}))?(?!\d)|(?<![\d\/])(\d{1,2})\/(\d{1,2})\/(\d{4})(?![\d\/])", true),
        StrengthRe = JsRegex.Make(@"\d+(?:\.\d+)?\s?(?:mg|mcg)\b", true),
        AnyWs = JsRegex.Make(@"\s"),
        Unit = JsRegex.Make(@"(mg|mcg)$"),
        CycleOfRecord = new(@"Cycle ([0-9])", RegexOptions.CultureInvariant, JsRegex.Limit),
        CycleNumber = JsRegex.Make(@"\bcycle\s*(?:no\.?\s*)?(\d)\b", true),
        CycleWord = JsRegex.Make(@"\b(first|second|third|fourth|fifth|1st|2nd|3rd|4th|5th)[- ]cycle\b", true),
        DueWord = JsRegex.Make(@"\bdue\b", true),
        DueOfItem = JsRegex.Make(@"^\s*due\s+(" + MonthWords + @")\.?\s+(\d{1,2})(?:st|nd|rd|th)?(?:,?\s+(\d{4}))?\s*$", true);

    private static readonly Dictionary<string, int> Ordinals = new(StringComparer.Ordinal)
        { ["first"] = 1, ["second"] = 2, ["third"] = 3, ["fourth"] = 4, ["fifth"] = 5, ["1st"] = 1, ["2nd"] = 2, ["3rd"] = 3, ["4th"] = 4, ["5th"] = 5 };

    /// <summary>The date and the name of the record's first action item that carries a date ("due Jul 21"), or null.</summary>
    public static (DateOnly Due, string What)? OpenItemDue(AndaRecord r, DateOnly today)
    {
        foreach (var p in r.Pending)
        {
            var m = DueOfItem.Match(p.Due ?? "");
            if (!m.Success) continue;
            int mon = DraftText.MonthOf(m.Groups[1].Value), day = DraftText.Int(m.Groups[2].Value);
            if (mon < 1 || day < 1) continue;
            var reference = r.AsOfDate ?? today;
            DateOnly? due = null;
            if (m.Groups[3].Success) due = Valid(DraftText.Int(m.Groups[3].Value), mon, day);
            else
            {
                // No year written: the nearest such day to the day the data was taken.
                foreach (int y in new[] { reference.Year - 1, reference.Year, reference.Year + 1 })
                    if (Valid(y, mon, day) is { } c && (due is null || Math.Abs(c.DayNumber - reference.DayNumber) < Math.Abs(due.Value.DayNumber - reference.DayNumber))) due = c;
            }
            if (due is null) continue;
            int dash = p.Title.IndexOf(" — ", StringComparison.Ordinal);
            string what = (dash >= 0 ? p.Title[..dash] : p.Title).Trim();
            return (due.Value, what.Length > 0 ? what : "open item");
        }
        return null;
    }

    private static DateOnly? Valid(int year, int mon, int day) =>
        year is >= 1 and <= 9999 && mon is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, mon) ? new DateOnly(year, mon, day) : null;

    /// <summary>Days in a month as the simulation counts them: a year from 0 to 99 is read as 1900 to 1999.</summary>
    private static int DaysIn(int year, int mon) => DateTime.DaysInMonth(Math.Clamp(year is >= 0 and <= 99 ? 1900 + year : year, 1, 9999), mon);

    public static FactExtrasResult Check(string? draft, Func<string, AndaRecord?> lookup, Func<AndaRecord, IReadOnlyList<DraftNote>> notesFor, DateOnly today)
    {
        var diffs = new List<(string Key, string Text)>(); var notes = new List<string>();
        int strengths = 0, cycles = 0, dues = 0;
        var ids = Proofreader.KnownIds(draft, lookup);
        if (draft is null || JsRegex.Trim(draft).Length == 0) return new(Array.Empty<string>(), notes, 0, 0, 0, ids);
        try
        {
            // A date that does not exist ("Jan 37").
            int n = 0;
            foreach (Match m in WrittenDate.Matches(draft))
            {
                if (n >= 3) break;
                int lineStart = m.Index == 0 ? 0 : draft.LastIndexOf('\n', m.Index) + 1;
                if (lineStart < m.Index && draft.IndexOf('\t', lineStart, m.Index - lineStart) >= 0) continue;
                bool named = m.Groups[1].Success;
                int mon = named ? DraftText.MonthOf(m.Groups[1].Value) : DraftText.Int(m.Groups[4].Value);
                int day = DraftText.Int(named ? m.Groups[2].Value : m.Groups[5].Value);
                bool hasYear = named ? m.Groups[3].Success : true;
                int year = named ? (m.Groups[3].Success ? DraftText.Int(m.Groups[3].Value) : 2028) : DraftText.Int(m.Groups[6].Value);     // with no year, Feb 29 is allowed
                if (mon < 1 || mon > 12) { n++; diffs.Add(("x" + m.Index, $"“{JsRegex.Squash(m.Value)}” is not a date: there is no month {mon}.")); continue; }
                int dim = DaysIn(year, mon);
                if (day < 1 || day > dim)
                {
                    n++;
                    diffs.Add(("x" + m.Index, $"“{JsRegex.Squash(m.Value)}” is not a date: {DraftText.MonthLong[mon - 1]}{(hasYear ? " " + year.ToString(Inv) : "")} has {(hasYear || mon != 2 ? "" : "at most ")}{dim} days."));
                }
            }

            var noteHits = new HashSet<string>(StringComparer.Ordinal);
            foreach (string paragraph in DraftText.ParagraphCut.Split(draft))
            {
                string? context = null;
                foreach (string sentence in DraftText.Sentences(paragraph))
                {
                    if (JsRegex.Trim(sentence).Length == 0) continue;
                    if (sentence.Contains('\t')) continue;                               // rows copied from a spreadsheet are for Ask, not for this
                    var nums = DraftText.AndaNumbers(sentence); string? subject;
                    if (nums.Count == 1) { subject = nums[0]; context = subject; }
                    else if (nums.Count > 1) { subject = null; context = null; }
                    else subject = context;
                    if (subject is null || lookup(subject) is not { } d) continue;

                    // Strength.
                    {
                        var mine = StrengthRe.Matches($"{d.Drug} {d.Strength}").Select(Norm).ToList();
                        var said = StrengthRe.Matches(sentence).Select(Norm).ToList();
                        if (mine.Count > 0 && said.Count > 0 && nums.Count == 1)
                        {
                            strengths++;
                            if (!said.Any(mine.Contains) && !diffs.Any(x => x.Key == "s" + subject))
                                diffs.Add(("s" + subject, $"ANDA {subject}: the draft says {Unit.Replace(said[0], " $1")}, but your data says {(StrengthRe.IsMatch(d.Drug) || string.IsNullOrWhiteSpace(d.Strength) ? d.Drug : d.Drug + ", " + d.Strength)}."));
                        }
                    }

                    // Review cycle.
                    {
                        var own = CycleOfRecord.Match(d.Meta);
                        int mine = own.Success ? own.Groups[1].Value[0] - '0' : 0;
                        var c = CycleNumber.Match(sentence); if (!c.Success) c = CycleWord.Match(sentence);
                        if (mine > 0 && c.Success)
                        {
                            string word = c.Groups[1].Value;
                            int said = word.Length == 1 && char.IsAsciiDigit(word[0]) ? word[0] - '0' : Ordinals.GetValueOrDefault(word.ToLowerInvariant());
                            cycles++;
                            if (said != mine && !diffs.Any(x => x.Key == "c" + subject))
                                diffs.Add(("c" + subject, $"ANDA {subject}: the draft says cycle {said}, but your data says cycle {mine}."));
                        }
                    }

                    // Dates: the open action item of the record, and dates that also stand in a note.
                    var mentions = DraftText.Mentions(sentence).Select(mt => (Mt: mt, Which: DraftText.KeywordFor(sentence, mt))).ToList();
                    if (OpenItemDue(d, today) is { } item && DueWord.IsMatch(sentence))
                    {
                        var plain = mentions.Where(x => x.Which is null).ToList();
                        if (plain.Count > 0)
                        {
                            dues++;
                            bool hit = plain.Any(x => x.Mt.Mon == item.Due.Month && x.Mt.Day == item.Due.Day && (x.Mt.Year is null || x.Mt.Year == item.Due.Year));
                            if (!hit && !diffs.Any(x => x.Key == "d" + subject))
                                diffs.Add(("d" + subject, $"ANDA {subject}: the draft gives a due date of {DraftText.Show(plain[0].Mt.Mon, plain[0].Mt.Day, plain[0].Mt.Year)}, but your data says the {item.What} is due {item.Due.ToString("MMM d, yyyy", Inv)}."));
                        }
                    }
                    if (mentions.Count == 0) continue;
                    var own2 = notesFor(d);
                    foreach (var (mt, _) in mentions)
                        foreach (var note in own2)
                        {
                            bool found = DraftText.Mentions(note.Text, skipGlued: false).Any(nt => nt.Mon == mt.Mon && nt.Day == mt.Day);
                            string key = subject + "|" + mt.Mon + "/" + mt.Day + "|" + note.Who;
                            if (found && !noteHits.Contains(key) && noteHits.Count < 3)
                            {
                                noteHits.Add(key);
                                notes.Add(note.Dated
                                    ? $"“{JsRegex.Squash(mt.Raw)}” is also in your note of {note.When} on ANDA {subject}: “{Cut(note.Text, 110)}”"
                                    : $"“{JsRegex.Squash(mt.Raw)}” is also in your notes ({note.Who}) on ANDA {subject}: “{Cut(note.Text, 110)}”");
                            }
                        }
                }
            }
        }
        catch (RegexMatchTimeoutException) { /* what was found so far is still shown */ }
        return new(diffs.Select(x => x.Text).ToList(), notes, strengths, cycles, dues, ids);
    }

    private static string Norm(Match m) => AnyWs.Replace(m.Value, "").ToLowerInvariant();

    private static readonly Regex WsRun = JsRegex.Make(@"\s+");

    /// <summary>One line, cut to a length with an ellipsis.</summary>
    internal static string Cut(string t, int n)
    {
        t = JsRegex.Trim(WsRun.Replace(t ?? "", " "));
        return t.Length > n ? JsRegex.Slice(t, 0, n - 1).TrimEnd(' ') + "…" : t;
    }
}

public sealed record RecallNote(DraftNote Note, IReadOnlyList<string> Shared);
public sealed record RecallItem(string Title, string Due, bool Urgent, string Source);
public sealed record RecallGroup(string Number, string Drug, IReadOnlyList<RecallNote> Notes, IReadOnlyList<RecallItem> Items, string? Latest);

/// <summary>
/// For recall: for each ANDA the draft names (the first three), the person's notes, with the ones the draft touches first and then the newest, and the
/// open items from the record and the two to-do lists. Nothing here is a finding; it is the person's own material, put beside the draft.
/// </summary>
public static class Recall
{
    private static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "that", "this", "with", "from", "have", "will", "were", "been", "they", "their", "there", "about", "would", "could", "should", "which", "where", "when", "what", "your",
        "anda", "status", "please", "thanks", "team", "also", "into", "than", "then", "them", "some", "same", "date", "next", "week",
    };
    private static readonly Regex LongWord = new("[a-z]{4,}", RegexOptions.CultureInvariant, JsRegex.Limit);

    private static List<string> WordsOf(string? t) =>
        LongWord.Matches((t ?? "").ToLowerInvariant()).Select(m => m.Value).Where(w => !Stop.Contains(w)).Distinct().ToList();

    private static List<string> DatesOf(string? t) => DraftText.Mentions(t ?? "").Select(mt => DraftText.Show(mt.Mon, mt.Day, null)).ToList();

    public static IReadOnlyList<RecallGroup> For(string? draft, Func<string, AndaRecord?> lookup, Func<AndaRecord, IReadOnlyList<DraftNote>> notesFor,
        IEnumerable<TodoItem>? andaTodo = null, IEnumerable<MyTodoItem>? myTodo = null)
    {
        var groups = new List<RecallGroup>();
        if (draft is null || JsRegex.Trim(draft).Length == 0) return groups;
        try
        {
            var words = new HashSet<string>(WordsOf(draft), StringComparer.Ordinal); var dates = new HashSet<string>(DatesOf(draft), StringComparer.Ordinal);
            var todo = andaTodo?.ToList() ?? new List<TodoItem>(); var mine = myTodo?.ToList() ?? new List<MyTodoItem>();
            foreach (string id in Proofreader.KnownIds(draft, lookup).Take(3))
            {
                var d = lookup(id)!;
                var all = notesFor(d);
                var notes = all.Select(n => new RecallNote(n, DatesOf(n.Text).Where(dates.Contains).Distinct().Concat(WordsOf(n.Text).Where(words.Contains)).ToList()))
                    .OrderByDescending(n => n.Shared.Count > 0).ThenByDescending(n => n.Note.T).ToList();
                var items = new List<RecallItem>();
                items.AddRange(d.Pending.Select(p => new RecallItem(p.Title, p.Due, p.Urgent, "your data")));
                items.AddRange(todo.Where(i => !i.Done && i.Number == id).Select(i => new RecallItem(i.Title,
                    TodoService.ParseIso(i.Due) is { } due ? "due " + due.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) : "", false, "ANDA to-do list")));
                items.AddRange(mine.Where(i => !i.Done && i.Title.Contains(id, StringComparison.Ordinal)).Select(i => new RecallItem(i.Title, "", false, "My to-do list")));
                var latest = all.OrderByDescending(n => n.T).FirstOrDefault();
                groups.Add(new RecallGroup(id, d.Drug, notes, items, latest?.When));
            }
        }
        catch (RegexMatchTimeoutException) { /* the groups found so far are still shown */ }
        return groups;
    }
}

/// <summary>What the "one question before you send" box holds: the most important open item, or that there is none.</summary>
public sealed record SendQuestion(bool Gap, string Text);

/// <summary>
/// One question before sending: the most important of the items the checks found, chosen by fixed rules, in a fixed order: a difference from the
/// record, an ANDA number that is not in the data, a difference in strength, cycle or due date, a date the draft itself puts in doubt, and a mention
/// of an attachment (the tool cannot see attachments). Whether to send, and to whom, stays the person's decision.
/// </summary>
public static class BeforeSend
{
    public const string NoGaps = "No open gaps.";
    public const string GapNote = "The most important of the items above, chosen by fixed rules on this PC. Whether to send, and to whom, stays your call.";
    public const string NoGapNote = "None of the checks above found a gap. This tool cannot see recipients or attachments, and it does not judge tone.";

    private static readonly Regex
        DateDiff = JsRegex.Make(@"^ANDA (\d{6}): the draft gives the (goal|forecast) date as (.+), but the record says (.+)\.$"),
        StatusDiff = JsRegex.Make(@"^ANDA (\d{6}): the draft sounds (on track|late or at risk), but the record's forecast is (.+)\.$"),
        WeekdayDiff = JsRegex.Make(@"^The draft says “(.+)”, but (.+) is an? ([A-Z][a-z]+day)\.$"),
        UnknownNumber = JsRegex.Make(@"^ANDA (\d{6}) "),
        SelfDoubt = JsRegex.Make(@"Please check|not a business day|a federal holiday|is in the past|appears twice"),
        Attachment = JsRegex.Make(@"\b(attached|attachment|attachments|attaching|enclosed)\b", true);

    public static SendQuestion? Ask(string? text, DraftCheckResult review, FactExtrasResult? extra)
    {
        if (text is null || JsRegex.Trim(text).Length == 0) return null;
        try
        {
            var f = review.Findings;
            var diff = f.FirstOrDefault(x => x.Kind == DraftFindingKind.Difference);
            var unknown = f.FirstOrDefault(x => x.Kind == DraftFindingKind.UnknownAnda);
            if (diff is not null)
            {
                Match m;
                if ((m = DateDiff.Match(diff.Text)).Success) return Q($"For ANDA {m.Groups[1].Value}, which {m.Groups[2].Value} date is right: {m.Groups[3].Value} (your draft) or {m.Groups[4].Value} (the record)?");
                if ((m = StatusDiff.Match(diff.Text)).Success) return Q($"Is ANDA {m.Groups[1].Value} really {m.Groups[2].Value}? The record's forecast is {m.Groups[3].Value}.");
                if ((m = WeekdayDiff.Match(diff.Text)).Success) return Q($"In “{m.Groups[1].Value}”, which is right: the day or the date? {m.Groups[2].Value} is a {m.Groups[3].Value}.");
                return Q("Is this difference intended? " + diff.Text);
            }
            if (unknown is not null && UnknownNumber.Match(unknown.Text) is { Success: true } u) return Q($"Is {u.Groups[1].Value} the right ANDA number? It is not in your data.");
            if (extra is not null && extra.Differences.Count > 0) return Q("Is this difference intended? " + extra.Differences[0]);
            var self = f.FirstOrDefault(x => x.Kind == DraftFindingKind.Note && SelfDoubt.IsMatch(x.Text));
            if (self is not null) return Q("Is this intended? " + self.Text);
            if (Attachment.IsMatch(text)) return Q("Is the file attached? The draft mentions an attachment, and this tool cannot see attachments.");
        }
        catch (RegexMatchTimeoutException) { }
        return new SendQuestion(false, NoGaps);
    }

    private static SendQuestion Q(string text) => new(true, text);
}
