using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AndaCompanion.Services;

public sealed record SecondLookResult(IReadOnlyList<string> Items, bool Respond);

/// <summary>
/// Signs worth a second look. This is not a detector, and nothing here says who or what wrote a text: reliable detection of AI-written text
/// does not exist, and a wrong flag does harm. The rules list wording an AI assistant often leaves behind, placeholders that were not filled
/// in, formatting marks that mail does not use, and citations that someone should confirm. The response is the same whoever wrote the text.
/// A port of the simulation's secondLook(), kept the same rule for rule (mockup/round27-golden.json).
/// </summary>
public static class SecondLook
{
    public const string NotAVerdict = "These are signs worth a second look, not a verdict. This tool cannot tell who or what wrote a text, and it does not say.";
    public const string None = "No leftover assistant wording, unfilled placeholders, formatting marks or citations were found. That says nothing about who or what wrote the text.";
    public static readonly IReadOnlyList<string> Response = new[]
    {
        "Whoever or whatever wrote it, the response is the same:", "1. Check each fact, date and citation against the source.", "2. Where no source is given, ask the sender for it.", "3. Follow your office's policy on AI-written content.",
    };

    /// <summary>An optional second opinion through the person's AI, after the usual preview. The prompt asks for signs on both sides and forbids a verdict.</summary>
    public const string AiTemplate = "Read the text below. List up to five concrete signs, quoting the words, that it may have been drafted with an AI writing tool, and up to five signs that it was not. Do not give a verdict, a score or a percentage: say in one line that such signs are weak evidence. Then list the facts, dates and citations a careful reader should verify.\n\n{selection}";

    private static readonly string[] Phrases =
    {
        "as an ai language model", "as an ai assistant", "as a large language model", "as an ai,", "i am an ai", "i'm an ai", "my knowledge cutoff", "my training data",
        "as of my last update", "i cannot browse", "i can't browse", "i don't have access to real-time", "i do not have access to real-time", "regenerate response", "certainly! here", "sure! here",
        "sure, here is", "sure, here's", "here is a revised version", "here's a revised version", "here is a draft", "here's a draft", "i hope this helps", "let me know if you'd like me to",
        "let me know if you would like me to", "feel free to customize", "[insert", "[your name]", "[recipient", "[company name]",
    };

    private static readonly Regex
        Stars = JsRegex.Make(@"\*\*[^*\n]{1,80}\*\*"), Hash = JsRegex.Make(@"(?:^|\n)#{1,6} [^\s]"), Fence = JsRegex.Make(@"(?:^|\n)```"), WsRun = JsRegex.Make(@"\s+");
    private static readonly Regex[] Cites =
    {
        JsRegex.Make(@"\b[0-9]{1,2} (?:CFR|C\.F\.R\.|U\.S\.C\.|USC)(?: (?:part|§+))? ?[0-9]+[A-Za-z]?(?:\.[0-9]+)?(?:\([A-Za-z0-9]+\))*"),
        JsRegex.Make(@"\bMAPP [0-9]{4}\.[0-9]+(?: Rev\. ?[0-9]+)?"),
        JsRegex.Make(@"\b(?:draft |final )?guidance for industry\b[^.\n]{0,80}", true),
    };

    /// <summary>A to Z made small and a curly apostrophe made straight. One character stays one character, so positions hold.</summary>
    internal static string AsciiLower(string t)
    {
        var sb = new StringBuilder(t.Length);
        foreach (char c in t) sb.Append(c is >= 'A' and <= 'Z' ? (char)(c + 32) : c == (char)0x2019 ? '\'' : c);
        return sb.ToString();
    }

    public static SecondLookResult Check(string? text)
    {
        string t = text ?? ""; var items = new List<string>();
        if (JsRegex.Trim(t).Length == 0) return new SecondLookResult(items, false);
        try
        {
            string low = AsciiLower(t); var seen = new List<(int From, int To)>();
            foreach (string p in Phrases)
            {
                int at = low.IndexOf(p, StringComparison.Ordinal); if (at < 0) continue;
                if (seen.Any(s => at >= s.From && at < s.To)) continue;
                seen.Add((at, at + p.Length));
                string quote = t.Substring(at, p.Length);
                items.Add(p[0] == '[' ? $"“{quote}”: a placeholder that was not filled in." : $"“{quote}”: wording that an AI assistant often leaves behind. Please read around it.");
            }
            var marks = new List<string>();
            if (Stars.IsMatch(t)) marks.Add("**"); if (Hash.IsMatch(t)) marks.Add("#"); if (Fence.IsMatch(t)) marks.Add("```");
            if (marks.Count > 0) items.Add($"Formatting marks ({string.Join(", ", marks)}) that an email does not use. They often come with text copied from an AI assistant or a writing tool.");
            var cites = new List<string>();
            foreach (var re in Cites)
                foreach (Match m in re.Matches(t)) { string q = JsRegex.Trim(WsRun.Replace(m.Value, " ")); if (!cites.Contains(q)) cites.Add(q); }
            foreach (string q in cites.Take(5)) items.Add($"Cited: {FileCompare.Show(q, 100)}. This tool cannot confirm that the source exists or says this. Please check it.");
            if (cites.Count > 5) items.Add($"{(cites.Count - 5).ToString(CultureInfo.InvariantCulture)} more {DocCompare.Pl(cites.Count - 5, "citation", "citations")} to check.");
        }
        catch (RegexMatchTimeoutException) { /* a text that takes too long is left unmarked rather than half marked */ return new SecondLookResult(Array.Empty<string>(), false); }
        return new SecondLookResult(items, items.Count > 0);
    }
}

public sealed record ReplyReminder(string Day, string Shown, string Title, string? Number);

/// <summary>
/// A reminder for a reply. When a draft asks for something by a date, the person can put "follow up if no reply" on the to-do list for that day.
/// The mailbox is not read: the tool cannot know whether a reply came. Fixed rules; the same as the simulation's replyBy().
/// </summary>
public static class ReplyBy
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly Regex
        Cue = JsRegex.Make(@"\b(?:respond|reply|response|answer|get back|let me know|let us know|send|provide|submit|return|confirm|feedback|comments?)\b", true),
        By = JsRegex.Make(@"(?:\bby|\bbefore|\bno later than|\bon or before|\buntil)\s+(?:the\s+)?(?:(?:Mon|Tues|Wednes|Thurs|Fri|Satur|Sun)day,?\s+)?$", true),
        WsRun = JsRegex.Make(@"\s+");

    public static ReplyReminder? Find(string? text, DateOnly today)
    {
        string t = text ?? "";
        try
        {
            foreach (Match m in DraftText.Date.Matches(t))
            {
                if (m.Index > 0 && (t[m.Index - 1] is (>= '0' and <= '9') or '/')) continue;
                if (m.Groups[4].Success && !m.Groups[6].Success) continue;              // "12/3" with no year is as often a fraction as a date
                int from = Math.Max(0, m.Index - 48);
                if (!By.IsMatch(t[from..m.Index])) continue;
                int s = m.Index; while (s > 0 && t[s - 1] is not ('.' or '!' or '?' or '\n')) s--;
                string sentence = t[s..m.Index]; if (!Cue.IsMatch(sentence)) continue;
                if (DraftText.ToMention(m) is not { } mt || mt.Mon < 1 || mt.Mon > 12 || mt.Day < 1) continue;
                DateOnly? day = null;
                if (mt.Year is { } y) { if (y >= 1900 && y <= 2200 && mt.Day <= DateTime.DaysInMonth(y, mt.Mon)) day = new DateOnly(y, mt.Mon, mt.Day); }
                else
                {
                    // No year: this year if the day is still to come; next year only if this year's day passed more than 180 days ago.
                    DateOnly? now = mt.Day <= DateTime.DaysInMonth(today.Year, mt.Mon) ? new DateOnly(today.Year, mt.Mon, mt.Day) : null;
                    if (now is { } n && n >= today) day = n;
                    else if (now is { } past && today.DayNumber - past.DayNumber > 180 && mt.Day <= DateTime.DaysInMonth(today.Year + 1, mt.Mon)) day = new DateOnly(today.Year + 1, mt.Mon, mt.Day);
                }
                if (day is not { } found || found < today) continue;
                string ask = JsRegex.Trim(WsRun.Replace(sentence + m.Value, " "));
                var numbers = DraftText.AndaNumbers(t);
                return new ReplyReminder(found.ToString("yyyy-MM-dd", Inv), found.ToString("dddd, MMM d, yyyy", Inv), "Follow up if no reply: " + FileCompare.Show(ask, 90), numbers.Count == 1 ? numbers[0] : null);
            }
        }
        catch (RegexMatchTimeoutException) { return null; }
        return null;
    }
}

/// <summary>One ANDA on a hand-off page: its status line, what needs a look, what is open, and the latest notes.</summary>
public sealed record HandoffEntry(string Status, IReadOnlyList<string> Watch, IReadOnlyList<string> Open, IReadOnlyList<string> Notes);

/// <summary>
/// A hand-off page and a meeting page: plain text made from the person's own data and notes, by fixed rules, for the person to copy or save.
/// Nothing is sent, and the person decides who gets it. One formatter, the same as the simulation's handoffText(); each program fills the
/// entries from its own data.
/// </summary>
public static class Handoff
{
    /// <param name="dataLine">Where the data is from, as of when and whether it is STALE, in the words of the card (<see cref="Clock.DataLine"/>).</param>
    public static IReadOnlyList<string> Text(string title, IReadOnlyList<HandoffEntry> entries, string todayLong, bool withNotes, string? dataLine = null)
    {
        // "made on": the day the page was written. How old the data is stands on its own line.
        var lines = new List<string>
        {
            $"{title}: {entries.Count.ToString(CultureInfo.InvariantCulture)} {DocCompare.Pl(entries.Count, "ANDA", "ANDAs")}, made on {todayLong}",
            "From my own data and notes on this PC. It is not the system of record.",
        };
        if (!string.IsNullOrEmpty(dataLine)) lines.Add("Data: " + dataLine);
        if (entries.Count == 0) { lines.Add(""); lines.Add("No ANDA to list."); }
        foreach (var e in entries)
        {
            lines.Add(""); lines.Add(e.Status);
            if (e.Watch.Count > 0) lines.Add("  Needs a look: " + string.Join("; ", e.Watch));
            lines.Add(e.Open.Count > 0 ? "  Open: " + string.Join("; ", e.Open) : "  Open: nothing in my data or my to-do lists");
            if (withNotes) lines.Add(e.Notes.Count > 0 ? "  Notes: " + string.Join(" | ", e.Notes) : "  Notes: none");
        }
        return lines;
    }
}
