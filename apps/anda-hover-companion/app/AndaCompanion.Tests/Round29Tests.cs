using System.Text.Json;
using AndaCompanion.Services;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// Round 29: what is switched on, said in every window; the event log; what changed. The simulation wrote mockup/round29-golden.json
/// (tools/round29-golden.cjs); the app must give the same answers, character for character.
/// </summary>
[Collection("Global state")]
public class Round29Tests
{
    private static string Folder(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "anda-hover-companion") dir = dir.Parent;
        return Path.Combine(new[] { dir?.FullName ?? throw new DirectoryNotFoundException("anda-hover-companion folder not found") }.Concat(parts).ToArray());
    }
    private static JsonDocument Golden() => JsonDocument.Parse(File.ReadAllText(Folder("mockup", "round29-golden.json")));
    private static string[] Strings(JsonElement e) => e.EnumerateArray().Select(x => x.GetString()!).ToArray();
    private static void Fail(List<string> bad) => Assert.True(bad.Count == 0, bad.Count + " differences from the simulation:\n" + string.Join("\n", bad.Take(8)));
    private static void Same(List<string> bad, string what, string expected, string actual)
    {
        if (expected != actual) bad.Add($"{what}\n  expected: {JsonSerializer.Serialize(expected.Length > 500 ? expected[..500] : expected)}\n  actual:   {JsonSerializer.Serialize(actual.Length > 500 ? actual[..500] : actual)}");
    }

    [Fact]
    public void What_is_switched_on_is_said_exactly_as_the_simulation_says_it()
    {
        using var doc = Golden(); var bad = new List<string>(); int n = 0;
        Same(bad, "offline", doc.RootElement.GetProperty("offline").GetString()!, Connections.Offline);
        Same(bad, "note", doc.RootElement.GetProperty("note").GetString()!, Connections.Note);
        foreach (var c in doc.RootElement.GetProperty("connections").EnumerateArray())
        {
            n++; var s = c.GetProperty("state");
            ConnAi? ai = s.GetProperty("ai").ValueKind == JsonValueKind.Null ? null : new(s.GetProperty("ai").GetProperty("name").GetString()!, s.GetProperty("ai").GetProperty("where").GetString()!);
            var agents = s.GetProperty("agents").EnumerateArray().Select(a => new ConnAgent(a.GetProperty("name").GetString()!, a.GetProperty("where").GetString()!, a.GetProperty("auto").GetBoolean())).ToList();
            ConnOutlook? outlook = s.GetProperty("outlook").ValueKind == JsonValueKind.Null ? null : new(s.GetProperty("outlook").GetProperty("where").GetString()!);
            var r = Connections.Describe(new ConnState(ai, agents, outlook)); var want = c.GetProperty("result");
            Same(bad, $"case {n} line", want.GetProperty("line").GetString()!, r.Line);
            Same(bad, $"case {n} items", string.Join("\n", want.GetProperty("items").EnumerateArray().Select(i => $"{i.GetProperty("name").GetString()}|{i.GetProperty("sends").GetString()}|{i.GetProperty("where").GetString()}|{i.GetProperty("kept").GetString()}")),
                string.Join("\n", r.Items.Select(i => $"{i.Name}|{i.Sends}|{i.Where}|{i.Kept}")));
            Same(bad, $"case {n} text", string.Join("\n", Strings(want.GetProperty("text"))), string.Join("\n", r.Text));
        }
        Assert.True(n >= 8, "cases are missing"); Fail(bad);
    }

    [Fact]
    public void The_event_log_keeps_times_kinds_and_counts_exactly_as_the_simulation_does_and_withholds_everything_else()
    {
        using var doc = Golden(); var bad = new List<string>(); int n = 0; var entries = new List<EventEntry>();
        Same(bad, "withheld", doc.RootElement.GetProperty("withheld").GetString()!, EventLog.Withheld);
        Same(bad, "kinds", string.Join("\n", doc.RootElement.GetProperty("eventTypes").EnumerateObject().Select(p => p.Name + "=" + p.Value.GetString())), string.Join("\n", EventLog.Types.Select(t => t.Type + "=" + t.Meaning)));
        foreach (var c in doc.RootElement.GetProperty("events").EnumerateArray())
        {
            n++; var want = c.GetProperty("entry");
            var e = EventLog.Entry(c.GetProperty("type").GetString(), c.GetProperty("detail").ValueKind == JsonValueKind.Null ? null : c.GetProperty("detail").GetString(),
                DateTimeOffset.FromUnixTimeMilliseconds(c.GetProperty("when").GetInt64()).UtcDateTime);
            entries.Add(e);
            Same(bad, $"case {n}", $"{want.GetProperty("t").GetString()}|{want.GetProperty("type").GetString()}|{want.GetProperty("detail").GetString()}", $"{e.T}|{e.Type}|{e.Detail}");
        }
        Assert.True(n >= 17, "cases are missing");
        Same(bad, "csv", doc.RootElement.GetProperty("csv").GetString()!, EventLog.Csv(entries));
        Same(bad, "empty csv", doc.RootElement.GetProperty("emptyCsv").GetString()!, EventLog.Csv(Array.Empty<EventEntry>()));
        Fail(bad);
        // What the log may never hold, whatever a caller passes: an ANDA number, quoted text, markup, a line break, a long text.
        foreach (string hostile in new[] { "ANDA 216482", "216482", "a \"quote\"", "<b>x</b>", "two\nlines", new string('a', 121), "naïve", "tab\there", "back\\slash", "at@sign" })
            Assert.Equal(EventLog.Withheld, EventLog.Entry("sent", hostile, DateTime.UtcNow).Detail);
        Assert.Equal("12,345 characters", EventLog.Entry("sent", "12,345 characters", DateTime.UtcNow).Detail);
    }

    [Fact]
    public void What_changed_is_the_same_list_in_both_programs()
    {
        using var doc = Golden(); var bad = new List<string>();
        Same(bad, "version", doc.RootElement.GetProperty("version").GetString()!, ChangeLog.Version);
        Same(bad, "changes", string.Join("\n", doc.RootElement.GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("round") + ": " + string.Join(" | ", Strings(c.GetProperty("items"))))),
            string.Join("\n", ChangeLog.Entries.Select(c => c.Round + ": " + string.Join(" | ", c.Items))));
        Same(bad, "text", string.Join("\n", Strings(doc.RootElement.GetProperty("changesText"))), string.Join("\n", ChangeLog.Text()));
        Fail(bad);
    }

    [Fact]
    public void The_app_counts_a_connection_as_switched_on_only_when_it_is_chosen_here_and_complete()
    {
        Func<string, string?> key = k => k == "ANTHROPIC_API_KEY" ? "sk-test-not-a-real-key" : null, none = _ => null;
        Assert.Equal(Connections.Offline, Connections.Describe(Connections.From(new Settings(), key)).Line);                                        // a key on the PC switches nothing on
        Assert.Equal(Connections.Offline, Connections.Describe(Connections.From(new Settings { Provider = ProviderChoice.Claude }, none)).Line);     // chosen, but no key: it could not be used
        Assert.Equal("Switched on: Claude (AI)", Connections.Describe(Connections.From(new Settings { Provider = ProviderChoice.Claude }, key)).Line);
        Assert.Equal("Switched on: Claude (AI)", Connections.Describe(Connections.From(new Settings { Provider = ProviderChoice.Auto }, key)).Line);
        Assert.Equal(Connections.Offline, Connections.Describe(Connections.From(new Settings { Provider = ProviderChoice.Auto }, none)).Line);

        var s = new Settings { Provider = ProviderChoice.MyApi };
        Assert.Equal(Connections.Offline, Connections.Describe(Connections.From(s, none)).Line);                                                     // My API with no address
        s.CustomApi = new CustomApiSettings { Name = "Division\nAPI", Endpoint = "https://api.division.example.test/v1/chat", SecretEnvVar = "" };
        var mine = Connections.Describe(Connections.From(s, none));
        Assert.Equal("Switched on: Division API (AI)", mine.Line); Assert.Equal("api.division.example.test", mine.Items[0].Where);                   // the host, never the whole address

        var t = new Settings();
        t.Agents[0].Enabled = true; t.Agents[1].Enabled = true; t.Agents[1].AutoRun = AgentRun.OnFullCard; t.Agents[1].Api.Endpoint = "https://agents.example.test/ask?token=abc";
        t.Outlook.Enabled = true; t.Outlook.Cloud = OutlookCloud.UsGovernment;
        var r = Connections.Describe(Connections.From(t, none));
        Assert.Equal($"Switched on: {t.Agents[0].Name} (agent) · {t.Agents[1].Name} (agent) · Outlook (read-only)", r.Line);
        Assert.Equal("no address yet, so nothing can be sent", r.Items[0].Where); Assert.Equal("agents.example.test", r.Items[1].Where);
        Assert.StartsWith("By itself when a full card opens", r.Items[1].Sends); Assert.Equal("graph.microsoft.us", r.Items[2].Where);
        Assert.DoesNotContain(r.Text, l => l.Contains("token=abc"));

        // A name is the person's own text: an invisible character that could reorder the line is removed. A look-alike letter in an address shows in the form the network uses.
        var u = new Settings();
        u.Agents[0].Enabled = true; u.Agents[0].Name = "Alert\u202E buH\u200B"; u.Agents[0].Api.Endpoint = "https://ex\u0430mple.test/ask";
        var v = Connections.Describe(Connections.From(u, none));
        Assert.Equal("Switched on: Alert buH (agent)", v.Line); Assert.Equal("xn--exmple-4nf.test", v.Items[0].Where);

        Assert.Equal(new[] { "AI switched on", "agents switched on: 0 to 2", "Outlook switched off" },
            Connections.Changes((false, 0, true), (true, 2, false)));
        Assert.Empty(Connections.Changes((true, 1, false), (true, 1, false)));
    }

    [Fact]
    public void The_event_log_file_is_only_added_to_reads_back_through_the_same_rule_and_writes_nothing_until_it_is_started()
    {
        string dir = Temp.NewDir(), path = Path.Combine(dir, "events.csv");
        try
        {
            EventLog.Configure(null);
            EventLog.Record("sent", "AI request, 10 characters"); Assert.False(File.Exists(path));              // not started: nothing is written anywhere
            EventLog.Configure(path);
            var t0 = new DateTime(2026, 7, 16, 13, 5, 7, 42, DateTimeKind.Utc);
            EventLog.Record("connection", "AI switched on", t0);
            EventLog.Record("sent", "AI request, 1,234 characters", t0.AddSeconds(1));
            EventLog.Record("sent", "about ANDA 216482", t0.AddSeconds(2));
            string first = File.ReadAllText(path);
            Assert.Equal("time_utc,type,detail\r\n2026-07-16T13:05:07.042Z,connection,\"AI switched on\"\r\n2026-07-16T13:05:08.042Z,sent,\"AI request, 1,234 characters\"\r\n2026-07-16T13:05:09.042Z,sent,\"(detail withheld: not in the allowed form)\"\r\n", first);
            EventLog.Record("deleted", "kept copies of the sheet", t0.AddSeconds(3));
            Assert.StartsWith(first, File.ReadAllText(path));                                                    // what was there is still there, unchanged
            var (entries, total) = EventLog.Read();
            Assert.Equal(4, total); Assert.Equal(new[] { "connection", "sent", "sent", "deleted" }, entries.Select(e => e.Type));
            Assert.Equal(File.ReadAllText(path), EventLog.Csv(entries));                                         // the export is the file

            // A line added by hand cannot make the window show what the log may not hold; a line that is not ours is passed over.
            File.AppendAllText(path, "2026-07-16T13:06:00.000Z,sent,\"the draft said: ANDA 216482 is late\"\r\nnot a line of the log\r\n2026-07-16T13:07:00.000Z,made-up,\"x\"\r\n");
            var (again, total2) = EventLog.Read();
            Assert.Equal(6, total2); Assert.Equal(EventLog.Withheld, again[4].Detail); Assert.Equal(("warning", "x"), (again[5].Type, again[5].Detail));
            Assert.Equal(2, EventLog.Read(newest: 2).Entries.Count);
            Assert.Equal((2, 6, "warning"), (EventLog.Read(newest: 2).Entries.Count, EventLog.Read(newest: 2).Total, EventLog.Read(newest: 2).Entries[1].Type));   // the newest are kept, every line is counted

            // A spreadsheet would read a detail that begins with = + or - as a formula: such a detail is never written and never shown.
            EventLog.Record("sent", "=1+1", t0.AddSeconds(4));
            Assert.EndsWith(",sent,\"" + EventLog.Withheld + "\"\r\n", File.ReadAllText(path));
            File.AppendAllText(path, "2026-07-16T13:08:00.000Z,sent,\"+1\"\r\n");
            Assert.Equal(EventLog.Withheld, EventLog.Read().Entries[^1].Detail);

            // Where the file cannot be written, nothing is thrown.
            EventLog.Configure(Path.Combine(dir, "events.csv", "inside-a-file.csv"));
            EventLog.Record("error", "x");
        }
        finally { EventLog.Configure(null); }
    }

    [Fact]
    public void A_time_of_the_log_is_shown_in_the_persons_time_zone_with_its_abbreviation_and_the_right_daylight_time()
    {
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        Assert.Equal("Jul 16, 9:05:07 AM EDT", EventLog.Local("2026-07-16T13:05:07.042Z", eastern));
        Assert.Equal("Nov 18, 2:03:59 PM EST", EventLog.Local("2026-11-18T19:03:59.000Z", eastern));
        Assert.Equal("Nov 1, 1:30:00 AM EDT", EventLog.Local("2026-11-01T05:30:00.000Z", eastern));              // the hour before the clocks go back
        Assert.Equal("Nov 1, 1:30:00 AM EST", EventLog.Local("2026-11-01T06:30:00.000Z", eastern));              // and the same wall time an hour later
        Assert.Equal("Jul 16, 1:05:07 PM UTC", EventLog.Local("2026-07-16T13:05:07.042Z", TimeZoneInfo.Utc));
        var odd = TimeZoneInfo.CreateCustomTimeZone("Somewhere", new TimeSpan(5, 30, 0), "Somewhere", "A zone with a long name that is no abbreviation");
        Assert.Equal("Jul 16, 6:35:07 PM UTC+5:30", EventLog.Local("2026-07-16T13:05:07.042Z", odd));
        Assert.Equal("not a time", EventLog.Local("not a time", eastern));
    }
}
