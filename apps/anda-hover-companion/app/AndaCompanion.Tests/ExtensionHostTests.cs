using System.Text;
using System.Text.Json.Nodes;
using AndaCompanion.Data;
using AndaCompanion.Models;
using AndaCompanion.Services;
using Xunit;

namespace AndaCompanion.Tests;

public class NativeHostFramingTests
{
    private static byte[] Frame(string json) { var b = Encoding.UTF8.GetBytes(json); return BitConverter.GetBytes((uint)b.Length).Concat(b).ToArray(); }

    private static NativeHostContext Ctx(IAndaRepository? repo = null, Settings? settings = null) => new()
    {
        Settings = settings ?? new Settings(), Repository = repo ?? new MockAndaRepository(), Calendar = new BusinessCalendar(),
        Today = () => new DateOnly(2026, 11, 2), Reference = r => ReferenceResolver.For(r, null, "", null, false),
    };

    private static List<JsonObject> Roundtrip(NativeHostContext ctx, params string[] requests)
    {
        var input = new MemoryStream(requests.SelectMany(Frame).ToArray());
        var output = new MemoryStream();
        Assert.Equal(0, NativeHost.Serve(input, output, ctx));
        output.Position = 0;
        var replies = new List<JsonObject>();
        while (NativeHost.ReadFrame(output) is { } f) replies.Add(JsonNode.Parse(f)!.AsObject());
        return replies;
    }

    [Fact]
    public void A_browser_launch_is_recognised_by_its_first_argument()
    {
        Assert.True(NativeHost.IsHostLaunch(new[] { "chrome-extension://abcdefghijklmnopabcdefghijklmnop/", "--parent-window=1234" }));
        Assert.True(NativeHost.IsHostLaunch(new[] { "--native-host" }));
        Assert.False(NativeHost.IsHostLaunch(Array.Empty<string>()));
        Assert.False(NativeHost.IsHostLaunch(new[] { "--selftest", "out" }));
    }

    [Fact]
    public void Every_request_gets_one_framed_reply_with_the_same_id_and_a_clean_close_ends_the_session()
    {
        var r = Roundtrip(Ctx(), """{"id":1,"type":"hello"}""", """{"id":"two","type":"settings"}""");
        Assert.Equal(2, r.Count);
        Assert.Equal(1, r[0]["id"]!.GetValue<int>());
        Assert.Equal("two", r[1]["id"]!.GetValue<string>());
        Assert.True(r[0]["ok"]!.GetValue<bool>());
        Assert.Equal(NativeHostSession.Protocol, r[0]["protocol"]!.GetValue<int>());
        Assert.Equal("demo", r[0]["dataKind"]!.GetValue<string>());
        Assert.Equal("whisper", r[1]["settings"]!["consent"]!.GetValue<string>());
        Assert.Equal(7, r[1]["settings"]!["urgentLateDays"]!.GetValue<int>());
    }

    [Fact]
    public void Lookup_returns_the_same_card_the_app_would_draw_and_says_plainly_when_there_is_none()
    {
        var r = Roundtrip(Ctx(), """{"id":1,"type":"lookup","number":"ANDA 216482"}""", """{"id":2,"type":"lookup","number":"999999"}""",
            """{"id":3,"type":"lookup","number":"NDA 021234"}""");
        var card = r[0]["card"]!;
        Assert.Equal("216482", card["number"]!.GetValue<string>());
        Assert.Equal("OFFLINE BRIEF", card["brief"]!["tag"]!.GetValue<string>());
        Assert.True(card["urgency"]!["isUrgent"]!.GetValue<bool>());
        Assert.Contains("falls on a weekend: Sat Oct 3", card["watch"]![0]!["text"]!.GetValue<string>());
        Assert.Equal(1, card["watchMore"]!.GetValue<int>());
        Assert.False(r[1]["ok"]!.GetValue<bool>());
        Assert.Equal("not-found", r[1]["error"]!.GetValue<string>());
        Assert.Equal("bad-number", r[2]["error"]!.GetValue<string>());   // an NDA is not guessed to be an ANDA
    }

    [Fact]
    public void Draft_check_works_through_the_host_and_refuses_an_oversized_draft()
    {
        var big = new string('a', NativeHostSession.MaxDraftChars + 1);
        var r = Roundtrip(Ctx(), """{"id":1,"type":"draft","text":"ANDA 216482 is on track. Goal date Oct 10, 2026."}""", $$"""{"id":2,"type":"draft","text":"{{big}}"}""");
        var kinds = r[0]["findings"]!.AsArray().Select(f => f!["kind"]!.GetValue<string>()).ToList();
        Assert.Contains("diff", kinds);
        Assert.Equal("too-long", r[1]["error"]!.GetValue<string>());
    }

    [Fact]
    public void Garbage_unknown_types_and_a_truncated_frame_never_crash_the_host()
    {
        var r = Roundtrip(Ctx(), "not json", """{"id":9,"type":"launch-missiles"}""", """[1,2,3]""");
        Assert.All(r, x => Assert.False(x["ok"]!.GetValue<bool>()));
        Assert.Equal("unknown-type", r[1]["error"]!.GetValue<string>());
        Assert.Equal(9, r[1]["id"]!.GetValue<int>());

        // a frame that promises 50 bytes and delivers 3: the session ends without throwing
        var cut = BitConverter.GetBytes(50u).Concat(new byte[] { 1, 2, 3 }).ToArray();
        Assert.Equal(0, NativeHost.Serve(new MemoryStream(cut), new MemoryStream(), Ctx()));
        // a frame that promises more than the limit is refused
        var huge = BitConverter.GetBytes((uint)NativeHost.MaxRequestBytes + 1);
        Assert.Equal(2, NativeHost.Serve(new MemoryStream(huge), new MemoryStream(), Ctx()));
    }

    [Fact]
    public void An_error_never_echoes_what_was_asked()
    {
        var r = Roundtrip(Ctx(), """{"id":1,"type":"lookup","number":"ANDA 777777 secret text"}""");
        string json = r[0].ToJsonString();
        Assert.DoesNotContain("secret", json);
    }

    [Fact]
    public void The_watch_list_ranks_worst_first_and_matches_the_cards()
    {
        var r = Roundtrip(Ctx(), """{"id":1,"type":"watchlist"}""");
        var items = r[0]["items"]!.AsArray();
        Assert.Equal(2, r[0]["total"]!.GetValue<int>());
        Assert.Equal("216482", items[0]!["number"]!.GetValue<string>());      // at risk and urgent comes before the healthy one
        Assert.True(items[0]!["urgent"]!.GetValue<bool>());
    }

    [Fact]
    public void Messages_over_the_browsers_one_megabyte_limit_are_replaced_by_a_short_error()
    {
        var ms = new MemoryStream();
        NativeHost.WriteFrame(ms, new string('x', NativeHost.MaxResponseBytes + 10));
        ms.Position = 0;
        var back = JsonNode.Parse(NativeHost.ReadFrame(ms)!)!;
        Assert.Equal("too-large", back["error"]!.GetValue<string>());
    }
}

public class NativeHostProcessTests
{
    [Fact]
    public void The_real_program_answers_as_a_native_host_over_real_pipes_and_exits_when_the_pipe_closes()
    {
        string dll = typeof(NativeHost).Assembly.Location;
        var psi = new System.Diagnostics.ProcessStartInfo("dotnet") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add(dll);
        psi.ArgumentList.Add($"chrome-extension://{NativeHostRegistration.DefaultExtensionId}/");
        psi.Environment[AppPaths.HomeEnvVar] = Temp.NewDir();   // never the developer's own settings
        using var p = System.Diagnostics.Process.Start(psi)!;
        JsonNode Ask(string json)
        {
            NativeHost.WriteFrame(p.StandardInput.BaseStream, json);
            var read = Task.Run(() => NativeHost.ReadFrame(p.StandardOutput.BaseStream));
            Assert.True(read.Wait(TimeSpan.FromSeconds(30)), "the host did not answer within 30 s");
            return JsonNode.Parse(read.Result!)!;
        }
        var hello = Ask("""{"id":1,"type":"hello"}""");
        Assert.True(hello["ok"]!.GetValue<bool>());
        Assert.Equal("demo", hello["dataKind"]!.GetValue<string>());
        var lookup = Ask("""{"id":2,"type":"lookup","number":"214990"}""");
        Assert.Equal("Metformin HCl ER Tablets, 750 mg", lookup["card"]!["drug"]!.GetValue<string>());
        p.StandardInput.Close();
        Assert.True(p.WaitForExit(15000), "the host kept running after the pipe closed");
        Assert.Equal(0, p.ExitCode);
    }
}

public class WatchListTests
{
    private sealed class FakeRepo(params AndaRecord[] records) : IAndaRepository
    {
        public AndaRecord? Find(string n) => records.FirstOrDefault(r => r.Number == n);
        public IReadOnlyCollection<string> Numbers => records.Select(r => r.Number).ToList();
    }

    private static AndaRecord R(string n, DateOnly? goal, DateOnly? asOf = null) => new MockAndaRepository().Find("216482")! with
    {
        Number = n, Demo = false, SourceLabel = "Test", GoalDateValue = goal, ForecastDateValue = null, ForecastDate = AndaRecord.Unknown,
        ForecastDelta = "", ForecastLate = false, ForecastDeltaDays = null, Attention = null, AsOfDate = asOf ?? new DateOnly(2026, 11, 2),
    };

    [Fact]
    public void Worst_first_then_urgent_then_earliest_goal_then_number_and_quiet_records_are_left_out()
    {
        var ctx = new CardContext(new DateOnly(2026, 11, 2), new BusinessCalendar());
        var repo = new FakeRepo(
            R("100003", new DateOnly(2026, 12, 8)),                 // a plain Tuesday far from any holiday: no flag at all
            R("100002", new DateOnly(2026, 10, 3)),                 // goal passed: bad
            R("100001", new DateOnly(2026, 11, 7)),                 // a Saturday: warn
            R("100004", new DateOnly(2026, 10, 1)));                // goal passed, earlier: bad
        var res = WatchList.Build(repo, ctx);
        Assert.Equal(4, res.Total);
        Assert.Equal(new[] { "100004", "100002", "100001" }, res.Items.Select(i => i.Number).ToArray());
        Assert.Equal(new[] { "bad", "bad", "warn" }, res.Items.Select(i => i.Severity).ToArray());
    }

    [Fact]
    public void The_limit_caps_what_is_returned_but_not_what_is_counted()
    {
        var ctx = new CardContext(new DateOnly(2026, 11, 2), new BusinessCalendar());
        var repo = new FakeRepo(Enumerable.Range(1, 5).Select(i => R($"20000{i}", new DateOnly(2026, 10, i))).ToArray());
        var res = WatchList.Build(repo, ctx, limit: 2);
        Assert.Equal(2, res.Items.Count);
        Assert.Equal(5, res.Shown);
        Assert.Equal(5, res.Total);
    }
}

public class LargePortfolioTests
{
    [Fact]
    public void Five_thousand_rows_load_and_build_their_watch_list_in_well_under_the_time_a_person_would_notice()
    {
        var sb = new StringBuilder("ANDA,Product,Applicant,Goal date,Forecast date,Status\n");
        for (int i = 0; i < 5000; i++) sb.Append($"{100000 + i},Drug {i % 50} tabs,Company {i % 10},2026-12-{1 + i % 28:00},2027-01-05,In review\n");
        string path = Path.Combine(Temp.NewDir(), "big.csv");
        File.WriteAllText(path, sb.ToString());
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var repo = new AndaCompanion.Services.Portfolio.PortfolioRepository(path, today: () => new DateOnly(2026, 11, 3));
        Assert.True(repo.Status.Ok, repo.Status.Message);
        var list = WatchList.Build(repo, new CardContext(new DateOnly(2026, 11, 3), new BusinessCalendar()));
        clock.Stop();
        Assert.Equal(5000, list.Total);
        Assert.True(clock.ElapsedMilliseconds < 6000, $"took {clock.ElapsedMilliseconds} ms (it was quadratic before the applicant index)");
        Assert.Equal(3, repo.Find("100000")!.Sisters.Count);   // sisters are still found, three at most
    }
}

public class NativeHostRegistrationTests
{
    [Fact]
    public void The_manifest_lets_exactly_one_extension_start_the_host()
    {
        string json = NativeHostRegistration.ManifestJson(@"C:\Apps\AndaCompanion.exe", NativeHostRegistration.DefaultExtensionId);
        var m = JsonNode.Parse(json)!;
        Assert.Equal("com.anda_hover_companion.host", m["name"]!.GetValue<string>());
        Assert.Equal("stdio", m["type"]!.GetValue<string>());
        var origins = m["allowed_origins"]!.AsArray();
        Assert.Single(origins);
        Assert.Equal($"chrome-extension://{NativeHostRegistration.DefaultExtensionId}/", origins[0]!.GetValue<string>());
        Assert.Equal(@"C:\Apps\AndaCompanion.exe", m["path"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("kkohidikfneelkeiojlmdinpncfmgjcl", true)]
    [InlineData("KKOHIDIKFNEELKEIOJLMDINPNCFMGJCL", false)]
    [InlineData("short", false)]
    [InlineData("kkohidikfneelkeiojlmdinpncfmgjcq", false)]   // q is outside a to p
    [InlineData("", false)]
    public void An_extension_id_is_32_letters_from_a_to_p(string id, bool valid) => Assert.Equal(valid, NativeHostRegistration.IsValidExtensionId(id));

    [Fact]
    public void Files_are_written_under_the_host_folder_and_remember_the_apps_data_folder()
    {
        string dir = Temp.NewDir(), home = Temp.NewDir();
        Environment.SetEnvironmentVariable(NativeHostRegistration.DirEnvVar, dir);
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvVar, home);
        try
        {
            string manifest = NativeHostRegistration.WriteFiles(@"C:\x\AndaCompanion.exe", NativeHostRegistration.DefaultExtensionId);
            Assert.StartsWith(dir, manifest);
            Assert.Equal(home, File.ReadAllText(Path.Combine(dir, "home.txt")).Trim());
            Environment.SetEnvironmentVariable(AppPaths.HomeEnvVar, null);
            NativeHostRegistration.AdoptInstalledHome();
            Assert.Equal(home, Environment.GetEnvironmentVariable(AppPaths.HomeEnvVar));
            Assert.Throws<ArgumentException>(() => NativeHostRegistration.WriteFiles(@"C:\x", "nope"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(NativeHostRegistration.DirEnvVar, null);
            Environment.SetEnvironmentVariable(AppPaths.HomeEnvVar, null);
        }
    }
}
