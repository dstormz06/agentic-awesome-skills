using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
            var settings = new Settings();
            var w = new SettingsWindow(settings, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => { });
            w.Show(); Settle(); Save(w, dir, "settings-new-install");
            w.ExpandAll(); Settle(); Save(w, dir, "settings-all-open");
            w.Close();

            var store = new ChecklistService(Path.Combine(Temp.NewDir(), "c.json"));
            var notes = new NotesWindow(new NotesHooks(store, () => day, () => "Ctrl+Alt+L", () => { }),
                "Don't forget to call the plumber about the leak under the sink tomorrow!! Also email Sam re: the Thursday huddle. Need to submit the IR response for ANDA 216482 by Friday. groceries: milk, eggs, bread\nThe weather was lovely.\nPay rent on the 1st every month", false);
            notes.Show(); Settle(); Save(notes, dir, "notes-to-checklist");
            notes.AddTicked(); Settle(); Save(notes, dir, "notes-added");
            notes.Close();

            store.Add("Pay the water bill", ChecklistCategory.Home, day.AddDays(-2), false, Repeat.None, -1, null, "You", day);
            store.Add("Call the applicant about the inspection", ChecklistCategory.Work, day, true, Repeat.None, -1, "216482", "You", day);
            var w2 = new ChecklistWindow(new ChecklistHooks(store, () => day, () => "Ctrl+Alt+L", _ => { }, (_, _) => { }));
            w2.Show(); Settle(); Save(w2, dir, "my-checklist");
            Avalonia.Automation.AutomationProperties.GetName(w2);
            var edit = w2.GetVisualDescendants().OfType<Button>().First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Edit: Pay the water bill");
            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(); Save(w2, dir, "my-checklist-editing");
            w2.Close();
        }, CancellationToken.None);
    }
}
