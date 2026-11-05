using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AndaCompanion.Data;

namespace AndaCompanion.Services;

public enum TodoCategory { Work = 0, Personal = 1, Other = 2 }

public enum Repeat { None = 0, Daily = 1, Weekdays = 2, Weekly = 3, Monthly = 4 }

/// <summary>One item the note reader found. <see cref="Actionable"/> is false for a sentence that reads like a note, not a task: it is offered, but not ticked.</summary>
public sealed record SuggestedItem(string Title, TodoCategory Category, DateOnly? Due, bool High, Repeat Repeat, int RepeatDay, string? Number, bool Actionable);

/// <summary>
/// Turns rough notes (a highlighted paragraph, a pasted email, a list typed in a hurry) into suggested to-do items. It is plain rules, offline:
/// no AI, no network, nothing leaves the PC. It splits the text into tasks, removes filler such as "don't forget to", and reads a due date,
/// a priority, a repeat and a category from the words. It never invents anything: a value it did not find stays empty, and the person can change
/// every field before anything is added. The same rules are ported to the browser simulation and kept equal by a shared fixture.
/// </summary>
public static class NoteParser
{
    public const int MaxInputChars = 6000, MaxItems = 30, MaxTitleChars = 160;
    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(250);
    // Digits are written [0-9], never \d: \d also matches digits of other scripts, which no date code can read. The browser copy of these rules has only ASCII digits too.
    private static Regex R(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Limit);

    private const string Full = "monday|tuesday|wednesday|thursday|friday|saturday|sunday";
    private const string Months = "jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|june?|july?|aug(?:ust)?|sept?(?:ember)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?";

    private static readonly string[] Verbs =
    {
        "call", "email", "e-mail", "mail", "text", "message", "send", "review", "submit", "check", "update", "schedule", "book", "pay", "buy", "fix", "order", "clean",
        "follow", "prepare", "draft", "confirm", "finish", "file", "renew", "remind", "ask", "find", "return", "pick", "drop", "cancel", "sign", "upload", "download", "print",
        "plan", "read", "write", "talk", "speak", "meet", "contact", "reply", "respond", "forward", "share", "attach", "approve", "request", "arrange", "organize", "organise",
        "set", "sort", "wash", "mow", "water", "feed", "walk", "take", "bring", "get", "make", "add", "remove", "delete", "create", "build", "test", "verify", "discuss",
        "decide", "research", "look", "investigate", "complete", "close", "open", "start", "stop", "change", "move", "replace", "install", "repair", "paint", "cook", "shop",
        "vacuum", "dust", "mop", "empty", "pack", "unpack", "ship", "post", "publish", "record", "log", "track", "monitor", "escalate", "notify", "inform", "brief",
        "summarize", "summarise", "compile", "collect", "gather", "prep", "coordinate", "assign", "delegate", "finalize", "finalise", "resolve", "address", "handle",
        "process", "reconcile", "double-check", "proofread", "edit", "revise", "rewrite", "cc", "loop", "ping", "nudge", "chase", "thank", "apologize", "invite", "rsvp",
        "register", "enroll", "apply", "vote", "donate", "charge", "refill", "restock", "tidy", "declutter", "assemble", "hang", "mount", "patch", "seal", "unclog",
        "flush", "reach", "touch", "double", "make sure", "ensure", "remember", "bake", "sweep", "scrub", "wipe", "fold", "iron", "swap", "rotate", "mark", "tag", "route", "triage", "prioritize", "prioritise", "reassign", "reschedule", "analyze", "analyse", "evaluate", "assess", "validate", "identify", "determine", "follow-up",
    };
    private static readonly HashSet<string> VerbSet = new(Verbs, StringComparer.OrdinalIgnoreCase);

    private static readonly string[] Abbreviations = { "dr", "mr", "mrs", "ms", "no", "vs", "e.g", "i.e", "a.m", "p.m", "st", "jr", "sr", "inc", "approx", "etc", "fig", "ext", "dept", "est", "mt", "ave" };

    private static readonly Regex Bullet = R(@"^\s*(?:[-*•–—▪◦·]+|[0-9]{1,3}[.)]|\[[ xX]?\]|(?-i:\(?[a-z][.)]))\s+");
    private static readonly Regex Header = R(@"^\s*(?:from|to|cc|bcc|subject|sent|date|reply-to|importance|attachments?)\s*:");
    private static readonly Regex GreetingWord = R(@"^\s*(?:hi|hello|hey|dear|thanks|thank you|regards|best regards|kind regards|sincerely|cheers|fyi|fwiw|ok|okay|noted|got it|sounds good)\b[\s,!.\-–]*");
    private static readonly Regex Fraction = R(@"^\s*(?:cup|cups|tsp|tbsp|teaspoon|tablespoon|inch|inches|in\b|lb|lbs|oz|mile|miles|hour|hours|hr|hrs|of|day|days)");
    private static readonly Regex UrlOnly = R(@"^\s*(?:https?://|www\.)\S+\s*$");
    private static readonly Regex Lead = R(@"^\s*(?:(?:oh,? )?and also|also|and then|then|plus|oh and|btw|and)[,:]?\s+");
    private static readonly Regex ListLead = R(@"^\s*(?:groceries|grocery list|shopping list|shopping|errands|to buy|to get|to bring|to pack)\s*[:\-]");
    private static readonly Regex Filler = R(@"^\s*(?:(?:please|pls)\s+)?(?:don'?t forget to|do not forget to|remember to|need to|needs to|have to|has to|got to|gotta|must|should|todo:?|to do:?|action(?: item)?:?|ai:|task:|reminder:|note to self:?|i need to|i have to|i must|i should|we need to|we should|we must|can you|could you|will you|would you|please|pls)\s+");
    private static readonly Regex Marker = R(@"^\s*(?:don'?t forget|do not forget|remember to|need to|needs to|have to|has to|got to|gotta|must\b|should\b|todo|to do|action|ai:|task:|reminder|note to self|i need to|i have to|i must|i should|we need to|we should|we must|can you|could you|will you|would you|please|pls)");

    // ---- annotations ----
    private static readonly Regex HighWords = R(@"\b(?:urgent|urgently|asap|a\.s\.a\.p|important|critical|immediately|right away|high priority|top priority|time[- ]sensitive|emergency)\b|!!|\(!\)|^\s*!\s");
    private static readonly Regex Daily = R(@"\b(?:every ?day|daily|each day|each morning|every morning|each evening|every evening|every night|nightly)\b");
    private static readonly Regex WeekdaysRx = R(@"\b(?:every|each)\s+week ?day\b|\bweek ?days\b|\bmon(?:day)?\s*(?:-|to|through|thru)\s*fri(?:day)?\b");
    private static readonly Regex WeeklyDay = R(@"\b(?:every|each)\s+(" + Full + @")\b|\b(?:weekly on|on)\s+(" + Full + @")s\b|\b(" + Full + @")s\b");
    private static readonly Regex WeeklyAny = R(@"\b(?:weekly|every week|each week|once a week)\b");
    private static readonly Regex MonthlyAny = R(@"\b(?:monthly|every month|each month|once a month)\b");

    // ---- due dates ----
    private static readonly Regex Iso = R(@"\b([0-9]{4})-([0-9]{2})-([0-9]{2})\b");
    private static readonly Regex Numeric = R(@"\b([0-9]{1,2})/([0-9]{1,2})(?:/([0-9]{4}|[0-9]{2}))?\b");
    private static readonly Regex MonthDay = R(@"\b(" + Months + @")\.?\s+([0-9]{1,2})(?:st|nd|rd|th)?(?:,?\s+([0-9]{4}))?\b");
    private static readonly Regex DayMonth = R(@"\b([0-9]{1,2})(?:st|nd|rd|th)?\s+(?:of\s+)?(" + Months + @")\.?(?:,?\s+([0-9]{4}))?\b");
    private static readonly Regex Today = R(@"\b(?:today|tonight|this evening|this afternoon|this morning|eod|end of (?:the )?day|cob|close of business)\b");
    private static readonly Regex Tomorrow = R(@"\b(?:tomorrow|tmrw|tmr)\b");
    private static readonly Regex DayAfter = R(@"\bday after tomorrow\b");
    private static readonly Regex InN = R(@"\bin ([0-9]{1,3}) (day|days|week|weeks|month|months)\b");
    private static readonly Regex NextWeek = R(@"\bnext week\b");
    private static readonly Regex ThisWeek = R(@"\b(?:this week|eow|end of (?:the )?week)\b");
    private static readonly Regex Weekend = R(@"\b(?:this weekend|(?:on|over|for) the weekend)\b");
    private static readonly Regex CommaLead = R(@"^\s*(?:and then|and also|and|then|also|plus)\s+");
    private static readonly Regex NextMonth = R(@"\bnext month\b");
    private static readonly Regex EndMonth = R(@"\b(?:eom|end of (?:the )?month)\b");
    private static readonly Regex NextDay = R(@"\bnext (" + Full + @")\b");
    private static readonly Regex Named = R(@"\b(" + Full + @")\b(?!['’]s)(?!s\b)");
    private static readonly Regex Short = R(@"\b(?:by|on|before|until|due|this|next|every|each)\s+(mon|tues?|wed|thur?s?|fri|sat|sun)\b");
    private static readonly Regex DayOfMonth = R(@"\bthe ([0-9]{1,2})(?:st|nd|rd|th)\b");

    private static readonly Regex Space = R(@"\s+");
    private static readonly Regex SplitVerbConnector = R(@"\s+(?:and also|and then|also|plus|then|after that|next)\s+");
    private static readonly Regex SplitAnd = R(@"\s+and\s+");

    private static readonly Regex WorkWords = R(@"\b(?:anda|ir|ind|nda|fda|dpm|rpm|supervisor|huddle|meeting|memo|report|review|submission|deficienc\w*|labeling|labelling|cycle|applicant|goal date|forecast|deadline|status|project|team|client|invoice|budget|presentation|slides|spreadsheet|timesheet|calendar|agenda|minutes|email|e-mail|inbox|manager|director|colleague|coworker|training|compliance|audit|policy|sop|procedure|ticket|stakeholder|quarterly|office|information requests?|discipline|consult\w*|amendments?|inspections?|facilit(?:y|ies)|assessments?|reviewers?|approval|tentative|complete response|controlled correspondence|citizen petition|patents?|exclusivity|bioequivalen\w*|cmc|division|branch|team lead|workload|backlog|queue|one-on-one|performance|appraisal|telework|travel|expenses?|briefing|deliverables?|milestones?|action items?|sign-off|clearance|docket|orange book|paragraph iv|gdufa|huddles?|tracker|tracking sheet)\b");
    private static readonly Regex PersonalWords = R(@"\b(?:home|house|apartment|landlord|rent|mortgage|hoa|plumber|plumbing|leak\w*|faucet|sink|toilet|drain|roof|gutters?|furnace|hvac|thermostat|water heater|appliances?|dishwasher|fridge|refrigerator|oven|stove|washer|dryer|laundry|dishes|vacuum\w*|mop\w*|dust\w*|clean\w*|tidy\w*|declutter\w*|trash|garbage|recycl\w*|lawn|mow\w*|garden\w*|yard|fence|paint\w*|repair\w*|handyman|electrician|groceries|grocery|dinner|lunch|cook\w*|kids?|child\w*|school|daycare|dentist|doctor|pharmacy|prescriptions?|vet|pets?|dog|cat|car|oil change|tires?|utilit\w*|electric bill|water bill|gas bill|insurance|garage|basement|attic|closet|bedroom|kitchen|bathroom|living room|sofa|couch|mattress|curtains?|light ?bulbs?|smoke detector|filters?)\b");

    /// <summary>All the items found in <paramref name="text"/>, in the order written, at most <see cref="MaxItems"/>. Never throws.</summary>
    public static IReadOnlyList<SuggestedItem> Parse(string? text, DateOnly today)
    {
        var found = new List<SuggestedItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var segment in Segments(Clean(text)))
            {
                var item = Read(segment, today);
                if (item is null || !seen.Add(item.Title)) continue;
                found.Add(item);
                if (found.Count >= MaxItems) break;
            }
        }
        catch (Exception ex) when (ex is RegexMatchTimeoutException or FormatException or OverflowException or ArgumentException) { /* very odd input: what was found so far is still offered */ }
        return found;
    }

    /// <summary>One item from one line the person typed (no splitting), or null if there is nothing in it.</summary>
    public static SuggestedItem? ParseOne(string? text, DateOnly today)
    {
        try
        {
            string clean = Clean(text);
            return clean.Length == 0 ? null : Read(clean.Replace('\n', ' '), today, keepEvenIfNote: true);
        }
        catch (Exception ex) when (ex is RegexMatchTimeoutException or FormatException or OverflowException or ArgumentException) { return null; }
    }

    private static string Clean(string? text)
    {
        var sb = new StringBuilder();
        bool cut = false; char next = '\0';
        foreach (char c in (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n'))
        {
            if (sb.Length >= MaxInputChars) { cut = true; next = c; break; }
            if (c == '\n' || c == '\t') sb.Append(c == '\t' ? ' ' : c);
            else if (!char.IsControl(c)) sb.Append(c);
        }
        return (cut ? CutAtWord(sb.ToString(), next) : sb.ToString()).Trim();
    }

    /// <summary>
    /// When the reading limit falls inside a sentence, that unfinished last sentence (or line) is dropped, so a cut-off fragment never becomes an item of its own.
    /// It looks back at most 300 characters; if there is no sentence end or line break there, it drops only the unfinished word. <paramref name="next"/> is the first character that did not fit.
    /// </summary>
    private static string CutAtWord(string head, char next)
    {
        if (next is '\n' or '\r') return head;                                   // the line ended exactly at the limit
        int end = head.Length;
        while (end > 0 && head[end - 1] is ' ' or '\n' or '\r') end--;
        if (end == 0 || head[end - 1] is '.' or '!' or '?' or ';' or '"' or '”' or ')') return head;     // it ended on a finished sentence
        int low = Math.Max(0, end - 300), space = -1;
        for (int i = end - 1; i >= low; i--)
        {
            char ch = head[i];
            if (ch is '.' or '!' or '?' or ';') return head[..(i + 1)];
            if (ch is '\n' or '\r') return head[..i];
            if (ch == ' ' && space < 0) space = i;
        }
        return space >= 0 ? head[..space] : head;
    }

    /// <summary>The text for the notes box: unchanged if it fits, otherwise cut at the reading limit without leaving half a word at the end.</summary>
    public static string FitToLimit(string? text)
    {
        string t = text ?? "";
        return t.Length <= MaxInputChars ? t : CutAtWord(t[..MaxInputChars], t[MaxInputChars]);
    }

    // ---------- splitting ----------

    private static IEnumerable<string> Segments(string text)
    {
        foreach (var rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0) continue;
            if (Bullet.IsMatch(line)) { foreach (var p in SplitInside(Bullet.Replace(line, "", 1))) yield return p; continue; }
            foreach (var sentence in Sentences(line))
                foreach (var p in SplitInside(sentence)) yield return p;
        }
    }

    /// <summary>Sentences, without splitting after an abbreviation (Dr., No., a.m.) or an initial.</summary>
    private static IEnumerable<string> Sentences(string line)
    {
        int start = 0;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == ';') { yield return line[start..i]; start = i + 1; continue; }
            if (c is not ('.' or '!' or '?')) continue;
            int j = i + 1;
            while (j < line.Length && line[j] is '.' or '!' or '?' or '"' or '”' or ')') j++;
            if (j >= line.Length) break;
            if (!char.IsWhiteSpace(line[j])) { i = j - 1; continue; }
            int k = j; while (k < line.Length && char.IsWhiteSpace(line[k])) k++;
            if (k >= line.Length) break;
            if (!(char.IsLetterOrDigit(line[k]) || line[k] is '"' or '“' or '(')) { i = j - 1; continue; }     // notes are often typed in lower case
            if (c == '.')
            {
                int s = i; while (s > start && !char.IsWhiteSpace(line[s - 1])) s--;
                string token = line[s..i].Trim('(', '"', '“').ToLowerInvariant();
                if (Array.IndexOf(Abbreviations, token) >= 0 || (token.Length == 1 && char.IsLetter(token[0])) || token.All(char.IsDigit)) { i = j - 1; continue; }
            }
            yield return line[start..j];                        // keeps every closing mark, so "!!" still shows how urgent it is
            start = k; i = k - 1;
        }
        if (start < line.Length) yield return line[start..];
    }

    /// <summary>Splits "call the applicant and email the team lead" into two tasks, but only where both halves start with an action word.</summary>
    private static IEnumerable<string> SplitInside(string segment)
    {
        string s = segment.Trim();
        if (s.Length == 0) yield break;
        for (int c = s.IndexOf(','); c >= 0; c = s.IndexOf(',', c + 1))        // "update the tracker, file the memo, and call the applicant" is three tasks
        {
            string left = s[..c], right = CommaLead.Replace(s[(c + 1)..], "", 1);
            if (left.Trim().Contains(' ') && right.Trim().Contains(' ') && StartsWithVerb(StripFiller(left, out _)) && StartsWithVerb(StripFiller(right, out _)))
            {
                foreach (var a in SplitInside(left)) yield return a;
                foreach (var b in SplitInside(right)) yield return b;
                yield break;
            }
        }
        foreach (var connector in new[] { SplitVerbConnector, SplitAnd })
        {
            var m = connector.Match(s);
            while (m.Success)
            {
                string left = s[..m.Index], right = s[(m.Index + m.Length)..];
                if (left.Trim().Contains(' ') && right.Trim().Contains(' ') && StartsWithVerb(StripFiller(left, out _)) && StartsWithVerb(StripFiller(right, out _)))
                {
                    foreach (var a in SplitInside(left)) yield return a;
                    foreach (var b in SplitInside(right)) yield return b;
                    yield break;
                }
                m = m.NextMatch();
            }
        }
        yield return s;
    }

    // ---------- reading one task ----------

    private static string StripFiller(string text, out bool hadMarker)
    {
        string s = text.Trim();
        var lead = Lead.Match(s);
        if (lead.Success) s = s[lead.Length..];
        hadMarker = Marker.IsMatch(s) || ListLead.IsMatch(s);
        for (int i = 0; i < 2; i++)
        {
            var m = Filler.Match(s);
            if (!m.Success) break;
            s = s[m.Length..];
        }
        return s.TrimStart(' ', '-', '–', '—', ':');
    }

    private static bool StartsWithVerb(string s)
    {
        var m = Regex.Match(s, @"^[A-Za-z][A-Za-z\-]*", RegexOptions.CultureInvariant, Limit);
        return m.Success && VerbSet.Contains(m.Value);
    }

    private static SuggestedItem? Read(string segment, DateOnly today, bool keepEvenIfNote = false)
    {
        string original = Space.Replace(segment, " ").Trim();
        if (original.Length < 3 || !original.Any(char.IsLetterOrDigit)) return null;
        if (!keepEvenIfNote && (Header.IsMatch(original) || UrlOnly.IsMatch(original))) return null;
        if (!keepEvenIfNote)
        {
            // "Hi Sam" or "Thanks," alone is dropped; "Thanks, please send the report" keeps its task without the greeting.
            var g = GreetingWord.Match(original);
            if (g.Success)
            {
                string rest = original[g.Length..].Trim(' ', ',', '!', '.');
                var words = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (rest.Length == 0 || (words.Length <= 2 && !VerbSet.Contains(words[0].Trim(',', '.').ToLowerInvariant()) && words.All(w => w.All(c => char.IsLetter(c) || c is '\'' or '-' or ',' or '.')))) return null;
                original = rest;
            }
        }

        string title = StripFiller(original, out bool hadMarker);
        title = title.TrimEnd('.', ',', ';', ':', '!', ' ', '-', '–', '—');
        if (title.Length == 0) return null;
        title = char.ToUpperInvariant(title[0]) + title[1..];
        if (title.Length > MaxTitleChars)
        {
            int cut = title.LastIndexOf(' ', MaxTitleChars - 1);
            title = title[..(cut > 40 ? cut : MaxTitleChars - 1)].TrimEnd(' ', ',', ';') + "…";
        }

        string? number = AndaValidator.Extract(original);
        bool high = HighWords.IsMatch(original);
        var (repeat, repeatDay) = ReadRepeat(original, today);
        var due = ReadDue(original, today);
        if (due is null && repeat != Repeat.None) due = FirstOccurrence(repeat, repeatDay, today);
        if (repeat == Repeat.Weekly && repeatDay < 0) repeatDay = (int)(due ?? today).DayOfWeek;
        if (repeat == Repeat.Monthly && repeatDay <= 0) repeatDay = (due ?? today).Day;
        if (repeat == Repeat.Weekly && repeatDay < 0) repeatDay = (int)today.DayOfWeek;

        bool actionable = hadMarker || StartsWithVerb(StripFiller(original, out _)) || high || due is not null || repeat != Repeat.None;
        return new SuggestedItem(title, ReadCategory(original, number), due, high, repeat, repeatDay, number, actionable);
    }

    private static TodoCategory ReadCategory(string text, string? number)
    {
        int work = WorkWords.Matches(text).Count + (number is null ? 0 : 2), home = PersonalWords.Matches(text).Count;
        return work == 0 && home == 0 ? TodoCategory.Other : home > work ? TodoCategory.Personal : TodoCategory.Work;
    }

    // ---------- repeat ----------

    private static (Repeat, int) ReadRepeat(string text, DateOnly today)
    {
        if (Daily.IsMatch(text)) return (Repeat.Daily, -1);
        if (WeekdaysRx.IsMatch(text)) return (Repeat.Weekdays, -1);
        var m = WeeklyDay.Match(text);
        if (m.Success)
        {
            string name = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
            return (Repeat.Weekly, (int)DayOf(name));
        }
        if (WeeklyAny.IsMatch(text)) return (Repeat.Weekly, -1);
        if (MonthlyAny.IsMatch(text))
        {
            var d = DayOfMonth.Match(text);
            return (Repeat.Monthly, d.Success && int.TryParse(d.Groups[1].Value, out int dd) && dd is >= 1 and <= 31 ? dd : -1);
        }
        return (Repeat.None, -1);
    }

    /// <summary>The first date a repeating item falls due, on or after <paramref name="from"/>.</summary>
    public static DateOnly FirstOccurrence(Repeat repeat, int day, DateOnly from) => OnOrAfter(repeat, day, from);

    /// <summary>The next date on or after <paramref name="from"/> that fits the repeat (a day of the week for weekly, a day of the month for monthly).</summary>
    public static DateOnly OnOrAfter(Repeat repeat, int day, DateOnly from)
    {
        var d = from;
        switch (repeat)
        {
            case Repeat.Daily: return d;
            case Repeat.Weekdays: while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(1); return d;
            case Repeat.Weekly:
                int want = day is >= 0 and <= 6 ? day : (int)from.DayOfWeek;
                while ((int)d.DayOfWeek != want) d = d.AddDays(1);
                return d;
            case Repeat.Monthly:
                int dom = Math.Clamp(day <= 0 ? from.Day : day, 1, 31);
                for (int i = 0; i < 400; i++)
                {
                    if (d.Day == Math.Min(dom, DateTime.DaysInMonth(d.Year, d.Month))) return d;
                    d = d.AddDays(1);
                }
                return from;
            default: return from;
        }
    }

    // ---------- due dates ----------

    private static DayOfWeek DayOf(string name) => name.ToLowerInvariant()[..3] switch
    {
        "mon" => DayOfWeek.Monday, "tue" => DayOfWeek.Tuesday, "wed" => DayOfWeek.Wednesday, "thu" => DayOfWeek.Thursday,
        "fri" => DayOfWeek.Friday, "sat" => DayOfWeek.Saturday, _ => DayOfWeek.Sunday,
    };

    private static DateOnly OnOrAfterDay(DayOfWeek day, DateOnly from) { var d = from; while (d.DayOfWeek != day) d = d.AddDays(1); return d; }
    private static DateOnly StrictlyAfterDay(DayOfWeek day, DateOnly from) => OnOrAfterDay(day, from.AddDays(1));

    private static int MonthNumber(string name) => name.ToLowerInvariant()[..3] switch
    {
        "jan" => 1, "feb" => 2, "mar" => 3, "apr" => 4, "may" => 5, "jun" => 6, "jul" => 7, "aug" => 8, "sep" => 9, "oct" => 10, "nov" => 11, _ => 12,
    };

    private static DateOnly? Make(int year, int month, int day, bool yearGiven, DateOnly today)
    {
        if (month is < 1 or > 12 || day < 1 || year < 1 || year > 9998 || day > DateTime.DaysInMonth(year, month)) return null;
        var d = new DateOnly(year, month, day);
        // No year written: this year, unless that is long past (then the next one): a note about 11/12 written in December means next November.
        if (!yearGiven && today.DayNumber - d.DayNumber > 30)
        {
            if (day > DateTime.DaysInMonth(year + 1, month)) return null;
            d = new DateOnly(year + 1, month, day);
        }
        return d;
    }

    /// <summary>The first date written in the text (the earliest in the sentence), or null. "next Friday" means the first Friday after today.</summary>
    public static DateOnly? ReadDue(string text, DateOnly today)
    {
        try { return ReadDueCore(text ?? "", today); }
        catch (Exception ex) when (ex is RegexMatchTimeoutException or FormatException or OverflowException or ArgumentException) { return null; }
    }

    private static DateOnly? ReadDueCore(string text, DateOnly today)
    {
        var hits = new List<(int Index, DateOnly Date)>();
        void Add(Match m, DateOnly? d) { if (m.Success && d is { } v) hits.Add((m.Index, v)); }

        foreach (Match m in Iso.Matches(text)) { Add(m, Make(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture), true, today)); break; }
        foreach (Match m in Numeric.Matches(text))
        {
            if (Fraction.IsMatch(text[(m.Index + m.Length)..])) continue;        // "1/2 cup" is not January 2
            int y = today.Year; bool given = m.Groups[3].Success;
            if (given) { y = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture); if (y < 100) y += 2000; }
            var d = Make(y, int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), given, today);
            if (d is not null) { Add(m, d); break; }
        }
        foreach (Match m in MonthDay.Matches(text))
        {
            bool given = m.Groups[3].Success;
            var d = Make(given ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : today.Year, MonthNumber(m.Groups[1].Value), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), given, today);
            if (d is not null) { Add(m, d); break; }
        }
        foreach (Match m in DayMonth.Matches(text))
        {
            bool given = m.Groups[3].Success;
            var d = Make(given ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : today.Year, MonthNumber(m.Groups[2].Value), int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), given, today);
            if (d is not null) { Add(m, d); break; }
        }

        var t = Today.Match(text); if (t.Success) hits.Add((t.Index, today));
        t = Tomorrow.Match(text); if (t.Success) hits.Add((t.Index, today.AddDays(1)));
        t = DayAfter.Match(text); if (t.Success) hits.Add((t.Index, today.AddDays(2)));
        t = InN.Match(text);
        if (t.Success && int.TryParse(t.Groups[1].Value, out int n))
        {
            string unit = t.Groups[2].Value.ToLowerInvariant();
            hits.Add((t.Index, unit.StartsWith("day") ? today.AddDays(n) : unit.StartsWith("week") ? today.AddDays(7 * n) : today.AddMonths(n)));
        }
        t = NextWeek.Match(text); if (t.Success) hits.Add((t.Index, StrictlyAfterDay(DayOfWeek.Monday, today)));
        t = ThisWeek.Match(text);
        if (t.Success) hits.Add((t.Index, today.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? today : OnOrAfterDay(DayOfWeek.Friday, today)));
        t = Weekend.Match(text); if (t.Success) hits.Add((t.Index, today.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? today : OnOrAfterDay(DayOfWeek.Saturday, today)));
        t = NextMonth.Match(text); if (t.Success) { var f = today.AddMonths(1); hits.Add((t.Index, new DateOnly(f.Year, f.Month, 1))); }
        t = EndMonth.Match(text); if (t.Success) hits.Add((t.Index, new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month))));
        t = NextDay.Match(text); if (t.Success) hits.Add((t.Index, StrictlyAfterDay(DayOf(t.Groups[1].Value), today)));
        t = Named.Match(text); if (t.Success) hits.Add((t.Index, OnOrAfterDay(DayOf(t.Groups[1].Value), today)));
        t = Short.Match(text); if (t.Success) hits.Add((t.Index, OnOrAfterDay(DayOf(t.Groups[1].Value), today)));
        t = DayOfMonth.Match(text);
        if (t.Success && int.TryParse(t.Groups[1].Value, out int dom) && dom is >= 1 and <= 31) hits.Add((t.Index, OnOrAfter(Repeat.Monthly, dom, today)));

        // "next Friday" also matches the plain weekday rule, at a later index than "next", so the earliest hit is the "next" one.
        if (hits.Count == 0) return null;
        return hits.OrderBy(h => h.Index).First().Date;
    }
}
