using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AndaCompanion.Models;

namespace AndaCompanion.Services;

/// <summary>One thing Proofread saw. <see cref="Fixable"/>: it can be changed without guessing. <see cref="AlsoInFacts"/>: Fact-check reports it too.</summary>
public sealed record ProofItem(string Text, bool Fixable, bool AlsoInFacts);

/// <summary>One change in the corrected text: the characters from <see cref="Start"/> to <see cref="End"/> of the draft become <see cref="To"/>.</summary>
public sealed record ProofMark(int Start, int End, string To);

public sealed record ProofResult(IReadOnlyList<ProofItem> Items, int More, string Corrected, int Fixes, int Total, IReadOnlyList<ProofMark> Marks)
{
    public static ProofResult Empty(string text) => new(Array.Empty<ProofItem>(), 0, text, 0, 0, Array.Empty<ProofMark>());
}

/// <summary>
/// The patterns of the browser simulation, read as JavaScript reads them, so that the Windows app and the simulation give the same answer for the same
/// draft (mockup/proofread-golden.json holds the shared cases). In JavaScript a word character, a digit and a word boundary are ASCII, white space is a
/// fixed list, and "$" is only the end of the text; .NET reads each of these more widely, so they are rewritten here once.
/// </summary>
internal static class JsRegex
{
    internal const string Ws = @"\t\n\v\f\r \u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000\ufeff";
    private const string Boundary = "(?:(?<=[A-Za-z0-9_])(?![A-Za-z0-9_])|(?<![A-Za-z0-9_])(?=[A-Za-z0-9_]))";
    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(2);

    internal static Regex Make(string js, bool ignoreCase = false) =>
        new(Translate(js), (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None) | RegexOptions.CultureInvariant, Limit);

    internal static string Translate(string js)
    {
        var sb = new StringBuilder(js.Length + 64);
        bool inClass = false;
        for (int i = 0; i < js.Length; i++)
        {
            char c = js[i];
            if (c == '\\' && i + 1 < js.Length)
            {
                char n = js[++i];
                switch (n)
                {
                    case 'b' when !inClass: sb.Append(Boundary); break;
                    case 'd': sb.Append(inClass ? "0-9" : "[0-9]"); break;
                    case 'w': sb.Append(inClass ? "A-Za-z0-9_" : "[A-Za-z0-9_]"); break;
                    case 's': sb.Append(inClass ? Ws : "[" + Ws + "]"); break;
                    case 'S' when !inClass: sb.Append("[^" + Ws + "]"); break;
                    case '/': sb.Append('/'); break;
                    default: sb.Append('\\').Append(n); break;
                }
                continue;
            }
            if (c == '[' && !inClass) inClass = true;
            else if (c == ']' && inClass) inClass = false;
            else if (c == '$' && !inClass) { sb.Append(@"\z"); continue; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>JavaScript's white space, for trim() and for split(/\s+/).</summary>
    internal static bool IsWs(char c) =>
        c is '\t' or '\n' or '\v' or '\f' or '\r' or ' ' or '\u00a0' or '\u1680' or '\u2028' or '\u2029' or '\u202f' or '\u205f' or '\u3000' or '\ufeff' || (c >= '\u2000' && c <= '\u200a');

    internal static string Trim(string s)
    {
        int a = 0, b = s.Length;
        while (a < b && IsWs(s[a])) a++;
        while (b > a && IsWs(s[b - 1])) b--;
        return s[a..b];
    }

    /// <summary>text.slice(from, to) with both ends kept inside the text.</summary>
    internal static string Slice(string s, int from, int to)
    {
        from = Math.Clamp(from, 0, s.Length); to = Math.Clamp(to, 0, s.Length);
        return to > from ? s[from..to] : "";
    }

    private static readonly Regex AnyWs = Make(@"\s"), Spaces = new(" {2,}", RegexOptions.CultureInvariant, Limit);

    /// <summary>A piece of the draft for a message: one line, at most 60 characters.</summary>
    internal static string Squash(string t)
    {
        string x = Spaces.Replace(AnyWs.Replace(Trim(t), " "), " ");
        return x.Length <= 60 ? x : x[..57] + "...";
    }

    internal static int WordCount(string sentence)
    {
        int n = 0; bool inWord = false;
        foreach (char c in sentence) { bool ws = IsWs(c); if (!ws && !inWord) n++; inWord = !ws; }
        return n;
    }
}

/// <summary>The dates and ANDA numbers of a draft, read exactly as the simulation reads them.</summary>
internal static class DraftText
{
    private static readonly string[] Months = { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };
    internal static readonly string[] MonthShort = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
    internal static readonly string[] MonthLong = { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };

    internal static readonly Regex Date = JsRegex.Make(
        @"(?:(Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|June?|July?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?\s+(\d{1,2})(?:st|nd|rd|th)?(?:,?\s+(\d{4}))?(?!\d)|(\d{1,2})\/(\d{1,2})(?:\/(\d{4}|\d{2}))?(?![\d\/])|(\d{4})-(\d{2})-(\d{2}))", true);
    private static readonly Regex Digits = new("[0-9]+", RegexOptions.CultureInvariant, JsRegex.Limit);
    private static readonly Regex OtherLabel = JsRegex.Make(@"(?:^|[^A-Za-z0-9_])(?:(?:NDA|BLA|IND|DMF|NDC|MF)(?:[-\s#:.]|No(?![A-Za-z])|Number(?![A-Za-z]))*|N-?)$", true);

    internal sealed record Mention(int Mon, int Day, int? Year, int Index, int Len, string Raw, bool Named, bool NumericWithYear);

    internal static int MonthOf(string word) => Array.IndexOf(Months, word[..3].ToLowerInvariant()) + 1;

    /// <summary>Six digits in a row that are not labeled as another kind of application ("NDA 021234", "N210793").</summary>
    internal static List<string> AndaNumbers(string sentence)
    {
        var found = new List<string>();
        foreach (Match m in Digits.Matches(sentence))
            if (m.Length == 6 && !found.Contains(m.Value) && !OtherLabel.IsMatch(sentence[..m.Index])) found.Add(m.Value);
        return found;
    }

    /// <summary>The dates written in a piece of text. A match glued to a digit or a slash before it is not a date ("12/3/2026" inside a longer number).</summary>
    internal static List<Mention> Mentions(string text, bool skipGlued = true)
    {
        var list = new List<Mention>();
        foreach (Match m in Date.Matches(text))
        {
            if (skipGlued && m.Index > 0 && (text[m.Index - 1] is (>= '0' and <= '9') or '/')) continue;
            if (ToMention(m) is { } mt) list.Add(mt);
        }
        return list;
    }

    internal static Mention? ToMention(Match m)
    {
        int mon, day; int? year = null;
        if (m.Groups[1].Success)
        {
            mon = MonthOf(m.Groups[1].Value); day = Int(m.Groups[2].Value); if (m.Groups[3].Success) year = Int(m.Groups[3].Value);
            if (mon < 1 || day < 1 || day > 31) return null;
        }
        else if (m.Groups[4].Success)
        {
            mon = Int(m.Groups[4].Value); day = Int(m.Groups[5].Value);
            if (m.Groups[6].Success) { year = Int(m.Groups[6].Value); if (year < 100) year += 2000; }
            if (mon < 1 || mon > 12 || day < 1 || day > 31) return null;
        }
        else if (m.Groups[7].Success) { year = Int(m.Groups[7].Value); mon = Int(m.Groups[8].Value); day = Int(m.Groups[9].Value); }
        else return null;
        return new Mention(mon, day, year, m.Index, m.Length, m.Value, m.Groups[1].Success, m.Groups[4].Success && m.Groups[6].Success);
    }

    internal static int Int(string digits) => int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);

    /// <summary>"goal" or "forecast" when one of those words stands near the date, else null.</summary>
    internal static string? KeywordFor(string sentence, Mention mt)
    {
        string before = JsRegex.Slice(sentence, mt.Index - 50, mt.Index).ToLowerInvariant();
        int g = before.LastIndexOf("goal", StringComparison.Ordinal);
        int f = Math.Max(before.LastIndexOf("forecast", StringComparison.Ordinal), before.LastIndexOf("projected", StringComparison.Ordinal));
        if (g >= 0 || f >= 0) return g > f ? "goal" : "forecast";
        string after = JsRegex.Slice(sentence, mt.Index + mt.Len, mt.Index + mt.Len + 30).ToLowerInvariant();
        int ag = after.IndexOf("goal", StringComparison.Ordinal), af = after.IndexOf("forecast", StringComparison.Ordinal);
        if (ag >= 0 && (af < 0 || ag < af)) return "goal";
        return af >= 0 ? "forecast" : null;
    }

    /// <summary>"Jul 21" or "Jul 21, 2026".</summary>
    internal static string Show(int mon, int day, int? year) =>
        MonthShort[(((mon - 1) % 12) + 12) % 12] + " " + day.ToString(CultureInfo.InvariantCulture) + (year is { } y && y != 0 ? ", " + y.ToString(CultureInfo.InvariantCulture) : "");

    /// <summary>Sentences of a paragraph: split after ., ! or ? followed by white space, and at line ends.</summary>
    private static readonly Regex SentenceEnd = JsRegex.Make(@"([.!?])\s+"), SentenceCut = new("\u0000|\r?\n", RegexOptions.CultureInvariant, JsRegex.Limit);
    internal static readonly Regex ParagraphCut = JsRegex.Make(@"\r?\n\s*\r?\n");
    internal static string[] Sentences(string paragraph) => SentenceCut.Split(SentenceEnd.Replace(paragraph, "$1\u0000"));
}

/// <summary>
/// Proofread: a short list of fixed rules that run on this PC. No AI is used and nothing is sent. It looks for words from a list of common slips,
/// a name that is one slip away from a name in the person's own data, words that sound alike, a verb that does not agree, a missing apostrophe,
/// "a" or "an", spacing, punctuation, capitals, brackets and quotation marks that do not close, dates written two ways, and very long sentences.
/// Each item says what was seen; a change is offered only where it can be made without guessing, and the corrected text is only ever copied or put
/// into the app's own box by the person. This is a port of the simulation's proofread(), kept the same rule for rule: the shared cases are in
/// mockup/proofread-golden.json. It is not a full spelling or grammar check and says so.
/// </summary>
public static class Proofreader
{
    public const int MaxItems = 16;

    private static readonly Dictionary<string, string> Words = new(StringComparer.Ordinal)
    {
        ["recieve"] = "receive", ["recieved"] = "received", ["recieving"] = "receiving", ["recived"] = "received", ["reciept"] = "receipt", ["seperate"] = "separate",
        ["seperately"] = "separately", ["definately"] = "definitely", ["definate"] = "definite", ["occured"] = "occurred", ["occuring"] = "occurring",
        ["occurence"] = "occurrence", ["occurance"] = "occurrence", ["accomodate"] = "accommodate", ["acommodate"] = "accommodate", ["acheive"] = "achieve",
        ["adress"] = "address", ["agressive"] = "aggressive", ["apparant"] = "apparent", ["begining"] = "beginning", ["beleive"] = "believe", ["calender"] = "calendar",
        ["collegue"] = "colleague", ["comittee"] = "committee", ["commitee"] = "committee", ["concensus"] = "consensus", ["embarass"] = "embarrass",
        ["enviroment"] = "environment", ["existance"] = "existence", ["foward"] = "forward", ["fowarded"] = "forwarded", ["goverment"] = "government",
        ["gaurantee"] = "guarantee", ["immediatly"] = "immediately", ["independant"] = "independent", ["liason"] = "liaison", ["maintainance"] = "maintenance",
        ["managment"] = "management", ["neccessary"] = "necessary", ["necesary"] = "necessary", ["noticable"] = "noticeable", ["occassion"] = "occasion",
        ["occassionally"] = "occasionally", ["persistant"] = "persistent", ["posession"] = "possession", ["priviledge"] = "privilege", ["publically"] = "publicly",
        ["reccomend"] = "recommend", ["recomend"] = "recommend", ["reccommend"] = "recommend", ["refered"] = "referred", ["refering"] = "referring",
        ["relevent"] = "relevant", ["resistence"] = "resistance", ["responsability"] = "responsibility", ["succesful"] = "successful", ["successfull"] = "successful",
        ["sucessful"] = "successful", ["supercede"] = "supersede", ["tommorow"] = "tomorrow", ["tommorrow"] = "tomorrow", ["tomorow"] = "tomorrow", ["truely"] = "truly",
        ["untill"] = "until", ["wich"] = "which", ["wierd"] = "weird", ["writting"] = "writing", ["teh"] = "the", ["adn"] = "and", ["taht"] = "that",
        ["thier"] = "their", ["aknowledge"] = "acknowledge", ["acknowlege"] = "acknowledge", ["ammend"] = "amend", ["ammendment"] = "amendment",
        ["ammendments"] = "amendments", ["amendmant"] = "amendment", ["aplicant"] = "applicant", ["applicent"] = "applicant", ["aplication"] = "application",
        ["submision"] = "submission", ["submited"] = "submitted", ["submiting"] = "submitting", ["responce"] = "response", ["reponse"] = "response",
        ["resposne"] = "response", ["deficency"] = "deficiency", ["defficiency"] = "deficiency",
        ["deficiences"] = "deficiencies", ["facilty"] = "facility", ["faciltiy"] = "facility", ["inspecton"] = "inspection", ["bioequivalance"] = "bioequivalence",
        ["disolution"] = "dissolution", ["dissolusion"] = "dissolution", ["pharmacuetical"] = "pharmaceutical", ["pharmaceutcal"] = "pharmaceutical",
        ["pharmacutical"] = "pharmaceutical", ["regulatroy"] = "regulatory", ["regualtory"] = "regulatory", ["shedule"] = "schedule", ["schedual"] = "schedule",
        ["forcast"] = "forecast", ["aproval"] = "approval", ["approvel"] = "approval", ["completly"] = "completely", ["confirmaton"] = "confirmation",
        ["extention"] = "extension", ["informaton"] = "information", ["infomation"] = "information", ["requst"] = "request", ["reqeust"] = "request",
        ["updtae"] = "update", ["comunicate"] = "communicate", ["comunication"] = "communication", ["correspondance"] = "correspondence",
        ["discrepency"] = "discrepancy", ["guidence"] = "guidance", ["prefered"] = "preferred", ["proceedure"] = "procedure", ["reveiw"] = "review",
        ["reveiwed"] = "reviewed", ["reveiwer"] = "reviewer", ["sumary"] = "summary", ["transfered"] = "transferred", ["attatched"] = "attached",
        ["attachement"] = "attachment", ["availible"] = "available", ["avaliable"] = "available", ["bussiness"] = "business", ["busines"] = "business",
        ["expidite"] = "expedite", ["expediate"] = "expedite", ["priorty"] = "priority", ["specifc"] = "specific", ["strenght"] = "strength", ["wether"] = "whether",
        ["recieveing"] = "receiving",
        ["labelling"] = "labeling", ["labelled"] = "labeled",   // wording-ok (the left side of each pair is the slip that is looked for)
    };

    private const string Nouns = "goal|forecast|response|responses|application|applications|amendment|amendments|draft|review|reviews|team|teams|letter|letters|date|dates|status|facility|facilities|labeling|applicant|applicants|comments|questions|request|requests|report|reports|data|notes|email|emails|plan|meeting|deadline|submission|submissions|records|file|files|site|sites|own";

    /// <summary>What to look for, what the first group becomes, the reason, and how many times to report it.</summary>
    private sealed record Rule(Regex Re, Func<string, string> To, string Why, int Max);

    private static Rule R(string js, string to, string why, bool ignoreCase = true) => new(JsRegex.Make(js, ignoreCase), _ => to, why, 2);
    private static Rule R(string js, Dictionary<string, string> map, string why, int max = 2) => new(JsRegex.Make(js, true), from => map[from.ToLowerInvariant()], why, max);
    private static Rule Capital(string js, string why) => new(JsRegex.Make(js), from => char.ToUpperInvariant(from[0]) + from[1..], why, 2);

    private static readonly Rule[] Grammar =
    {
        R(@"\b(their|there) (?:going|coming|sending|planning|waiting|reviewing|working|asking|still|also|already)\b", "they're", "this may be"),
        R(@"\b(their) (?:is|are|was|were|will be|has been|have been)\b", "there", "this may be"),
        R(@"\b(?:for|of|with|by|from|on|about|to|at|meet|met|miss|missed|reach|reached|send|sent|review|reviewed|check|checked|update|updated|confirm|confirmed) (there|they're) (?:" + Nouns + @")\b", "their", "this is usually"),
        R(@"\b(your) (?:welcome|going to|not|right that|correct that|invited)\b", "you're", "this is usually"),
        R(@"\b(you're) (?:" + Nouns + @")\b", "your", "this is usually"),
        R(@"\b(its) (?:a|an|the|not|been|going to|on track|too)\b", "it's", "this is usually"),
        R(@"\b(it's) (?:" + Nouns + @")\b", "its", "this is usually"),
        R(@"\b(?:more|less|later|earlier|sooner|better|worse|greater|fewer|higher|lower|longer|shorter|rather|other) (then)\b", "than", "after a comparison this is"),
        R(@"\b(?:is|are|was|were|be|been|it's|far|way|not|much) (to) (?:late|early|many|much|soon|few|long|short)\b", "too", "this is usually"),
        R(@"\b(?:could|should|would|must|might) (of)\b(?! course)", "have", "this is"),
        R(@"\b(?:an|the|no|any|little) (affect)\b", "effect", "the noun is"),
        R(@"\b(weather) (?:or not|we|it|this|that|to)\b", "whether", "this is usually"),
        R(@"\b(?:an|any|the|your|our|my|for|some) (advise)\b", "advice", "the noun is"),
        R(@"\b(loose) (?:the|a|an|track|time|sight)\b", "lose", "this is usually"),
        R(@"\b(?:[Bb]etween|[Ff]or|[Ww]ith|[Tt]o|[Ff]rom) (?:you|him|her|them) and (I)\b", "me", "after a preposition this is", ignoreCase: false),
        R(@"\b(me and him|him and me) (?:will|is|are|was|were|have|has|can|should|would|did|do)\b", "he and I", "as the subject this reads"),
        R(@"\b(me and her|her and me) (?:will|is|are|was|were|have|has|can|should|would|did|do)\b", "she and I", "as the subject this reads"),
        R(@"\b(me and them|them and me) (?:will|is|are|was|were|have|has|can|should|would|did|do)\b", "they and I", "as the subject this reads"),
        R(@"\b(?:ANDA\s*(?:No\.?\s*)?[0-9]{6}|it|he|she|the applicant|the response|the amendment|the review|the goal date|the forecast|the facility|the draft|the letter|the request) (are)\b(?! you)", "is", "with one subject this is"),
        R(@"\b(?:we|they|the applicants|the reviewers|both ANDAs|the teams) (is|was|has|does)\b",
            new Dictionary<string, string> { ["is"] = "are", ["was"] = "were", ["has"] = "have", ["does"] = "do" }, "with more than one this is"),
        R(@"\b(?:has|have|had) (went|came|did|saw|ran|began|wrote|took|gave)\b",
            new Dictionary<string, string> { ["went"] = "gone", ["came"] = "come", ["did"] = "done", ["saw"] = "seen", ["ran"] = "run", ["began"] = "begun", ["wrote"] = "written", ["took"] = "taken", ["gave"] = "given" }, "after has or have this is"),
        R(@"\b(?:will|did not|didn't|does not|doesn't|do not|don't) (submitted|received|reviewed|approved|responded|confirmed|completed|provided|requested|updated|scheduled)\b",
            new Dictionary<string, string> { ["submitted"] = "submit", ["received"] = "receive", ["reviewed"] = "review", ["approved"] = "approve", ["responded"] = "respond", ["confirmed"] = "confirm", ["completed"] = "complete", ["provided"] = "provide", ["requested"] = "request", ["updated"] = "update", ["scheduled"] = "schedule" }, "after this word the verb is"),
        R(@"\b(dont|cant|wont|doesnt|isnt|didnt|wasnt|werent|hasnt|havent|wouldnt|couldnt|shouldnt|arent|thats|whats|youre|theyre|ive)\b",
            new Dictionary<string, string> { ["dont"] = "don't", ["cant"] = "can't", ["wont"] = "won't", ["doesnt"] = "doesn't", ["isnt"] = "isn't", ["didnt"] = "didn't", ["wasnt"] = "wasn't", ["werent"] = "weren't", ["hasnt"] = "hasn't", ["havent"] = "haven't", ["wouldnt"] = "wouldn't", ["couldnt"] = "couldn't", ["shouldnt"] = "shouldn't", ["arent"] = "aren't", ["thats"] = "that's", ["whats"] = "what's", ["youre"] = "you're", ["theyre"] = "they're", ["ive"] = "I've" }, "an apostrophe is missing:", 4),
        R(@"\b(aswell|incase|infact|inorder|atleast|eachother|noone|thankyou|alot)\b",
            new Dictionary<string, string> { ["aswell"] = "as well", ["incase"] = "in case", ["infact"] = "in fact", ["inorder"] = "in order", ["atleast"] = "at least", ["eachother"] = "each other", ["noone"] = "no one", ["thankyou"] = "thank you", ["alot"] = "a lot" }, "these are two words:", 3),
        Capital(@"\b(?:on|by|until|since|last|next|this|every) (monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b", "a day of the week takes a capital:"),
        Capital(@"\b(january|february|april|june|july|august|september|october|november|december)\b", "a month takes a capital:"),
    };

    private static readonly HashSet<string> Abbreviations = new(StringComparer.Ordinal)
        { "e.g", "i.e", "etc", "vs", "no", "dr", "mr", "ms", "mrs", "approx", "inc", "ltd", "co", "st", "jr", "sr", "fig", "ref", "est", "dept", "attn", "cc", "u.s", "a.m", "p.m" };
    private static readonly HashSet<string> Generic = new(StringComparer.Ordinal)
        { "tablets", "tablet", "capsules", "capsule", "injection", "solution", "extended", "release", "fictional", "pharmaceuticals", "pharmaceutical", "regulatory", "agents" };

    private static readonly Regex
        Word = new("[A-Za-z][A-Za-z']*", RegexOptions.CultureInvariant, JsRegex.Limit),
        LongWord = new("[A-Za-z]{6,}", RegexOptions.CultureInvariant, JsRegex.Limit),
        KeepsI = JsRegex.Make(@"^I\b|\bI$|\bI'"),
        Article = JsRegex.Make(@"\b(a|an) ([A-Za-z][A-Za-z']*)"),
        SpelledOut = new("^[AEFHILMNORSX]", RegexOptions.CultureInvariant, JsRegex.Limit),
        Vowel = new("^[aeiou]", RegexOptions.CultureInvariant, JsRegex.Limit),
        VowelButConsonantSound = new("^(?:uni|use|usa|usu|uti|ute|ubi|eu|one|once|ura|uro)", RegexOptions.CultureInvariant, JsRegex.Limit),
        SilentH = new("^(?:hour|honest|honor|heir)", RegexOptions.CultureInvariant, JsRegex.Limit),
        LowerAnda = JsRegex.Make(@"\b(anda)(?=\s*(?:no\.?|#|number)?\s*[0-9])", true),
        AAnda = JsRegex.Make(@"\b([Aa]) (?=ANDA\b)"),
        LoneI = JsRegex.Make(@"(?<![A-Za-z'’.@-])i(?![A-Za-z'’.@-])(?!\.e)"),
        Fda = JsRegex.Make(@"(?<![.@\w-])(fda|Fda)(?![.\w-])"),
        SpaceBeforeMark = JsRegex.Make("""([A-Za-z0-9)\]”"]) +([,;:!?]|\.(?![.\d]))(?=\s|$|[A-Za-z])"""),
        NoSpaceAfterComma = JsRegex.Make(@"([a-z])([,;])([A-Za-z0-9])"),
        NoSpaceAfterPeriod = JsRegex.Make(@"([a-z]{2})\.([A-Z][a-z])"),
        AddressBefore = new(@"[@/\\]", RegexOptions.CultureInvariant, JsRegex.Limit),
        DoubleSpace = JsRegex.Make(@"(?<=\S)  +(?=\S)"),
        DoubledMark = JsRegex.Make(@"([,;:!?])\1+|(?<!\.)\.\.(?!\.)"),
        SentenceStart = JsRegex.Make("""(^|[.!?]["”)]?\s+)([a-z])([A-Za-z']*)"""),
        CamelStart = new("^[a-z][A-Z]", RegexOptions.CultureInvariant, JsRegex.Limit),
        DigitThenPeriod = JsRegex.Make(@"[0-9]\.$"),
        WordThenPeriod = JsRegex.Make(@"([A-Za-z.]+)\.$"),
        Repeated = JsRegex.Make(@"(?<![A-Za-z'])([A-Za-z]{2,})\s+\1(?![A-Za-z'])", true),
        Placeholder = JsRegex.Make(@"\[(?:TBD|TBC|TODO|XX+|INSERT[^\]\r\n]{0,40}|NAME|DATE|PLACEHOLDER)\]|\bTBD\b|\bTBC\b|\bXXX+\b|\?\?\?+|<<[^>\r\n]{1,40}>>|\{\{[^}\r\n]{1,40}\}\}|lorem ipsum", true),
        BarePlaceholder = JsRegex.Make(@"^(?:tbd|tbc|xxx+)$", true);

    private sealed class Item
    {
        public string? Text; public int? Start; public int End; public string? To; public bool AlsoInFacts;
    }

    /// <summary>Upper case when the original is all capitals, a capital first letter when the original has one, else as given.</summary>
    internal static string Case(string like, string word) =>
        like.Length > 1 && like == like.ToUpperInvariant() ? word.ToUpperInvariant()
        : like.Length > 0 && like[0] is >= 'A' and <= 'Z' ? char.ToUpperInvariant(word[0]) + word[1..] : word;

    /// <summary>Edits to turn one word into the other; swapping two neighbors counts as one. Three means "too far to matter".</summary>
    internal static int Distance(string a, string b)
    {
        int m = a.Length, n = b.Length;
        if (Math.Abs(m - n) > 2) return 3;
        var d = new int[m + 1, n + 1];
        for (int i = 0; i <= m; i++) d[i, 0] = i;
        for (int j = 1; j <= n; j++) d[0, j] = j;
        for (int i = 1; i <= m; i++)
            for (int j = 1; j <= n; j++)
            {
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        return d[m, n];
    }

    /// <summary>The ANDAs of the person's data that the text names, in the order they first appear.</summary>
    internal static List<string> KnownIds(string? text, Func<string, AndaRecord?> lookup) =>
        DraftText.AndaNumbers(text ?? "").Where(id => lookup(id) is not null).ToList();

    /// <summary>The words of a record that are worth protecting from a slip of the fingers: the drug, the applicant and the active ingredient.</summary>
    internal static string NameSource(AndaRecord r) => $"{r.Drug} {r.Meta} {(r.ActiveIngredient ?? "").ToLowerInvariant()}";

    private static List<(string Key, string Word, string Id)> Names(IEnumerable<string> ids, Func<string, AndaRecord?> lookup)
    {
        var names = new List<(string, string, string)>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in ids)
        {
            if (lookup(id) is not { } r) continue;
            foreach (Match w in LongWord.Matches(NameSource(r)))
            {
                string k = w.Value.ToLowerInvariant();
                if (!Generic.Contains(k) && seen.Add(k)) names.Add((k, w.Value, id));
            }
        }
        return names;
    }

    public static ProofResult Check(string? draft, Func<string, AndaRecord?> lookup)
    {
        string text = draft ?? "";
        if (JsRegex.Trim(text).Length == 0) return ProofResult.Empty(text);
        var items = new List<Item>();
        try { Run(text, lookup, items); }
        catch (RegexMatchTimeoutException) { /* what was found so far is still shown */ }

        // The corrected text: every change that can be made without guessing, applied from the end so positions stay right; overlapping changes are skipped.
        var fixes = items.Where(i => i.To is not null && i.Start is not null).OrderByDescending(i => i.Start!.Value).ToList();
        var corrected = new StringBuilder(text); int floor = int.MaxValue, applied = 0; var marks = new List<ProofMark>();
        foreach (var f in fixes)
        {
            if (f.End > floor) continue;
            corrected.Remove(f.Start!.Value, f.End - f.Start.Value).Insert(f.Start.Value, f.To);
            floor = f.Start.Value; applied++; marks.Insert(0, new ProofMark(f.Start.Value, f.End, f.To!));
        }
        var shown = items.Where(i => !string.IsNullOrEmpty(i.Text)).ToList();
        return new ProofResult(shown.Take(MaxItems).Select(i => new ProofItem(i.Text!, i.To is not null, i.AlsoInFacts)).ToList(),
            Math.Max(0, shown.Count - MaxItems), corrected.ToString(), applied, shown.Count, marks);
    }

    private static void Run(string text, Func<string, AndaRecord?> lookup, List<Item> items)
    {
        void Add(string? msg, int? start = null, int end = 0, string? to = null, bool alsoInFacts = false) =>
            items.Add(new Item { Text = msg, Start = start, End = end, To = to, AlsoInFacts = alsoInFacts });

        // 1. Words from a short list of common slips, and names that are one slip away from a name in the person's data.
        var names = Names(KnownIds(text, lookup), lookup);
        var nameKeys = new HashSet<string>(names.Select(n => n.Key), StringComparer.Ordinal);
        var seenWord = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Word.Matches(text))
        {
            string w = m.Value, k = w.ToLowerInvariant();
            if (seenWord.Contains(k)) continue;
            if (Words.TryGetValue(k, out string? right))
            {
                seenWord.Add(k); string to = Case(w, right);
                Add($"“{w}” is usually spelled “{to}”.", m.Index, m.Index + w.Length, to);
                continue;
            }
            if (k.Length < 6 || nameKeys.Contains(k) || Generic.Contains(k)) continue;
            foreach (var (nk, word, id) in names)
            {
                if (nk == k + "s" || k == nk + "s") continue;
                int dist = Distance(k, nk);
                if (dist == 1 || (dist == 2 && nk.Length >= 10))
                {
                    seenWord.Add(k);
                    string to = Case(w, word.ToLowerInvariant() == nk && word[0] is >= 'a' and <= 'z' ? nk : word);
                    Add($"“{w}” looks like “{to}”, a name in your data for ANDA {id}.", m.Index, m.Index + w.Length, to);
                    break;
                }
            }
        }

        // 1b. Grammar that fixed rules can see: words that sound alike, a verb that does not agree, a missing apostrophe, "a" or "an".
        var seenRule = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in Grammar)
        {
            int n = 0;
            foreach (Match m in rule.Re.Matches(text))
            {
                if (n >= rule.Max) break;
                string from = m.Groups[1].Value; int at = m.Index + m.Value.IndexOf(from, StringComparison.Ordinal);
                string fix = rule.To(from);
                if (!seenRule.Add(rule.Why + "|" + from.ToLowerInvariant())) continue;
                n++;
                string shown = from == "I" || (KeepsI.IsMatch(fix) && from == from.ToLowerInvariant()) ? fix : Case(from, fix);
                Add($"“{JsRegex.Squash(m.Value)}”: {rule.Why} “{shown}”.", at, at + from.Length, shown);
            }
        }
        {
            int n = 0;
            foreach (Match m in Article.Matches(text))
            {
                if (n >= 3) break;
                string art = m.Groups[1].Value, w = m.Groups[2].Value, lw = w.ToLowerInvariant();
                bool letters = w.Length > 1 && w.Length <= 3 && w == w.ToUpperInvariant();      // a short abbreviation is spelled out: an NDA, an RLD, a PSG
                bool vowelSound = letters ? SpelledOut.IsMatch(w) : (Vowel.IsMatch(lw) && !VowelButConsonantSound.IsMatch(lw)) || SilentH.IsMatch(lw);
                if (lw == "anda" || lw == "and" || w.Length < 2) continue;                      // "a ANDA" has its own rule below
                if (art == "a" && vowelSound) { n++; Add($"“{m.Value}” reads “an {w}”.", m.Index, m.Index + 1, "an"); }
                else if (art == "an" && !vowelSound) { n++; Add($"“{m.Value}” reads “a {w}”.", m.Index, m.Index + 2, "a"); }
            }
        }

        // 2. ANDA written in lower case before a number, "a ANDA", "i" on its own, and FDA.
        {
            int n = 0;
            foreach (Match m in LowerAnda.Matches(text))
                if (m.Groups[1].Value != "ANDA" && ++n <= 2) Add($"“{m.Groups[1].Value}” is written “ANDA”.", m.Index, m.Index + 4, "ANDA");
        }
        if (AAnda.Match(text) is { Success: true } aAnda) Add("“a ANDA” reads “an ANDA”.", aAnda.Index, aAnda.Index + 1, aAnda.Groups[1].Value == "A" ? "An" : "an");
        {
            int n = 0;
            foreach (Match m in LoneI.Matches(text))
                if (++n == 1) Add("“i” on its own is written “I”.", m.Index, m.Index + 1, "I"); else if (n <= 20) Add("", m.Index, m.Index + 1, "I");
        }
        if (Fda.Match(text) is { Success: true } fda) Add($"“{fda.Groups[1].Value}” is written “FDA”.", fda.Index, fda.Index + 3, "FDA");

        // 3. Spacing and punctuation.
        {
            int n = 0; Match? first = null; var mine = new List<Item>();
            foreach (Match m in SpaceBeforeMark.Matches(text))
            {
                n++; first ??= m;
                int after = m.Index + m.Length;
                bool glued = after < text.Length && text[after] is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');       // "216482 .It": the mark moves back, and the space goes after it
                if (n <= 6) { var it = new Item { Text = null, Start = m.Index + 1, End = after, To = m.Groups[2].Value + (glued ? " " : "") }; items.Add(it); mine.Add(it); }
            }
            for (int k = 0; k < mine.Count; k++)
                mine[k].Text = k == 0 ? $"{(n == 1 ? "A space comes" : n + " spaces come")} before a punctuation mark, for example “{JsRegex.Squash(first!.Value)}”." : "";
        }
        {
            int n = 0;
            foreach (Match m in NoSpaceAfterComma.Matches(text))
                if (++n <= 3) Add($"A space is missing after “{m.Groups[2].Value}” in “{JsRegex.Squash(JsRegex.Slice(text, m.Index - 8, m.Index + 10))}”.", m.Index + 2, m.Index + 2, " ");
        }
        {
            int n = 0;
            foreach (Match m in NoSpaceAfterPeriod.Matches(text))
            {
                if (AddressBefore.IsMatch(JsRegex.Slice(text, m.Index - 30, m.Index))) continue;
                if (++n <= 3) Add($"A space is missing after the period in “{JsRegex.Squash(JsRegex.Slice(text, m.Index - 8, m.Index + 12))}”.", m.Index + 3, m.Index + 3, " ");
            }
        }
        {
            var spots = new List<Match>();
            foreach (Match m in DoubleSpace.Matches(text))
            {
                int line = text.LastIndexOf('\n', m.Index);
                if (line >= 0 && text.IndexOf('\t', line, m.Index - line) >= 0) continue;      // a row copied from a spreadsheet
                spots.Add(m);
            }
            int n = spots.Count;
            for (int k = 0; k < Math.Min(40, n); k++)
                Add(k == 0 ? $"{(n == 1 ? "One place has" : n + " places have")} two or more spaces in a row." : "", spots[k].Index, spots[k].Index + spots[k].Length, " ");
        }
        {
            int n = 0;
            foreach (Match m in DoubledMark.Matches(text))
                if (++n <= 2) Add($"A punctuation mark is doubled: “{JsRegex.Squash(JsRegex.Slice(text, m.Index - 10, m.Index + 6))}”.", m.Index, m.Index + m.Length, m.Value[..1]);
        }

        // 4. A sentence that starts with a small letter.
        {
            int n = 0;
            foreach (Match m in SentenceStart.Matches(text))
            {
                int at = m.Index + m.Groups[1].Length; string word = m.Groups[2].Value + m.Groups[3].Value;
                if (CamelStart.IsMatch(word)) continue;                                        // eCTD, iOS
                if (m.Groups[1].Length > 0)
                {
                    string before = JsRegex.Slice(text, m.Index - 12, m.Index + 1);
                    var prev = WordThenPeriod.Match(before);
                    if (DigitThenPeriod.IsMatch(before) || (prev.Success && (Abbreviations.Contains(prev.Groups[1].Value.ToLowerInvariant()) || prev.Groups[1].Length == 1))) continue;
                }
                if (++n <= 3) Add($"A sentence starts with a small letter: “{JsRegex.Squash(JsRegex.Slice(text, at, at + 28))}”.", at, at + 1, m.Groups[2].Value.ToUpperInvariant());
            }
        }

        // 5. Brackets and quotation marks that do not close.
        {
            int Count(char ch) => text.Count(c => c == ch);
            if (Count('(') != Count(')')) Add($"The draft has {Count('(')} opening and {Count(')')} closing parentheses.");
            if (Count('“') != Count('”')) Add($"The draft has {Count('“')} opening and {Count('”')} closing quotation marks.");
            else if (Count('"') % 2 == 1) Add("A quotation mark is opened and not closed.");
        }

        // 6. Dates written two ways, and very long sentences.
        {
            string? named = null, numeric = null;
            foreach (var mt in DraftText.Mentions(text))
            {
                if (mt.Named) named ??= mt.Raw;
                else if (mt.NumericWithYear) numeric ??= mt.Raw;
            }
            if (named is not null && numeric is not null) Add($"Dates are written in two ways (“{named}” and “{numeric}”). One style reads more easily.");
        }
        {
            int n = 0;
            foreach (string sentence in DraftText.Sentences(text))
            {
                int words = JsRegex.WordCount(sentence);
                if (words > 45 && !sentence.Contains('\t') && ++n <= 2) Add($"A sentence has {words} words: “{JsRegex.Squash(sentence)}”. Two sentences may read more easily.");
            }
        }

        // 7. What Fact-check already reports (a repeated word, a leftover placeholder): listed here too when Proofread is used alone.
        {
            var seen = new HashSet<string>(StringComparer.Ordinal); int n = 0;
            foreach (Match m in Repeated.Matches(text))
            {
                string w = m.Groups[1].Value.ToLowerInvariant();
                if (w == "that" || w == "had" || !seen.Add(w)) continue;
                if (++n > 3) break;
                Add($"The word “{w}” appears twice in a row: “{JsRegex.Squash(m.Value)}”.", m.Index + m.Groups[1].Length, m.Index + m.Length, "", true);
            }
        }
        {
            var seen = new HashSet<string>(StringComparer.Ordinal); int n = 0;
            foreach (Match m in Placeholder.Matches(text))
            {
                if (BarePlaceholder.IsMatch(m.Value) && m.Value != m.Value.ToUpperInvariant()) continue;
                if (!seen.Add(m.Value.ToLowerInvariant())) continue;
                if (++n <= 5) Add($"A placeholder is still in the draft: “{JsRegex.Squash(m.Value)}”.", alsoInFacts: true);
            }
        }
    }
}
