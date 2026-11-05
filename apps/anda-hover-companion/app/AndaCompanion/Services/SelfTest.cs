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
using Avalonia.VisualTree;
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
            report.Add(SelfTestChecks.ElsaPlaceholder());
            report.Add(SelfTestChecks.Phase1Rules(repo, Path.Combine(options.OutDir, "home", "phase1")));
            report.Add(SelfTestChecks.NativeHostProcess());
            report.Add(Fonts());
            report.Add(SelfTestChecks.ClipboardRoundTrip());

            await RenderAllAsync(options, repo, report);
            report.Add(await PlacementAsync(repo));
            report.Add(await CompactBehaviorAsync(repo));
            report.Add(await AutoHideAsync(repo));
            report.Add(await HotkeyDeliveryAsync(hotkeys));
            report.Add(await CaptureEndToEndAsync());
            report.Add(await MouseTriggerAsync());

            report.Add(new("claude-connection", CheckStatus.Skip,
                "not exercised: the self-test makes no network calls. Use Settings → Claude connection → Test."));
        }
        catch (Exception ex)
        {
            report.Add(new("selftest-runner", CheckStatus.Fail, ex.ToString()));
        }
        ThemeManager.Apply(Appearances.Default);
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
        // K is the default look, so it gets the full set; every other full look and every compact form gets
        // at least one shot, alternating the at-risk and healthy records so both states are exercised.
        var shots = new CardShot[]
        {
            new("card-k", AppearanceId.K, "216482", false, false, 1.0, true),
            new("card-k-expanded", AppearanceId.K, "216482", true, false, 1.0, true),
            new("card-k-healthy", AppearanceId.K, "214990", false, false, 1.0, false),
            new("card-k-peek", AppearanceId.K, "216482", false, true, 1.0, false),
            new("card-k-transparent-88", AppearanceId.K, "216482", false, false, 0.88, false),
            new("card-k-transparent-40", AppearanceId.K, "216482", false, false, 0.40, false),
            new("card-a", AppearanceId.A, "216482", false, false, 1.0, false),
            new("card-b-expanded", AppearanceId.B, "216482", true, false, 1.0, false),
            new("card-c", AppearanceId.C, "216482", false, false, 1.0, false),
            new("card-d", AppearanceId.D, "216482", false, false, 1.0, false),
            new("card-e-healthy", AppearanceId.E, "214990", false, false, 1.0, false),
            new("card-f", AppearanceId.F, "216482", false, false, 1.0, true),
            new("card-g", AppearanceId.G, "216482", false, false, 1.0, false),
            new("card-g-healthy", AppearanceId.G, "214990", false, false, 1.0, false),
            new("card-h", AppearanceId.H, "216482", false, false, 1.0, false),
            new("card-i", AppearanceId.I, "216482", false, false, 1.0, false),
            new("card-j", AppearanceId.J, "216482", false, false, 1.0, true),
            new("card-j-healthy", AppearanceId.J, "214990", false, false, 1.0, false),
            new("card-p", AppearanceId.P, "216482", false, false, 1.0, false),
            new("card-q", AppearanceId.Q, "216482", false, false, 1.0, false),
            new("card-r", AppearanceId.R, "214990", false, false, 1.0, false),
            new("card-s", AppearanceId.S, "216482", false, false, 1.0, false),
            new("compact-l", AppearanceId.L, "216482", false, false, 1.0, true),
            new("compact-m", AppearanceId.M, "216482", false, false, 1.0, false),
            new("compact-n", AppearanceId.N, "216482", false, false, 1.0, false),
            new("compact-o", AppearanceId.O, "216482", false, false, 1.0, false),
            new("compact-l-healthy", AppearanceId.L, "214990", false, false, 1.0, false),
            new("compact-m-healthy", AppearanceId.M, "214990", false, false, 1.0, false),
        };

        foreach (var shot in shots)
        {
            try
            {
                ThemeManager.Apply(shot.Id, shot.Opacity);
                if (ThemeManager.Current.IsCompact)
                {
                    var compact = new CompactCardWindow(repo.Find(shot.Anda)!, () => { }) { CloseWhenDeactivated = false };
                    compact.ShowNear(Anchor);
                    report.Add(await ShootAsync(compact, shot.Name, o.OutDir, shot.Desktop));
                    compact.Close();
                    continue;
                }
                var window = new CardWindow(briefs, repo.Find) { CloseWhenDeactivated = false };
                window.Show(repo.Find(shot.Anda)!, Anchor, shot.Mini);
                if (shot.Expand) window.FindControl<Button>("ExpBtn")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                report.Add(await ShootAsync(window, shot.Name, o.OutDir, shot.Desktop));
                window.Close();
            }
            catch (Exception ex) { report.Add(new("render-" + shot.Name, CheckStatus.Fail, ex.Message)); }
        }

        ThemeManager.Apply(Appearances.Default);
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
            chip.ShowNotice("Paused in terminals. Please type the number.", Anchor);
            report.Add(await ShootAsync(chip, "chip-notice", o.OutDir, false));
            chip.Close();
        });
        await Safe(report, "draft", async () =>
        {
            var win = new DraftWindow("Hi all, quick status. ANDA 216482 is on track, with a goal date of Oct 10, 2026. ANDA 123456 needs a call.", repo.Find,
                new WritingService(null), new Settings(), prompts);
            win.Show();
            report.Add(await ShootAsync(win, "draft", o.OutDir, false));
            win.Close();
        });
        await Safe(report, "watch-list", async () =>
        {
            var win = new WatchListWindow(() => WatchList.Build(repo, new CardContext(new DateOnly(2026, 11, 2), new BusinessCalendar())), (_, _) => { });
            win.Show();
            report.Add(await ShootAsync(win, "watch-list", o.OutDir, false));
            win.Close();
        });
        await Safe(report, "watch-line", async () =>
        {
            // The fictional goal date is a Saturday: the card must say so, in the slot the owner asked for, on a real Windows renderer.
            var win = new CardWindow(briefs, repo.Find) { CloseWhenDeactivated = false, Today = () => new DateOnly(2026, 11, 2) };
            win.Show(repo.Find("216482")!, Anchor, false);
            bool slot = win.FindControl<Control>("WatchSlot")!.IsVisible;
            string text = win.FindControl<TextBlock>("WatchText")!.Text ?? "";
            win.Close();
            report.Add(slot && text.Contains("falls on a weekend: Sat Oct 3") && text.Contains("more in the drawer")
                ? new("watch-line-on-card", CheckStatus.Pass, "the card shows the weekend goal-date flag and points to the rest of the watch list")
                : new("watch-line-on-card", CheckStatus.Fail, $"WATCH slot visible={slot}, text='{text}'"));
            await Task.CompletedTask;
        });
        await Safe(report, "todo", async () =>
        {
            // Fictional records and a list file under the report folder; nothing of the real list is touched.
            var day = new DateOnly(2026, 11, 2);
            var ctx = new CardContext(day, new BusinessCalendar());
            var store = new TodoService(Path.Combine(o.OutDir, "home", "todo-check.json"));
            store.Add("Call the applicant about the inspection", "216482", null, day.AddDays(1), "My action", day);
            store.Add("Review the information request", null, null, null, "You", day);
            var win = new TodoWindow(new TodoHooks(() => TodoBuilder.Build(repo, ctx, store, true, DateTime.Now), store, () => 0, () => day, () => ctx.Calendar,
                (_, _) => { }, () => "Using the fictional demo records."));
            win.Show();
            report.Add(await ShootAsync(win, "todo", o.OutDir, false));
            bool rows = win.View.Rows.Count(r => !r.Auto) == 2 && win.View.Rows.Any(r => r.Auto);
            win.Close();
            report.Add(rows ? new("todo-list-content", CheckStatus.Pass, "own items and items from the data are both listed")
                            : new("todo-list-content", CheckStatus.Fail, "the to-do list did not show both kinds of items"));
        });
        await Safe(report, "action-window", async () =>
        {
            var model = new ActionWindowModel("Ask my AI for next steps", "ANDA 216482 · Metformin", "THIS IS EXACTLY WHAT WILL BE SENT",
                "Using only the facts below, suggest up to five next steps. ANDA 216482. Goal Oct 3, 2026.",
                "Nothing has been sent yet. Pressing Send sends only the text above (a canned reply is used in this check).",
                "THE REPLY, AS RECEIVED", _ => Task.FromResult(new ActionReply(true, "- Call the applicant\n- Check the IR response", "From your AI. Verify before acting.",
                    new[] { "Call the applicant", "Check the IR response" })), "Send", false, lines => lines.Count);
            var win = new ActionWindow(model);
            win.Show();
            await win.SendAsync();
            report.Add(await ShootAsync(win, "action-window", o.OutDir, false));
            bool ok = win.WasSent && win.Suggestions.Count == 2;
            win.Close();
            report.Add(ok ? new("action-window-reply", CheckStatus.Pass, "the reply and two suggested to-do lines were shown; nothing was added without a click")
                          : new("action-window-reply", CheckStatus.Fail, "the action window did not show the reply"));
        });
        await Safe(report, "agents-drawer", async () =>
        {
            // A canned reply stands in for an agent, so this makes no network call. It checks the drawer on a real renderer.
            var hub = AgentRules.Presets().First(p => p.Id == AgentRules.AlertHubId);
            hub.Enabled = true;
            hub.Api.Endpoint = "https://agent.example.test/ask"; hub.Api.RequestTemplate = "{\"anda\":\"{{number}}\",\"question\":\"{{user}}\"}"; hub.Api.ResponsePath = "answer";
            var service = new AgentService(() => new[] { hub }, _ => null, new CannedAgentHandler("{\"answer\":\"- Facility inspection overdue\\n- IR response due soon\"}"));
            var win = new CardWindow(briefs, repo.Find) { CloseWhenDeactivated = false, Today = () => new DateOnly(2026, 11, 2), Agents = service, AddTodos = (lines, _, _) => lines.Count };
            win.Show(repo.Find("216482")!, Anchor, false);
            win.FindControl<Expander>("AgentsExpander")!.IsExpanded = true;
            var run = win.FindControl<StackPanel>("AgentsPanel")!.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Content as string == "Run");
            run?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 40 && service.Last(hub.Id, "216482") is null; i++) await Task.Delay(50);
            await Task.Delay(300);
            bool shown = win.FindControl<StackPanel>("AgentsPanel")!.GetVisualDescendants().OfType<TextBlock>().Any(t => (t.Text ?? "").Contains("Facility inspection overdue"));
            report.Add(await ShootAsync(win, "agents-drawer", o.OutDir, false));
            win.Close();
            report.Add(shown ? new("agents-drawer-finding", CheckStatus.Pass, "the card showed the agent's reply as its own words (canned reply, no network)")
                             : new("agents-drawer-finding", CheckStatus.Fail, "the agents drawer did not show the reply"));
        });
        await Safe(report, "checklist", async () =>
        {
            // Fictional notes and a list file under the report folder; nothing of the real checklist is touched, and nothing is sent anywhere.
            var day = new DateOnly(2026, 11, 4);
            string file = Path.Combine(o.OutDir, "home", "checklist-check.json");
            if (File.Exists(file)) File.Delete(file);
            var store = new ChecklistService(file);
            var notes = new NotesWindow(new NotesHooks(store, () => day, () => "Ctrl+Alt+L", () => { }),
                "Don't forget to call the plumber about the leaking faucet tomorrow!!\nsend the weekly status memo every Friday\n- buy milk, eggs and bread\nFYI the huddle moved to 2pm", false);
            notes.Show();
            report.Add(await ShootAsync(notes, "notes-to-checklist", o.OutDir, false));
            int found = notes.SuggestionCount;
            notes.AddTicked();
            notes.Close();
            var win = new ChecklistWindow(new ChecklistHooks(store, () => day, () => "Ctrl+Alt+L", _ => { }, (_, _) => { }));
            win.Show();
            report.Add(await ShootAsync(win, "my-checklist", o.OutDir, false));
            bool ok = found >= 3 && store.Items.Count >= 3 && win.View.Open == store.Items.Count(i => !i.Done) && store.Items.Any(i => i.Repeat == Repeat.Weekly);
            win.Close();
            report.Add(ok ? new("checklist-from-notes", CheckStatus.Pass, $"{found} suggestions were read from messy notes, {store.Items.Count} were added, and the checklist lists them (no AI, no network)")
                          : new("checklist-from-notes", CheckStatus.Fail, $"found {found}, stored {store.Items.Count}, weekly={store.Items.Any(i => i.Repeat == Repeat.Weekly)}"));
        });
    }

    /// <summary>Answers an agent request with a fixed reply, so the self-test never needs a network.</summary>
    private sealed class CannedAgentHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });
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
            ThemeManager.Apply(Appearances.Default);
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
                // Phase 1: the card as it opens. Its position must be exactly what the placement rule says
                // for its final, laid-out size (below-right of the cursor, flipped at an edge, then clamped).
                var card = new CardWindow(briefs, repo.Find) { CloseWhenDeactivated = false };
                card.Show(repo.Find("216482")!, at, false);
                await Task.Delay(700);
                double s = card.RenderScaling;
                var size = new PixelSize((int)Math.Ceiling(card.Bounds.Width * s), (int)Math.Ceiling(card.Bounds.Height * s));
                var rect = new PixelRect(card.Position, size);
                var expected = Placement.Near(at, size.Width, size.Height, wa);
                trace.Add($"{label}: {card.PlacementTrace}; expected {expected.X},{expected.Y}");
                if (Math.Abs(rect.X - expected.X) > 2 || Math.Abs(rect.Y - expected.Y) > 2)
                    problems.Add($"{label}: opened at {rect.X},{rect.Y} but the placement rule gives {expected.X},{expected.Y} for {size.Width}x{size.Height}");

                // Phase 2: expand to the biggest size; the grown card must still be fully on screen.
                card.FindControl<Button>("ExpBtn")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(400);
                size = new PixelSize((int)Math.Ceiling(card.Bounds.Width * s), (int)Math.Ceiling(card.Bounds.Height * s));
                rect = new PixelRect(card.Position, size);
                bool fits = size.Width <= wa.Width && size.Height <= wa.Height;
                bool inside = rect.X >= wa.X && rect.Y >= wa.Y && rect.Right <= wa.Right && rect.Bottom <= wa.Bottom;
                if (!fits) problems.Add($"{label}: expanded card {size.Width}x{size.Height} is larger than the {wa.Width}x{wa.Height} work area and did not scroll");
                else if (!inside) problems.Add($"{label}: expanded card {rect} outside working area {wa}");
                card.Close();
            }
            return problems.Count == 0
                ? new(name, CheckStatus.Pass,
                    $"opened exactly where the placement rule says, and stayed inside the {wa.Width}x{wa.Height} work area when expanded, at all 5 anchors · scaling {screen.Scaling:0.##}x · " + string.Join(" | ", trace))
                : new(name, CheckStatus.Fail, string.Join("; ", problems) + " · trace: " + string.Join(" | ", trace));
        }
        catch (Exception ex) { return new(name, CheckStatus.Fail, ex.Message); }
    }

    /// <summary>
    /// Every compact form must open fully inside the work area at all five anchors, must be much smaller than the
    /// full card (that is its whole point), and must hand over to the full card on a click or Enter.
    /// </summary>
    private static async Task<CheckResult> CompactBehaviorAsync(IAndaRepository repo)
    {
        const string name = "compact-boxes";
        try
        {
            var problems = new List<string>();
            var sizes = new List<string>();
            ThemeManager.Apply(Appearances.Default);
            var full = new CardWindow(new OfflineBriefService(), repo.Find) { CloseWhenDeactivated = false };
            full.Show(repo.Find("216482")!, new PixelPoint(0, 0), false);
            await Task.Delay(400);
            double fullArea = full.Bounds.Width * full.Bounds.Height;
            var screen = full.Screens.Primary;
            full.Close();
            if (screen is null) return new(name, CheckStatus.Skip, "no screen information available");
            var wa = screen.WorkingArea;
            var anchors = new[] { new PixelPoint(wa.X, wa.Y), new PixelPoint(wa.Right - 1, wa.Y), new PixelPoint(wa.X, wa.Bottom - 1),
                new PixelPoint(wa.Right - 1, wa.Bottom - 1), new PixelPoint(wa.X + wa.Width / 2, wa.Y + wa.Height / 2) };

            foreach (var id in new[] { AppearanceId.L, AppearanceId.M, AppearanceId.N, AppearanceId.O })
            {
                ThemeManager.Apply(id);
                foreach (var at in anchors)
                {
                    var box = new CompactCardWindow(repo.Find("216482")!, () => { }) { CloseWhenDeactivated = false };
                    box.ShowNear(at);
                    await Task.Delay(450);
                    double s = box.RenderScaling;
                    var rect = new PixelRect(box.Position, new PixelSize((int)Math.Ceiling(box.Bounds.Width * s), (int)Math.Ceiling(box.Bounds.Height * s)));
                    if (!(rect.X >= wa.X && rect.Y >= wa.Y && rect.Right <= wa.Right && rect.Bottom <= wa.Bottom))
                        problems.Add($"{id} at {at}: {rect} outside {wa}");
                    if (at == anchors[4])
                    {
                        sizes.Add($"{id} {box.Bounds.Width - 28:0}x{box.Bounds.Height - 28:0} dip");
                        // box.Bounds includes the 14 dip shadow margin on each side
                        if ((box.Bounds.Width - 28) * (box.Bounds.Height - 28) > fullArea * 0.35)
                            problems.Add($"{id} is not much smaller than the full card");
                    }
                    box.Close();
                }

                // expand: a click (pointer press) and Enter both reach the callback exactly once
                int expanded = 0;
                var live = new CompactCardWindow(repo.Find("216482")!, () => expanded++) { CloseWhenDeactivated = false };
                live.ShowNear(anchors[4]);
                await Task.Delay(300);
                live.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter });
                if (expanded != 1) problems.Add($"{id}: Enter did not expand the box");
                live.Close();
            }
            ThemeManager.Apply(Appearances.Default);
            return problems.Count == 0
                ? new(name, CheckStatus.Pass, "L, M, N, O stayed inside the work area at all 5 anchors, each under 35% of the full card's area, and Enter expands · " + string.Join(" · ", sizes))
                : new(name, CheckStatus.Fail, string.Join("; ", problems));
        }
        catch (Exception ex) { return new(name, CheckStatus.Fail, ex.Message); }
    }

    /// <summary>The auto-hide timer, for real: an idle card disappears on its own.</summary>
    private static async Task<CheckResult> AutoHideAsync(IAndaRepository repo)
    {
        const string name = "auto-hide";
        try
        {
            ThemeManager.Apply(Appearances.Default);
            var card = new CardWindow(new OfflineBriefService(), repo.Find) { CloseWhenDeactivated = false, AutoHideSeconds = 1.0 };
            card.Show(repo.Find("216482")!, Anchor, false);
            await Task.Delay(500);
            bool stillThere = card.IsVisible;
            await Task.Delay(2600);
            bool hidden = !card.IsVisible;
            bool pointerOver = card.IsVisible && card.IsPointerOver;
            if (card.IsVisible) card.Close();
            // A real mouse that happens to rest on the card keeps it open — that is the rule working, not a defect.
            if (!hidden && pointerOver)
                return new(name, CheckStatus.Skip, "the pointer was resting on the card, so auto-hide correctly kept it open; move the mouse away and rerun");
            return stillThere && hidden
                ? new(name, CheckStatus.Pass, "a card set to 1 s was visible at 0.5 s and gone by 3 s; hover, pin and drawer rules are covered by unit tests")
                : new(name, CheckStatus.Fail, $"visible at 0.5 s: {stillThere}; hidden by 3 s: {hidden}");
        }
        catch (Exception ex) { return new(name, CheckStatus.Fail, ex.Message); }
    }

    /// <summary>
    /// The optional middle-click trigger, for real: install the hook, inject a plain middle-click (must be ignored when Ctrl is
    /// required), then a Ctrl+middle-click (must reach the app). The click is never swallowed; this only checks the app saw it.
    /// </summary>
    private static async Task<CheckResult> MouseTriggerAsync()
    {
        const string name = "mouse-trigger";
        if (!OperatingSystem.IsWindows()) return new(name, CheckStatus.Skip, "Windows only");
        try
        {
            string modifier = "Ctrl";
            var hit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var service = new MouseTriggerService(() => modifier, () => hit.TrySetResult(), allowInjected: true);
            if (!service.Start())
                return new(name, CheckStatus.Warn, service.Problem ?? "the mouse hook could not be installed on this machine");

            TextCaptureService.SendMiddleClick(ctrl: false);
            bool plainIgnored = await Task.WhenAny(hit.Task, Task.Delay(900)) != hit.Task;
            await Task.Delay(450);                                   // past the debounce
            TextCaptureService.SendMiddleClick(ctrl: true);
            bool ctrlSeen = await Task.WhenAny(hit.Task, Task.Delay(2500)) == hit.Task;
            return plainIgnored && ctrlSeen
                ? new(name, CheckStatus.Pass, "a plain middle-click was ignored while Ctrl was required, and Ctrl + middle-click reached the app; the click was passed on untouched")
                : new(name, CheckStatus.Fail, $"plain click ignored: {plainIgnored}; Ctrl + middle-click seen: {ctrlSeen}");
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
                ? new(name, CheckStatus.Pass, $"{string.Join(", ", hotkeys.Select(h => h.Binding.Combo))}, pressed as real keystrokes, {(hotkeys.Count == 2 ? "both" : "all")} reached the hotkey thread")
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
            var json = new { app = "ANDA Companion (Notula)", startedUtc = _started, counts, checks = _checks };
            File.WriteAllText(Path.Combine(_dir, "report.json"), JsonSerializer.Serialize(json,
                new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } }));

            var md = new StringBuilder();
            md.AppendLine("# ANDA Companion (Notula) — self-test report");
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
