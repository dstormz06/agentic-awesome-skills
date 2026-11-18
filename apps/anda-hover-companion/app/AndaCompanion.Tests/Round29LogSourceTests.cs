using System.Text.RegularExpressions;
using AndaCompanion.Services;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// Round 29, after the review: the event log keeps only its own sentences. This test reads the app's source and holds every call that writes
/// to the log to that list, so that a new call cannot write a sentence the log would withhold, and cannot pass text of the person's.
/// </summary>
[Collection("Global state")]
public class Round29LogSourceTests
{
    private sealed class Answer(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Task.FromResult(respond());
    }

    [Fact]
    public async Task A_connection_test_a_sign_in_and_a_download_are_requests_too_and_each_is_written_before_it_goes()
    {
        string path = Path.Combine(Temp.NewDir(), "events.csv");
        try
        {
            EventLog.Configure(path);
            // "Test" in Settings: a real request, with a fixed sentence.
            var api = new CustomApiSettings { Name = "Division API", Endpoint = "https://api.example.test/v1/chat", SecretEnvVar = "MY_TEST_TOKEN", Model = "m-1" };
            await ConnectionService.TestMyApiAsync(api, n => n == "MY_TEST_TOKEN" ? "t" : null, new Answer(() => new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)));
            await ConnectionService.TestMyApiAsync(api, _ => null, new Answer(() => new HttpResponseMessage(System.Net.HttpStatusCode.OK)));      // not set up: nothing is sent, nothing is written
            // Signing in to Outlook: the app's ID and the permission asked for go to Microsoft's sign-in address.
            var outlook = new OutlookSettings { Enabled = true, ClientId = "12345678-1234-1234-1234-123456789abc" };
            var svc = new OutlookService(() => outlook, new Answer(() => new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)), (_, _) => Task.CompletedTask, () => new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero));
            await svc.SignInAsync();
            // "Update now" for the reference file.
            await ReferenceDownloader.UpdateAsync("https://files.example.test/drugsfda.zip", Path.Combine(Temp.NewDir(), "drugsfda.zip"), new Answer(() => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)));
            Assert.Equal(new[] { ("sent", "connection test"), ("sent", "Outlook sign-in request"), ("sent", "request for the Drugs at FDA file") }, EventLog.Read().Entries.Select(e => (e.Type, e.Detail)));
            string file = File.ReadAllText(path);
            foreach (string secret in new[] { "example.test", "Division", "12345678", "MY_TEST_TOKEN" }) Assert.DoesNotContain(secret, file);
        }
        finally { EventLog.Configure(null); }
    }

    private static string AppSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "anda-hover-companion") dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("anda-hover-companion folder not found"), "app", "AndaCompanion");
    }

    [Fact]
    public void Every_call_that_writes_to_the_event_log_writes_one_of_the_logs_own_sentences()
    {
        char sep = Path.DirectorySeparatorChar;
        var calls = new List<(string File, string Detail)>();
        foreach (string file in Directory.EnumerateFiles(AppSource(), "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains(sep + "obj" + sep) && !f.Contains(sep + "bin" + sep)))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"EventLog\.Record\(""[a-z_]+"", (.*?)\);"))
                calls.Add((Path.GetFileName(file), m.Groups[1].Value));
        Assert.True(calls.Count >= 14, "the calls were not found: " + calls.Count);

        int literal = 0;
        foreach (var (file, detail) in calls)
        {
            // What a call may pass: sentences written in the call itself, and nothing else except a line built by Connections (held by the
            // shared cases), a count, or the name of one of the app's own files.
            string rest = Regex.Replace(detail, @"\$?""(?:[^""\\]|\\.)*""", "\"\"");
            Assert.True(Regex.IsMatch(rest, @"^(""""|line|what \+ """"|on \? """" : """"|Connections\.StartLine\(_connectionState\))$"), $"{file}: the log is given something other than its own sentences: {detail}");
            foreach (Match m in Regex.Matches(detail, @"\$?""((?:[^""\\]|\\.)*)"""))
            {
                string piece = Regex.Replace(m.Groups[1].Value, @"\{[^}]*\}", "#");      // a count stands where # is
                if (piece.Length == 0) continue;
                literal++;
                Assert.True(EventLog.Phrases.Any(p => p == piece || p.EndsWith(piece, StringComparison.Ordinal) && piece.StartsWith(' ')), $"{file}: not a sentence of the log: \"{piece}\"");
            }
        }
        Assert.True(literal >= 14, "the sentences were not found: " + literal);

        // The names of the app's own files that a start note may carry.
        foreach (string what in new[] { "settings file", "ANDA to-do list", "to-do list", "prompt library", "quick replies" })
            Assert.Contains(what + " could not be used as it was at start", EventLog.Phrases);
        // A count is always written the way the log reads it.
        foreach (long n in new long[] { 0, 7, 999, 1000, 216482, 1234567 })
            Assert.Equal($"AI request, {EventLog.Count(n)} characters", EventLog.Entry("sent", $"AI request, {EventLog.Count(n)} characters", DateTime.UtcNow).Detail);
    }
}
