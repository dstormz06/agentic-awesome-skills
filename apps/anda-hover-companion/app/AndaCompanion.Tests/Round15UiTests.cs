using System.Net;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AndaCompanion.Data;
using AndaCompanion.Models;
using AndaCompanion.Services;
using AndaCompanion.Theming;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>Round 15, in real windows: Settings groups and the 30% slider, the action editor, the to-do window, the action window and the agents drawer.</summary>
[Collection("Global state")]
public class Round15UiTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public Round15UiTests(HeadlessFixture fx) => _fx = fx;

    private static readonly DateOnly Today = new(2026, 11, 2);
    private static readonly MockAndaRepository Repo = new();
    private static AndaRecord Demo => Repo.Find("216482")!;

    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Until(Func<bool> done)
    {
        for (int i = 0; i < 200 && !done(); i++) { await Task.Delay(15); Settle(); }
    }
    private static IEnumerable<T> All<T>(Visual v) where T : class => v.GetVisualDescendants().OfType<T>();
    private static Button ButtonNamed(Visual v, string content) => All<Button>(v).First(b => b.Content as string == content);
    private static T Named<T>(Visual v, string automationName) where T : Control => All<T>(v).First(c => AutomationProperties.GetName(c) == automationName);

    private static SettingsWindow NewSettings(Settings settings)
    {
        settings.ShowAllTools = true;                  // these tests are about every section
        var w = new SettingsWindow(settings, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => { });
        w.Show(); w.ExpandAll(); Settle();            // sections start collapsed; these tests look inside all of them
        return w;
    }

    // ---------- Settings: group lines and the 30% slider ----------

    [Fact]
    public async Task Settings_has_eight_named_sections_each_with_its_own_line_and_a_40_to_100_slider()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            using var home = new EnvScope(AppPaths.HomeEnvVar, Temp.NewDir());
            var settings = new Settings();
            var w = NewSettings(settings);
            var groups = All<TextBlock>(w).Where(t => t.Classes.Contains("grp")).Select(t => t.Text!).ToList();
            int sections = All<Expander>(w).Count(b => b.Classes.Contains("sec") && b.BorderThickness.Bottom >= 2);
            int hairs = All<Border>(w).Count(b => b.Classes.Contains("hair"));
            var slider = w.FindControl<Slider>("OpacitySlider")!;
            double min = slider.Minimum, max = slider.Maximum;
            slider.Value = 40; Settle();
            double at40 = settings.CardOpacity;
            slider.Value = 65; Settle();
            double at65 = settings.CardOpacity;
            slider.Value = 5; Settle();                                   // below the range: held at 40
            double below = settings.CardOpacity;
            var jump = w.FindControl<ComboBox>("JumpBox")!.ItemsSource!.Cast<string>().ToList();
            w.Close(); Settle();
            return (groups, sections, hairs, min, max, at40, at65, below, jump);
        }, CancellationToken.None);

        Assert.Equal(new[] { "Look and feel", "How it starts", "Dates", "My files and writing", "My to-do list", "My actions, agents and ANDA to-do list", "AI and connections", "Maintenance" }, r.groups);
        Assert.Equal(r.groups.Count, r.sections);                          // every group is a section with a strong line under it
        Assert.True(r.hairs >= 8, $"{r.hairs} fine lines between the settings inside a group");
        Assert.Equal(40, r.min);
        Assert.Equal(100, r.max);
        Assert.Equal(0.40, r.at40, 3);
        Assert.Equal(0.65, r.at65, 3);
        Assert.Equal(0.40, r.below, 3);
        Assert.Contains(r.jump, j => j.StartsWith("My actions ·"));
        Assert.Contains(r.jump, j => j.StartsWith("My agents"));
        Assert.Contains(r.jump, j => j.StartsWith("My ANDA to-do list"));
        Assert.Contains(r.jump, j => j.StartsWith("My to-do list ·"));
        Assert.Contains(r.jump, j => j == "My to-do list");                 // the group names are in the list too
    }

    // ---------- Settings: my actions ----------

    [Fact]
    public async Task An_action_can_be_added_given_a_shortcut_changed_to_an_agent_and_removed()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            using var home = new EnvScope(AppPaths.HomeEnvVar, Temp.NewDir());
            var settings = new Settings();
            int before = settings.MyActions.Count;
            var w = NewSettings(settings);
            Click(w.FindControl<Button>("ActionAdd")!); Settle();
            int afterAdd = settings.MyActions.Count;
            var added = settings.MyActions[^1];

            // choose a shortcut by pressing the keys
            var box = Named<TextBox>(w, "Shortcut for " + added.Name);
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.T, KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt });
            Settle();
            string hotkey = added.Hotkey;

            // the same keys on a second action are refused, and so are the keys of Select & Peek
            Click(w.FindControl<Button>("ActionAdd")!); Settle();
            var second = settings.MyActions[^1];
            var box2 = Named<TextBox>(w, "Shortcut for " + second.Name);
            box2.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.T, KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt }); Settle();
            string secondHotkey = second.Hotkey, status1 = w.FindControl<TextBlock>("ActionsStatus")!.Text ?? "";
            box2.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A, KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt }); Settle();
            string status2 = w.FindControl<TextBlock>("ActionsStatus")!.Text ?? "";

            // switching the first action to "Run one of my agents" picks the first agent so the action is complete
            // the editors are listed in order; the added ones are the last two
            var kinds = All<ComboBox>(w).Where(c => AutomationProperties.GetName(c) == "What the action does").ToList();
            kinds[^2].SelectedIndex = (int)ActionKind.RunAgent; Settle();
            var changed = settings.MyActions[^2];

            // remove the second one
            Click(All<Button>(w).First(b => AutomationProperties.GetName(b) == "Remove the action " + second.Name)); Settle();
            int afterRemove = settings.MyActions.Count;
            w.Close(); Settle();
            return (before, afterAdd, hotkey, secondHotkey, status1, status2, changed.Kind, changed.AgentId, afterRemove);
        }, CancellationToken.None);

        Assert.Equal(3, r.before);                                       // the three starter examples
        Assert.Equal(4, r.afterAdd);
        Assert.Equal("Ctrl+Alt+T", r.hotkey);
        Assert.Equal("", r.secondHotkey);                                // refused: already used by the first
        Assert.Contains("already used", r.status1);
        Assert.Contains("already used", r.status2);                      // Ctrl+Alt+A belongs to Select & Peek
        Assert.Equal(ActionKind.RunAgent, r.Kind);
        Assert.Equal(AgentRules.AlertHubId, r.AgentId);
        Assert.Equal(4, r.afterRemove);                                  // five, then one removed
    }

    [Fact]
    public async Task The_three_preset_agents_can_be_switched_on_but_not_removed()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            using var home = new EnvScope(AppPaths.HomeEnvVar, Temp.NewDir());
            var settings = new Settings();
            var w = NewSettings(settings);
            bool removable = new[] { "Alert Hub", "Patent Analysis", "Triaging Agent" }
                .Any(n => All<Button>(w).First(b => AutomationProperties.GetName(b) == "Remove the agent " + n).IsEnabled);
            Click(w.FindControl<Button>("AgentAdd")!); Settle();
            int agents = settings.Agents.Count;
            w.Close(); Settle();
            return (removable, agents, names: settings.Agents.Select(a => a.Name).ToList());
        }, CancellationToken.None);
        Assert.False(r.removable);
        Assert.Equal(4, r.agents);
        Assert.Contains("Alert Hub", r.names);
        Assert.Contains("Patent Analysis", r.names);
        Assert.Contains("Triaging Agent", r.names);
    }

    // ---------- the action window ----------

    private static ActionWindowModel Model(Func<CancellationToken, Task<ActionReply>>? send, bool onOpen = false, Func<IReadOnlyList<string>, int>? add = null) =>
        new("My action", "ANDA 216482", "THIS IS EXACTLY WHAT WILL BE SENT", "Please summarize ANDA 216482.", "Nothing has been sent yet.", "THE REPLY", send, "Send", onOpen, add);

    [Fact]
    public async Task A_prompt_with_nothing_to_send_shows_only_the_prompt_and_copy()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var w = new ActionWindow(Model(null)); w.Show(); Settle();
            var result = (send: w.FindControl<Button>("SendBtn")!.IsVisible, panel: w.FindControl<StackPanel>("ResultPanel")!.IsVisible,
                prompt: w.FindControl<TextBox>("PromptBox")!.Text, sent: w.WasSent);
            w.Close(); Settle();
            return result;
        }, CancellationToken.None);
        Assert.False(r.send);
        Assert.False(r.panel);
        Assert.Equal("Please summarize ANDA 216482.", r.prompt);
        Assert.False(r.sent);                                            // nothing is sent just because the window opened
    }

    [Fact]
    public async Task Nothing_is_sent_until_Send_is_pressed_then_the_reply_and_ticked_items_can_be_added()
    {
        var r = await _fx.Session.Dispatch(async () =>
        {
            ThemeManager.Apply(AppearanceId.K);
            int sends = 0; var added = new List<string>();
            var w = new ActionWindow(Model(ct => { sends++; return Task.FromResult(new ActionReply(true, "Next steps:\n- Call the applicant\n- Check the IR", "From your AI. Verify before acting.", new[] { "Call the applicant", "Check the IR" })); },
                add: lines => { added.AddRange(lines); return lines.Count; }));
            w.Show(); Settle();
            int beforeClick = sends;
            Click(w.FindControl<Button>("SendBtn")!);
            await Until(() => w.WasSent);
            var addBtn = w.FindControl<Button>("AddTodoBtn")!;
            bool addOffAtFirst = !addBtn.IsEnabled;
            w.Suggestions[1].IsChecked = true; Settle();
            bool addOnWhenTicked = addBtn.IsEnabled;
            Click(addBtn); Settle();
            var res = (beforeClick, sends, result: w.ResultText, panel: w.FindControl<StackPanel>("ResultPanel")!.IsVisible, boxes: w.Suggestions.Count,
                addOffAtFirst, addOnWhenTicked, added: added.ToList(), status: w.FindControl<TextBlock>("StatusText")!.Text, label: w.FindControl<Button>("SendBtn")!.Content);
            w.Close(); Settle();
            return res;
        }, CancellationToken.None);
        Assert.Equal(0, r.beforeClick);
        Assert.Equal(1, r.sends);
        Assert.Contains("Call the applicant", r.result);
        Assert.True(r.panel);
        Assert.Equal(2, r.boxes);
        Assert.True(r.addOffAtFirst);                                    // nothing is ticked, so nothing can be added
        Assert.True(r.addOnWhenTicked);
        Assert.Equal(new[] { "Check the IR" }, r.added);                 // only the ticked line
        Assert.Contains("Added 1 item", r.status);
        Assert.Equal("Send again", r.label);
    }

    [Fact]
    public async Task Send_at_once_sends_when_the_window_opens_and_only_once()
    {
        int sends = 0;
        await _fx.Session.Dispatch(async () =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var w = new ActionWindow(Model(ct => { Interlocked.Increment(ref sends); return Task.FromResult(new ActionReply(true, "ok", "From your AI.", Array.Empty<string>())); }, onOpen: true));
            w.Show();
            await Until(() => w.WasSent);
            await w.SendAsync();                                         // a second press right after: allowed, but it is a new, visible send
            w.Close(); Settle();
            return 0;
        }, CancellationToken.None);
        Assert.Equal(2, sends);
    }

    [Fact]
    public async Task A_second_press_while_one_is_running_does_not_send_twice_and_a_failure_does_not_leak_detail()
    {
        var r = await _fx.Session.Dispatch(async () =>
        {
            ThemeManager.Apply(AppearanceId.K);
            int sends = 0; var gate = new TaskCompletionSource<ActionReply>();
            var w = new ActionWindow(Model(ct => { sends++; return gate.Task; }));
            w.Show(); Settle();
            var send = w.FindControl<Button>("SendBtn")!;
            Click(send); Click(send); Settle();
            int whileRunning = sends; bool disabled = !send.IsEnabled;
            gate.SetResult(new ActionReply(false, "", "Your AI could not be reached.", Array.Empty<string>()));
            await Until(() => w.WasSent);
            bool panelAfterFailure = w.FindControl<StackPanel>("ResultPanel")!.IsVisible;
            string status = w.FindControl<TextBlock>("StatusText")!.Text ?? "";
            w.Close(); Settle();

            var w2 = new ActionWindow(Model(ct => throw new InvalidOperationException("tok-SECRET-123 should never be shown")));
            w2.Show(); Settle();
            await w2.SendAsync();
            string status2 = w2.FindControl<TextBlock>("StatusText")!.Text ?? "";
            w2.Close(); Settle();
            return (whileRunning, disabled, panelAfterFailure, status, status2);
        }, CancellationToken.None);
        Assert.Equal(1, r.whileRunning);
        Assert.True(r.disabled);
        Assert.False(r.panelAfterFailure);
        Assert.Equal("Your AI could not be reached.", r.status);
        Assert.DoesNotContain("SECRET", r.status2);
        Assert.Contains("Nothing was changed", r.status2);
    }

    // ---------- the to-do window ----------

    private sealed class Rig
    {
        public TodoService Store = new(Path.Combine(Temp.NewDir(), "todo.json"));
        public int Seconds = 30;
        public bool FromData = true;
        public TodoHooks Hooks => new(
            Build: () => TodoBuilder.Build(Repo, new CardContext(Today, new BusinessCalendar(), 3, 7), Store, FromData, DateTime.Now),
            Store: Store, RefreshSeconds: () => Seconds, Today: () => Today, Calendar: () => new BusinessCalendar(),
            OpenCard: (_, _) => { }, SourceNote: () => "Using the fictional demo records.");
    }

    [Fact]
    public async Task The_todo_window_shows_data_items_takes_new_ones_and_updates_the_moment_the_list_changes()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var rig = new Rig();
            var w = new TodoWindow(rig.Hooks); w.Show(); Settle();
            int autoRows = w.View.Rows.Count(x => x.Auto);
            string summary = w.FindControl<TextBlock>("Summary")!.Text ?? "";

            w.FindControl<TextBox>("AddBox")!.Text = "Call the applicant about ANDA 216482";
            Click(w.FindControl<Button>("AddBtn")!); Settle();
            bool inStore = rig.Store.Items.Any(i => i.Title.StartsWith("Call the applicant"));
            bool inView = w.View.Rows.Any(x => !x.Auto && x.Title.StartsWith("Call the applicant"));
            string boxAfter = w.FindControl<TextBox>("AddBox")!.Text ?? "";

            // a change made elsewhere (a shortcut, a card) shows up without pressing anything
            rig.Store.Add("Added from a shortcut", "214990", null, null, "My action", Today); Settle();
            bool elsewhere = w.View.Rows.Any(x => x.Title == "Added from a shortcut");

            // an empty item is refused with a plain sentence
            w.FindControl<TextBox>("AddBox")!.Text = "   ";
            Click(w.FindControl<Button>("AddBtn")!); Settle();
            string refused = w.FindControl<TextBlock>("AddStatus")!.Text ?? "";
            w.Close(); Settle();
            return (autoRows, summary, inStore, inView, boxAfter, elsewhere, refused);
        }, CancellationToken.None);
        Assert.True(r.autoRows > 0);
        Assert.Contains("open", r.summary);
        Assert.True(r.inStore);
        Assert.True(r.inView);
        Assert.Equal("", r.boxAfter);
        Assert.True(r.elsewhere);
        Assert.Contains("Please type", r.refused);
    }

    [Fact]
    public async Task The_todo_window_refreshes_only_while_open_and_follows_the_refresh_setting()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var rig = new Rig { Seconds = 30 };
            var w = new TodoWindow(rig.Hooks); w.Show(); Settle();
            bool running = w.TimerRunning;
            rig.Seconds = 0; w.ApplyTimer();
            bool offWhenZero = !w.TimerRunning;
            string offText = w.FindControl<TextBlock>("LiveLine")!.Text ?? "";
            rig.Seconds = 15; w.ApplyTimer();
            bool onAgain = w.TimerRunning;
            w.Close(); Settle();
            bool stoppedOnClose = !w.TimerRunning;
            var viewBefore = w.View;
            rig.Store.Add("After the window closed", null, null, null, "You", Today); Settle();
            bool notRefreshedAfterClose = ReferenceEquals(viewBefore, w.View);
            return (running, offWhenZero, offText, onAgain, stoppedOnClose, notRefreshedAfterClose);
        }, CancellationToken.None);
        Assert.True(r.running);
        Assert.True(r.offWhenZero);
        Assert.Contains("off", r.offText);
        Assert.True(r.onAgain);
        Assert.True(r.stoppedOnClose);
        Assert.True(r.notRefreshedAfterClose);                            // nothing runs once it is closed
    }

    [Fact]
    public async Task Finishing_snoozing_and_clearing_items_work_from_the_buttons()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var rig = new Rig { FromData = false };
            rig.Store.Add("Finish me", null, null, Today, "You", Today);
            rig.Store.Add("Snooze me", null, null, Today, "You", Today);
            var w = new TodoWindow(rig.Hooks); w.Show(); Settle();

            var finish = Named<CheckBox>(w, "Mark finished: Finish me");
            finish.IsChecked = true; Settle();
            bool done = rig.Store.Items.Single(i => i.Title == "Finish me").Done;

            Click(All<Button>(w).First(b => AutomationProperties.GetName(b) == "Snooze until the next business day: Snooze me")); Settle();
            var snoozed = rig.Store.Items.Single(i => i.Title == "Snooze me").SnoozedUntil;
            bool wakeShown = All<Button>(w).Any(b => AutomationProperties.GetName(b) == "Wake: Snooze me");

            Click(w.FindControl<Button>("ClearBtn")!); Settle();
            int left = rig.Store.Items.Count;
            string status = w.FindControl<TextBlock>("AddStatus")!.Text ?? "";
            w.Close(); Settle();
            return (done, snoozed, wakeShown, left, status);
        }, CancellationToken.None);
        Assert.True(r.done);
        Assert.NotNull(r.snoozed);
        Assert.True(r.wakeShown);
        Assert.Equal(1, r.left);
        Assert.Contains("Cleared 1 finished item", r.status);
    }

    // ---------- the agents drawer on the card ----------

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Sent;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Sent);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static string? Env(string name) => name == "HUB_TOKEN" ? "tok-SECRET-123" : null;

    private static AgentSettings Hub(Action<AgentSettings>? tweak = null)
    {
        var a = AgentRules.Presets().First(p => p.Id == AgentRules.AlertHubId);
        a.Enabled = true;
        a.Api.Endpoint = "https://agent.example.test/ask"; a.Api.AuthHeader = "x-api-key"; a.Api.AuthScheme = ""; a.Api.SecretEnvVar = "HUB_TOKEN";
        a.Api.RequestTemplate = "{\"anda\":\"{{number}}\",\"question\":\"{{user}}\"}"; a.Api.ResponsePath = "answer";
        tweak?.Invoke(a);
        return a;
    }

    private static CardWindow NewCard(AgentService? service, Func<AndaRecord, bool>? atRisk = null, Func<IReadOnlyList<string>, AndaRecord, string, int>? add = null) =>
        new(new OfflineBriefService(), Repo.Find) { CloseWhenDeactivated = false, Today = () => Today, Agents = service, AtRisk = atRisk, AddTodos = add };

    [Fact]
    public async Task The_agents_drawer_appears_only_with_an_enabled_agent_and_a_peek_never_asks()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.A);
            var handler = new Handler(_ => Json("{\"answer\":\"- one\"}"));
            var hub = Hub(a => a.AutoRun = AgentRun.OnFullCard);                            // even an automatic agent must not run on a peek
            var service = new AgentService(() => new[] { hub }, Env, handler);

            var off = NewCard(new AgentService(() => AgentRules.Presets(), Env, handler)); off.Show(Demo, new PixelPoint(40, 40), false); Settle();
            bool visibleWhenNoneEnabled = off.FindControl<Expander>("AgentsExpander")!.IsVisible; off.Close(); Settle();

            var none = NewCard(null); none.Show(Demo, new PixelPoint(40, 40), false); Settle();
            bool visibleWithoutService = none.FindControl<Expander>("AgentsExpander")!.IsVisible; none.Close(); Settle();

            var peek = NewCard(service); peek.Show(Demo, new PixelPoint(40, 40), mini: true); Settle();
            bool visibleOnPeek = peek.FindControl<Expander>("AgentsExpander")!.IsVisible; peek.Close(); Settle();
            return (visibleWhenNoneEnabled, visibleWithoutService, visibleOnPeek, handler.Sent);
        }, CancellationToken.None);
        Assert.False(r.visibleWhenNoneEnabled);
        Assert.False(r.visibleWithoutService);
        Assert.False(r.visibleOnPeek);
        Assert.Equal(0, r.Sent);                                                           // no request of any kind
    }

    [Fact]
    public async Task Expanding_a_peek_to_the_full_card_shows_the_drawer_and_runs_only_agents_set_to_run_by_themselves()
    {
        var r = await _fx.Session.Dispatch(async () =>
        {
            ThemeManager.Apply(AppearanceId.A);
            var quiet = new Handler(_ => Json("{\"answer\":\"- a\"}"));
            var s1 = new AgentService(() => new[] { Hub() }, Env, quiet);
            var w1 = NewCard(s1); w1.Show(Demo, new PixelPoint(40, 40), mini: true); Settle();
            bool hiddenOnPeek = !w1.FindControl<Expander>("AgentsExpander")!.IsVisible;
            Click(w1.FindControl<Button>("MoreBtn")!); await Task.Delay(120); Settle();
            bool shownAfterExpand = w1.FindControl<Expander>("AgentsExpander")!.IsVisible;
            int askedWhenSetToOnRequest = quiet.Sent;                                       // "only when I ask" stays that way
            w1.Close(); Settle();

            var auto = new Handler(_ => Json("{\"answer\":\"- a\"}"));
            var s2 = new AgentService(() => new[] { Hub(a => a.AutoRun = AgentRun.OnFullCard) }, Env, auto);
            var w2 = NewCard(s2); w2.Show(Demo, new PixelPoint(40, 40), mini: true); await Task.Delay(120); Settle();
            int onPeek = auto.Sent;
            Click(w2.FindControl<Button>("MoreBtn")!);
            await Until(() => auto.Sent > 0); w2.Close(); Settle();
            return (hiddenOnPeek, shownAfterExpand, askedWhenSetToOnRequest, onPeek, afterExpand: auto.Sent);
        }, CancellationToken.None);
        Assert.True(r.hiddenOnPeek);
        Assert.True(r.shownAfterExpand);
        Assert.Equal(0, r.askedWhenSetToOnRequest);
        Assert.Equal(0, r.onPeek);                                                          // a peek never asks an agent
        Assert.Equal(1, r.afterExpand);
    }

    [Fact]
    public async Task An_agent_is_asked_when_Run_is_pressed_its_words_are_shown_and_items_are_added_only_on_a_click()
    {
        var r = await _fx.Session.Dispatch(async () =>
        {
            ThemeManager.Apply(AppearanceId.A);
            var handler = new Handler(_ => Json("{\"answer\":\"Two alerts:\\n- Facility inspection overdue\\n- IR response due soon\"}"));
            var hub = Hub();
            var service = new AgentService(() => new[] { hub }, Env, handler);
            var added = new List<(string Line, string Number, string Agent)>();
            var w = NewCard(service, add: (lines, rec, agent) => { foreach (var l in lines) added.Add((l, rec.Number, agent)); return lines.Count; });
            w.Show(Demo, new PixelPoint(40, 40), false); Settle();
            var expander = w.FindControl<Expander>("AgentsExpander")!;
            bool visible = expander.IsVisible;
            int sentOnOpen = handler.Sent;
            string before = w.FindControl<StackPanel>("AgentsPanel")!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).FirstOrDefault(t => t?.StartsWith("Not asked") == true) ?? "";

            expander.IsExpanded = true; Settle();
            Click(ButtonNamed(w.FindControl<StackPanel>("AgentsPanel")!, "Run"));
            await Until(() => service.Last("alert-hub", "216482") is not null);
            Settle();
            var texts = w.FindControl<StackPanel>("AgentsPanel")!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
            int addedBeforeClick = added.Count;
            Click(ButtonNamed(w.FindControl<StackPanel>("AgentsPanel")!, "Add to to-do"));
            Settle();
            string header = w.FindControl<TextBlock>("AgentsHeader")!.Text ?? "";
            w.Close(); Settle();
            return (visible, sentOnOpen, before, texts, addedBeforeClick, added: added.ToList(), header, sentTotal: handler.Sent);
        }, CancellationToken.None);
        Assert.True(r.visible);
        Assert.Equal(0, r.sentOnOpen);                                                      // agents are off-by-default for auto-run
        Assert.StartsWith("Not asked yet", r.before);
        Assert.Contains(r.texts, t => t.Contains("Facility inspection overdue"));
        Assert.Contains(r.texts, t => t.StartsWith("From Alert Hub"));
        Assert.Equal(0, r.addedBeforeClick);                                                // nothing is added by itself
        var line = Assert.Single(r.added);
        Assert.Equal(("Facility inspection overdue", "216482", "Alert Hub"), line);
        Assert.Equal("Agent findings · 1", r.header);
        Assert.Equal(1, r.sentTotal);
    }

    [Fact]
    public async Task An_agent_set_to_run_by_itself_does_so_when_the_full_card_opens_or_only_when_at_risk()
    {
        var r = await _fx.Session.Dispatch(async () =>
        {
            ThemeManager.Apply(AppearanceId.A);
            var always = new Handler(_ => Json("{\"answer\":\"- a\"}"));
            var svc1 = new AgentService(() => new[] { Hub(a => a.AutoRun = AgentRun.OnFullCard) }, Env, always);
            var w1 = NewCard(svc1); w1.Show(Demo, new PixelPoint(40, 40), false);
            await Until(() => svc1.Last("alert-hub", "216482") is not null); w1.Close(); Settle();

            var risk = new Handler(_ => Json("{\"answer\":\"- a\"}"));
            var svc2 = new AgentService(() => new[] { Hub(a => a.AutoRun = AgentRun.WhenAtRisk) }, Env, risk);
            var w2 = NewCard(svc2, atRisk: _ => false); w2.Show(Demo, new PixelPoint(40, 40), false); await Task.Delay(150); Settle(); w2.Close(); Settle();
            int notAtRisk = risk.Sent;
            var w3 = NewCard(svc2, atRisk: _ => true); w3.Show(Demo, new PixelPoint(40, 40), false);
            await Until(() => risk.Sent > 0); w3.Close(); Settle();
            return (always.Sent, notAtRisk, atRisk: risk.Sent);
        }, CancellationToken.None);
        Assert.Equal(1, r.Item1);
        Assert.Equal(0, r.notAtRisk);
        Assert.Equal(1, r.atRisk);
    }

    [Fact]
    public async Task A_failed_ask_is_shown_as_a_failure_not_as_not_asked_yet_and_leaks_no_detail()
    {
        var r = await _fx.Session.Dispatch(async () =>
        {
            ThemeManager.Apply(AppearanceId.A);
            var handler = new Handler(_ => Json("{\"error\":\"tok-SECRET-123 internal detail\"}", HttpStatusCode.InternalServerError));
            var service = new AgentService(() => new[] { Hub() }, Env, handler);
            var w = NewCard(service); w.Show(Demo, new PixelPoint(40, 40), false); Settle();
            w.FindControl<Expander>("AgentsExpander")!.IsExpanded = true; Settle();
            Click(ButtonNamed(w.FindControl<StackPanel>("AgentsPanel")!, "Run"));
            await Until(() => service.Latest("alert-hub", "216482") is not null); Settle();
            var texts = w.FindControl<StackPanel>("AgentsPanel")!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
            bool offersAgain = All<Button>(w.FindControl<StackPanel>("AgentsPanel")!).Any(b => b.Content as string == "Try again");
            w.Close(); Settle();
            return (texts, offersAgain, answered: service.Last("alert-hub", "216482"));
        }, CancellationToken.None);
        Assert.DoesNotContain(r.texts, t => t.StartsWith("Not asked yet"));
        Assert.DoesNotContain(r.texts, t => t.Contains("SECRET"));
        Assert.Contains(r.texts, t => t == "Alert Hub answered 500 Internal Server Error.");
        Assert.True(r.offersAgain);
        Assert.Null(r.answered);                                                           // a failure is never kept as a finding
    }
}
