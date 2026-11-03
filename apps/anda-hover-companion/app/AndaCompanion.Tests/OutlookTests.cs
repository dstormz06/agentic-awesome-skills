using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using AndaCompanion.Data;
using AndaCompanion.Models;
using AndaCompanion.Services;
using AndaCompanion.Theming;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>A stand-in for Microsoft's servers. Records every request so a test can prove what was (and was not) sent.</summary>
internal sealed class FakeMicrosoft : HttpMessageHandler
{
    public readonly List<(HttpMethod Method, string Url, string Body, string? Auth)> Requests = new();
    public Func<HttpRequestMessage, string, HttpResponseMessage> Respond = (_, _) => new HttpResponseMessage(HttpStatusCode.NotFound);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        Requests.Add((request.Method, request.RequestUri!.ToString(), body, request.Headers.Authorization?.ToString()));
        return Respond(request, body);
    }

    public static HttpResponseMessage Json(HttpStatusCode code, string json) => new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

public class OutlookTests
{
    private const string Client = "12345678-1234-1234-1234-123456789abc";
    private static readonly DateTimeOffset T0 = new(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);

    private static OutlookSettings On(OutlookCloud cloud = OutlookCloud.Commercial) => new() { Enabled = true, ClientId = Client, Cloud = cloud };

    private static (OutlookService Svc, FakeMicrosoft Fake, OutlookSettings S) Make(Action<FakeMicrosoft>? setup = null, OutlookSettings? s = null)
    {
        var fake = new FakeMicrosoft();
        setup?.Invoke(fake);
        s ??= On();
        return (new OutlookService(() => s, fake, (_, _) => Task.CompletedTask, () => T0), fake, s);
    }

    private static HttpResponseMessage DeviceCode() => FakeMicrosoft.Json(HttpStatusCode.OK,
        "{\"device_code\":\"DEV\",\"user_code\":\"ABCD1234\",\"verification_uri\":\"https://microsoft.com/devicelogin\",\"expires_in\":900,\"interval\":5}");

    // ---------------- settings ----------------

    [Fact]
    public void Outlook_is_off_by_default_and_a_damaged_file_is_repaired()
    {
        Assert.False(new Settings().Outlook.Enabled);
        var s = OutlookSettings.Normalize(new OutlookSettings { MaxItems = 99, TenantId = "  ", ClientId = $"  {Client} ", Cloud = (OutlookCloud)7 });
        Assert.Equal((5, "organizations", Client, OutlookCloud.Commercial), (s.MaxItems, s.TenantId, s.ClientId, s.Cloud));
        Assert.NotNull(OutlookSettings.Normalize(null));

        string path = Path.Combine(Temp.NewDir(), "s.json");
        File.WriteAllText(path, "{\"Outlook\":null}");
        Assert.NotNull(SettingsService.Load(path).Outlook);
    }

    [Theory]
    [InlineData("", false)] [InlineData("not-a-guid", false)] [InlineData(Client, true)] [InlineData("{12345678-1234-1234-1234-123456789abc}", false)] [InlineData("12345678123412341234123456789abc", false)]
    public void The_application_id_must_look_like_one(string id, bool ok) =>
        Assert.Equal(ok, OutlookService.ValidateSetup(new OutlookSettings { Enabled = true, ClientId = id }) is null);

    [Theory]
    [InlineData("organizations", true)] [InlineData("contoso.onmicrosoft.com", true)] [InlineData("11111111-2222-3333-4444-555555555555", true)]
    [InlineData("bad tenant", false)] [InlineData("a/b", false)] [InlineData("../x", false)]
    public void The_directory_is_a_plain_name_so_it_cannot_change_the_address(string tenant, bool ok) =>
        Assert.Equal(ok, OutlookService.ValidateSetup(new OutlookSettings { Enabled = true, ClientId = Client, TenantId = tenant }) is null);

    [Fact]
    public async Task An_incomplete_setup_makes_no_request_and_says_what_is_missing()
    {
        var (svc, fake, s) = Make(s: new OutlookSettings { Enabled = true, ClientId = "" });
        await svc.SignInAsync();
        Assert.Empty(fake.Requests);
        Assert.Equal(OutlookState.NotSetUp, svc.Snapshot.State);
        Assert.Contains("Application (client) ID", svc.Snapshot.Message);
        s.Enabled = false;
        Assert.Equal(OutlookState.Off, svc.Snapshot.State);
    }

    [Fact]
    public void Only_Microsoft_addresses_are_ever_opened()
    {
        foreach (string ok in new[] { "https://microsoft.com/devicelogin", "https://www.microsoft.com/link", "https://login.microsoftonline.com/x", "https://microsoft.com/deviceloginus", "https://login.microsoftonline.us/x" })
            Assert.True(OutlookService.IsMicrosoftAddress(ok), ok);
        foreach (string bad in new[] { "http://microsoft.com/devicelogin", "https://evil.example/microsoft.com", "https://microsoft.com.evil.example/", "https://notmicrosoft.com/", "file:///c:/x", "javascript:alert(1)", "", null! })
            Assert.False(OutlookService.IsMicrosoftAddress(bad), bad ?? "null");
    }

    // ---------------- sign-in ----------------

    [Fact]
    public async Task Device_sign_in_waits_while_pending_then_signs_in_and_never_stores_the_token()
    {
        int polls = 0;
        var (svc, fake, s) = Make(f => f.Respond = (req, body) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/devicecode")) return DeviceCode();
            return ++polls < 3
                ? FakeMicrosoft.Json(HttpStatusCode.BadRequest, "{\"error\":\"authorization_pending\"}")
                : FakeMicrosoft.Json(HttpStatusCode.OK, "{\"access_token\":\"SECRET-TOKEN\",\"expires_in\":3600}");
        });
        var seen = new List<OutlookState>();
        svc.Changed += () => seen.Add(svc.Snapshot.State);
        await svc.SignInAsync();

        Assert.Equal(OutlookState.SignedIn, svc.Snapshot.State);
        Assert.Contains(OutlookState.SigningIn, seen);
        Assert.Equal(4, fake.Requests.Count);                                   // one code request, three polls
        Assert.StartsWith("https://login.microsoftonline.com/organizations/oauth2/v2.0/devicecode", fake.Requests[0].Url);
        Assert.Contains("client_id=" + Client, fake.Requests[0].Body);
        Assert.Contains("Mail.Read", Uri.UnescapeDataString(fake.Requests[0].Body));
        Assert.DoesNotContain("Send", fake.Requests[0].Body);                    // read-only, never a send or write permission
        Assert.Contains("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code", fake.Requests[1].Body);

        // The token is in memory only: nothing the app saves or exports can contain it.
        string saved = JsonSerializer.Serialize(new Settings { Outlook = s });
        Assert.DoesNotContain("SECRET-TOKEN", saved);
        var dir = Temp.NewDir();
        string backup = SettingsBackup.Export(new Settings { Outlook = s }, new PromptLibraryService(Path.Combine(dir, "p.json")), new QuickReplyService(Path.Combine(dir, "r.json")));
        Assert.DoesNotContain("SECRET-TOKEN", backup);
        Assert.DoesNotContain("DEV", backup.Replace("DEVICE", ""));              // nor the device code
    }

    [Fact]
    public async Task The_code_and_address_are_shown_while_waiting()
    {
        OutlookSnapshot? waiting = null;
        OutlookService? svc = null;
        var fake = new FakeMicrosoft
        {
            Respond = (req, _) => req.RequestUri!.AbsolutePath.EndsWith("/devicecode") ? DeviceCode() : FakeMicrosoft.Json(HttpStatusCode.OK, "{\"access_token\":\"t\",\"expires_in\":3600}"),
        };
        var s = On();
        svc = new OutlookService(() => s, fake, (_, _) => { waiting ??= svc!.Snapshot; return Task.CompletedTask; }, () => T0);
        await svc.SignInAsync();
        Assert.Equal(OutlookState.SigningIn, waiting!.State);
        Assert.Equal("ABCD1234", waiting.UserCode);
        Assert.Equal("https://microsoft.com/devicelogin", waiting.VerificationUrl);
        Assert.Contains("ABCD1234", waiting.Message);
    }

    [Theory]
    [InlineData("authorization_declined", "declined")]
    [InlineData("expired_token", "expired")]
    public async Task Declined_or_expired_codes_end_signed_out_with_a_plain_reason(string error, string word)
    {
        var (svc, _, _) = Make(f => f.Respond = (req, _) => req.RequestUri!.AbsolutePath.EndsWith("/devicecode")
            ? DeviceCode() : FakeMicrosoft.Json(HttpStatusCode.BadRequest, "{\"error\":\"" + error + "\"}"));
        await svc.SignInAsync();
        Assert.Equal(OutlookState.SignedOut, svc.Snapshot.State);
        Assert.Contains(word, svc.Snapshot.Message);
    }

    [Fact]
    public async Task An_unknown_application_or_a_blocked_one_is_an_error_that_blames_nothing_hidden()
    {
        var (svc, _, _) = Make(f => f.Respond = (_, _) => FakeMicrosoft.Json(HttpStatusCode.BadRequest, "{\"error\":\"invalid_request\",\"error_description\":\"AADSTS700016 secret detail\"}"));
        await svc.SignInAsync();
        Assert.Equal(OutlookState.Error, svc.Snapshot.State);
        Assert.Contains("Application ID", svc.Snapshot.Message);
        Assert.DoesNotContain("AADSTS", svc.Snapshot.Message);

        var (svc2, _, _) = Make(f => f.Respond = (req, _) => req.RequestUri!.AbsolutePath.EndsWith("/devicecode")
            ? DeviceCode() : FakeMicrosoft.Json(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\",\"error_description\":\"AADSTS65001 consent\"}"));
        await svc2.SignInAsync();
        Assert.Equal(OutlookState.Error, svc2.Snapshot.State);
        Assert.Contains("approve", svc2.Snapshot.Message);
        Assert.DoesNotContain("AADSTS", svc2.Snapshot.Message);
    }

    [Fact]
    public async Task A_network_failure_or_an_unreadable_reply_is_one_plain_sentence()
    {
        var (a, _, _) = Make(f => f.Respond = (_, _) => throw new HttpRequestException("boom 10.1.2.3"));
        await a.SignInAsync();
        Assert.Equal(OutlookState.Error, a.Snapshot.State);
        Assert.DoesNotContain("10.1.2.3", a.Snapshot.Message);

        var (b, _, _) = Make(f => f.Respond = (_, _) => FakeMicrosoft.Json(HttpStatusCode.OK, "<html>proxy login</html>"));
        await b.SignInAsync();
        Assert.Equal(OutlookState.Error, b.Snapshot.State);
    }

    [Fact]
    public async Task The_US_Government_cloud_uses_its_own_addresses_and_commercial_never_does()
    {
        var (gov, fg, _) = Make(f => f.Respond = (req, _) => req.RequestUri!.AbsolutePath.EndsWith("/devicecode")
            ? DeviceCode() : FakeMicrosoft.Json(HttpStatusCode.OK, "{\"access_token\":\"t\",\"expires_in\":3600}"), On(OutlookCloud.UsGovernment));
        await gov.SignInAsync();
        await gov.SearchAsync("216482");
        Assert.All(fg.Requests, r => Assert.True(r.Url.StartsWith("https://login.microsoftonline.us/") || r.Url.StartsWith("https://graph.microsoft.us/"), r.Url));
        Assert.Contains("graph.microsoft.us", Uri.UnescapeDataString(fg.Requests[0].Body));

        var (com, fc, _) = Make(f => f.Respond = (req, _) => req.RequestUri!.AbsolutePath.EndsWith("/devicecode")
            ? DeviceCode() : FakeMicrosoft.Json(HttpStatusCode.OK, "{\"access_token\":\"t\",\"expires_in\":3600}"));
        await com.SignInAsync();
        await com.SearchAsync("216482");
        Assert.All(fc.Requests, r => Assert.True(r.Url.StartsWith("https://login.microsoftonline.com/") || r.Url.StartsWith("https://graph.microsoft.com/"), r.Url));
    }

    [Fact]
    public async Task Sign_out_forgets_the_token_and_cancels_a_sign_in_in_progress()
    {
        var (svc, fake, _) = Make(f => f.Respond = (req, _) => req.RequestUri!.AbsolutePath.EndsWith("/devicecode")
            ? DeviceCode() : FakeMicrosoft.Json(HttpStatusCode.OK, "{\"access_token\":\"t\",\"expires_in\":3600}"));
        await svc.SignInAsync();
        Assert.Equal(OutlookState.SignedIn, svc.Snapshot.State);
        svc.SignOut();
        Assert.Equal(OutlookState.SignedOut, svc.Snapshot.State);
        fake.Requests.Clear();
        var r = await svc.SearchAsync("216482");
        Assert.False(r.Ok);
        Assert.Empty(fake.Requests);                                           // no token, so no request at all

        // Sign out while waiting for the code ends the wait.
        var cts = new CancellationTokenSource();
        var s = On();
        var waiting = new OutlookService(() => s, new FakeMicrosoft { Respond = (req, _) => req.RequestUri!.AbsolutePath.EndsWith("/devicecode") ? DeviceCode() : FakeMicrosoft.Json(HttpStatusCode.BadRequest, "{\"error\":\"authorization_pending\"}") },
            (_, ct) => Task.Delay(Timeout.Infinite, ct), () => T0);
        var task = waiting.SignInAsync();
        await Task.Delay(50);
        Assert.Equal(OutlookState.SigningIn, waiting.Snapshot.State);
        waiting.SignOut();
        await task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(OutlookState.SignedOut, waiting.Snapshot.State);
        Assert.Contains("Signed out", waiting.Snapshot.Message);
    }

    // ---------------- reading mail ----------------

    private static async Task<(OutlookService Svc, FakeMicrosoft Fake)> SignedIn(Func<HttpRequestMessage, HttpResponseMessage> mail, OutlookSettings? s = null)
    {
        var (svc, fake, _) = Make(f => f.Respond = (req, _) =>
            req.RequestUri!.Host.StartsWith("login") ? (req.RequestUri.AbsolutePath.EndsWith("/devicecode") ? DeviceCode() : FakeMicrosoft.Json(HttpStatusCode.OK, "{\"access_token\":\"TOK\",\"expires_in\":3600}")) : mail(req), s);
        await svc.SignInAsync();
        fake.Requests.Clear();
        return (svc, fake);
    }

    [Fact]
    public async Task Search_asks_for_one_six_digit_number_read_only_and_cleans_what_comes_back()
    {
        var (svc, fake) = await SignedIn(_ => FakeMicrosoft.Json(HttpStatusCode.OK, """
            {"value":[
              {"subject":"RE: 216482 \u2014 labeling\r\n  comments","from":{"emailAddress":{"name":"Pat Example","address":"pat@example.test"}},"receivedDateTime":"2026-11-01T14:30:00Z","bodyPreview":"Hi\u0000 team,\n\n  attached are the \u0007 comments."},
              {"subject":"","from":{"emailAddress":{"address":"only@example.test"}},"receivedDateTime":"not a date","bodyPreview":""},
              {"subject":"<b>x</b>","bodyPreview":"third"}
            ]}
            """));
        var r = await svc.SearchAsync("216482");
        Assert.True(r.Ok);
        Assert.Equal(3, r.Items.Count);
        Assert.Equal("RE: 216482 \u2014 labeling comments", r.Items[0].Subject);
        Assert.Equal("Pat Example", r.Items[0].From);
        Assert.Equal("Hi team, attached are the comments.", r.Items[0].Preview);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 14, 30, 0, TimeSpan.Zero), r.Items[0].Received);
        Assert.Equal(("(no subject)", "only@example.test", null), (r.Items[1].Subject, r.Items[1].From, r.Items[1].Received));
        Assert.Equal("<b>x</b>", r.Items[2].Subject);                           // plain text only; the window never interprets it

        var req = Assert.Single(fake.Requests);
        Assert.Equal(HttpMethod.Get, req.Method);                               // a read, never a write
        Assert.Equal("Bearer TOK", req.Auth);
        Assert.StartsWith("https://graph.microsoft.com/v1.0/me/messages?", req.Url);
        string query = Uri.UnescapeDataString(req.Url);
        Assert.Contains("$search=\"216482\"", query);
        Assert.Contains("$top=5", query);
        Assert.Contains("$select=subject,from,receivedDateTime,bodyPreview", query);
    }

    [Fact]
    public async Task Search_respects_the_number_of_messages_asked_for_and_repairs_a_bad_value()
    {
        var many = "{\"value\":[" + string.Join(",", Enumerable.Range(1, 12).Select(i => "{\"subject\":\"m" + i + "\"}")) + "]}";
        var (svc, fake) = await SignedIn(_ => FakeMicrosoft.Json(HttpStatusCode.OK, many), new OutlookSettings { Enabled = true, ClientId = Client, MaxItems = 3 });
        var r = await svc.SearchAsync("216482");
        Assert.Equal(3, r.Items.Count);                                          // even if a server sends more than asked
        Assert.Contains("$top=3", Uri.UnescapeDataString(fake.Requests[0].Url));

        var (svc2, fake2) = await SignedIn(_ => FakeMicrosoft.Json(HttpStatusCode.OK, many), new OutlookSettings { Enabled = true, ClientId = Client, MaxItems = 999 });
        Assert.Equal(5, (await svc2.SearchAsync("216482")).Items.Count);
        Assert.Contains("$top=5", Uri.UnescapeDataString(fake2.Requests[0].Url));
    }

    [Theory]
    [InlineData("")] [InlineData("12345")] [InlineData("1234567")] [InlineData("21648a")] [InlineData("216482 OR password")] [InlineData("\"216482\" subject:salary")] [InlineData("216482\n")] [InlineData("\u0662\u0661\u0666\u0664\u0668\u0662")]
    public async Task Anything_that_is_not_exactly_six_digits_is_never_searched(string text)
    {
        var (svc, fake) = await SignedIn(_ => FakeMicrosoft.Json(HttpStatusCode.OK, "{\"value\":[]}"));
        var r = await svc.SearchAsync(text);
        Assert.False(r.Ok);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task No_matches_is_an_answer_not_an_error()
    {
        var (svc, _) = await SignedIn(_ => FakeMicrosoft.Json(HttpStatusCode.OK, "{\"value\":[]}"));
        var r = await svc.SearchAsync("216482");
        Assert.True(r.Ok);
        Assert.Empty(r.Items);
        Assert.Equal("No messages mention 216482.", r.Message);
    }

    [Theory]
    [InlineData(403, "administrator")]
    [InlineData(429, "pause")]
    [InlineData(500, "500")]
    public async Task A_refusal_is_explained_in_a_sentence_without_the_server_text(int status, string word)
    {
        var (svc, _) = await SignedIn(_ => FakeMicrosoft.Json((HttpStatusCode)status, "{\"error\":{\"message\":\"internal tenant detail\"}}"));
        var r = await svc.SearchAsync("216482");
        Assert.False(r.Ok);
        Assert.Contains(word, r.Message);
        Assert.DoesNotContain("tenant detail", r.Message);
        Assert.Equal(OutlookState.SignedIn, svc.Snapshot.State);                // a refusal for one search does not sign anyone out
    }

    [Fact]
    public async Task An_expired_token_signs_out_and_asks_again()
    {
        var (svc, _) = await SignedIn(_ => FakeMicrosoft.Json(HttpStatusCode.Unauthorized, "{}"));
        var r = await svc.SearchAsync("216482");
        Assert.False(r.Ok);
        Assert.Equal(OutlookState.SignedOut, svc.Snapshot.State);
        Assert.Contains("sign in again", svc.Snapshot.Message);
    }

    [Fact]
    public async Task A_token_that_has_run_out_by_the_clock_is_not_used()
    {
        var now = T0;
        var fake = new FakeMicrosoft
        {
            Respond = (req, _) => req.RequestUri!.Host.StartsWith("login")
                ? (req.RequestUri.AbsolutePath.EndsWith("/devicecode") ? DeviceCode() : FakeMicrosoft.Json(HttpStatusCode.OK, "{\"access_token\":\"TOK\",\"expires_in\":600}"))
                : FakeMicrosoft.Json(HttpStatusCode.OK, "{\"value\":[]}"),
        };
        var s = On();
        var svc = new OutlookService(() => s, fake, (_, _) => Task.CompletedTask, () => now);
        await svc.SignInAsync();
        Assert.True((await svc.SearchAsync("216482")).Ok);
        now = T0.AddMinutes(11);
        fake.Requests.Clear();
        Assert.False((await svc.SearchAsync("216482")).Ok);
        Assert.Empty(fake.Requests);
        Assert.Equal(OutlookState.SignedOut, svc.Snapshot.State);
    }

    [Fact]
    public async Task Turning_Outlook_off_stops_every_request()
    {
        var (svc, fake) = await SignedIn(_ => FakeMicrosoft.Json(HttpStatusCode.OK, "{\"value\":[]}"));
        // the settings object is shared with the service, so the switch takes effect at once
        var field = typeof(OutlookService).GetField("_settings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        ((OutlookSettings)((Func<OutlookSettings>)field.GetValue(svc)!)()).Enabled = false;
        Assert.False((await svc.SearchAsync("216482")).Ok);
        Assert.Empty(fake.Requests);
    }

    [Theory]
    [InlineData("a\tb\r\nc   d", 99, "a b c d")]
    [InlineData("  x  ", 99, "x")]
    [InlineData("abcdefghij", 5, "abcd…")]
    [InlineData("a\u200Bb", 99, "a\u200Bb")]
    public void Clean_makes_one_short_plain_line(string text, int max, string expected) => Assert.Equal(expected, OutlookService.Clean(text, max));

    // ---------------- backup and reset ----------------

    [Fact]
    public void A_backup_carries_the_Outlook_choices_but_a_reset_turns_Outlook_off()
    {
        string dir = Temp.NewDir();
        var lenses = new PromptLibraryService(Path.Combine(dir, "p.json"));
        var replies = new QuickReplyService(Path.Combine(dir, "r.json"));
        var s = new Settings { Outlook = { Enabled = true, ClientId = Client, TenantId = "contoso.onmicrosoft.com", Cloud = OutlookCloud.UsGovernment, MaxItems = 10 } };
        string json = SettingsBackup.Export(s, lenses, replies);
        var target = new Settings();
        Assert.True(SettingsBackup.Import(json, target, lenses, replies).Ok);
        Assert.Equal((true, Client, "contoso.onmicrosoft.com", OutlookCloud.UsGovernment, 10), (target.Outlook.Enabled, target.Outlook.ClientId, target.Outlook.TenantId, target.Outlook.Cloud, target.Outlook.MaxItems));
        SettingsBackup.Reset(target, lenses, replies);
        Assert.False(target.Outlook.Enabled);
        Assert.Equal("", target.Outlook.ClientId);
    }
}

[Collection("Global state")]
public class OutlookUiTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public OutlookUiTests(HeadlessFixture fx) => _fx = fx;
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static AndaRecord Demo => new MockAndaRepository().Find("216482")!;
    private const string Client = "12345678-1234-1234-1234-123456789abc";

    private static OutlookService Signed(OutlookSettings s, Func<string, HttpResponseMessage> mail, FakeMicrosoft? fake = null)
    {
        fake ??= new FakeMicrosoft();
        fake.Respond = (req, _) => req.RequestUri!.Host.StartsWith("login")
            ? (req.RequestUri.AbsolutePath.EndsWith("/devicecode") ? FakeMicrosoft.Json(HttpStatusCode.OK, "{\"device_code\":\"d\",\"user_code\":\"U\",\"verification_uri\":\"https://microsoft.com/devicelogin\",\"expires_in\":900,\"interval\":5}")
                : FakeMicrosoft.Json(HttpStatusCode.OK, "{\"access_token\":\"t\",\"expires_in\":3600}"))
            : mail(req.RequestUri.ToString());
        return new OutlookService(() => s, fake, (_, _) => Task.CompletedTask);
    }

    [Fact]
    public async Task The_card_shows_a_mail_drawer_only_when_Outlook_is_on_and_searches_only_when_it_is_opened()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var fake = new FakeMicrosoft();
            var s = new OutlookSettings { Enabled = true, ClientId = Client };
            var svc = Signed(s, _ => FakeMicrosoft.Json(HttpStatusCode.OK, "{\"value\":[{\"subject\":\"Labeling comments\",\"from\":{\"emailAddress\":{\"name\":\"Pat Example\"}},\"receivedDateTime\":\"2026-11-01T14:30:00Z\",\"bodyPreview\":\"Please review.\"}]}"), fake);

            // off: the drawer is not even there
            var off = new CardWindow(new OfflineBriefService(), new MockAndaRepository().Find) { CloseWhenDeactivated = false, Outlook = new OutlookService(() => new OutlookSettings()) };
            off.Show(Demo, new PixelPoint(40, 40), false); Settle();
            bool offVisible = off.FindControl<Expander>("MailExpander")!.IsVisible;
            off.Close(); Settle();

            // on but signed out: the drawer says how to sign in and makes no request
            var w = new CardWindow(new OfflineBriefService(), new MockAndaRepository().Find) { CloseWhenDeactivated = false, Outlook = svc };
            w.Show(Demo, new PixelPoint(40, 40), false); Settle();
            var exp = w.FindControl<Expander>("MailExpander")!;
            bool onVisible = exp.IsVisible;
            int beforeOpen = fake.Requests.Count;
            exp.IsExpanded = true; Settle();
            string signedOutText = string.Join(" ", w.FindControl<StackPanel>("MailPanel")!.Children.OfType<TextBlock>().Select(t => t.Text));
            int afterSignedOut = fake.Requests.Count;

            // signed in: opening the drawer lists the mail
            svc.SignInAsync().GetAwaiter().GetResult();
            exp.IsExpanded = false; Settle();
            exp.IsExpanded = true;
            for (int i = 0; i < 50 && w.FindControl<StackPanel>("MailPanel")!.Children.OfType<TextBlock>().All(t => t.Text != "Labeling comments"); i++) { Thread.Sleep(20); Settle(); }
            var texts = w.FindControl<StackPanel>("MailPanel")!.Children.OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
            string header = w.FindControl<TextBlock>("MailHeader")!.Text!;
            w.Close(); Settle();
            return (offVisible, onVisible, beforeOpen, signedOutText, afterSignedOut, texts, header);
        }, CancellationToken.None);

        Assert.False(r.offVisible);
        Assert.True(r.onVisible);
        Assert.Equal(0, r.beforeOpen);                                           // closed drawer, no search
        Assert.Contains("Not signed in to Outlook", r.signedOutText);
        Assert.Equal(0, r.afterSignedOut);
        Assert.Contains("Labeling comments", r.texts);
        Assert.Contains(r.texts, t => t.StartsWith("Pat Example"));
        Assert.Contains("Please review.", r.texts);
        Assert.Contains(r.texts, t => t.Contains("Not saved, not sent to any AI service"));
        Assert.Equal("Recent Outlook mail · 1", r.header);
    }

    [Fact]
    public async Task Settings_offers_Outlook_off_by_default_with_honest_status_and_no_secret_field()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            string dir = Temp.NewDir();
            var s = new Settings();
            var svc = new OutlookService(() => s.Outlook);
            var w = new SettingsWindow(s, new PromptLibraryService(Path.Combine(dir, "p.json")), () => { }, new SettingsHooks(() => "", () => null, () => "", null, svc), new QuickReplyService(Path.Combine(dir, "r.json")));
            w.Show(); Settle();
            var on = w.FindControl<CheckBox>("OutlookOn")!;
            var fields = w.FindControl<StackPanel>("OutlookFields")!;
            var sections = w.FindControl<ComboBox>("JumpBox")!.Items.Cast<string>().ToList();
            var offState = (on.IsChecked, fields.IsEnabled, w.FindControl<TextBlock>("OutlookStatus")!.Text);

            on.IsChecked = true; Settle();
            w.FindControl<TextBox>("OutlookClientId")!.Text = Client;
            w.FindControl<TextBox>("OutlookTenant")!.Text = "contoso.onmicrosoft.com";
            w.FindControl<TextBox>("OutlookClientId")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Input.InputElement.LostFocusEvent)); Settle();
            w.FindControl<TextBox>("OutlookTenant")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Input.InputElement.LostFocusEvent)); Settle();
            var radios = w.FindControl<WrapPanel>("OutlookMaxPanel")!.Children.OfType<RadioButton>().ToList();
            radios[2].IsChecked = true; Settle();
            var onState = (fields.IsEnabled, s.Outlook.Enabled, s.Outlook.ClientId, s.Outlook.TenantId, s.Outlook.MaxItems, w.FindControl<TextBlock>("OutlookStatus")!.Text, w.FindControl<Button>("OutlookSignIn")!.IsEnabled);

            // no control in this section can hold a token or password
            var section = fields.Children.OfType<TextBox>().Select(t => t.PasswordChar).ToList();
            w.Close(); Settle();
            return (offState, onState, sections, section);
        }, CancellationToken.None);

        Assert.Equal((false, false, "○ Outlook is off."), r.offState);
        Assert.Contains("OUTLOOK · OPTIONAL, READ-ONLY", r.sections);
        Assert.True(r.onState.Item1);
        Assert.True(r.onState.Item2);
        Assert.Equal((Client, "contoso.onmicrosoft.com", 10), (r.onState.Item3, r.onState.Item4, r.onState.Item5));
        Assert.StartsWith("○ Not signed in", r.onState.Item6);
        Assert.True(r.onState.Item7);
        Assert.DoesNotContain(r.section, c => c != default);
    }
}
