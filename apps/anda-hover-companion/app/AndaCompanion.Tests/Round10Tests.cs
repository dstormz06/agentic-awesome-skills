using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using AndaCompanion.Data;
using AndaCompanion.Models;
using AndaCompanion.Services;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>Round 10: connection status, the person's own API, writing preferences and quick replies, the middle-click rule, the reference update.</summary>
[Collection("Global state")]
public class ConnectionStatusTests
{
    private sealed class Fake(Func<string> reply, string name = "Fake AI") : IChatCompleter
    {
        public string ProviderName => name;
        public Task<string> CompleteAsync(string system, string user, int maxTokens, CancellationToken ct) => Task.FromResult(reply());
    }

    [Fact]
    public void Nothing_is_connected_until_a_request_has_really_succeeded()
    {
        ConnectionStatus.Configure(null);
        Assert.Equal(ConnectionState.Offline, ConnectionStatus.Current.State);
        Assert.Equal("OFFLINE · no API connected", ConnectionStatus.Current.Label);
        Assert.Contains("nothing is sent anywhere", ConnectionStatus.Current.Sentence);

        ConnectionStatus.Configure(new Fake(() => "x"));           // a service exists: set up, not proven
        Assert.Equal(ConnectionState.SetUp, ConnectionStatus.Current.State);
        Assert.Contains("set up, not tested", ConnectionStatus.Current.Label);
        Assert.Contains("not known to work", ConnectionStatus.Current.Sentence);
    }

    [Fact]
    public async Task A_successful_request_means_connected_and_a_failed_one_says_not_reachable()
    {
        int calls = 0;
        var good = new ReportingChatCompleter(new Fake(() => ++calls == 1 ? "text" : throw new HttpRequestException("boom with a secret body")));
        await good.CompleteAsync("s", "u", 10, default);
        Assert.Equal(ConnectionState.Connected, ConnectionStatus.Current.State);
        Assert.StartsWith("CONNECTED · Fake AI · checked ", ConnectionStatus.Current.Label);

        await Assert.ThrowsAsync<HttpRequestException>(() => good.CompleteAsync("s", "u", 10, default));
        Assert.Equal(ConnectionState.Failed, ConnectionStatus.Current.State);
        Assert.Contains("NOT REACHABLE", ConnectionStatus.Current.Label);
        Assert.DoesNotContain("secret body", ConnectionStatus.Current.Sentence);     // the exception text never reaches the screen
        Assert.Contains("HttpRequestException", ConnectionStatus.Current.Sentence);
        ConnectionStatus.Configure(null);
    }

    [Fact]
    public async Task A_cancelled_request_is_not_a_failure()
    {
        ConnectionStatus.Configure(new Fake(() => "x"));
        var c = new ReportingChatCompleter(new Fake(() => throw new OperationCanceledException()));
        await Assert.ThrowsAsync<OperationCanceledException>(() => c.CompleteAsync("s", "u", 1, default));
        Assert.Equal(ConnectionState.SetUp, ConnectionStatus.Current.State);
        ConnectionStatus.Configure(null);
    }
}

[Collection("Global state")]
public class CustomApiTests
{
    private sealed class Handler(Func<HttpRequestMessage, string, (HttpStatusCode, string)> respond) : HttpMessageHandler
    {
        public List<(string Url, string Body, string? Auth)> Seen { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Seen.Add((request.RequestUri!.ToString(), body, request.Headers.TryGetValues("Authorization", out var v) ? v.First() : null));
            var (status, text) = respond(request, body);
            return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }
    }

    private static CustomApiSettings Good(string var = "MY_TEST_TOKEN") => new() { Name = "Division API", Endpoint = "https://api.example.test/v1/chat", SecretEnvVar = var, Model = "m-1" };
    private static Func<string, string?> Env(string? token) => n => n == "MY_TEST_TOKEN" ? token : null;

    [Theory]
    [InlineData("", "enter the endpoint")]
    [InlineData("http://api.example.test/v1", "https://")]
    [InlineData("ftp://api.example.test", "https://")]
    [InlineData("https://user:pw@api.example.test/v1", "user name or password")]
    public void The_endpoint_must_be_a_plain_https_address(string endpoint, string expected)
    {
        var s = Good(); s.Endpoint = endpoint;
        var r = CustomApiConfig.Inspect(s, Env("t"));
        Assert.False(r.Ok);
        Assert.Contains(expected, r.Message);
    }

    [Fact]
    public void Plain_http_is_accepted_only_for_localhost()
    {
        var s = Good(); s.Endpoint = "http://localhost:5099/x";
        Assert.True(CustomApiConfig.Inspect(s, Env("t")).Ok);
    }

    [Fact]
    public void The_token_is_a_name_never_a_value_and_must_exist_in_the_environment()
    {
        var s = Good("sk-live-abc123def");                      // someone pasted a token where the NAME goes
        var r = CustomApiConfig.Inspect(s, Env("t"));
        Assert.False(r.Ok);
        Assert.Contains("NAME of the variable", r.Message);

        var missing = CustomApiConfig.Inspect(Good(), Env(null));
        Assert.False(missing.Ok);
        Assert.Contains("MY_TEST_TOKEN is not set", missing.Message);

        var ok = CustomApiConfig.Inspect(Good(), Env("t0k3n"));
        Assert.True(ok.Ok);
        Assert.DoesNotContain("t0k3n", ok.Message);
        Assert.DoesNotContain("t0k3n", ok.Settings!.ToString());   // redacted in every printout
    }

    [Fact]
    public void A_bad_template_or_reply_path_is_caught_before_anything_is_sent()
    {
        var s = Good(); s.RequestTemplate = "{\"messages\": [ {{user}} ";
        Assert.Contains("not valid JSON", CustomApiConfig.Inspect(s, Env("t")).Message);
        s = Good(); s.ResponsePath = "choices[0.message";
        Assert.Contains("reply path", CustomApiConfig.Inspect(s, Env("t")).Message);
        s = Good(); s.ResponsePath = "a..b";
        Assert.False(CustomApiMapping.IsValidPath(s.ResponsePath));
        Assert.True(CustomApiMapping.IsValidPath("content[0].text"));
        Assert.True(CustomApiMapping.IsValidPath("data.items[2][0].value"));
    }

    [Fact]
    public async Task A_draft_with_quotes_and_newlines_cannot_break_out_of_the_request_and_the_token_goes_only_in_the_header()
    {
        var handler = new Handler((_, _) => (HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"content\":\"hello back\"}}]}"));
        var completer = CustomApiConfig.CreateCompleter(Good(), Env("t0k3n"), handler)!;
        string evil = "He said \"stop\"\nand then {{user}} \\ and </record> done";
        string text = await completer.CompleteAsync("sys \"x\"", evil, 321, default);
        Assert.Equal("hello back", text);

        var seen = Assert.Single(handler.Seen);
        Assert.Equal("Bearer t0k3n", seen.Auth);
        Assert.DoesNotContain("t0k3n", seen.Body);
        using var doc = JsonDocument.Parse(seen.Body);                                   // still valid JSON
        Assert.Equal(321, doc.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal("m-1", doc.RootElement.GetProperty("model").GetString());
        var messages = doc.RootElement.GetProperty("messages");
        Assert.Equal("sys \"x\"", messages[0].GetProperty("content").GetString());
        Assert.Equal(evil, messages[1].GetProperty("content").GetString());              // exactly what was written, as data
        Assert.Equal("Division API · m-1", completer.ProviderName);
    }

    [Fact]
    public async Task An_error_or_an_unexpected_answer_never_shows_the_body()
    {
        var bad = new Handler((_, _) => (HttpStatusCode.Unauthorized, "{\"error\":\"your key sk-leak-123 is wrong\"}"));
        var c1 = CustomApiConfig.CreateCompleter(Good(), Env("t"), bad)!;
        var ex = await Assert.ThrowsAsync<ElsaException>(() => c1.CompleteAsync("s", "u", 10, default));
        Assert.Contains("401", ex.Message);
        Assert.DoesNotContain("sk-leak", ex.Message);

        var odd = new Handler((_, _) => (HttpStatusCode.OK, "{\"unexpected\":\"shape sk-leak-456\"}"));
        var c2 = CustomApiConfig.CreateCompleter(Good(), Env("t"), odd)!;
        var ex2 = await Assert.ThrowsAsync<ElsaException>(() => c2.CompleteAsync("s", "u", 10, default));
        Assert.Contains("choices[0].message.content", ex2.Message);
        Assert.DoesNotContain("sk-leak", ex2.Message);
    }

    [Fact]
    public async Task Test_proves_the_channel_and_updates_the_connection_status()
    {
        ConnectionStatus.Configure(null);
        var handler = new Handler((_, _) => (HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"content\":\"OK\"}}]}"));
        string result = await ConnectionService.TestMyApiAsync(Good(), Env("t"), handler);
        Assert.StartsWith("Connected · Division API · m-1", result);
        Assert.Equal(ConnectionState.Connected, ConnectionStatus.Current.State);

        var down = new Handler((_, _) => (HttpStatusCode.ServiceUnavailable, ""));
        string failed = await ConnectionService.TestMyApiAsync(Good(), Env("t"), down);
        Assert.StartsWith("Connection failed", failed);
        Assert.Equal(ConnectionState.Failed, ConnectionStatus.Current.State);

        string notSetUp = await ConnectionService.TestMyApiAsync(Good(), Env(null), handler);
        Assert.Contains("is not set", notSetUp);
        ConnectionStatus.Configure(null);
    }

    [Fact]
    public void The_provider_choice_in_Settings_wins_over_the_environment_and_a_secret_is_never_saved()
    {
        var env = (string n) => n == "ANTHROPIC_API_KEY" ? "k" : n == "MY_TEST_TOKEN" ? "t0k3n" : null;
        Assert.Null(ChatCompleterFactory.Create(env, null, new Settings { Provider = ProviderChoice.Offline }));
        Assert.IsType<AnthropicChatCompleter>(ChatCompleterFactory.Create(env, null, new Settings { Provider = ProviderChoice.Claude }));
        Assert.Null(ChatCompleterFactory.Create(n => null, null, new Settings { Provider = ProviderChoice.Claude }));
        var mine = ChatCompleterFactory.Create(env, null, new Settings { Provider = ProviderChoice.MyApi, CustomApi = Good() });
        Assert.StartsWith("Division API", mine!.ProviderName);
        Assert.Null(ChatCompleterFactory.Create(env, null, new Settings { Provider = ProviderChoice.MyApi }));   // nothing filled in: offline, not a guess

        string json = JsonSerializer.Serialize(new Settings { Provider = ProviderChoice.MyApi, CustomApi = Good() });
        Assert.DoesNotContain("t0k3n", json);
        Assert.Contains("MY_TEST_TOKEN", json);                                          // only the variable's NAME is stored
    }

    [Fact]
    public void Settings_round_trip_keeps_the_new_fields_and_repairs_bad_ones()
    {
        string path = Path.Combine(Temp.NewDir(), "settings.json");
        var s = new Settings { Provider = ProviderChoice.MyApi, CustomApi = Good(), Tone = "Warm", SignOff = "Thanks, Pat", MiddleClickTrigger = true, MiddleClickModifier = "Shift", ReferenceDataUrl = "https://x.example/f.zip" };
        SettingsService.Save(s, path);
        var back = SettingsService.Load(path);
        Assert.Equal(ProviderChoice.MyApi, back.Provider);
        Assert.Equal("Division API", back.CustomApi.Name);
        Assert.Equal("Warm", back.Tone); Assert.Equal("Thanks, Pat", back.SignOff);
        Assert.True(back.MiddleClickTrigger); Assert.Equal("Shift", back.MiddleClickModifier);

        File.WriteAllText(path, "{\"Tone\":\"Sarcastic\",\"Provider\":99,\"MiddleClickModifier\":\"Hyper\",\"CustomApi\":{\"RequestTemplate\":\"\"}}");
        var fixedUp = SettingsService.Load(path);
        Assert.Equal("", fixedUp.Tone); Assert.Equal(ProviderChoice.Auto, fixedUp.Provider); Assert.Equal("Ctrl", fixedUp.MiddleClickModifier);
        Assert.Equal(CustomApiSettings.ExampleTemplate, fixedUp.CustomApi.RequestTemplate);
        Assert.False(fixedUp.MiddleClickTrigger);                                        // off unless chosen
    }

    [Fact]
    public void Tone_style_and_sign_off_become_one_instruction_block_for_the_writing_actions()
    {
        var s = new Settings { Tone = "Plain", StandingInstructions = "Under 120 words.", SignOff = "Thanks, Pat" };
        string text = s.WritingInstructions();
        Assert.Contains("Tone: plain.", text);
        Assert.Contains("Under 120 words.", text);
        Assert.Contains("sign-off exactly: Thanks, Pat", text);
        Assert.Equal("", new Settings().WritingInstructions());
        var req = WritingPrompts.Build(WritingAction.Rewrite, "draft", text, null, Array.Empty<AndaRecord>());
        Assert.Contains("<instructions>", req.User);
        Assert.Contains("Do not change any number", WritingPrompts.SystemFor(WritingAction.Proofread));
    }
}

public class QuickReplyTests
{
    private static AndaRecord Rec => new MockAndaRepository().Find("216482")!;
    private static readonly DateOnly Today = new(2026, 11, 2);

    [Fact]
    public void Placeholders_are_filled_from_the_card_and_nothing_else()
    {
        string text = QuickReplyFill.Fill("ANDA {number} ({drug}) from {applicant}, {cycle}: goal {goal}, forecast {forecast} ({delta}). {attention} Next: {next}. As of {today}.", Rec, Today);
        Assert.Contains("ANDA 216482 (Metformin HCl ER Tablets, 500 mg) from Meridian Pharmaceuticals (fictional), Cycle 2", text);
        Assert.Contains("goal Oct 3, 2026, forecast Oct 17, 2026 (+14 d vs goal)", text);
        Assert.Contains("IR response due in 6 days", text);
        Assert.Contains("As of Nov 2, 2026.", text);
        Assert.Equal(StatusLine.Build(Rec), QuickReplyFill.Fill("{status}", Rec, Today));
    }

    [Fact]
    public void A_value_the_data_does_not_have_is_said_and_never_invented()
    {
        var bare = Rec with { ForecastDate = AndaRecord.Unknown, ProgressPct = -1, Attention = null };
        string text = QuickReplyFill.Fill("{forecast} / {progress} / {attention} / {unknown}", bare, Today);
        Assert.Equal("(not in my data) / (not in my data) / no open action items in the record / {unknown}", text);
    }

    [Fact]
    public void The_psg_and_rld_placeholders_use_the_same_two_lines_as_the_card()
    {
        string t = QuickReplyFill.Fill("{psg} | {rld}", Rec with { Demo = false }, Today);
        Assert.Equal(ReferenceResolver.NoPsg + " | " + ReferenceResolver.NoRld, t);
        Assert.Contains("fictional demo date", QuickReplyFill.Fill("{psg}", Rec, Today));
    }

    [Fact]
    public void The_person_can_add_replace_delete_and_the_file_survives_a_restart()
    {
        string path = Path.Combine(Temp.NewDir(), "replies.json");
        var svc = new QuickReplyService(path);
        Assert.Equal(QuickReplyService.Starters.Count, svc.Replies.Count);       // starters on first use
        Assert.Null(svc.Upsert(new("Tell my lead", "Hi {number}")));
        Assert.Null(svc.Upsert(new("tell my lead", "Hi again {number}")));         // same name, any case: replaced
        Assert.Equal("Hi again {number}", svc.Replies.Single(r => r.Name == "tell my lead").Text);
        svc.Delete("Short status");
        var again = new QuickReplyService(path);
        Assert.DoesNotContain(again.Replies, r => r.Name == "Short status");
        Assert.Contains(again.Replies, r => r.Name == "tell my lead");
    }

    [Fact]
    public void Limits_are_enforced_with_a_plain_reason()
    {
        var svc = new QuickReplyService(Path.Combine(Temp.NewDir(), "r.json"));
        Assert.Contains("name", svc.Upsert(new(" ", "x"))!);
        Assert.Contains("characters", svc.Upsert(new(new string('n', 61), "x"))!);
        Assert.Contains("characters", svc.Upsert(new("ok", new string('t', QuickReplyService.MaxTextChars + 1)))!);
        for (int i = svc.Replies.Count; i < QuickReplyService.MaxReplies; i++) Assert.Null(svc.Upsert(new($"r{i}", "x")));
        Assert.Contains("up to", svc.Upsert(new("one too many", "x"))!);
    }

    [Fact]
    public void A_corrupt_replies_file_falls_back_to_the_starters()
    {
        string path = Path.Combine(Temp.NewDir(), "r.json"); File.WriteAllText(path, "{not json");
        Assert.Equal(QuickReplyService.Starters.Count, new QuickReplyService(path).Replies.Count);
    }
}

public class MouseTriggerRuleTests
{
    [Theory]
    [InlineData(true, "Ctrl", true, false, false, false, true)]
    [InlineData(true, "Ctrl", false, false, false, false, false)]     // a plain middle-click is left alone when Ctrl is required
    [InlineData(true, "Ctrl", true, true, false, false, false)]       // Ctrl+Shift+middle-click belongs to someone else
    [InlineData(true, "None", false, false, false, false, true)]
    [InlineData(true, "None", true, false, false, false, false)]
    [InlineData(true, "Shift", false, true, false, false, true)]
    [InlineData(true, "Alt", false, false, true, false, true)]
    [InlineData(false, "Ctrl", true, false, false, false, false)]     // off means off
    [InlineData(true, "Ctrl", true, false, false, true, false)]       // a click simulated by another program is ignored
    public void The_middle_click_starts_a_peek_only_when_on_and_exactly_the_chosen_key_is_held(bool on, string mod, bool ctrl, bool shift, bool alt, bool injected, bool expected) =>
        Assert.Equal(expected, MouseTriggerRules.ShouldTrigger(on, mod, ctrl, shift, alt, injected));

    [Fact]
    public void Only_the_self_test_may_accept_a_simulated_click() =>
        Assert.True(MouseTriggerRules.ShouldTrigger(true, "None", false, false, false, injected: true, allowInjected: true));

    [Fact]
    public void Off_by_default_and_ctrl_when_it_is_turned_on()
    {
        var s = new Settings();
        Assert.False(s.MiddleClickTrigger);
        Assert.Equal("Ctrl", s.MiddleClickModifier);
    }

    [Fact]
    public void Off_Windows_the_service_says_so_plainly_instead_of_failing()
    {
        if (OperatingSystem.IsWindows()) return;
        using var svc = new MouseTriggerService(() => "Ctrl", () => { });
        Assert.False(svc.Start());
        Assert.Contains("needs Windows", svc.Problem);
    }
}

public class ReferenceUpdateTests
{
    private const string P = "ApplNo\tProductNo\tForm\tStrength\tReferenceDrug\tDrugName\tActiveIngredient\tReferenceStandard\n021574\t001\tTABLET, EXTENDED RELEASE;ORAL\t500MG\t1\tFICTIONAL-ER\tMETFORMIN HYDROCHLORIDE\t0\n";
    private const string C = "SubmissionClassCodeID\tSubmissionClassCode\tSubmissionClassCodeDescription\n1\tLABELING\tLabeling\n";
    private const string S = "ApplNo\tSubmissionClassCodeID\tSubmissionType\tSubmissionNo\tSubmissionStatus\tSubmissionStatusDate\n021574\t1\tSUPPL\t11\tAP\t2025-09-17 00:00:00\n";

    private static byte[] ZipBytes()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Put(string n, string t) { var e = zip.CreateEntry(n); using var w = new StreamWriter(e.Open(), Encoding.Latin1); w.Write(t); }
            Put("Products.txt", P); Put("Submissions.txt", S); Put("SubmissionClass_Lookup.txt", C);
        }
        return ms.ToArray();
    }

    private sealed class Serve(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) { Calls++; return Task.FromResult(respond()); }
    }

    [Fact]
    public async Task Update_replaces_the_file_only_with_a_real_Drugs_at_FDA_file()
    {
        string dest = Path.Combine(Temp.NewDir(), "drugsfda.zip");
        var ok = new Serve(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ZipBytes()) });
        var (good, message) = await ReferenceDownloader.UpdateAsync("https://files.example.test/drugsfda.zip", dest, ok);
        Assert.True(good, message);
        Assert.Contains("1 reference products", message);
        Assert.NotNull(DrugsAtFdaFile.TryLoad(dest, out _));
        long size = new FileInfo(dest).Length;

        var page = new Serve(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>not a zip</html>") });
        var (bad, why) = await ReferenceDownloader.UpdateAsync("https://files.example.test/page", dest, page);
        Assert.False(bad);
        Assert.Contains("did not give a Drugs@FDA data file", why);
        Assert.Equal(size, new FileInfo(dest).Length);                      // the earlier file is untouched
        Assert.False(File.Exists(dest + ".part"));
    }

    [Theory]
    [InlineData("", "paste the address")]
    [InlineData("http://files.example.test/a.zip", "https://")]
    [InlineData("https://u:p@files.example.test/a.zip", "user name or password")]
    [InlineData("not a url", "https://")]
    public async Task A_bad_address_fetches_nothing(string url, string expected)
    {
        var h = new Serve(() => new HttpResponseMessage(HttpStatusCode.OK));
        var (ok, message) = await ReferenceDownloader.UpdateAsync(url, Path.Combine(Temp.NewDir(), "x.zip"), h);
        Assert.False(ok);
        Assert.Contains(expected, message);
        Assert.Equal(0, h.Calls);
    }

    [Fact]
    public async Task A_server_error_leaves_the_earlier_file_alone_and_says_so()
    {
        var h = new Serve(() => new HttpResponseMessage(HttpStatusCode.NotFound));
        var (ok, message) = await ReferenceDownloader.UpdateAsync("https://files.example.test/a.zip", Path.Combine(Temp.NewDir(), "x.zip"), h);
        Assert.False(ok);
        Assert.Contains("404", message);
        Assert.Contains("unchanged", message);
    }
}

public class ReferenceLinesTests
{
    [Fact]
    public void The_resolver_always_gives_two_lines_and_a_brief_reason_when_there_is_no_date()
    {
        var real = new MockAndaRepository().Find("216482")! with { Demo = false };
        var none = ReferenceResolver.For(real, null, "", null, loading: false);
        Assert.Equal(ReferenceResolver.NoPsg, none.PsgText);
        Assert.Equal(ReferenceResolver.NoRld, none.RldText);

        var broken = ReferenceResolver.For(real, null, "The Drugs@FDA file could not be read.", "C:\\x.zip", loading: false);
        Assert.StartsWith("not available: ", broken.RldText);
        var reading = ReferenceResolver.For(real, null, "", "C:\\x.zip", loading: true);
        Assert.Equal("reading the Drugs@FDA file…", reading.RldText);

        var withSheet = ReferenceResolver.For(real with { PsgDateFromSheet = new DateOnly(2026, 8, 21) }, null, "", null, false);
        Assert.Equal("Aug 21, 2026 · from your sheet, not an official source", withSheet.PsgText);
    }

    [Fact]
    public void The_card_model_always_carries_both_lines()
    {
        var ctx = new CardContext(new DateOnly(2026, 11, 2), new BusinessCalendar());
        foreach (var n in new[] { "216482", "214990" })
        {
            var card = CardModels.Build(new MockAndaRepository().Find(n)!, ctx);
            Assert.EndsWith("· fictional demo date", card.Reference.Psg);
            Assert.EndsWith("· fictional demo date", card.Reference.Rld);
        }
        var real = CardModels.Build(new MockAndaRepository().Find("216482")! with { Demo = false }, ctx);
        Assert.StartsWith("not available:", real.Reference.Psg);
        Assert.StartsWith("not available:", real.Reference.Rld);
    }
}
