using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

/// <summary>
/// The line at the foot of every window that says what is switched on: "Offline · Local only", or each connection by name. One press opens
/// the list of connections and the event log. The card, its small boxes and the invite have their own connection label and are left as they are.
/// </summary>
public static class ConnectionBar
{
    /// <summary>The line as it is now. The app sets this; a test may too.</summary>
    public static Func<string> Line { get; set; } = () => Connections.Offline;

    /// <summary>Opens the list of connections and the event log.</summary>
    public static Action? Open { get; set; }

    private static event Action? Changed;

    /// <summary>Tells every open window that what is switched on may have changed.</summary>
    public static void Refresh() => Changed?.Invoke();

    private const string Suffix = ". Opens the list of connections and the event log.";

    /// <summary>The room the line takes at the foot of a window. A window that fits itself to the screen leaves this much free.</summary>
    public const double Height = 28;

    /// <summary>The windows that carry the line. Anything else (the card, a compact box, the invite, the Connections window itself) does not.</summary>
    public static bool Carries(Window w) => w is SettingsWindow or LookupWindow or DraftWindow or AskWindow or CompareWindow or HandoffWindow or TodoWindow or MyTodoWindow
        or NotesWindow or WatchListWindow or PromptLibraryWindow or QuickRepliesWindow or ActionWindow or UsageWindow or AboutWindow or BackupsWindow;

    /// <summary>Puts the line at the foot of a window, once. Returns the line's button, or null when the window carries none.</summary>
    public static Button? Attach(Window w)
    {
        if (!Carries(w) || w.Content is not Control content || content is DockPanel { Tag: "with-onbar" }) return null;
        IBrush? Res(string key) => Application.Current?.Resources.TryGetResource(key, null, out var v) == true ? v as IBrush : null;
        var bar = new Button
        {
            Content = Line(), Tag = "onbar", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            FontSize = 11.5, MinHeight = 26, Padding = new Thickness(20, 5), Background = Brushes.Transparent, CornerRadius = new CornerRadius(0),
            BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = Res("C.Line"), Foreground = Res("C.Ink2"),
        };
        AutomationProperties.SetName(bar, Line() + Suffix);
        bar.Click += (_, _) => Open?.Invoke();
        w.Content = null;
        var dock = new DockPanel { Tag = "with-onbar" };
        DockPanel.SetDock(bar, Dock.Bottom);
        dock.Children.Add(bar); dock.Children.Add(content);
        w.Content = dock;
        void Update() => Dispatcher.UIThread.Post(() => { string line = Line(); bar.Content = line; AutomationProperties.SetName(bar, line + Suffix); });
        Changed += Update;
        w.Closed += (_, _) => Changed -= Update;
        return bar;
    }
}
