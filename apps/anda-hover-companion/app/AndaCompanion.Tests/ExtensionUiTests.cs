using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AndaCompanion.Data;
using AndaCompanion.Services;
using AndaCompanion.Theming;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

[Collection("Global state")]
public class ExtensionUiTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public ExtensionUiTests(HeadlessFixture fx) => _fx = fx;

    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static string[] Texts(Window w) => w.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();

    private static WatchListResult Compute() =>
        WatchList.Build(new MockAndaRepository(), new CardContext(new DateOnly(2026, 11, 2), new BusinessCalendar()));

    [Fact]
    public async Task The_watch_list_window_lists_worst_first_hides_notes_by_default_and_opens_a_row()
    {
        string? opened = null;
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var w = new WatchListWindow(Compute, (n, _) => opened = n);
            w.Show(); Settle();
            var summary = w.FindControl<TextBlock>("Summary")!.Text;
            var rows = w.FindControl<StackPanel>("Rows")!.Children.OfType<Button>().ToList();
            var first = Texts(w).ToList();
            rows[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle();
            w.FindControl<CheckBox>("NotesCheck")!.IsChecked = true; Settle();
            int withNotes = w.FindControl<StackPanel>("Rows")!.Children.OfType<Button>().Count();
            w.Close(); Settle();
            return (summary, rows.Count, first, withNotes);
        }, CancellationToken.None);
        Assert.Equal("216482", opened);
        Assert.Contains("2 of 2 ANDAs have something worth a look", r.summary);
        Assert.True(r.Count >= 1);
        Assert.Contains(r.first, t => t.Contains("falls on a weekend: Sat Oct 3"));
        Assert.True(r.withNotes >= r.Count);
    }

    [Fact]
    public async Task The_watch_list_window_says_so_when_nothing_needs_a_look()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(AppearanceId.K);
            var w = new WatchListWindow(() => new WatchListResult(Array.Empty<WatchListItem>(), 5, 0), (_, _) => { });
            w.Show(); Settle();
            var t = Texts(w);
            w.Close(); Settle();
            return t;
        }, CancellationToken.None);
        Assert.Contains(r, t => t.Contains("Nothing needs a second look in your data"));
        Assert.Contains(r, t => t.Contains("0 of 5 ANDAs"));
    }

    [Fact]
    public async Task Settings_shows_the_edge_connection_and_refuses_a_bad_extension_id_without_touching_anything()
    {
        string dir = Temp.NewDir();
        Environment.SetEnvironmentVariable(NativeHostRegistration.DirEnvVar, dir);
        try
        {
            var settings = new Settings();
            var r = await _fx.Session.Dispatch(() =>
            {
                ThemeManager.Apply(AppearanceId.K);
                var w = new SettingsWindow(settings, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => { });
                w.Show(); Settle();
                var idBox = w.FindControl<TextBox>("ExtIdBox")!;
                var before = (idBox.Text, w.FindControl<TextBlock>("ExtStatus")!.Text);
                idBox.Text = "not-an-id";
                w.FindControl<Button>("ExtConnect")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle();
                var after = w.FindControl<TextBlock>("ExtStatus")!.Text;
                w.Close(); Settle();
                return (before, after);
            }, CancellationToken.None);
            Assert.Equal(NativeHostRegistration.DefaultExtensionId, r.before.Item1);
            Assert.Contains("That is not an extension ID", r.after);
            Assert.False(Directory.Exists(Path.Combine(dir, "native-host")) || File.Exists(NativeHostRegistration.ManifestPath));
            Assert.Equal(NativeHostRegistration.DefaultExtensionId, settings.ExtensionId);   // the bad value was not saved
        }
        finally { Environment.SetEnvironmentVariable(NativeHostRegistration.DirEnvVar, null); }
    }

    [Fact]
    public void A_saved_extension_id_that_is_not_valid_falls_back_to_the_default()
    {
        string path = Path.Combine(Temp.NewDir(), "settings.json");
        File.WriteAllText(path, "{\"ExtensionId\":\"nonsense\"}");
        Assert.Equal(NativeHostRegistration.DefaultExtensionId, SettingsService.Load(path).ExtensionId);
    }
}
