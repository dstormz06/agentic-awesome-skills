using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AndaCompanion.Data;
using AndaCompanion.Models;
using AndaCompanion.Services.Portfolio;

namespace AndaCompanion.Services;

/// <summary>
/// What the Edge extension talks to when it works "together" with the app: the same executable, started by the browser through
/// Chrome native messaging (stdin and stdout, each message a 4-byte length then UTF-8 JSON). It is read-only, makes no network
/// call, never opens a window, and answers from the same settings and portfolio file the app uses, so there is one source of truth.
/// The browser only lets the extension whose ID is in the host manifest start it.
/// </summary>
public sealed class NativeHostContext
{
    public required Settings Settings { get; init; }
    public required IAndaRepository Repository { get; init; }
    public required BusinessCalendar Calendar { get; init; }
    public Func<DateOnly> Today { get; init; } = () => DateOnly.FromDateTime(DateTime.Now);
    public string DataKind { get; init; } = "demo";                 // "demo" or "portfolio"
    public string DataLabel { get; init; } = "Fictional demo data";
    public string DataMessage { get; init; } = "";
    public string DataFile { get; init; } = "";
    public Func<AndaRecord, ReferenceInfo?>? Reference { get; init; }

    public CardContext Card() => new(Today(), Calendar, Settings.HolidayLeadDays, Settings.UrgentLateDays, Reference);

    /// <summary>Builds the context the way the app does: the person's portfolio if one is set and readable, otherwise the demo records.</summary>
    public static NativeHostContext Create(Settings settings, bool useRealFiles = true)
    {
        IAndaRepository repo = new MockAndaRepository();
        string kind = "demo", label = "Fictional demo data", message = "", file = "";
        Func<AndaRecord, ReferenceInfo?>? reference = null;
        if (useRealFiles && !string.IsNullOrWhiteSpace(settings.PortfolioPath))
        {
            var p = new PortfolioRepository(settings.PortfolioPath!, settings.PortfolioColumns, settings.NotesFolder);
            message = p.Status.Message;
            if (p.Status.Ok) { repo = p; kind = "portfolio"; label = $"Your portfolio: {p.FileName}"; file = p.FileName; }
        }
        if (useRealFiles && !string.IsNullOrWhiteSpace(settings.ReferenceDataPath))
        {
            DrugsAtFdaFile? drugs = null; string drugMessage = "";
            bool tried = false;
            reference = r =>
            {
                if (!tried) { tried = true; drugs = DrugsAtFdaFile.TryLoad(settings.ReferenceDataPath, out drugMessage); }
                return ReferenceResolver.For(r, drugs, drugMessage, settings.ReferenceDataPath, loading: false);
            };
        }
        else reference = r => ReferenceResolver.For(r, null, "", null, loading: false);
        return new NativeHostContext
        {
            Settings = settings, Repository = repo, Calendar = BusinessCalendar.FromFile(AppPaths.ClosuresFile),
            DataKind = kind, DataLabel = label, DataMessage = message, DataFile = file, Reference = reference,
        };
    }
}

public static class ReferenceResolver
{
    public const string NoPsg = "not available: no PSG date in your data, and no official PSG source is connected";
    public const string NoRld = "not available: no Drugs@FDA file chosen (app: Settings, My data)";

    /// <summary>The fictional demo records carry made-up reference dates so the layout can be shown. They say so.</summary>
    private static readonly Dictionary<string, (DateOnly Psg, DateOnly Rld)> DemoDates = new()
    {
        ["216482"] = (new DateOnly(2026, 1, 14), new DateOnly(2026, 3, 3)),
        ["214990"] = (new DateOnly(2025, 11, 20), new DateOnly(2026, 3, 3)),
    };

    private static string? DemoText(AndaRecord r, bool psg) =>
        r.Demo && DemoDates.TryGetValue(r.Number, out var d)
            ? (psg ? d.Psg : d.Rld).ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture) + " · fictional demo date"
            : null;

    /// <summary>
    /// What the card's PSG and RLD lines say, shared by the app and the extension host so the two never differ. Always two
    /// lines: a date with where it came from, or a brief reason there is none. Never null, never a guess.
    /// </summary>
    public static ReferenceInfo For(AndaRecord record, DrugsAtFdaFile? drugs, string drugsMessage, string? referencePath, bool loading)
    {
        string psg = DrugsAtFdaFile.PsgOf(record) ?? DemoText(record, psg: true) ?? NoPsg;
        if (drugs is not null) return new ReferenceInfo(psg, drugs.Describe(record).RldText);
        string rld = string.IsNullOrWhiteSpace(referencePath)
            ? DemoText(record, psg: false) ?? NoRld
            : loading ? "reading the Drugs@FDA file…"
            : $"not available: {(drugsMessage.Length > 0 ? drugsMessage : "the Drugs@FDA file could not be used")}";
        return new ReferenceInfo(psg, rld);
    }
}

public sealed class NativeHostSession(NativeHostContext ctx)
{
    public const int Protocol = 1;
    public const int MaxDraftChars = 20_000;

    private static string Version => typeof(NativeHostSession).Assembly.GetName().Version?.ToString(3) ?? "0";

    /// <summary>Never throws: any failure becomes an error reply with a short, content-free message.</summary>
    public string Handle(string requestJson)
    {
        JsonNode? id = null;
        try
        {
            var req = JsonNode.Parse(requestJson) as JsonObject ?? throw new FormatException("not an object");
            id = req["id"]?.DeepClone();
            string type = req["type"]?.GetValue<string>() ?? "";
            var body = type switch
            {
                "hello" => Hello(),
                "lookup" => Lookup(req["number"]?.GetValue<string>() ?? ""),
                "draft" => Draft(req["text"]?.GetValue<string>() ?? ""),
                "settings" => SettingsReply(),
                "watchlist" => Watch(),
                _ => Err("unknown-type", "This host does not know that request."),
            };
            return Finish(id, body);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return Finish(id, Err("bad-request", "The request could not be read."));
        }
        catch (Exception ex)
        {
            return Finish(id, Err("host-error", ex.GetType().Name));
        }
    }

    private static string Finish(JsonNode? id, JsonObject body)
    {
        body["id"] = id;
        return body.ToJsonString(CardModels.Json);
    }

    private static JsonObject Err(string code, string message) => new() { ["ok"] = false, ["error"] = code, ["message"] = message };

    private JsonObject Hello() => new()
    {
        ["ok"] = true, ["protocol"] = Protocol, ["app"] = "ANDA Companion (Notula)", ["version"] = Version,
        ["dataKind"] = ctx.DataKind, ["dataLabel"] = ctx.DataLabel, ["dataMessage"] = ctx.DataMessage,
        ["count"] = ctx.Repository.Numbers.Count,
    };

    private JsonObject Lookup(string text)
    {
        string? number = AndaValidator.Extract(text);
        if (number is null) return Err("bad-number", "That is not an ANDA number.");
        if (ctx.Repository.Find(number) is not { } record)
            return Err("not-found", ctx.DataKind == "portfolio" ? $"{number} is not in your portfolio ({ctx.DataFile})" : $"{number} is not a recognized ANDA");
        var card = CardModels.Build(record, ctx.Card());
        return new JsonObject { ["ok"] = true, ["card"] = JsonSerializer.SerializeToNode(card, CardModels.Json) };
    }

    private JsonObject Draft(string text)
    {
        if (text.Length > MaxDraftChars) return Err("too-long", $"Drafts over {MaxDraftChars:N0} characters are not checked.");
        var r = DraftCheck.Check(text, ctx.Repository.Find);
        return new JsonObject
        {
            ["ok"] = true,
            ["findings"] = JsonSerializer.SerializeToNode(r.Findings.Select(f => new { kind = f.Kind switch {
                DraftFindingKind.Difference => "diff", DraftFindingKind.UnknownAnda => "unknown", DraftFindingKind.Ok => "ok", _ => "note" }, text = f.Text }), CardModels.Json),
            ["numbersChecked"] = r.NumbersChecked, ["datesChecked"] = r.DatesChecked, ["statusChecked"] = r.StatusChecked,
        };
    }

    private JsonObject SettingsReply()
    {
        var s = ctx.Settings;
        return new JsonObject
        {
            ["ok"] = true,
            ["settings"] = new JsonObject
            {
                ["consent"] = s.Consent switch { ConsentModel.Full => "full", ConsentModel.PeekFirst => "peek", _ => "whisper" },
                ["escalateUrgent"] = s.EscalateUrgent, ["urgentLateDays"] = s.UrgentLateDays, ["holidayLeadDays"] = s.HolidayLeadDays,
                ["autoHideSeconds"] = s.AutoHideSeconds,
            },
        };
    }

    private JsonObject Watch()
    {
        var result = WatchList.Build(ctx.Repository, ctx.Card(), limit: 300);
        return new JsonObject
        {
            ["ok"] = true, ["total"] = result.Total, ["shown"] = result.Shown,
            ["items"] = JsonSerializer.SerializeToNode(result.Items, CardModels.Json),
        };
    }
}

public static class NativeHost
{
    public const int MaxRequestBytes = 1024 * 1024;
    public const int MaxResponseBytes = 1024 * 1024;   // Chrome's own limit for host-to-browser messages

    /// <summary>The browser starts a native host with the calling extension's origin as the first argument.</summary>
    public static bool IsHostLaunch(string[] args) =>
        args.Length > 0 && (args[0].StartsWith("chrome-extension://", StringComparison.Ordinal) || args.Contains("--native-host"));

    public static byte[]? ReadFrame(Stream input)
    {
        var header = new byte[4];
        if (!ReadExactly(input, header)) return null;
        uint length = BitConverter.ToUInt32(header, 0);   // native byte order, as Chrome specifies
        if (length > MaxRequestBytes) throw new InvalidDataException("request too large");
        var body = new byte[length];
        if (length > 0 && !ReadExactly(input, body)) return null;
        return body;
    }

    public static void WriteFrame(Stream output, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > MaxResponseBytes)
            bytes = Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"too-large\",\"message\":\"The answer was too large to send.\"}");
        output.Write(BitConverter.GetBytes((uint)bytes.Length));
        output.Write(bytes);
        output.Flush();
    }

    private static bool ReadExactly(Stream s, byte[] buffer)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = s.Read(buffer, read, buffer.Length - read);
            if (n <= 0) return false;
            read += n;
        }
        return true;
    }

    /// <summary>Serves requests until the browser closes the pipe. Returns 0 on a clean close.</summary>
    public static int Serve(Stream input, Stream output, NativeHostContext ctx)
    {
        var session = new NativeHostSession(ctx);
        while (true)
        {
            byte[]? frame;
            try { frame = ReadFrame(input); }
            catch (InvalidDataException) { return 2; }
            if (frame is null) return 0;
            WriteFrame(output, session.Handle(Encoding.UTF8.GetString(frame)));
        }
    }

    /// <summary>Entry point for a browser launch. Uses the data folder of the app that installed the connection.</summary>
    public static int Run(string[] args)
    {
        NativeHostRegistration.AdoptInstalledHome();
        var ctx = NativeHostContext.Create(SettingsService.Load());
        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();
        return Serve(input, output, ctx);
    }
}
