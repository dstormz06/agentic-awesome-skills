using System.Text;
using AndaCompanion.Data;
using AndaCompanion.Models;

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
                Appearance = Theming.AppearanceId.E, Consent = ConsentModel.Whisper, CardOpacity = 0.9,
                ActiveLens = "Facility focus", CaptureViaCopy = false, FirstRunDone = true,
            };
            SettingsService.Save(s, settingsPath);
            var l = SettingsService.Load(settingsPath);
            if (l.Appearance != s.Appearance || l.Consent != s.Consent || Math.Abs(l.CardOpacity - 0.9) > 1e-9 ||
                l.ActiveLens != s.ActiveLens || l.CaptureViaCopy || !l.FirstRunDone)
                problems.Add("settings did not round-trip");

            File.WriteAllText(settingsPath, "{ this is not json");
            var d = SettingsService.Load(settingsPath);
            if (d.Appearance != Theming.AppearanceId.A || !d.CaptureViaCopy || d.Consent != ConsentModel.Full)
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
                    problems.Add($"{number}: offline brief empty or mislabelled as generated");
            }
            if (repo.Find("999999") is not null) problems.Add("unknown number should not resolve");

            return problems.Count == 0
                ? new(name, CheckStatus.Pass, "recognizer, 2 records, status line and offline brief behave")
                : new(name, CheckStatus.Fail, string.Join("; ", problems));
        }
        catch (Exception ex) { return new(name, CheckStatus.Fail, ex.GetType().Name + ": " + ex.Message); }
    }
}
