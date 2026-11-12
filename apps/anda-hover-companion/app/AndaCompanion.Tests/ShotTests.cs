using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AndaCompanion.Data;
using AndaCompanion.Services;
using AndaCompanion.Theming;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>Pictures of the round-16 windows, for a person to look at. Not run unless ANDA_SHOTS_DIR is set.</summary>
[Collection("Global state")]
public class ShotTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public ShotTests(HeadlessFixture fx) => _fx = fx;

    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }

    private static void Save(Window w, string dir, string name)
    {
        Settle(); Settle();
        w.CaptureRenderedFrame()?.Save(Path.Combine(dir, name + ".png"));
    }

    [ShotFact]
    public async Task Save_pictures_of_the_new_windows()
    {
        string dir = Environment.GetEnvironmentVariable("ANDA_SHOTS_DIR")!;
        Directory.CreateDirectory(dir);
        await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            using var home = new EnvScope(AppPaths.HomeEnvVar, Temp.NewDir());
            var day = new DateOnly(2026, 11, 4);
            var settings = new Settings { ShowAllTools = true };
            var w = new SettingsWindow(settings, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => { });
            w.Show(); Settle(); Save(w, dir, "settings-new-install");
            w.ExpandAll(); Settle(); Save(w, dir, "settings-all-open");
            w.Close();

            var store = new MyTodoService(Path.Combine(Temp.NewDir(), "c.json"));
            var notes = new NotesWindow(new NotesHooks(store, () => day, () => "Ctrl+Alt+Shift+L", () => { }),
                "Don't forget to confirm the facility inspection date with the team tomorrow ASAP. Also email the team lead re: the Thursday huddle. Need to submit the IR response for ANDA 216482 by Friday. errands: book the dentist, pick up the dry cleaning\nThe inspection report was clear.\nSubmit the expense report on the 1st every month", false);
            notes.Show(); Settle(); Save(notes, dir, "notes-to-my-todo-list");
            notes.AddTicked(); Settle(); Save(notes, dir, "notes-added");
            notes.Close();

            store.Add("Renew the vehicle registration", TodoCategory.Personal, day.AddDays(-2), false, Repeat.None, -1, null, "You", day);
            store.Add("Call the applicant about the inspection", TodoCategory.Work, day, true, Repeat.None, -1, "216482", "You", day);
            var w2 = new MyTodoWindow(new MyTodoHooks(store, () => day, () => "Ctrl+Alt+Shift+L", _ => { }, (_, _) => { }));
            w2.Show(); Settle(); Save(w2, dir, "my-todo-list");
            Avalonia.Automation.AutomationProperties.GetName(w2);
            var edit = w2.GetVisualDescendants().OfType<Button>().First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Edit: Renew the vehicle registration");
            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(); Save(w2, dir, "my-todo-list-editing");
            w2.Close();
        }, CancellationToken.None);
    }

    [ShotFact]
    public async Task Save_pictures_of_the_focused_edition()
    {
        string dir = Environment.GetEnvironmentVariable("ANDA_SHOTS_DIR")!;
        Directory.CreateDirectory(dir);
        await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            using var home = new EnvScope(AppPaths.HomeEnvVar, Temp.NewDir());
            var focused = new SettingsWindow(new Settings(), new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => { });
            focused.Show(); Settle(); Save(focused, dir, "focus-settings-new-install");
            focused.ExpandAll(); Settle(); Save(focused, dir, "focus-settings-all-open");
            focused.Close();

            var calls = new List<string>();
            var hooks = new AskHooks(() => MyActionRules.Starters(), (a, s) => calls.Add(a.Name), t => calls.Add("review"), n => calls.Add("lookup " + n));
            const string table = "ANDA\tApplicant\tGoal date\tForecast\n216482\tFictional Pharma A\tOct 10, 2026\tOct 17, 2026\n214990\tFictional Pharma B\tAug 15, 2026\tAug 15, 2026\n216482\tFictional Pharma A\tOct 10, 2026\tOct 17, 2026\n123456\tFictional Pharma C\tNov 2, 2026\t";
            var ask = new AskWindow(hooks, table);
            ask.Show(); Settle(); Save(ask, dir, "ask-table");
            Avalonia.Automation.AutomationProperties.GetName(ask);
            ask.FindControl<Button>("SuggestBtn")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(); Save(ask, dir, "ask-table-duplicates");
            ask.Close();
            var one = new AskWindow(hooks, "Quick status. ANDA 216482 is on track, with a goal date of Oct 10, 2026 and a forecast of Oct 17, 2026.");
            one.Show(); Settle(); Save(one, dir, "ask-text");
            one.Close();
            var none = new AskWindow(hooks, "");
            none.Show(); Settle(); Save(none, dir, "ask-empty");
            none.Close();

            var draft = new DraftWindow("Hi all, quick status. ANDA 216482 is on track, with a goal date of Oct 10, 2026 and a forecast of Oct 17, 2026. ANDA 214990 is running late. Please send it by Saturday, Nov 7, 2026, [name] and and thank you. ANDA 123456 needs a call.",
                new MockAndaRepository().Find, new WritingService(null), new Settings(), new PromptLibraryService(Path.Combine(Temp.NewDir(), "p2.json")), () => new DateOnly(2026, 11, 4));
            draft.Show(); Settle(); Save(draft, dir, "review-a-draft");
            draft.Close();
        }, CancellationToken.None);
    }
}
