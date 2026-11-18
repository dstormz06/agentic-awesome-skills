using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

/// <summary>
/// "My usage": how often each tool was used in the last 7 and 30 days, read from the optional usage log on this PC. Counts only. Nothing is sent;
/// the person copies the summary if they choose to share it, for example when leadership asks how the tool is used.
/// </summary>
public sealed class UsageWindow : Window
{
    internal TextBox Summary { get; }
    internal TextBlock Note { get; }
    internal Button CopyButton { get; }
    public Func<string, Task>? WriteClipboard { get; set; }

    public UsageWindow(string? logPath, bool enabled, DateOnly? today = null)
    {
        Title = "ANDA Companion (Notula) — My usage"; Width = 500; SizeToContent = SizeToContent.Height; CanResize = false; Topmost = true; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        IBrush Res(string key) => (IBrush)Application.Current!.Resources[key]!;
        Background = Res("C.Bg");
        var days = UsageSummary.Read(logPath);
        var lines = UsageSummary.Lines(days, today ?? DateOnly.FromDateTime(DateTime.Now));
        Summary = new TextBox { Text = string.Join("\n", lines), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 150, FontSize = 12.5 };
        AutomationProperties.SetName(Summary, "My usage summary");
        Note = new TextBlock
        {
            FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = Res("C.Ink2"),
            Text = enabled ? "Counting is on (Settings, Maintenance). Nothing is sent; you decide whether to copy this and share it."
                           : "Counting is off, so nothing new is being counted. Switch it on under Settings, Maintenance, if you want these numbers.",
        };
        AutomationProperties.SetLiveSetting(Note, AutomationLiveSetting.Polite);
        CopyButton = new Button { Content = "Copy the summary" };
        CopyButton.Click += async (_, _) =>
        {
            try
            {
                if (WriteClipboard is not null) await WriteClipboard(Summary.Text ?? "");
                else if (Clipboard is { } clipboard) await clipboard.SetTextAsync(Summary.Text ?? "");
                else throw new InvalidOperationException("no clipboard");
                Note.Text = "Summary copied.";
            }
            catch (Exception) { Note.Text = "Windows did not allow copying just now. Please select the text and copy it."; }
        };
        var close = new Button { Content = "Close" };
        close.Click += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(CopyButton); buttons.Children.Add(close);
        var body = new StackPanel { Spacing = 10, Margin = new Thickness(20) };
        body.Children.Add(new TextBlock { Text = "My usage", FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Res("C.Ink") });
        body.Children.Add(Summary); body.Children.Add(buttons); body.Children.Add(Note);
        Content = body;
    }
}
