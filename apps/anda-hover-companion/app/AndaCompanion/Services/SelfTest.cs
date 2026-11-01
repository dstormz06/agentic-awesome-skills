using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AndaCompanion.Data;
using AndaCompanion.Theming;
using AndaCompanion.UI;

namespace AndaCompanion.Services;

/// <summary>
/// <c>--selftest</c>: renders the real windows, drives the Windows integration for real and writes a
/// report. Its purpose is evidence. Every claim the demo makes about Windows behavior (hotkeys fire,
/// the card stays on-screen, the clipboard comes back intact, Segoe UI is used) is either confirmed
/// here on a real machine or reported as skipped with the reason. Nothing is silently assumed.
/// </summary>
internal static class SelfTest
{
    private static readonly Color Backdrop = Color.Parse("#E9E7E3");
    private static readonly PixelPoint Anchor = new(60, 60);

    public static async Task<int> RunAsync(
        SelfTestOptions options, IReadOnlyList<HotkeyStatus> hotkeys, bool trayCreated, string? trayError, IAndaRepository repo)
    {
        Directory.CreateDirectory(options.OutDir);
        var report = new Report(options.OutDir);
        // The checks below overwrite the clipboard with test data. Put the user's back afterwards.
        ClipboardSnapshot? userClipboard = OperatingSystem.IsWindows() ? ClipboardSnapshot.Take() : null;
        try
        {
            report.Add(Environment_(options));
            report.Add(Hotkeys(hotkeys));
            report.Add(trayCreated
                ? new("tray-icon", CheckStatus.Pass, "created (Windows 11 may place new icons under the ^ overflow until pinned)")
                : new("tray-icon", CheckStatus.Warn, "not created: " + (trayError ?? "unknown")));
            report.Add(SelfTestChecks.DataAndBriefs(repo));
            report.Add(SelfTestChecks.SettingsAndPrompts(Path.Combine(options.OutDir, "home", "check")));
            report.Add(Fonts());
            report.Add(SelfTestChecks.ClipboardRoundTrip());

            await RenderAllAsync(options, repo, report);
            report.Add(await PlacementAsync(repo));
            report.Add(await HotkeyDeliveryAsync(hotkeys));
            report.Add(await CaptureEndToEndAsync());

            report.Add(new("claude-connection", CheckStatus.Skip,
                "not exercised: the self-test makes no network calls. Use Settings → Claude connection → Test."));
        }
        catch (Exception ex)
        {
            report.Add(new("selftest-runner", CheckStatus.Fail, ex.ToString()));
        }
        ThemeManager.Apply(AppearanceId.A);
        if (userClipboard is not null && !userClipboard.TooLarge) userClipboard.Restore();
        report.Write();
        return report.HasFailures ? 1 : 0;
    }

    // ---------- plain checks ----------

    private static CheckResult Environment_(SelfTestOptions o)
    {
        string home = AppPaths.Root;
        bool hermetic = home.StartsWith(o.OutDir, StringComparison.OrdinalIgnoreCase);
        string detail =
            $"{RuntimeInformation.OSDescription} · {RuntimeInformation.OSArchitecture} · .NET {System.Environment.Version} · " +
            $"interactive={System.Environment.UserInteractive} · session={Process.GetCurrentProcess().SessionId} · " +
            $"Avalonia {typeof(Application).Assembly.GetName().Version} · data folder {(hermetic ? "isolated under the report folder" : home)}";
        return new("environment", hermetic ? CheckStatus.Pass : CheckStatus.Fail,
            hermetic ? detail : "self-test would have used the real data folder: " + detail);
    }

    private static CheckResult Hotkeys(IReadOnlyList<HotkeyStatus> status)
    {
        if (status.Count == 0) return new("hotkeys-registered", CheckStatus.Fail, "registration did not finish");
        var bad = status.Where(s => !s.Registered).ToList();
        string detail = string.Join(" · ", status.Select(s => $"{s.Binding.Combo} {(s.Registered ? "registered" : "FAILED (Windows error " + s.Win32Error + ")")}"));
        // A taken hotkey is a property of this machine, not a defect: Warn, with the user-facing advice.
        return bad.Count == 0 ? new("hotkeys-registered", CheckStatus.Pass, detail)
            : new("hotkeys-registered", CheckStatus.Warn, detail + " — " + string.Join(" ", bad.Select(HotkeyService.Describe)));
    }

    private static CheckResult Fonts()
    {
        var installed = new HashSet<string>(
            Avalonia.Media.FontManager.Current.SystemFonts.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
        string? Resolve(string stack) =>
            stack.Split(',').Select(n => n.Trim()).FirstOrDefault(installed.Contains);

        var lines = new List<string>();
        bool segoe = false;
        foreach (var stack in Appearances.All.SelectMany(a => new[] { a.BodyFont, a.NumFont, a.DrugFont }).Distinct())
        {
            string? hit = Resolve(stack);
            lines.Add($"[{stack.Split(',')[0].Trim()}…] → {hit ?? "generic fallback"}");
            if (hit is not null && hit.StartsWith("Segoe UI", StringComparison.OrdinalIgnoreCase)) segoe = true;
        }
        return new("fonts", segoe ? CheckStatus.Pass : CheckStatus.Warn,
            (segoe ? "Segoe UI in use. " : "Segoe UI not found; Inter fallback in use. ") + $"{installed.Count} system families. " + string.Join(" · ", lines));
    }

    // ---------- rendering ----------

    private sealed record CardShot(string Name, AppearanceId Id, string Anda, bool Expand, bool Mini, double Opacity, bool Desktop);

    private static async Task RenderAllAsync(SelfTestOptions o, IAndaRepository repo, Report report)
    {
        var briefs = new OfflineBriefService();
        var shots = new CardShot[]
        {
            new("card-a", AppearanceId.A, "216482", false, false, 1.0, true),
            new("card-a-expanded", AppearanceId.A, "216482", true, false, 1.0, true),
            new("card-a-healthy", AppearanceId.A, "214990", false, false, 1.0, false),
            new("card-a-peek", AppearanceId.A, "216482", false, true, 1.0, false),
            new("card-a-transparent-88", AppearanceId.A, "216482", false, false, 0.88, false),
            new("card-b-expanded", AppearanceId.B, "216482", true, false, 1.0, false),
            new("card-c", AppearanceId.C, "216482", false, false, 1.0, true),
            new("card-d", AppearanceId.D, "216482", false, false, 1.0, false),
            new("card-e-healthy", AppearanceId.E, "214990", false, false, 1.0, true),
        };

        foreach (var shot in shots)
        {
            try
            {
                ThemeManager.Apply(shot.Id, shot.Opacity);
                var window = new CardWindow(briefs, repo.Find) { CloseWhenDeactivated = false };
                window.Show(repo.Find(shot.Anda)!, Anchor, shot.Mini);
                if (shot.Expand) window.FindControl<Button>("ExpBtn")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                report.Add(await ShootAsync(window, shot.Name, o.OutDir, shot.Desktop));
                window.Close();
            }
            catch (Exception ex) { report.Add(new("render-" + shot.Name, CheckStatus.Fail, ex.Message)); }
        }

        ThemeManager.Apply(AppearanceId.A);
        var prompts = new PromptLibraryService(Path.Combine(o.OutDir, "home", "prompts.json"));

        await Safe(report, "settings", async () =>
        {
            var win = new SettingsWindow(new Settings(), prompts, () => { });
            win.Show();
            report.Add(await ShootAsync(win, "settings", o.OutDir, false));
            win.Close();
        });
        await Safe(report, "prompt-library", async () =>
        {
            var win = new PromptLibraryWindow(prompts, new Settings { ActiveLens = "Facility focus" }, () => { });
            win.Show();
            report.Add(await ShootAsync(win, "prompt-library", o.OutDir, false));
            win.Close();
        });
        await Safe(report, "lookup", async () =>
        {
            var win = new LookupWindow(_ => { });
            win.Show();
            report.Add(await ShootAsync(win, "lookup", o.OutDir, false));
            win.Close();
        });
        await Safe(report, "chip-invite", async () =>
        {
            var chip = new ChipWindow();
            chip.ShowInvite("ANDA 216482", Anchor, () => { });
            report.Add(await ShootAsync(chip, "chip-invite", o.OutDir, false));
            chip.Close();
        });
        await Safe(report, "chip-notice", async () =>
        {
            var chip = new ChipWindow();
            chip.ShowNotice("Paused in terminals. Type the number instead", Anchor);
            report.Add(await ShootAsync(chip, "chip-notice", o.OutDir, false));
            chip.Close();
        });
    }

    private static async Task Safe(Report report, string name, Func<Task> body)
    {
        try { await body(); }
        catch (Exception ex) { report.Add(new("render-" + name, CheckStatus.Fail, ex.Message)); }
    }

    /// <summary>
    /// Waits for the entrance animation, optionally saves what is really on the desktop, then renders
    /// the window itself over a light backdrop (so a transparent corner never reads as black).
    /// </summary>
    private static async Task<CheckResult> ShootAsync(Window w, string name, string outDir, bool desktop)
    {
        await Task.Delay(550);
        if (!w.IsVisible) return new("render-" + name, CheckStatus.Fail, "window closed itself before it could be captured");
        if (w.Bounds.Width < 1 || w.Bounds.Height < 1) return new("render-" + name, CheckStatus.Fail, "window has no size");

        string note = "";
        if (desktop)
        {
            bool grabbed = ScreenGrab.TrySave(Path.Combine(outDir, $"desktop-{name}.png"), out string d);
            note = grabbed ? $" · desktop {d}" : $" · desktop grab failed ({d})";
        }

        w.Background = new SolidColorBrush(Backdrop);
        await Task.Delay(120);
        double scale = w.RenderScaling;
        var size = new PixelSize((int)Math.Ceiling(w.Bounds.Width * scale), (int)Math.Ceiling(w.Bounds.Height * scale));
        using var rtb = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        rtb.Render(w);
        string path = Path.Combine(outDir, name + ".png");
        rtb.Save(path);

        long bytes = new FileInfo(path).Length;
        // A flat rectangle compresses to roughly 0.01 bytes per pixel; text and shapes land above 0.1.
        bool plausible = bytes > Math.Max(1500, 0.05 * size.Width * size.Height);
        return new("render-" + name, plausible ? CheckStatus.Pass : CheckStatus.Fail,
            $"{size.Width}x{size.Height}px @ {scale:0.##}x · {bytes / 1024} KB{note}" + (plausible ? "" : " — image looks blank"));
    }

    // ---------- real-screen behavior ----------

    /// <summary>The card must land fully inside the working area wherever the cursor is, including after it grows.</summary>
    private static async Task<CheckResult> PlacementAsync(IAndaRepository repo)
    {
        const string name = "card-placement-on-screen";
        try
        {
            ThemeManager.Apply(AppearanceId.A);
            var briefs = new OfflineBriefService();
            var probe = new CardWindow(briefs, repo.Find) { CloseWhenDeactivated = false };
            probe.Show(repo.Find("216482")!, new PixelPoint(0, 0), false);
            await Task.Delay(300);
            var screen = probe.Screens.Primary;
            probe.Close();
            if (screen is null) return new(name, CheckStatus.Skip, "no screen information available");
            var wa = screen.WorkingArea;

            var anchors = new (string Label, PixelPoint At)[]
            {
                ("top-left", new(wa.X, wa.Y)),
                ("top-right", new(wa.Right - 1, wa.Y)),
                ("bottom-left", new(wa.X, wa.Bottom - 1)),
                ("bottom-right (tray)", new(wa.Right - 1, wa.Bottom - 1)),
                ("center", new(wa.X + wa.Width / 2, wa.Y + wa.Height / 2)),
            };
            var problems = new List<string>();
            var trace = new List<string>();
            foreach (var (label, at) in anchors)
            {
                var card = new CardWindow(briefs, repo.Find) { CloseWhenDeactivated = false };
                card.Show(repo.Find("216482")!, at, false);
                card.FindControl<Button>("ExpBtn")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));   // worst case: biggest size
                await Task.Delay(500);
                double s = card.RenderScaling;
                var size = new PixelSize((int)Math.Ceiling(card.Bounds.Width * s), (int)Math.Ceiling(card.Bounds.Height * s));
                var rect = new PixelRect(card.Position, size);
                var expected = Placement.Near(at, size.Width, size.Height, wa);
                trace.Add($"{label}: anchor {at.X},{at.Y} → at {rect.X},{rect.Y} size {size.Width}x{size.Height} (expected x,y {expected.X},{expected.Y})");

                bool fits = size.Width <= wa.Width && size.Height <= wa.Height;
                bool inside = rect.X >= wa.X && rect.Y >= wa.Y && rect.Right <= wa.Right && rect.Bottom <= wa.Bottom;
                if (!fits) problems.Add($"{label}: card {size.Width}x{size.Height} is larger than the {wa.Width}x{wa.Height} work area and did not scroll");
                else if (!inside) problems.Add($"{label}: card {rect} outside working area {wa}");
                card.Close();
            }
            return problems.Count == 0
                ? new(name, CheckStatus.Pass,
                    $"expanded card stayed inside the {wa.Width}x{wa.Height} working area at all 5 anchors · scaling {screen.Scaling:0.##}x · " + string.Join(" | ", trace))
                : new(name, CheckStatus.Fail, string.Join("; ", problems) + " · trace: " + string.Join(" | ", trace));
        }
        catch (Exception ex) { return new(name, CheckStatus.Fail, ex.Message); }
    }

    /// <summary>Presses the real hotkeys with injected keystrokes and waits for the app to see them.</summary>
    private static async Task<CheckResult> HotkeyDeliveryAsync(IReadOnlyList<HotkeyStatus> hotkeys)
    {
        const string name = "hotkeys-delivered";
        if (!OperatingSystem.IsWindows()) return new(name, CheckStatus.Skip, "Windows only");
        if (hotkeys.Count == 0 || hotkeys.Any(h => !h.Registered))
            return new(name, CheckStatus.Skip, "a hotkey is not registered on this machine, so delivery cannot be tested");

        var seen = new Dictionary<int, TaskCompletionSource>();
        foreach (var h in hotkeys) seen[h.Binding.Id] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnSeen(int id) { if (seen.TryGetValue(id, out var tcs)) tcs.TrySetResult(); }
        App.HotkeyObserved += OnSeen;
        try
        {
            var problems = new List<string>();
            foreach (var h in hotkeys)
            {
                ushort vk = (ushort)h.Binding.VirtualKey;
                TextCaptureService.SendKeys((0x11, false), (0x12, false), (vk, false), (vk, true), (0x12, true), (0x11, true));
                var done = await Task.WhenAny(seen[h.Binding.Id].Task, Task.Delay(2500));
                if (done != seen[h.Binding.Id].Task) problems.Add($"{h.Binding.Combo} was injected but never reached the app");
                await Task.Delay(150);
            }
            return problems.Count == 0
                ? new(name, CheckStatus.Pass, "Ctrl+Alt+A and Ctrl+Alt+Space, pressed as real keystrokes, both reached the hotkey thread")
                : new(name, CheckStatus.Fail, string.Join("; ", problems));
        }
        finally { App.HotkeyObserved -= OnSeen; }
    }

    /// <summary>
    /// The riskiest code in the product, run for real: a window with selected text is brought to the
    /// front, CaptureSelection sends Ctrl+C, and we verify the number comes back AND the user's
    /// clipboard is exactly as it was. Skipped (never faked) if Windows will not let the test window
    /// take the foreground, which happens in locked or non-interactive sessions.
    /// </summary>
    private static async Task<CheckResult> CaptureEndToEndAsync()
    {
        const string name = "select-and-peek-end-to-end";
        if (!OperatingSystem.IsWindows()) return new(name, CheckStatus.Skip, "Windows only");
        Window? win = null;
        try
        {
            const string sentence = "Please review ANDA 216482 before Friday.";
            var box = new TextBox { Text = sentence, FontSize = 16, Margin = new Thickness(16), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            win = new Window
            {
                Width = 560, Height = 120, Topmost = true, Title = "Self-test copy target",
                Content = box, WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(Colors.White),
            };
            win.Show();
            win.Activate();
            await Task.Delay(300);
            box.Focus();

            IntPtr hwnd = win.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            bool foreground = false;
            for (int attempt = 0; attempt < 8 && !foreground; attempt++)
            {
                if (attempt > 0)
                {
                    // The classic permission trick: a synthetic Alt tap lets this process take the foreground.
                    TextCaptureService.SendKeys((0x12, false), (0x12, true));
                    SelfTestNative.SetForegroundWindow(hwnd);
                    win.Activate();
                    box.Focus();
                }
                await Task.Delay(250);
                foreground = hwnd != IntPtr.Zero && SelfTestNative.GetForegroundWindow() == hwnd;
            }
            if (!foreground)
                return new(name, CheckStatus.Skip,
                    "Windows would not let the test window take the foreground (locked or non-interactive session). " +
                    "Select text in Notepad and press Ctrl+Alt+A to test this by hand.");

            var problems = new List<string>();
            uint custom = ClipboardNative.RegisterFormat("AndaCompanion.SelfTest.Custom");
            byte[] customBytes = Enumerable.Range(0, 512).Select(i => (byte)(i * 7 % 253)).ToArray();
            string sentinel = "USER-CLIPBOARD-" + Guid.NewGuid().ToString("N")[..8];

            async Task<CaptureResult> Capture() => await Task.Run(TextCaptureService.CaptureSelection);
            async Task Select(int start, int length)
            {
                box.Focus();
                box.SelectionStart = start;
                box.SelectionEnd = start + length;
                await Task.Delay(150);
            }
            int at = sentence.IndexOf("216482", StringComparison.Ordinal);

            // 1. A normal selection; the user's clipboard (text + a custom format) must survive.
            ClipboardProbe.Set((ClipboardNative.CfUnicodeText, ClipboardProbe.Utf16(sentinel)), (custom, customBytes));
            await Select(at, 6);
            var r1 = await Capture();
            if (r1.Outcome != CaptureOutcome.Captured || AndaValidator.Extract(r1.Text) != "216482")
                problems.Add($"selection case: outcome {r1.Outcome}, text '{r1.Text}'");
            if (ClipboardProbe.GetText() != sentinel) problems.Add($"clipboard text not restored (now '{ClipboardProbe.GetText()}')");
            if (!ClipboardProbe.StartsWith(ClipboardProbe.Get(custom), customBytes)) problems.Add("clipboard custom format not restored");

            // 2. The selection is IDENTICAL to what is already on the clipboard; must still be detected.
            ClipboardProbe.SetText("216482");
            await Select(at, 6);
            var r2 = await Capture();
            if (r2.Outcome != CaptureOutcome.Captured || AndaValidator.Extract(r2.Text) != "216482")
                problems.Add($"same-as-clipboard case: outcome {r2.Outcome}");

            // 3. Nothing selected; must say so and must not disturb the clipboard.
            ClipboardProbe.SetText(sentinel);
            await Select(3, 0);
            var r3 = await Capture();
            if (r3.Outcome != CaptureOutcome.NothingSelected) problems.Add($"no-selection case: outcome {r3.Outcome}");
            if (ClipboardProbe.GetText() != sentinel) problems.Add("clipboard disturbed when nothing was selected");

            return problems.Count == 0
                ? new(name, CheckStatus.Pass,
                    "captured 216482 from a real selection; also when identical to the clipboard; reported 'nothing selected' cleanly; clipboard restored every time")
                : new(name, CheckStatus.Fail, string.Join("; ", problems));
        }
        catch (Exception ex) { return new(name, CheckStatus.Fail, ex.GetType().Name + ": " + ex.Message); }
        finally { win?.Close(); }
    }

    private static class SelfTestNative
    {
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    }

    // ---------- report ----------

    private sealed class Report
    {
        private readonly string _dir;
        private readonly List<CheckResult> _checks = new();
        private readonly DateTime _started = DateTime.UtcNow;

        public Report(string dir) => _dir = dir;
        public bool HasFailures => _checks.Any(c => c.Status == CheckStatus.Fail);
        public void Add(CheckResult c) => _checks.Add(c);

        public void Write()
        {
            var counts = Enum.GetValues<CheckStatus>().ToDictionary(s => s.ToString().ToLowerInvariant(), s => _checks.Count(c => c.Status == s));
            var json = new { app = "ANDA Hover Companion", startedUtc = _started, counts, checks = _checks };
            File.WriteAllText(Path.Combine(_dir, "report.json"), JsonSerializer.Serialize(json,
                new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } }));

            var md = new StringBuilder();
            md.AppendLine("# ANDA Hover Companion — self-test report");
            md.AppendLine();
            md.AppendLine($"{_started:u} · pass {counts["pass"]} · warn {counts["warn"]} · fail {counts["fail"]} · skipped {counts["skip"]}");
            md.AppendLine();
            md.AppendLine("| Result | Check | Detail |");
            md.AppendLine("|---|---|---|");
            foreach (var c in _checks)
            {
                string mark = c.Status switch { CheckStatus.Pass => "PASS", CheckStatus.Warn => "WARN", CheckStatus.Fail => "FAIL", _ => "SKIP" };
                md.AppendLine($"| {mark} | {c.Name} | {c.Detail.Replace("|", "/").Replace("\r", " ").Replace("\n", " ")} |");
            }
            md.AppendLine();
            md.AppendLine("PASS = confirmed on this machine. WARN = works but something about this machine needs attention. " +
                          "SKIP = could not be tested here (reason given). FAIL = a defect.");
            File.WriteAllText(Path.Combine(_dir, "report.md"), md.ToString());
        }
    }
}
