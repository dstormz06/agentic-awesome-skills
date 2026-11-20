using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using AndaCompanion.Services;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// Round 41 (the owner's yes): a real, harmless program started as an MCP server: AndaCompanion.McpStub, a stand-in built with these
/// tests that answers with fictional words and reaches nothing. On Windows (the Windows workflow) everything is real: the path check,
/// Windows' own path for the file, the SHA-256, the start, the job object. Elsewhere the same real process code runs (pipes, the pump,
/// the withheld variables, the stop), with only the start pointed at the stub's file, since a Windows path cannot be started here.
/// </summary>
[Collection("Global state")]
public class Round41ProgramTests
{
    /// <summary>The stub's program file, copied next to the tests by the build (stub/): an .exe on Windows, no extension elsewhere.</summary>
    private static string Stub()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "stub", OperatingSystem.IsWindows() ? "AndaCompanion.McpStub.exe" : "AndaCompanion.McpStub");
        return File.Exists(path) ? path : throw new FileNotFoundException("The stand-in program was not copied next to the tests; build AndaCompanion.Tests again.", path);
    }

    private static McpServerSettings ServerFor(string program, string stub) => new()
    {
        Id = "stub", Name = "Fictional stub", IsProgram = true, Program = program, Enabled = true,
        ProgramSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(stub))).ToLowerInvariant(), ProgramArguments = new() { "--fictional" },
    };

    private sealed class CertificateRefused : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("The SSL connection could not be established.", new System.Security.Authentication.AuthenticationException("fictional: revocation status unknown"));
    }

    [Fact]
    public async Task A_certificate_that_is_refused_or_cannot_be_checked_is_said_as_such()
    {
        // round 41 review: with the revocation check on, a proxy that blocks the revocation list must not read as "could not be reached"
        using var c = new McpClient(new McpServerSettings { Name = "Fictional", Endpoint = "https://mcp.example.test/mcp", Enabled = true }, _ => null, new CertificateRefused());
        Assert.Equal(McpRules.SaidCertificate, (await Assert.ThrowsAsync<McpException>(() => c.ListToolsAsync(CancellationToken.None))).Message);
    }

    [Fact]
    public void The_job_limits_have_the_size_Windows_expects()
    {
        // JOBOBJECT_EXTENDED_LIMIT_INFORMATION is 144 bytes in a 64-bit process and 112 in a 32-bit one; a wrong layout would make
        // Windows refuse the limit, and the job would not stop programs
        Assert.Equal(IntPtr.Size == 8 ? 144 : 112, Marshal.SizeOf<McpJob.ExtendedLimits>());
        Assert.Equal(IntPtr.Size == 8 ? 64 : 48, Marshal.SizeOf<McpJob.BasicLimits>());
        // the flag that matters, and where the I/O counters and memory limits start (a mutation that kept the size but moved them was not caught)
        Assert.Equal(16, (int)Marshal.OffsetOf<McpJob.BasicLimits>(nameof(McpJob.BasicLimits.LimitFlags)));
        Assert.Equal(IntPtr.Size == 8 ? 64 : 48, (int)Marshal.OffsetOf<McpJob.ExtendedLimits>(nameof(McpJob.ExtendedLimits.Io)));
        Assert.Equal(IntPtr.Size == 8 ? 112 : 96, (int)Marshal.OffsetOf<McpJob.ExtendedLimits>(nameof(McpJob.ExtendedLimits.ProcessMemoryLimit)));
    }

    [Fact]
    public async Task A_real_program_is_started_spoken_to_given_no_token_and_stopped()
    {
        string stub = Stub();
        string program = OperatingSystem.IsWindows() ? stub : @"C:\Program Files\Fictional\AndaCompanion.McpStub.exe";
        var server = ServerFor(program, stub);
        var withheld = new[] { "ANTHROPIC_API_KEY", ElsaConfig.TokenVar, "FICTIONAL_R41_STUB_TOKEN" };
        Environment.SetEnvironmentVariable("FICTIONAL_R41_STUB_TOKEN", "fictional"); Environment.SetEnvironmentVariable("FICTIONAL_R41_STUB_KEPT", "fictional");
        try
        {
            // on Windows: the app's own way in (McpLocal.Real); elsewhere: the real process, started from the stub's file
            using var c = OperatingSystem.IsWindows()
                ? new McpClient(server, withheld)
                : new McpClient(server, null, local: new McpLocal(_ => File.OpenRead(stub), info => { info.FileName = stub; return McpLocal.Real.Start(info); }), withheld: withheld);
            Assert.True(c.NeedsStart);
            Assert.Equal(McpRules.SaidNotStarted(McpRules.ProgramName(program)), (await Assert.ThrowsAsync<McpException>(() => c.ListToolsAsync(CancellationToken.None))).Message);
            c.PermitStart();
            Assert.Equal(new[] { "fictional_echo", "fictional_variable" }, (await c.ListToolsAsync(CancellationToken.None)).Select(t => t.Name));
            Assert.False(c.NeedsStart);
            Assert.Equal("Fictional echo: Fictional text", (await c.CallToolAsync("fictional_echo", new JsonObject { ["text"] = "Fictional text" }, CancellationToken.None)).Text);
            Assert.Equal("not set", (await c.CallToolAsync("fictional_variable", new JsonObject { ["name"] = "FICTIONAL_R41_STUB_TOKEN" }, CancellationToken.None)).Text);
            Assert.Equal("set", (await c.CallToolAsync("fictional_variable", new JsonObject { ["name"] = "FICTIONAL_R41_STUB_KEPT" }, CancellationToken.None)).Text);
            var ending = Stopwatch.StartNew();
            await c.EndAsync();
            Assert.True(ending.Elapsed < TimeSpan.FromSeconds(2), ending.Elapsed.ToString());      // it ended by itself when its input closed
            Assert.True(c.NeedsStart);
        }
        finally { Environment.SetEnvironmentVariable("FICTIONAL_R41_STUB_TOKEN", null); Environment.SetEnvironmentVariable("FICTIONAL_R41_STUB_KEPT", null); }
    }

    [WindowsFact]
    public async Task On_Windows_a_program_reached_through_a_link_is_refused()
    {
        // the link sits beside the stub's own folder (long names, where the direct path is accepted by the test above), so only the
        // link can be the reason for the refusal (round 41 review: under the temp folder a short name would have refused it anyway)
        string stub = Stub(), link = Path.Combine(AppContext.BaseDirectory, "stub-link-" + Guid.NewGuid().ToString("N"));
        try { Directory.CreateSymbolicLink(link, Path.GetDirectoryName(stub)!); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            Assert.Fail("This machine does not allow making a link (Windows Developer Mode or administrator), so the check could not be made: " + ex.Message);
        }
        try
        {
            using (var direct = new McpClient(ServerFor(stub, stub)))          // the same folder without the link: accepted
            {
                direct.PermitStart();
                Assert.Equal(2, (await direct.ListToolsAsync(CancellationToken.None)).Count);
                await direct.EndAsync();
            }
            string through = Path.Combine(link, Path.GetFileName(stub));
            var server = ServerFor(through, stub);
            Assert.Null(McpRules.ProgramProblem(server.Program, server.ProgramSha256));     // the path itself is well formed
            using var c = new McpClient(server);
            c.PermitStart();
            Assert.Equal(McpRules.SaidNotThere("AndaCompanion.McpStub.exe"), (await Assert.ThrowsAsync<McpException>(() => c.ListToolsAsync(CancellationToken.None))).Message);
        }
        finally { Directory.Delete(link); }
    }

    [WindowsFact, System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void A_program_the_app_starts_is_in_its_job()
    {
        // round 41 review: the program the app starts is really put in the app's job (McpLocal.Real, as the app starts it)
        var info = new System.Diagnostics.ProcessStartInfo(Stub()) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var started = (McpLocal.RealProcess)McpLocal.Real.Start(info);
        try { Assert.True(McpJob.Holds(started.Process)); }
        finally { started.Kill(); }
    }

    [WindowsFact, System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void On_Windows_closing_the_job_stops_the_program_in_it()
    {
        // what happens when this app ends in any way, a crash included: Windows closes the app's job, and the program in it is stopped
        using var p = Process.Start(new ProcessStartInfo(Stub()) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true })!;
        try
        {
            var job = McpJob.Create();
            Assert.NotNull(job);
            Assert.True(McpJob.Assign(job!, p));
            Assert.False(p.WaitForExit(500));            // waiting on its input, so still running
            job!.Dispose();
            Assert.True(p.WaitForExit(5000));
        }
        finally { if (!p.HasExited) p.Kill(); }
    }
}
