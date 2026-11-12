using System.Text;
using AndaCompanion.Data;
using AndaCompanion.Models;
using AndaCompanion.Services.Portfolio;

namespace AndaCompanion.Services;

public enum CheckStatus { Pass, Warn, Fail, Skip }

public sealed record CheckResult(string Name, CheckStatus Status, string Detail);

/// <summary>Raw clipboard helpers for the self-test and the Windows CI tests. Windows only.</summary>
internal static class ClipboardProbe
{
    public static byte[] Utf16(string text) => Encoding.Unicode.GetBytes(text + "\0");

    public static bool Set(params (uint Format, byte[] Data)[] items)
    {
        if (!ClipboardNative.OpenWithRetry(ClipboardOwner.Handle)) return false;
        try
        {
            ClipboardNative.EmptyClipboard();
            foreach (var (format, data) in items)
            {
                IntPtr handle = ClipboardNative.AllocGlobal(data);
                if (handle == IntPtr.Zero) return false;
                if (ClipboardNative.SetClipboardData(format, handle) == IntPtr.Zero)
                {
                    ClipboardNative.GlobalFree(handle);
                    return false;
                }
            }
            return true;
        }
        finally { ClipboardNative.CloseClipboard(); }
    }

    public static bool SetText(string text) => Set((ClipboardNative.CfUnicodeText, Utf16(text)));

    public static bool Clear()
    {
        if (!ClipboardNative.OpenWithRetry(ClipboardOwner.Handle)) return false;
        try { return ClipboardNative.EmptyClipboard(); }
        finally { ClipboardNative.CloseClipboard(); }
    }

    public static byte[]? Get(uint format)
    {
        if (!ClipboardNative.OpenWithRetry(ClipboardOwner.Handle)) return null;
        try
        {
            IntPtr handle = ClipboardNative.GetClipboardData(format);
            if (handle == IntPtr.Zero) return null;
            long size = (long)ClipboardNative.GlobalSize(handle);
            IntPtr ptr = ClipboardNative.GlobalLock(handle);
            if (ptr == IntPtr.Zero) return null;
            try
            {
                var bytes = new byte[size];
                System.Runtime.InteropServices.Marshal.Copy(ptr, bytes, 0, (int)size);
                return bytes;
            }
            finally { ClipboardNative.GlobalUnlock(handle); }
        }
        finally { ClipboardNative.CloseClipboard(); }
    }

    public static string? GetText() => TextCaptureService.ReadUnicodeText();

    public static int CountFormats()
    {
        if (!ClipboardNative.OpenWithRetry(ClipboardOwner.Handle)) return -1;
        try
        {
            int n = 0;
            uint f = 0;
            while ((f = ClipboardNative.EnumClipboardFormats(f)) != 0) n++;
            return n;
        }
        finally { ClipboardNative.CloseClipboard(); }
    }

    /// <summary>True when the first <paramref name="expected"/>.Length bytes match (GlobalAlloc may pad).</summary>
    public static bool StartsWith(byte[]? actual, byte[] expected) =>
        actual is not null && actual.Length >= expected.Length && actual.AsSpan(0, expected.Length).SequenceEqual(expected);
}

/// <summary>
/// The self-test checks that need no windows. They are shared by <c>--selftest</c> and the
/// Windows-only xUnit tests, so the same assertions run in both places.
/// </summary>
internal static class SelfTestChecks
{
    /// <summary>
    /// Snapshot → overwrite → restore must give the user's clipboard back in every plain-memory
    /// format, leave an empty clipboard empty, and refuse an oversized one instead of half-saving it.
    /// </summary>
    public static CheckResult ClipboardRoundTrip()
    {
        const string name = "clipboard-snapshot-restore";
        if (!OperatingSystem.IsWindows()) return new(name, CheckStatus.Skip, "Windows only");
        var problems = new List<string>();
        try
        {
            uint html = ClipboardNative.RegisterFormat("HTML Format");
            uint custom = ClipboardNative.RegisterFormat("AndaCompanion.SelfTest.Custom");
            string original = "ANDA 216482 — original ✓";
            byte[] originalText = ClipboardProbe.Utf16(original);
            byte[] htmlBytes = Encoding.UTF8.GetBytes("Version:0.9\r\nStartHTML:0000000000\r\n<html><body><b>216482</b></body></html>");
            byte[] customBytes = Enumerable.Range(0, 2048).Select(i => (byte)(i * 31 % 251)).ToArray();

            if (!ClipboardProbe.Set((ClipboardNative.CfUnicodeText, originalText), (html, htmlBytes), (custom, customBytes)))
                return new(name, CheckStatus.Fail, "could not place test data on the clipboard");

            var snap = ClipboardSnapshot.Take();
            if (snap is null) return new(name, CheckStatus.Fail, "snapshot could not open the clipboard");
            if (snap.TooLarge) problems.Add("small clipboard reported as too large");

            if (!ClipboardProbe.SetText("something else entirely")) problems.Add("could not overwrite the clipboard");
            if (ClipboardProbe.GetText() != "something else entirely") problems.Add("overwrite not visible");

            if (!snap.Restore()) problems.Add("Restore() returned false");
            if (ClipboardProbe.GetText() != original) problems.Add($"text not restored (got '{ClipboardProbe.GetText()}')");
            if (!ClipboardProbe.StartsWith(ClipboardProbe.Get(html), htmlBytes)) problems.Add("HTML Format not restored");
            if (!ClipboardProbe.StartsWith(ClipboardProbe.Get(custom), customBytes)) problems.Add("custom format not restored");
            int preserved = snap.FormatCount;

            // An originally-empty clipboard must stay empty after a capture.
            ClipboardProbe.Clear();
            var empty = ClipboardSnapshot.Take();
            if (empty is null || empty.FormatCount != 0) problems.Add("empty clipboard not snapshotted as empty");
            ClipboardProbe.SetText("temporary capture");
            empty?.Restore();
            if (ClipboardProbe.CountFormats() != 0) problems.Add($"empty clipboard not restored empty ({ClipboardProbe.CountFormats()} formats)");

            // Oversized clipboards are refused outright.
            byte[] big = new byte[(int)ClipboardSnapshot.MaxBytes + 4096];
            if (ClipboardProbe.Set((custom, big)))
            {
                var tooBig = ClipboardSnapshot.Take();
                if (tooBig is null || !tooBig.TooLarge) problems.Add("oversized clipboard not flagged TooLarge");
            }
            else problems.Add("could not place oversized test data (skipped that assertion)");
            ClipboardProbe.Clear();

            return problems.Count == 0
                ? new(name, CheckStatus.Pass, $"{preserved} formats snapshotted and restored byte-for-byte; empty stays empty; >32 MB refused")
                : new(name, CheckStatus.Fail, string.Join("; ", problems));
        }
        catch (Exception ex) { return new(name, CheckStatus.Fail, ex.GetType().Name + ": " + ex.Message); }
    }

    /// <summary>Settings and prompts survive a save/load; a corrupt file falls back to defaults.</summary>
    public static CheckResult SettingsAndPrompts(string home)
    {
        const string name = "settings-and-prompts";
        var problems = new List<string>();
        try
        {
            Directory.CreateDirectory(home);
            string settingsPath = Path.Combine(home, "settings.json");
            string promptsPath = Path.Combine(home, "prompts.json");

            var s = new Settings
            {
                Appearance = Theming.AppearanceId.K, Consent = ConsentModel.PeekFirst, CardOpacity = 0.9,
                ActiveLens = "Facility focus", CaptureViaCopy = false, FirstRunDone = true, AutoHideSeconds = 40,
            };
            SettingsService.Save(s, settingsPath);
            var l = SettingsService.Load(settingsPath);
            if (l.Appearance != s.Appearance || l.Consent != s.Consent || Math.Abs(l.CardOpacity - 0.9) > 1e-9 ||
                l.ActiveLens != s.ActiveLens || l.CaptureViaCopy || !l.FirstRunDone || l.AutoHideSeconds != 40)
                problems.Add("settings did not round-trip");

            // A hand-edited auto-hide value that is not one of the offered choices falls back to the default.
            File.WriteAllText(settingsPath, "{ \"AutoHideSeconds\": 7 }");
            if (SettingsService.Load(settingsPath).AutoHideSeconds != UI.AutoHidePolicy.Default)
                problems.Add("an unsupported auto-hide value was not normalised");

            File.WriteAllText(settingsPath, "{ this is not json");
            var d = SettingsService.Load(settingsPath);
            if (d.Appearance != Theming.Appearances.Default || d.Appearance != Theming.AppearanceId.W ||
                !d.CaptureViaCopy || d.Consent != ConsentModel.Whisper || d.AutoHideSeconds != UI.AutoHidePolicy.Default)
                problems.Add("corrupt settings did not fall back to defaults");

            var lib = new PromptLibraryService(promptsPath);
            int starters = lib.Prompts.Count;
            if (starters < 3) problems.Add($"expected 3 starter lenses, found {starters}");
            lib.Upsert(new LensPrompt("Self-test lens", "Test text."));
            if (!new PromptLibraryService(promptsPath).Prompts.Any(p => p.Name == "Self-test lens")) problems.Add("lens not persisted");
            lib.Delete("Self-test lens");
            if (new PromptLibraryService(promptsPath).Prompts.Any(p => p.Name == "Self-test lens")) problems.Add("lens not deleted");

            return problems.Count == 0
                ? new(name, CheckStatus.Pass, "round-trip OK; corrupt file → defaults; starter lenses present; files written only under the test home")
                : new(name, CheckStatus.Fail, string.Join("; ", problems));
        }
        catch (Exception ex) { return new(name, CheckStatus.Fail, ex.GetType().Name + ": " + ex.Message); }
    }

    /// <summary>
    /// The Elsa placeholder resolves every configuration state to a clear message, never reaches the network
    /// without a safe endpoint and a written mapping, and falls back to offline instead of failing.
    /// </summary>
    public static CheckResult ElsaPlaceholder()
    {
        const string name = "elsa-placeholder";
        var problems = new List<string>();
        try
        {
            static Func<string, string?> Env(params (string K, string V)[] kv) => k => kv.FirstOrDefault(p => p.K == k).V;
            var provider = ("ANDA_COMPANION_PROVIDER", "elsa");
            var unfilled = new UnfilledElsaMapping();

            if (ElsaConfig.Inspect(Env(), unfilled).Status != ElsaStatus.NotSelected) problems.Add("not-selected state wrong");
            if (ElsaConfig.Inspect(Env(provider), unfilled).Status != ElsaStatus.MissingEndpoint) problems.Add("missing-endpoint state wrong");
            if (ElsaConfig.Inspect(Env(provider, (ElsaConfig.EndpointVar, "http://elsa.example.gov/api")), unfilled).Status != ElsaStatus.InsecureEndpoint)
                problems.Add("plain-http endpoint was not refused");
            var placeholder = ElsaConfig.Inspect(Env(provider, (ElsaConfig.EndpointVar, "https://elsa.example.gov/api")), unfilled);
            if (placeholder.Status != ElsaStatus.MappingNotFilled) problems.Add("unfilled-mapping state wrong");
            if (ChatCompleterFactory.Create(Env(provider, (ElsaConfig.EndpointVar, "https://elsa.example.gov/api")), unfilled) is not null)
                problems.Add("the factory returned an Elsa completer while the mapping is a placeholder");
            if (placeholder.Message.Contains("secret", StringComparison.OrdinalIgnoreCase)) problems.Add("message mentions a secret");

            return problems.Count == 0
                ? new(name, CheckStatus.Pass, "all five configuration states resolve to a plain message; plain-http refused; placeholder mapping keeps briefs offline; no network call made")
                : new(name, CheckStatus.Fail, string.Join("; ", problems));
        }
        catch (Exception ex) { return new(name, CheckStatus.Fail, ex.GetType().Name + ": " + ex.Message); }
    }

    /// <summary>Recognizer, repository, status line and the offline brief — everything that needs no UI.</summary>
    public static CheckResult DataAndBriefs(IAndaRepository repo)
    {
        const string name = "data-and-offline-brief";
        var problems = new List<string>();
        try
        {
            (string Text, string? Expected)[] cases =
            {
                ("216482", "216482"), ("ANDA 216482", "216482"), ("ANDA-216482", "216482"), ("see ANDA #214990.", "214990"),
                ("12345", null), ("1234567", null), ("abc", null), ("", null),
            };
            foreach (var (text, expected) in cases)
                if (AndaValidator.Extract(text) != expected)
                    problems.Add($"Extract('{text}') = '{AndaValidator.Extract(text)}', expected '{expected}'");

            foreach (var number in new[] { "216482", "214990" })
            {
                AndaRecord? r = repo.Find(number);
                if (r is null) { problems.Add($"{number} missing from the repository"); continue; }
                string line = StatusLine.Build(r);
                if (string.IsNullOrWhiteSpace(line) || line.Contains("null", StringComparison.OrdinalIgnoreCase))
                    problems.Add($"{number}: bad status line '{line}'");
                var brief = new OfflineBriefService().GetBriefAsync(r).GetAwaiter().GetResult();
                if (string.IsNullOrWhiteSpace(brief.Text) || brief.Generated)
                    problems.Add($"{number}: offline brief empty or mislabeled as generated");
            }
            if (repo.Find("999999") is not null) problems.Add("unknown number should not resolve");

            return problems.Count == 0
                ? new(name, CheckStatus.Pass, "recognizer, 2 records, status line and offline brief behave")
                : new(name, CheckStatus.Fail, string.Join("; ", problems));
        }
        catch (Exception ex) { return new(name, CheckStatus.Fail, ex.GetType().Name + ": " + ex.Message); }
    }

    /// <summary>
    /// Starts the published executable the way the browser starts a native-messaging host (the extension's origin as the first argument,
    /// pipes for stdin and stdout), asks it three things, and checks the answers and a clean exit. Hermetic: the child inherits the
    /// self-test's own data folder, reads nothing of the person's, and makes no network call.
    /// </summary>
    public static CheckResult NativeHostProcess()
    {
        const string name = "native-host-process";
        string? exe = Environment.ProcessPath;
        if (exe is null || !Path.GetFileNameWithoutExtension(exe).Equals("AndaCompanion", StringComparison.OrdinalIgnoreCase))
            return new(name, CheckStatus.Skip, "not running as the published executable, so it cannot start itself as a host");
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(exe)
            { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add($"chrome-extension://{NativeHostRegistration.DefaultExtensionId}/");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            using var p = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("the host did not start");
            var stdin = p.StandardInput.BaseStream; var stdout = p.StandardOutput.BaseStream;
            System.Text.Json.Nodes.JsonNode? Ask(string json)
            {
                NativeHost.WriteFrame(stdin, json);
                var read = Task.Run(() => NativeHost.ReadFrame(stdout));
                if (!read.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("no answer within 20 s");
                return read.Result is { } b ? System.Text.Json.Nodes.JsonNode.Parse(b) : null;
            }
            var problems = new List<string>();
            var hello = Ask("{\"id\":1,\"type\":\"hello\"}");
            long firstMs = clock.ElapsedMilliseconds;
            if (hello?["ok"]?.GetValue<bool>() != true || hello["protocol"]?.GetValue<int>() != NativeHostSession.Protocol) problems.Add("hello was not answered");
            var lookup = Ask("{\"id\":2,\"type\":\"lookup\",\"number\":\"216482\"}");
            if (lookup?["card"]?["brief"]?["tag"]?.GetValue<string>() != "OFFLINE BRIEF") problems.Add("lookup did not return a card");
            else if (!lookup["card"]!["watch"]![0]!["text"]!.GetValue<string>().Contains("falls on a weekend: Sat Oct 3")) problems.Add("the card lacked the weekend flag");
            var draft = Ask("{\"id\":3,\"type\":\"draft\",\"text\":\"ANDA 216482 is on track. Goal date Oct 10, 2026.\"}");
            if (draft?["findings"]?.AsArray().Any(f => f?["kind"]?.GetValue<string>() == "diff") != true) problems.Add("the draft check found nothing");
            stdin.Close();
            if (!p.WaitForExit(10000)) { p.Kill(); problems.Add("the host did not exit when the pipe closed"); }
            else if (p.ExitCode != 0) problems.Add($"the host exited with code {p.ExitCode}");
            return problems.Count == 0
                ? new(name, CheckStatus.Pass, $"the exe answered hello, a card lookup and a draft check over stdin/stdout and exited cleanly when the pipe closed; first answer after {firstMs} ms")
                : new(name, CheckStatus.Fail, string.Join("; ", problems));
        }
        catch (Exception ex) { return new(name, CheckStatus.Fail, ex.GetType().Name + ": " + ex.Message); }
    }

    /// <summary>
    /// The Phase 1 rules, with no UI and no network: federal-calendar dates, the weekend/holiday watch flag, the
    /// deterministic draft check, shortcut parsing, and reading a portfolio file. Uses a throwaway CSV in the
    /// self-test folder; nothing outside it is read or written.
    /// </summary>
    public static CheckResult Phase1Rules(IAndaRepository repo, string scratchFolder)
    {
        const string name = "calendar-watch-draft-portfolio";
        var problems = new List<string>();
        try
        {
            var cal = new BusinessCalendar();
            (DateOnly Date, string Name)[] mustBeClosed =
            {
                (new(2026, 1, 1), "New Year"), (new(2026, 7, 3), "Independence Day (observed)"),
                (new(2026, 11, 11), "Veterans Day"), (new(2026, 12, 25), "Christmas"),
                (new(2027, 6, 18), "Juneteenth (observed)"), (new(2027, 7, 5), "Independence Day (observed)"),
            };
            foreach (var (d, n) in mustBeClosed)
                if (cal.IsBusinessDay(d)) problems.Add($"{d:yyyy-MM-dd} ({n}) was treated as a business day");
            if (cal.KindOf(new DateOnly(2026, 10, 3)) != DayKind.Weekend) problems.Add("Sat 2026-10-03 not seen as a weekend");
            if (cal.NextBusinessDay(new DateOnly(2026, 10, 3)) != new DateOnly(2026, 10, 5)) problems.Add("next business day after Sat 2026-10-03 is wrong");

            var record = repo.Find("216482");
            if (record is null) problems.Add("demo record 216482 missing");
            else
            {
                var flags = WatchRules.Evaluate(record with { GoalDateValue = new DateOnly(2026, 10, 3) }, new DateOnly(2026, 9, 1), cal);
                if (!flags.Any(f => f.Code == "goal-date-calendar")) problems.Add("a goal date on a Saturday raised no flag");
            }

            var draft = DraftCheck.Check("ANDA 216482 is on track.", repo.Find);
            if (draft.NumbersChecked < 1) problems.Add("draft check did not read the ANDA number");
            if (DraftCheck.Check("ANDA 999999 is done.", repo.Find).Findings.All(f => f.Kind != DraftFindingKind.UnknownAnda)) problems.Add("draft check did not flag an unknown ANDA");

            if (!HotkeyParser.TryParse("Ctrl+Alt+Shift+Q", out _, out _, out _, out _)) problems.Add("a valid shortcut was refused");
            if (HotkeyParser.TryParse("Q", out _, out _, out _, out _)) problems.Add("a bare letter was accepted as a shortcut");

            Directory.CreateDirectory(scratchFolder);
            string csv = Path.Combine(scratchFolder, "portfolio-check.csv");
            File.WriteAllText(csv, "ANDA,Product,Goal date\n123456,Check drug,2026-12-01\n");
            var portfolio = new PortfolioRepository(csv, today: () => new DateOnly(2026, 11, 2));
            if (!portfolio.Status.Ok || portfolio.Find("123456") is not { } row) problems.Add("a sample portfolio file was not read");
            else if (row.HasForecast || row.HasProgress) problems.Add("a sheet without forecast/progress showed an estimate");
            File.Delete(csv);

            return problems.Count == 0
                ? new(name, CheckStatus.Pass, "federal-calendar dates, weekend flag, draft check, shortcut parsing and a sample portfolio file all behave; no network, nothing outside the self-test folder")
                : new(name, CheckStatus.Fail, string.Join("; ", problems));
        }
        catch (Exception ex) { return new(name, CheckStatus.Fail, ex.GetType().Name + ": " + ex.Message); }
    }
}
