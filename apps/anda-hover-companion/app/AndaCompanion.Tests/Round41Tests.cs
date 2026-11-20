using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using AndaCompanion.Services;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// Round 41: what used to need a source edit is in Settings. Elsa's request body, reply path and token header (held to the simulation's
/// answers in mockup/round41-golden.json, and sent as typed), and MCP servers that are programs on this PC as IT approved them: by full
/// path and SHA-256, checked before each start, started without a shell, only on a second press. The programs here are stand-ins that run in
/// memory; no real program is started. Fictional data only.
/// </summary>
[Collection("Global state")]
public class Round41Tests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public Round41Tests(HeadlessFixture fx) => _fx = fx;
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }

    private static string Folder(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "anda-hover-companion") dir = dir.Parent;
        return Path.Combine(new[] { dir?.FullName ?? throw new DirectoryNotFoundException("anda-hover-companion folder not found") }.Concat(parts).ToArray());
    }
    private static readonly Lazy<JsonElement> Gold = new(() => JsonDocument.Parse(File.ReadAllText(Folder("mockup", "round41-golden.json"))).RootElement.Clone());
    private static string? S(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetString();
    private static Settings ElsaSettings(JsonElement e) => new()
    {
        ElsaAuthHeader = e.GetProperty("header").GetString()!, ElsaAuthScheme = e.GetProperty("scheme").GetString()!,
        ElsaRequestTemplate = e.GetProperty("template").GetString()!, ElsaReplyPath = e.GetProperty("path").GetString()!,
    };

    // ---------- the shared answers ----------

    [Fact]
    public void Elsa_settings_and_token_headers_are_judged_as_the_simulation_judges_them()
    {
        var g = Gold.Value;
        Assert.Equal(g.GetProperty("elsaSaid").GetProperty("hint").GetString(), ElsaMapping.Hint);
        Assert.Equal(g.GetProperty("elsaSaid").GetProperty("blank").GetString(), ElsaMapping.SaidBlank);
        Assert.Equal(g.GetProperty("elsaSaid").GetProperty("filled").GetString(), ElsaMapping.SaidFilled);
        int n = 0;
        foreach (var c in g.GetProperty("elsa").EnumerateArray())
        {
            var s = ElsaSettings(c.GetProperty("elsa"));
            Assert.True(S(c.GetProperty("problem")) == ElsaMapping.Problem(s), $"case {n}: {ElsaMapping.Problem(s)}");
            Assert.Equal(c.GetProperty("filled").GetBoolean(), ElsaMapping.For(s).IsFilledIn);
            n++;
        }
        Assert.Equal(12, n);
        foreach (var c in g.GetProperty("headers").EnumerateArray())
            Assert.Equal(S(c.GetProperty("problem")), ApiTarget.Authorization(c.GetProperty("header").GetString(), c.GetProperty("scheme").GetString(), null).Problem);
    }

    [Fact]
    public void Programs_on_this_PC_are_judged_named_kept_and_placed_as_the_simulation_does()
    {
        var g = Gold.Value;
        Assert.Equal(g.GetProperty("programSaid").GetProperty("hint").GetString(), McpRules.ProgramHint);
        Assert.Equal(g.GetProperty("programSaid").GetProperty("starts").GetString(), McpRules.SaidStartsProgram("mcp-server.exe"));
        Assert.Equal(g.GetProperty("programSaid").GetProperty("mcpHint").GetString(), McpRules.Hint);
        foreach (var c in g.GetProperty("programs").EnumerateArray())
        {
            string program = c.GetProperty("program").GetString()!, sha = c.GetProperty("sha").GetString()!;
            Assert.True(S(c.GetProperty("problem")) == McpRules.ProgramProblem(program, sha), program);
            Assert.Equal(c.GetProperty("name").GetString(), McpRules.ProgramName(program));
            Assert.Equal(c.GetProperty("path").GetString(), McpRules.ProgramPath(program));
        }
        var input = g.GetProperty("normalize").GetProperty("input").EnumerateArray().Select(s => new McpServerSettings
        {
            Name = s.GetProperty("name").GetString()!, IsProgram = s.TryGetProperty("isProgram", out var p) && p.GetBoolean(),
            Program = s.TryGetProperty("program", out var pr) ? pr.GetString()! : "", ProgramSha256 = s.TryGetProperty("sha", out var sh) ? sh.GetString()! : "",
            ProgramArguments = s.TryGetProperty("args", out var a) ? a.EnumerateArray().Select(x => x.GetString()!).ToList() : new(),
            Endpoint = s.TryGetProperty("endpoint", out var ep) ? ep.GetString()! : "", Enabled = s.GetProperty("enabled").GetBoolean(), TimeoutSeconds = s.GetProperty("timeout").GetInt32(),
        });
        var got = McpRules.Normalize(input);
        Assert.Equal(g.GetProperty("normalize").GetProperty("output").EnumerateArray().Select(s => string.Join("|", s.GetProperty("id").GetString(), s.GetProperty("enabled").GetBoolean(),
                s.GetProperty("isProgram").GetBoolean(), s.GetProperty("program").GetString(), s.GetProperty("sha").GetString(), string.Join(",", s.GetProperty("args").EnumerateArray().Select(x => x.GetString())),
                s.GetProperty("endpoint").GetString())),
            got.Select(s => string.Join("|", s.Id, s.Enabled, s.IsProgram, s.Program, s.ProgramSha256, string.Join(",", s.ProgramArguments), s.Endpoint)));
        Assert.Equal(g.GetProperty("where").EnumerateArray().Select(x => x.GetString()!), got.Select(McpRules.Where));
        Assert.Equal(g.GetProperty("phrases").EnumerateArray().Select(x => x.GetString()!), EventLog.Phrases.Where(p => p.StartsWith("MCP program", StringComparison.Ordinal)));
        Assert.Equal(g.GetProperty("changes").GetProperty("items").EnumerateArray().Select(x => x.GetString()!).ToList(), ChangeLog.Entries.First(e => e.Round == 41).Items.ToList());
        Assert.Equal(g.GetProperty("version").GetString(), ChangeLog.Version);
    }

    // ---------- Elsa from Settings ----------

    private static Func<string, string?> Env(params (string Key, string Value)[] pairs) => k => pairs.FirstOrDefault(p => p.Key == k).Value;

    [Fact]
    public async Task Elsa_typed_in_Settings_is_used_with_no_source_edit_and_sends_what_was_typed()
    {
        var s = new Settings
        {
            ElsaRequestTemplate = "{\"model\":\"{{model}}\",\"agent\":\"{{agent}}\",\"max\":{{max_tokens}},\"q\":[\"{{system}}\",\"{{user}}\"]}",
            ElsaReplyPath = "output.text", ElsaAuthHeader = "x-api-key", ElsaAuthScheme = "",
        };
        var env = Env(("ANDA_COMPANION_PROVIDER", "elsa"), (ElsaConfig.EndpointVar, "https://elsa.example.test/api"), (ElsaConfig.TokenVar, "fictional-elsa-token"),
            (ElsaConfig.ModelVar, "fictional-model"), (ElsaConfig.AgentVar, "fictional-agent"));
        var mapping = ElsaMapping.For(s);
        var inspection = ElsaConfig.Inspect(env, mapping);
        Assert.Equal(ElsaStatus.Ready, inspection.Status);
        using var request = mapping.BuildRequest(inspection.Settings!, "Fictional system", "Fictional \"quoted\" user", 64);
        Assert.Equal("{\"model\":\"fictional-model\",\"agent\":\"fictional-agent\",\"max\":64,\"q\":[\"Fictional system\",\"Fictional \\u0022quoted\\u0022 user\"]}", await request.Content!.ReadAsStringAsync());
        Assert.Equal("fictional-elsa-token", request.Headers.GetValues("x-api-key").Single());
        Assert.Equal("Fictional answer", mapping.ReadReply("{\"output\":{\"text\":\"Fictional answer\"}}"));
        // blank: the placeholder, which says where to fill it in; and what is switched on follows
        Assert.Equal(ElsaStatus.MappingNotFilled, ElsaConfig.Inspect(env, ElsaMapping.For(new Settings())).Status);
        Assert.Contains("Settings, AI and connections, Elsa", ElsaConfig.Inspect(env, ElsaMapping.For(new Settings())).Message);
        Assert.Equal("Elsa", Connections.From(new Settings { Provider = ProviderChoice.Auto, ElsaRequestTemplate = s.ElsaRequestTemplate, ElsaReplyPath = "output.text" }, env).Ai?.Name);
        Assert.Null(Connections.From(new Settings { Provider = ProviderChoice.Auto }, env).Ai);
        Assert.Contains("configured", ConnectionService.DescribeFor(new Settings { Provider = ProviderChoice.Auto, ElsaRequestTemplate = s.ElsaRequestTemplate, ElsaReplyPath = "output.text" }, env));
    }

    [Fact]
    public async Task Settings_shows_Elsas_fields_and_says_what_they_mean()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            var dir = Temp.NewDir(); var s = new Settings();
            var w = new SettingsWindow(s, new PromptLibraryService(Path.Combine(dir, "p.json")), () => { }, null, new QuickReplyService(Path.Combine(dir, "r.json")));
            try
            {
                w.Show(); Settle();
                string blank = w.ElsaStatusLine.Text ?? "";
                w.ElsaTemplate.Text = "{\"q\":\"{{user}}\"}"; w.ElsaTemplate.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(InputElement.LostFocusEvent)); Settle();
                string half = w.ElsaStatusLine.Text ?? "";
                w.ElsaPath.Text = "output.text"; w.ElsaPath.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(InputElement.LostFocusEvent)); Settle();
                return (blank, half, filled: w.ElsaStatusLine.Text ?? "", s.ElsaRequestTemplate, s.ElsaReplyPath, hint: w.ElsaHint.Text ?? "");
            }
            finally { w.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.Equal(ElsaMapping.SaidBlank, r.blank);
        Assert.Contains("reply path", r.half);
        Assert.Equal(ElsaMapping.SaidFilled, r.filled);
        Assert.Equal(("{\"q\":\"{{user}}\"}", "output.text"), (r.ElsaRequestTemplate, r.ElsaReplyPath));
        Assert.Equal(ElsaMapping.Hint, r.hint);
    }

    // ---------- a program on this PC, as a stand-in in memory ----------

    private static readonly byte[] Exe = Encoding.ASCII.GetBytes("MZ fictional program bytes");
    private static readonly string ExeSha = Convert.ToHexString(SHA256.HashData(Exe)).ToLowerInvariant();
    private const string ExePath = @"C:\Program Files\Fictional\mcp-server.exe";

    private static McpServerSettings Program(string? sha = null, params string[] args) => new()
    {
        Id = "p", Name = "Fictional program", IsProgram = true, Program = ExePath, ProgramSha256 = sha ?? ExeSha, ProgramArguments = args.ToList(), Enabled = true,
    };

    /// <summary>A program in memory: two pipes, and a loop that answers each request line with what the test says.</summary>
    private sealed class FakeProgram : IMcpProcess
    {
        private readonly AnonymousPipeServerStream _in = new(PipeDirection.Out), _out = new(PipeDirection.In);
        private readonly AnonymousPipeClientStream _inClient, _outClient;
        private readonly Task _loop;
        public List<string> Seen { get; } = new();
        public bool Killed { get; private set; }
        public FakeProgram(Func<JsonObject, IEnumerable<string>> answer, bool endsOnClose = true)
        {
            _inClient = new AnonymousPipeClientStream(PipeDirection.In, _in.ClientSafePipeHandle);
            _outClient = new AnonymousPipeClientStream(PipeDirection.Out, _out.ClientSafePipeHandle);
            _loop = Task.Run(async () =>
            {
                using var reader = new StreamReader(_inClient, Encoding.UTF8);
                var writer = new StreamWriter(_outClient, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                string? line;
                while ((line = await reader.ReadLineAsync()) is not null)
                {
                    lock (Seen) Seen.Add(line);
                    foreach (var reply in answer((JsonNode.Parse(line) as JsonObject)!)) await writer.WriteLineAsync(reply);
                }
                if (!endsOnClose) await Task.Delay(Timeout.Infinite);
            });
        }
        public Stream Input => _in;
        public Stream Output => _out;
        public async Task<bool> EndedWithinAsync(TimeSpan time) => await Task.WhenAny(_loop, Task.Delay(time)) == _loop;
        public void Kill() { Killed = true; _in.Dispose(); }
        public void Dispose() { _in.Dispose(); _out.Dispose(); }
    }

    private static IEnumerable<string> Answers(JsonObject m)
    {
        string method = m["method"]!.GetValue<string>();
        JsonObject Result(JsonObject r) => new() { ["jsonrpc"] = "2.0", ["id"] = m["id"]!.DeepClone(), ["result"] = r };
        return method switch
        {
            "initialize" => new[] { "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/message\",\"params\":{}}", Result(new JsonObject { ["protocolVersion"] = "2025-06-18", ["capabilities"] = new JsonObject() }).ToJsonString() },
            "tools/list" => new[] { "not json, passed over", Result(new JsonObject { ["tools"] = new JsonArray(new JsonObject { ["name"] = "fictional_lookup", ["inputSchema"] = new JsonObject { ["type"] = "object" } }) }).ToJsonString() },
            "tools/call" => new[] { Result(new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "Fictional local answer" }) }).ToJsonString() },
            _ => Array.Empty<string>(),
        };
    }

    private sealed class Starter
    {
        public List<string> Steps { get; } = new();
        public ProcessStartInfo? Info { get; private set; }
        public FakeProgram? Program { get; private set; }
        public byte[] Bytes { get; set; } = Exe;
        public Func<JsonObject, IEnumerable<string>> Answer { get; set; } = Answers;
        public bool EndsOnClose { get; set; } = true;
        public McpLocal Local => new(path => { Steps.Add("open " + path); return new Tracked(Bytes, () => Steps.Add("closed")); },
            info => { Steps.Add("start"); Info = info; return Program = new FakeProgram(Answer, EndsOnClose); });
        private sealed class Tracked(byte[] bytes, Action closed) : MemoryStream(bytes) { protected override void Dispose(bool disposing) { if (disposing) closed(); base.Dispose(disposing); } }
    }

    [Fact]
    public async Task A_program_is_checked_held_started_without_a_shell_and_spoken_to_one_line_at_a_time()
    {
        var starter = new Starter();
        string path = Path.Combine(Temp.NewDir(), "events.csv");
        try
        {
            EventLog.Configure(path);
            using (var c = new McpClient(Program(null, "--stdio", "--quiet"), local: starter.Local))
            {
                var tools = await c.ListToolsAsync(CancellationToken.None);
                Assert.Equal("fictional_lookup", Assert.Single(tools).Name);
                var answer = await c.CallToolAsync("fictional_lookup", new JsonObject { ["q"] = "Fictional" }, CancellationToken.None);
                Assert.Equal("Fictional local answer", answer.Text);
                await c.EndAsync();
            }
            var info = starter.Info!;
            Assert.Equal(ExePath, info.FileName);
            Assert.False(info.UseShellExecute);                                                   // never through a shell
            Assert.True(info.CreateNoWindow && info.RedirectStandardInput && info.RedirectStandardOutput && info.RedirectStandardError);
            Assert.Equal(new[] { "--stdio", "--quiet" }, info.ArgumentList);
            Assert.Equal("", info.Arguments);
            Assert.Equal(new[] { "open " + ExePath, "start", "closed" }, starter.Steps);              // the file is held until the program has started
            Assert.Equal(new[] { "initialize", "notifications/initialized", "tools/list", "tools/call" }, starter.Program!.Seen.Select(l => JsonNode.Parse(l)!["method"]!.GetValue<string>()));
            Assert.All(starter.Program.Seen, l => Assert.DoesNotContain("\n", l));
            Assert.False(starter.Program.Killed);                                                  // it ended by itself when its input was closed
            var (entries, _, _) = EventLog.Read();
            Assert.Equal(new[] { "MCP program started", "MCP tools listed, 1 tools", "MCP request, 17 characters", "MCP program stopped" }, entries.Select(e => e.Detail).TakeLast(4));
        }
        finally { EventLog.Configure(null); }
    }

    [Fact]
    public async Task A_program_that_is_not_the_one_approved_is_never_started()
    {
        var other = new Starter { Bytes = Encoding.ASCII.GetBytes("MZ a different program") };
        using (var c = new McpClient(Program(), local: other.Local))
            Assert.Equal(McpRules.SaidHashMismatch("mcp-server.exe"), (await Assert.ThrowsAsync<McpException>(() => c.ListToolsAsync(CancellationToken.None))).Message);
        Assert.Equal(new[] { "open " + ExePath, "closed" }, other.Steps);                           // opened to check, never started
        foreach (var (server, said) in new (McpServerSettings, string)[]
        {
            (Program(sha: "short"), "SHA-256 exactly as IT gives it"),
            (new McpServerSettings { Name = "x", IsProgram = true, Program = @"C:\Tools\run.bat", ProgramSha256 = ExeSha }, "full path to an .exe"),
            (new McpServerSettings { Name = "x", IsProgram = true, Program = "", ProgramSha256 = ExeSha }, "Please enter the program's full path"),
        })
        {
            var starter = new Starter();
            using var c = new McpClient(server, local: starter.Local);
            Assert.Contains(said, (await Assert.ThrowsAsync<McpException>(() => c.ListToolsAsync(CancellationToken.None))).Message);
            Assert.Empty(starter.Steps);                                                             // not even opened
        }
        var missing = new McpLocal(_ => throw new FileNotFoundException("fictional"), _ => throw new InvalidOperationException("must not start"));
        using (var c = new McpClient(Program(), local: missing))
            Assert.Equal(McpRules.SaidNoProgram("mcp-server.exe"), (await Assert.ThrowsAsync<McpException>(() => c.ListToolsAsync(CancellationToken.None))).Message);
    }

    [Fact]
    public async Task A_program_that_floods_stops_early_or_will_not_end_is_handled()
    {
        var flood = new Starter { Answer = m => m["method"]!.GetValue<string>() == "initialize" ? new[] { new string('x', 1_100_000) } : Array.Empty<string>() };
        using (var c = new McpClient(Program(), local: flood.Local))
            Assert.Equal(McpRules.SaidTooLarge, (await Assert.ThrowsAsync<McpException>(() => c.ListToolsAsync(CancellationToken.None))).Message);
        Assert.True(flood.Program!.Killed);                                                         // a program that broke off is stopped, not left running

        var silent = new Starter { Answer = _ => Array.Empty<string>() };
        using (var c = new McpClient(Program(), local: silent.Local) { TimeLimit = TimeSpan.FromMilliseconds(300) })
            Assert.Contains("did not answer in time", (await Assert.ThrowsAsync<McpException>(() => c.ListToolsAsync(CancellationToken.None))).Message);

        var stubborn = new Starter { EndsOnClose = false };
        using (var c = new McpClient(Program(), local: stubborn.Local))
        {
            await c.ListToolsAsync(CancellationToken.None);
            await c.EndAsync();
        }
        Assert.True(stubborn.Program!.Killed);                                                      // input closed, two seconds, then stopped
    }

    [Fact]
    public async Task The_window_starts_a_program_only_on_the_second_press_and_Settings_Test_does_the_same()
    {
        var starter = new Starter();
        var r = await _fx.Session.Dispatch(async () =>
        {
            var w = new McpWindow(new[] { Program() }, null, s => new McpClient(s, local: starter.Local));
            try
            {
                w.Show(); Settle();
                await w.ListAsync(); Settle();
                var first = (w.Status.Text ?? "", starter.Steps.Count);
                await w.ListAsync(); Settle();
                return (first, second: (w.Tools.ItemCount, starter.Steps.Contains("start")));
            }
            finally { w.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.Equal((McpRules.SaidStartsProgram("mcp-server.exe"), 0), r.first);
        Assert.Equal((1, true), r.second);
        string settings = Round37Tests.Code(Folder("app", "AndaCompanion", "UI", "SettingsWindow.Mcp.cs"));
        Assert.Contains("if (local && !armed && Problem() is null) { armed = true; status.Text = McpRules.SaidStartsProgram(McpRules.ProgramName(s.Program)); return; }", settings);
    }

    [Fact]
    public async Task Settings_adds_a_program_that_stays_off_until_it_is_named_as_IT_approved_it()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            var dir = Temp.NewDir(); var s = new Settings();
            var w = new SettingsWindow(s, new PromptLibraryService(Path.Combine(dir, "p.json")), () => { }, null, new QuickReplyService(Path.Combine(dir, "r.json")));
            try
            {
                w.Show(); Settle();
                w.McpAddLocal.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle();
                var row = w.McpList.Children.OfType<StackPanel>().Single();
                var names = row.GetLogicalDescendants().OfType<Control>().Select(c => Avalonia.Automation.AutomationProperties.GetName(c)).Where(x => !string.IsNullOrEmpty(x)).ToList();
                var use = row.Children.OfType<CheckBox>().Single();
                use.IsChecked = true; Settle();
                var refused = (use.IsChecked, s.McpServers[0].Enabled);
                return (names, refused, isProgram: s.McpServers[0].IsProgram, connections: Connections.From(s, _ => null).Mcp.Count);
            }
            finally { w.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.Equal(new[] { "Use MCP server 1", "Name of MCP server 1", "Program of MCP server 1", "SHA-256 of MCP server 1", "Arguments of MCP server 1", "Test MCP server 1", "Remove MCP server 1" }, r.names);
        Assert.Equal((false, false), r.refused);
        Assert.True(r.isProgram);
        Assert.Equal(0, r.connections);
    }

    [Fact]
    public void A_restored_backup_brings_programs_back_switched_off_and_named_for_the_Connections_window()
    {
        var dir = Temp.NewDir();
        var lenses = new PromptLibraryService(Path.Combine(dir, "p.json")); var replies = new QuickReplyService(Path.Combine(dir, "r.json"));
        var mine = new Settings { McpServers = McpRules.Normalize(new[] { Program() }) };
        Assert.True(mine.McpServers[0].Enabled);
        Assert.Equal("this PC: mcp-server.exe", Connections.From(mine, _ => null).Mcp.Single().Where);
        var target = new Settings();
        var (ok, message) = SettingsBackup.Import(SettingsBackup.Export(mine, lenses, replies), target, lenses, replies);
        Assert.True(ok);
        Assert.False(target.McpServers.Single().Enabled);
        Assert.Equal(ExePath, target.McpServers[0].Program);
        Assert.Contains(McpRules.SaidSwitchedOff(1), message);
    }
}
