using System.Net;
using AndaCompanion.Services;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// The Elsa placeholder: every configuration state, the safety guards, and the plumbing around a mapping —
/// proven with a fake HTTP handler, so no network is touched. When the real mapping is written, these tests keep
/// protecting everything around it; only <see cref="UnfilledElsaMapping"/> expectations are about the placeholder.
/// </summary>
public class ElsaTests
{
    private const string Good = "https://elsa.example.gov/api/chat";

    private static Func<string, string?> Env(params (string K, string V)[] kv) =>
        k => kv.Where(p => p.K == k).Select(p => p.V).FirstOrDefault();

    private static readonly (string, string) Elsa = ("ANDA_COMPANION_PROVIDER", "elsa");

    private sealed class FilledMapping : IElsaMapping
    {
        public bool IsFilledIn => true;
        public HttpRequestMessage BuildRequest(ElsaSettings s, string system, string user, int maxTokens)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, s.Endpoint)
            {
                Content = new StringContent($"{{\"system\":{System.Text.Json.JsonSerializer.Serialize(system)},\"max\":{maxTokens}}}"),
            };
            if (s.Token is not null) req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + s.Token);
            return req;
        }
        public string ReadReply(string body) => body.Trim('"');
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Seen { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    [Fact]
    public void Not_selected_by_default()
    {
        var r = ElsaConfig.Inspect(Env(), new UnfilledElsaMapping());
        Assert.Equal(ElsaStatus.NotSelected, r.Status);
        Assert.False(ElsaConfig.IsSelected(Env()));
        Assert.True(ElsaConfig.IsSelected(Env(("ANDA_COMPANION_PROVIDER", " ELSA "))));
    }

    [Fact]
    public void A_missing_endpoint_is_named_in_the_message()
    {
        var r = ElsaConfig.Inspect(Env(Elsa), new UnfilledElsaMapping());
        Assert.Equal(ElsaStatus.MissingEndpoint, r.Status);
        Assert.Contains("ANDA_ELSA_ENDPOINT", r.Message);
    }

    [Theory]
    [InlineData("http://elsa.example.gov/api")]
    [InlineData("ftp://elsa.example.gov/api")]
    [InlineData("not a url")]
    [InlineData("//elsa.example.gov/api")]
    public void Anything_but_https_is_refused_so_a_token_never_travels_in_clear(string endpoint)
    {
        var r = ElsaConfig.Inspect(Env(Elsa, (ElsaConfig.EndpointVar, endpoint), (ElsaConfig.TokenVar, "tok")), new FilledMapping());
        Assert.Equal(ElsaStatus.InsecureEndpoint, r.Status);
        Assert.Null(r.Settings);
    }

    [Fact]
    public void Plain_http_is_accepted_only_for_a_local_test_server()
    {
        var r = ElsaConfig.Inspect(Env(Elsa, (ElsaConfig.EndpointVar, "http://localhost:5000/chat")), new FilledMapping());
        Assert.Equal(ElsaStatus.Ready, r.Status);
    }

    [Fact]
    public void An_unfilled_mapping_reports_the_placeholder_and_keeps_the_app_offline()
    {
        var env = Env(Elsa, (ElsaConfig.EndpointVar, Good), (ElsaConfig.TokenVar, "tok"));
        var r = ElsaConfig.Inspect(env, new UnfilledElsaMapping());
        Assert.Equal(ElsaStatus.MappingNotFilled, r.Status);
        Assert.Contains("ELSA-INTEGRATION.md", r.Message);
        Assert.Null(ChatCompleterFactory.Create(env, new UnfilledElsaMapping()));
    }

    [Fact]
    public void A_filled_mapping_makes_the_factory_return_an_Elsa_completer()
    {
        var env = Env(Elsa, (ElsaConfig.EndpointVar, Good), (ElsaConfig.ModelVar, "m1"), (ElsaConfig.AgentVar, "agent-x"));
        var c = ChatCompleterFactory.Create(env, new FilledMapping());
        Assert.IsType<ElsaChatCompleter>(c);
        Assert.Equal("Elsa · m1", c!.ProviderName);
        var r = ElsaConfig.Inspect(env, new FilledMapping());
        Assert.Equal(ElsaStatus.Ready, r.Status);
        Assert.Equal("agent-x", r.Settings!.Agent);
        Assert.Null(r.Settings.Token);                            // the token is optional (integrated auth may be used)
    }

    [Fact]
    public void The_token_never_appears_in_any_message()
    {
        const string secret = "S3CRET-TOKEN-VALUE";
        foreach (var mapping in new IElsaMapping[] { new UnfilledElsaMapping(), new FilledMapping() })
        {
            var r = ElsaConfig.Inspect(Env(Elsa, (ElsaConfig.EndpointVar, Good), (ElsaConfig.TokenVar, secret)), mapping);
            Assert.DoesNotContain(secret, r.Message);
            Assert.DoesNotContain(secret, r.Settings!.ToString() ?? "", StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_placeholder_mapping_throws_a_specific_error_not_a_guess()
    {
        var m = new UnfilledElsaMapping();
        Assert.False(m.IsFilledIn);
        var ex = Assert.Throws<ElsaNotMappedException>(() => m.BuildRequest(new ElsaSettings(Good, null, null, null), "s", "u", 10));
        Assert.Contains("ELSA-INTEGRATION.md", ex.Message);
        Assert.Throws<ElsaNotMappedException>(() => m.ReadReply("{}"));
        Assert.False(ElsaMapping.Current.IsFilledIn);
    }

    [Fact]
    public async Task The_completer_sends_what_the_mapping_builds_and_returns_what_it_reads()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("\"Hello from the fake Elsa\"") });
        using var c = new ElsaChatCompleter(new ElsaSettings(Good, "tok", null, null), new FilledMapping(), handler);
        string text = await c.CompleteAsync("sys", "user", 123, CancellationToken.None);
        Assert.Equal("Hello from the fake Elsa", text);
        var sent = Assert.Single(handler.Seen);
        Assert.Equal(Good, sent.RequestUri!.ToString());
        Assert.Equal("Bearer tok", sent.Headers.GetValues("Authorization").Single());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task An_error_status_becomes_a_short_exception_without_the_body(HttpStatusCode status)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(status) { Content = new StringContent("PRIVATE DETAIL tok-123") });
        using var c = new ElsaChatCompleter(new ElsaSettings(Good, "tok-123", null, null), new FilledMapping(), handler);
        var ex = await Assert.ThrowsAsync<ElsaException>(() => c.CompleteAsync("s", "u", 10, CancellationToken.None));
        Assert.Contains(((int)status).ToString(), ex.Message);
        Assert.DoesNotContain("PRIVATE", ex.Message);
        Assert.DoesNotContain("tok-123", ex.Message);
    }

    [Fact]
    public async Task A_brief_falls_back_to_offline_when_Elsa_fails()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var c = new ElsaChatCompleter(new ElsaSettings(Good, "t", null, null), new FilledMapping(), handler);
        var record = new AndaCompanion.Data.MockAndaRepository().Find("216482")!;
        var svc = new GeneratedBriefService(c, new OfflineBriefService());
        var brief = await svc.GetBriefAsync(record);
        Assert.False(brief.Generated);
        Assert.False(string.IsNullOrWhiteSpace(brief.Text));
    }

    [Fact]
    public async Task A_brief_is_generated_through_the_seam_when_Elsa_answers()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("\"Two sentences. Grounded.\"") });
        using var c = new ElsaChatCompleter(new ElsaSettings(Good, "t", null, null), new FilledMapping(), handler);
        var record = new AndaCompanion.Data.MockAndaRepository().Find("216482")!;
        var brief = await new GeneratedBriefService(c, new OfflineBriefService()).GetBriefAsync(record);
        Assert.True(brief.Generated);
        Assert.Equal("Two sentences. Grounded.", brief.Text);
    }

    [Fact]
    public async Task Test_button_text_explains_each_state_and_proves_a_working_channel()
    {
        Assert.Contains("ANDA_ELSA_ENDPOINT", await ConnectionService.TestElsaAsync(Env(Elsa), new UnfilledElsaMapping()));
        Assert.Contains("placeholder", await ConnectionService.TestElsaAsync(Env(Elsa, (ElsaConfig.EndpointVar, Good)), new UnfilledElsaMapping()));
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("\"OK\"") });
        string ok = await ConnectionService.TestElsaAsync(Env(Elsa, (ElsaConfig.EndpointVar, Good)), new FilledMapping(), handler);
        Assert.StartsWith("Connected · Elsa", ok);
        string bad = await ConnectionService.TestElsaAsync(Env(Elsa, (ElsaConfig.EndpointVar, Good)), new FilledMapping(),
            new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));
        Assert.StartsWith("Connection failed", bad);
        Assert.Contains("403", bad);
    }

    [Fact]
    public void Describe_is_provider_aware()
    {
        Assert.Contains("Offline mode", ConnectionService.Describe(Env()));
        Assert.Contains("Key detected", ConnectionService.Describe(Env(("ANTHROPIC_API_KEY", "x"))));
        Assert.Contains("ANDA_ELSA_ENDPOINT", ConnectionService.Describe(Env(Elsa), new UnfilledElsaMapping()));
    }

    [Fact]
    public void The_self_test_check_for_the_placeholder_passes()
    {
        var r = SelfTestChecks.ElsaPlaceholder();
        Assert.True(r.Status == CheckStatus.Pass, r.Detail);
    }
}
