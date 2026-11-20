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
    private static string Folder(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "anda-hover-companion") dir = dir.Parent;
        return Path.Combine(new[] { dir?.FullName ?? throw new DirectoryNotFoundException("anda-hover-companion folder not found") }.Concat(parts).ToArray());
    }

    /// <summary>The stub's program file as built with these tests: an .exe on Windows, a file with no extension elsewhere.</summary>
    private static string Stub()
    {
        string name = OperatingSystem.IsWindows() ? "AndaCompanion.McpStub.exe" : "AndaCompanion.McpStub";
        string config = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}") ? "Debug" : "Release";
        return Directory.GetFiles(Folder("app", "AndaCompanion.McpStub", "bin"), name, SearchOption.AllDirectories)
            .Where(f => f.Contains(Path.DirectorySeparatorChar + config + Path.DirectorySeparatorChar)).OrderByDescending(File.GetLastWriteTimeUtc).First();
    }

    private static McpServerSettings ServerFor(string program, string stub) => new()
    {
        Id = "stub", Name = "Fictional stub", IsProgram = true, Program = program, Enabled = true,
        ProgramSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(stub))).ToLowerInvariant(), ProgramArguments = new() { "--fictional" },
    };

    [Fact]
    public void The_job_limits_have_the_size_Windows_expects()
    {
        // JOBOBJECT_EXTENDED_LIMIT_INFORMATION is 144 bytes in a 64-bit process and 112 in a 32-bit one; a wrong layout would make
        // Windows refuse the limit, and the job would not stop programs
        Assert.Equal(IntPtr.Size == 8 ? 144 : 112, Marshal.SizeOf<McpJob.ExtendedLimits>());
        Assert.Equal(IntPtr.Size == 8 ? 64 : 48, Marshal.SizeOf<McpJob.BasicLimits>());
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

    [Fact]
    public async Task On_Windows_a_program_reached_through_a_link_is_refused()
    {
        if (!OperatingSystem.IsWindows()) return;
        string stub = Stub(), link = Path.Combine(Path.GetTempPath(), "anda-r41-link-" + Guid.NewGuid().ToString("N"));
        try { Directory.CreateSymbolicLink(link, Path.GetDirectoryName(stub)!); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return; }      // no right to make links on this machine
        try
        {
            string through = Path.Combine(link, Path.GetFileName(stub));
            var server = ServerFor(through, stub);
            Assert.Null(McpRules.ProgramProblem(server.Program, server.ProgramSha256));     // the path itself is well formed
            using var c = new McpClient(server);
            c.PermitStart();
            Assert.Equal(McpRules.SaidNotThere("AndaCompanion.McpStub.exe"), (await Assert.ThrowsAsync<McpException>(() => c.ListToolsAsync(CancellationToken.None))).Message);
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public void On_Windows_closing_the_job_stops_the_program_in_it()
    {
        if (!OperatingSystem.IsWindows()) return;
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
