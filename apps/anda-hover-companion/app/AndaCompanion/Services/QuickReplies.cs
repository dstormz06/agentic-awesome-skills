using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AndaCompanion.Models;

namespace AndaCompanion.Services;

public sealed record QuickReply(string Name, string Text);

/// <summary>
/// Fills a quick-reply template from one record. No AI and no network: every placeholder is replaced by a value the card already
/// shows, and a value the data does not have becomes "(not in my data)" instead of a guess. Unknown placeholders are left as typed.
/// </summary>
public static class QuickReplyFill
{
    public const string Missing = "(not in my data)";

    public static readonly IReadOnlyList<(string Name, string Meaning)> Placeholders = new[]
    {
        ("number", "the ANDA number"), ("drug", "the product name"), ("applicant", "the applicant"), ("cycle", "the review cycle"),
        ("progress", "percent through review"), ("goal", "the goal date"), ("forecast", "the forecast date"), ("delta", "days against the goal"),
        ("confidence", "forecast confidence"), ("attention", "the item that needs action"), ("latest", "the latest event"),
        ("next", "what lands next"), ("status", "the one-line status sentence"), ("today", "today's date"),
        ("psg", "the PSG line"), ("rld", "the RLD labeling line"),
    };

    private static readonly Regex Token = new(@"\{([a-z]+)\}", RegexOptions.Compiled);

    public static string Fill(string template, AndaRecord r, DateOnly today, ReferenceInfo? reference = null)
    {
        string meta = r.Meta ?? "";
        int dot = meta.LastIndexOf('·');
        string Clean(string s) => s.Replace("▲", "").Replace("▼", "").Trim();
        string Known(string? v) => string.IsNullOrWhiteSpace(v) || v == AndaRecord.Unknown ? Missing : v!;
        var info = reference ?? ReferenceResolver.For(r, null, "", null, loading: false);
        var values = new Dictionary<string, string>
        {
            ["number"] = r.Number, ["drug"] = Known(r.Drug),
            ["applicant"] = Known(dot >= 0 ? meta[..dot].Trim() : meta.Trim()), ["cycle"] = Known(dot >= 0 ? meta[(dot + 1)..].Trim() : null),
            ["progress"] = r.HasProgress ? $"{r.ProgressPct}%" : Missing,
            ["goal"] = Known(r.GoalDate),
            ["forecast"] = r.HasForecast ? r.ForecastDate : Missing,
            ["delta"] = r.HasForecast ? Clean(r.ForecastDelta) : Missing,
            ["confidence"] = r.HasForecast ? Known(r.ForecastConfidence) : Missing,
            ["attention"] = r.Attention?.Text ?? "no open action items in the record",
            ["latest"] = Known(r.Latest), ["next"] = Known(r.Ahead),
            ["status"] = StatusLine.Build(r),
            ["today"] = today.ToString("MMM d, yyyy", CultureInfo.InvariantCulture),
            ["psg"] = info.PsgText ?? ReferenceResolver.NoPsg, ["rld"] = info.RldText ?? ReferenceResolver.NoRld,
        };
        return Token.Replace(template, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
    }
}

/// <summary>The person's own quick replies and templates, stored as plain JSON they can read and edit (replies.json).</summary>
public sealed class QuickReplyService
{
    public const int MaxReplies = 30, MaxTextChars = 2000, MaxNameChars = 60;
    private readonly string _path;
    private List<QuickReply> _replies = new();
    public IReadOnlyList<QuickReply> Replies => _replies;

    public static IReadOnlyList<QuickReply> Starters { get; } = new QuickReply[]
    {
        new("Short status", "{status}"),
        new("Status for my lead", "Hi, quick status on ANDA {number} ({drug}). It is {progress} through {cycle}. Goal date is {goal} and the forecast is {forecast} ({delta}, {confidence}). Needs action: {attention}. Next: {next}."),
        new("Date check before I quote it", "Before I give dates on ANDA {number}: goal {goal}, forecast {forecast}. Please note the weekend and holiday check on the card was run on {today}."),
    };

    public QuickReplyService(string? path = null)
    {
        _path = path ?? AppPaths.RepliesFile;
        try
        {
            if (File.Exists(_path) && JsonSerializer.Deserialize<List<QuickReply>>(File.ReadAllText(_path)) is { } loaded)
                _replies = loaded.Where(r => !string.IsNullOrWhiteSpace(r?.Name) && r.Text is not null).Take(MaxReplies).ToList();
            else _replies = Starters.ToList();
        }
        catch { _replies = Starters.ToList(); }
    }

    /// <summary>Adds or replaces a reply by name. Returns a plain reason if it was refused.</summary>
    public string? Upsert(QuickReply reply)
    {
        string name = (reply.Name ?? "").Trim();
        if (name.Length == 0) return "Give the reply a name.";
        if (name.Length > MaxNameChars) return $"A name can be up to {MaxNameChars} characters.";
        if ((reply.Text ?? "").Length > MaxTextChars) return $"A reply can be up to {MaxTextChars:N0} characters.";
        if (_replies.All(r => !r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) && _replies.Count >= MaxReplies) return $"You can keep up to {MaxReplies} replies.";
        _replies.RemoveAll(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        _replies.Add(new QuickReply(name, reply.Text ?? ""));
        Save();
        return null;
    }

    public void Delete(string name) { _replies.RemoveAll(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); Save(); }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_replies, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best effort */ }
    }
}
