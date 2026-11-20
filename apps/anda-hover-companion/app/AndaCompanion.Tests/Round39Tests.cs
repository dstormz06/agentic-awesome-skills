using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AndaCompanion.Data;
using AndaCompanion.Services;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// Round 39: a switch beside "Since you last looked" for the to-do items in Today's next week. Off: neither list is read for Today and no
/// title shows. The words are the simulation's (mockup/round39-golden.json). Fictional data only.
/// </summary>
[Collection("Global state")]
public class Round39Tests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public Round39Tests(HeadlessFixture fx) => _fx = fx;
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }

    private static string Folder(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "anda-hover-companion") dir = dir.Parent;
        return Path.Combine(new[] { dir?.FullName ?? throw new DirectoryNotFoundException("anda-hover-companion folder not found") }.Concat(parts).ToArray());
    }
    private static readonly Lazy<JsonElement> Gold = new(() => JsonDocument.Parse(File.ReadAllText(Folder("mockup", "round39-golden.json"))).RootElement.Clone());

    [Fact]
    public void The_switch_says_what_the_simulation_says_is_on_by_default_and_is_in_a_backup()
    {
        Assert.Equal(Gold.Value.GetProperty("label").GetString(), NextWeek.SwitchLabel);
        Assert.Equal(Gold.Value.GetProperty("hint").GetString(), NextWeek.SwitchHint);
        Assert.Equal(Gold.Value.GetProperty("changes").GetProperty("items").EnumerateArray().Select(x => x.GetString()!).ToList(), ChangeLog.Entries.First(e => e.Round == 39).Items.ToList());
        Assert.True(new Settings().WeekTodos);
        Assert.True(JsonSerializer.Deserialize<Settings>("{}")!.WeekTodos);                    // an older settings file reads as on, as before
        string dir = Temp.NewDir();
        var lenses = new PromptLibraryService(Path.Combine(dir, "p.json")); var replies = new QuickReplyService(Path.Combine(dir, "r.json"));
        var target = new Settings();
        Assert.True(SettingsBackup.Import(SettingsBackup.Export(new Settings { WeekTodos = false }, lenses, replies), target, lenses, replies).Item1);
        Assert.False(target.WeekTodos);
    }

    [Fact]
    public void Off_reads_neither_list()
    {
        int read = 0;
        IEnumerable<TodoItem> Anda() { read++; return new[] { new TodoItem { Title = "Open", Due = "2026-12-01" } }; }
        IEnumerable<MyTodoItem> Mine() { read++; return new[] { new MyTodoItem { Title = "Mine", Due = "2026-12-02" } }; }
        Assert.Empty(NextWeek.TasksIfOn(new Settings { WeekTodos = false }, Anda, Mine));
        Assert.Equal(0, read);
        Assert.Equal(2, NextWeek.TasksIfOn(new Settings(), Anda, Mine).Count);
        Assert.Equal(2, read);
    }

    [Fact]
    public async Task Settings_offers_the_switch_and_an_open_Today_follows_it()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            var s = new Settings(); int changed = 0;
            var w = new SettingsWindow(s, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => changed++);
            var repo = new MockAndaRepository();
            Today.Look? Find(string id) => repo.Find(id) is { } rec ? Round33Tests.Look(rec) : null;
            var tasks = new[] { new NextWeek.WeekTask("My to-do list", "Fictional task", "2026-12-01") };
            var today = new TodayWindow(new TodayHooks(() => repo.Numbers.Select(n => Find(n)!).ToList(), Find, () => Array.Empty<string>(), () => Array.Empty<string>(), _ => { },
                Week: () => NextWeek.Of(new DateOnly(2026, 11, 27), 10, Array.Empty<Models.AndaRecord>(), new BusinessCalendar(), NextWeek.TasksIfOn(s, () => Array.Empty<TodoItem>(), () => Array.Empty<MyTodoItem>()).Concat(s.WeekTodos ? tasks : Array.Empty<NextWeek.WeekTask>()))));
            List<string> T() => today.List.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
            try
            {
                w.Show(); today.Show(); Settle();
                var check = w.FindControl<CheckBox>("WeekTodosCheck")!;
                bool before = T().Contains("Tue Dec 1 · My to-do list: Fictional task");
                check.IsChecked = false; Settle(); today.Repaint(); Settle();
                bool after = T().Contains("Tue Dec 1 · My to-do list: Fictional task"), empty = T().Contains(NextWeek.SaidEmpty);
                return (label: check.Content as string, help: Avalonia.Automation.AutomationProperties.GetHelpText(check), before, after, empty, off: !s.WeekTodos, changed);
            }
            finally { w.Close(); today.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.Equal(NextWeek.SwitchLabel, r.label);
        Assert.Equal(NextWeek.SwitchHint, r.help);
        Assert.True(r.before); Assert.False(r.after); Assert.True(r.empty);
        Assert.True(r.off); Assert.True(r.changed >= 1);
    }

    [Fact]
    public void The_app_asks_the_switch_and_repaints_an_open_Today()
    {
        string app = Round37Tests.Code(Folder("app", "AndaCompanion", "App.axaml.cs")), more = Round37Tests.Code(Folder("app", "AndaCompanion", "App.Round33.cs"));
        Assert.Contains("NextWeek.TasksIfOn(_settings, () => _todo.Items, () => _myTodo.Items)", more);
        Assert.Contains("if (_weekTodosSwitch != _settings.WeekTodos) { _weekTodosSwitch = _settings.WeekTodos; _todayWindow?.Repaint(); }", app);
    }
}
