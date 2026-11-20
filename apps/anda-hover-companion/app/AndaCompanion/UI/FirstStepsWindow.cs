using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

/// <summary>
/// What the guide's buttons do, put together from what the app can do. Kept apart from the app so that it can be tested: which record is the
/// sample, that it opens as the full card, which section of Settings, which window.
/// </summary>
public static class FirstStepsWiring
{
    public const string SampleNumber = "216482", FilesSection = "SecFiles";

    /// <param name="sample">The bundled sample records, never the person's own data.</param>
    /// <param name="ownDataLoaded">True when the person's own file is in use: notes typed on the sample would then be kept under a number of theirs.</param>
    /// <param name="openSampleCard">Opens the full card of a record and keeps it open; the flag says whether notes are on.</param>
    /// <param name="openSettingsAt">Opens Settings at a section; false when that section could not be shown.</param>
    public static FirstStepsHooks Build(Func<Settings> settings, Data.IAndaRepository sample, Func<IReadOnlyList<HotkeyStatus>> hotkeys, Func<bool> ownDataLoaded,
        Action<Models.AndaRecord, bool> openSampleCard, Func<string, bool> openSettingsAt, Action openConnections) => new(
        () =>
        {
            var peek = hotkeys().FirstOrDefault(h => h.Binding.Id == HotkeyService.IdPeek);
            return Guide.Shortcut(settings().PeekHotkey, peek?.Registered);      // not known (no keyboard service yet) is not "refused"
        },
        () => Connections.Describe(Connections.From(settings())).Line,
        () =>
        {
            if (sample.Find(SampleNumber) is not { Demo: true } record) return "The sample card could not be opened.";
            bool notes = !ownDataLoaded();
            openSampleCard(record, notes);
            return notes ? Guide.SaidSample : Guide.SaidSampleNoNotes;
        },
        () => openSettingsAt(FilesSection) ? Guide.SaidSettings : "Settings was opened, in front of this guide.",
        () => { openConnections(); return Guide.SaidConnections; });
}

/// <summary>What the guide's own buttons do. Each returns the sentence the guide then says, or null when there is nothing to say.</summary>
public sealed record FirstStepsHooks(Func<string> Shortcut, Func<string> Connections, Func<string?> ShowSample, Func<string?> OpenSettings, Func<string?> OpenConnections);

/// <summary>
/// First steps: five short steps, one at a time. Back, Next (Enter), and "Skip the guide" at every step; Esc closes it. Nothing is asked for
/// and nothing is changed by walking it: its three buttons open a card of sample data, Settings, and the list of connections.
/// </summary>
public sealed class FirstStepsWindow : Window
{
    private readonly FirstStepsHooks _hooks;
    internal int At { get; private set; }
    internal TextBlock Heading { get; }
    internal TextBlock Place { get; }
    internal StackPanel Lines { get; }
    internal Button ActionButton { get; }
    internal Button BackButton { get; }
    internal Button NextButton { get; }
    internal Button LeaveButton { get; }
    internal TextBlock Status { get; }

    private readonly string _note;

    /// <param name="note">A sentence shown at the first step (that the guide could not be remembered as shown), or null.</param>
    public FirstStepsWindow(FirstStepsHooks hooks, string? note = null)
    {
        _hooks = hooks; _note = note ?? "";
        Title = "ANDA Companion (Notula) — First steps"; Width = 500; SizeToContent = SizeToContent.Height; CanResize = false; Topmost = true; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        IBrush Res(string key) => (IBrush)Application.Current!.Resources[key]!;
        Background = Res("C.Bg");
        Heading = new TextBlock { FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Res("C.Ink"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        Place = new TextBlock { FontSize = 12, Foreground = Res("C.Ink2") };
        AutomationProperties.SetLiveSetting(Place, AutomationLiveSetting.Polite);      // a screen reader is told the step it is on
        Lines = new StackPanel { Spacing = 8 };
        ActionButton = new Button { HorizontalAlignment = HorizontalAlignment.Left, MinHeight = 28 };
        Status = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Res("C.Ink2"), MinHeight = 16 };
        AutomationProperties.SetLiveSetting(Status, AutomationLiveSetting.Polite);
        BackButton = new Button { Content = "Back", MinHeight = 28 };
        NextButton = new Button { Content = "Next", MinHeight = 28, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        LeaveButton = new Button { Content = "Skip the guide", MinHeight = 28, HorizontalAlignment = HorizontalAlignment.Right };
        ActionButton.Click += (_, _) => Act();
        BackButton.Click += (_, _) => Go(-1);
        NextButton.Click += (_, _) => Go(1);
        LeaveButton.Click += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        head.Children.Add(Heading); Grid.SetColumn(LeaveButton, 1); head.Children.Add(LeaveButton);
        var foot = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        foot.Children.Add(BackButton); Grid.SetColumn(NextButton, 2); foot.Children.Add(NextButton);
        var body = new StackPanel { Spacing = 8, Margin = new Thickness(20) };
        body.Children.Add(head); body.Children.Add(Place); body.Children.Add(Lines); body.Children.Add(ActionButton); body.Children.Add(Status); body.Children.Add(foot);
        Content = body;
        Paint();
        // After everything else that happens when a window opens (the app puts its "what is switched on" line at the foot then, which moves the content).
        Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => NextButton.Focus());
    }

    private GuideView View() => Guide.Step(At, _hooks.Shortcut(), _hooks.Connections());

    private void Paint()
    {
        var g = View();
        IBrush ink = (IBrush)Application.Current!.Resources["C.Ink"]!;
        Heading.Text = g.Title; Place.Text = g.Place;
        AutomationProperties.SetName(Place, $"{g.Place}: {g.Title}");      // what a screen reader is told when the step changes: its place and its title
        AutomationProperties.SetName(this, $"First steps. {g.Place}: {g.Title}");
        Lines.Children.Clear();
        foreach (string line in g.Lines) Lines.Children.Add(new TextBlock { Text = line, FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = ink });
        ActionButton.IsVisible = g.Action is not null; ActionButton.Content = g.Action ?? "";
        BackButton.IsVisible = g.Back; NextButton.Content = g.Next; LeaveButton.Content = g.Leave;
        Status.Text = g.Index == 0 ? _note : "";
    }

    /// <summary>One step on or back. Next on the last step is "Done" and closes the guide.</summary>
    internal void Go(int by)
    {
        if (by > 0 && At >= Guide.Steps.Count - 1) { Close(); return; }
        At = Math.Min(Math.Max(0, At + by), Guide.Steps.Count - 1);
        Paint();
        (by < 0 && BackButton.IsVisible ? BackButton : NextButton).Focus();      // the keyboard stays on the button that was pressed, when it is still there
    }

    private void Act()
    {
        string? said = View().Action switch
        {
            Guide.ShowSample => _hooks.ShowSample(),
            Guide.OpenSettings => _hooks.OpenSettings(),
            Guide.OpenConnections => _hooks.OpenConnections(),
            _ => null,
        };
        Status.Text = said ?? "";
    }
}
