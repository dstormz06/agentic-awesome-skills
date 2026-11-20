using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

/// <summary>
/// Round 40: MCP tools. Nothing is sent by itself: the person chooses a server and presses List tools (the start of a session and a request
/// for its tools), chooses a tool, reads and changes the arguments, presses "Show what will be sent", and only then Send. The answer is shown
/// as plain text and kept nowhere. The client writes each request to the event log as a count, never its content. While a request is out,
/// the server, the tool and the arguments cannot be changed, so what goes is what was shown (round 40 review). One session per server
/// for as long as the window is open; closing it cancels what is out and ends the sessions.
/// </summary>
public sealed class McpWindow : Window
{
    internal ComboBox Servers { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    internal ComboBox Tools { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    internal Button ListButton { get; } = new() { Content = "List tools" };
    internal TextBlock About { get; } = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    internal TextBox Arguments { get; } = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 110, FontSize = 12 };
    internal Button PreviewButton { get; } = new() { Content = "Show what will be sent" };
    internal Button SendButton { get; } = new() { Content = "Send", IsEnabled = false };
    internal Button CopyButton { get; } = new() { Content = "Copy the answer", IsEnabled = false };
    internal TextBlock Shown { get; } = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    internal TextBlock Status { get; } = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    internal TextBox Answer { get; } = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, FontSize = 12 };
    public Func<string, Task<bool>>? WriteClipboard { get; set; }

    private readonly IReadOnlyList<McpServerSettings> _servers;
    private readonly Func<McpServerSettings, McpClient> _newClient;
    private readonly Dictionary<string, McpClient> _clients = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _closing = new();
    private readonly string _selection;
    private IReadOnlyList<McpTool> _tools = Array.Empty<McpTool>();
    private (McpServerSettings Server, string Tool, JsonObject Args)? _ready;
    private string? _armed;          // round 41: the program whose start the person was told about; it starts on the next press

    /// <param name="servers">The servers that are switched on.</param>
    /// <param name="selection">Text from the Ask window ("Use with an MCP tool…"), put into the first text argument of a tool, for the person to check.</param>
    /// <param name="client">Makes the client for a server (a test gives one that answers without a network).</param>
    public McpWindow(IReadOnlyList<McpServerSettings> servers, string? selection = null, Func<McpServerSettings, McpClient>? client = null)
    {
        _servers = servers; _selection = selection ?? ""; _newClient = client ?? (s => new McpClient(s));
        Title = "ANDA Companion (Notula) — MCP tools"; Width = 560; SizeToContent = SizeToContent.Height; CanResize = false; Topmost = true; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        IBrush Res(string key) => (IBrush)Application.Current!.Resources[key]!;
        Background = Res("C.Bg");
        foreach (var t in new[] { About, Shown, Status }) t.Foreground = Res("C.Ink2");
        AutomationProperties.SetName(Servers, "Server"); AutomationProperties.SetName(Tools, "Tool"); AutomationProperties.SetName(Arguments, "Arguments, as JSON");
        AutomationProperties.SetName(Answer, "The server's answer"); AutomationProperties.SetLiveSetting(Status, AutomationLiveSetting.Polite);

        foreach (var s in servers) Servers.Items.Add(new ComboBoxItem { Content = s.Name, Tag = s.Id });
        Servers.SelectedIndex = servers.Count > 0 ? 0 : -1;
        Servers.SelectionChanged += (_, _) => { _tools = Array.Empty<McpTool>(); Tools.Items.Clear(); About.Text = ""; Arguments.Text = ""; _armed = null; Reset(); };
        Tools.SelectionChanged += (_, _) => FillTool();
        Arguments.TextChanged += (_, _) => Reset();
        ListButton.Click += async (_, _) => await ListAsync();
        PreviewButton.Click += (_, _) => Preview();
        SendButton.Click += async (_, _) => await SendAsync();
        CopyButton.Click += async (_, _) => Status.Text = await Copying.TextAsync(this, Answer.Text ?? "", WriteClipboard)
            ? "Copied." : "Windows did not allow copying just now. Please select the answer and copy it.";
        var close = new Button { Content = "Done" };
        close.Click += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closed += async (_, _) =>
        {
            _closing.Cancel();
            foreach (var c in _clients.Values) { await c.EndAsync(); c.Dispose(); }
            _clients.Clear();
        };
        Status.Text = servers.Count == 0 ? McpRules.SaidNoServer : McpRules.SaidNothingYet;

        Label LabelFor(string text, Control target) { var l = new Label { Content = text, Target = target, Padding = new Thickness(0), FontSize = 12, Foreground = Res("C.Ink2") }; return l; }
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        row.Children.Add(Servers); Grid.SetColumn(ListButton, 1); row.Children.Add(ListButton);
        var buttons = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
        foreach (var b in new[] { PreviewButton, SendButton, CopyButton, close }) buttons.Children.Add(b);
        var body = new StackPanel { Spacing = 6, Margin = new Thickness(20) };
        body.Children.Add(new TextBlock { Text = "MCP tools", FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Res("C.Ink") });
        foreach (var c in new Control[] { LabelFor("_Server", Servers), row, LabelFor("_Tool", Tools), Tools, About, LabelFor(McpRules.ArgumentsLabel, Arguments),
                     Arguments, buttons, Shown, Status, Answer })
            body.Children.Add(c);
        Content = new ScrollViewer { Content = body, MaxHeight = 760 };
    }

    private McpServerSettings? Server => Servers.SelectedItem is ComboBoxItem { Tag: string id } ? _servers.FirstOrDefault(s => s.Id == id) : null;

    private void Reset() { _ready = null; SendButton.IsEnabled = false; Shown.Text = ""; }

    private McpClient ClientFor(McpServerSettings server) =>
        _clients.TryGetValue(server.Id, out var c) ? c : _clients[server.Id] = _newClient(server);

    /// <summary>While a request is out nothing that decides what is sent can change.</summary>
    private void Busy(bool busy)
    {
        foreach (var c in new Control[] { Servers, Tools, ListButton, PreviewButton }) c.IsEnabled = !busy;
        Arguments.IsReadOnly = busy;
        if (busy) SendButton.IsEnabled = false;
    }

    /// <summary>
    /// One request, with every way it can end said in one sentence: a server's error, a cancel when the window closes, and anything else a
    /// server might cause (round 40 review: an unexpected error in a button's handler would have closed the app).
    /// </summary>
    private async Task Attempt(string asking, Func<CancellationToken, Task> work)
    {
        Busy(true); Status.Text = asking;
        try { await work(_closing.Token); }
        catch (McpException ex) { Status.Text = ex.Message; }
        catch (OperationCanceledException) { /* the window is closing */ }
        catch (Exception) { Status.Text = McpRules.SaidUnexpected; }
        finally { if (!_closing.IsCancellationRequested) Busy(false); }
    }

    internal Task ListAsync()
    {
        if (Server is not { } server) { Status.Text = McpRules.SaidNoServer; return Task.CompletedTask; }
        // round 41: a program on this PC starts only on a second press, after the first has said what it starts
        if (McpRules.IsLocal(server) && !_clients.ContainsKey(server.Id) && _armed != server.Id)
        {
            _armed = server.Id; Status.Text = McpRules.SaidStartsProgram(McpRules.ProgramName(server.Program));
            return Task.CompletedTask;
        }
        _armed = null;
        return Attempt("Asking the server for its tools…", async ct =>
        {
            _tools = await ClientFor(server).ListToolsAsync(ct);
            Tools.Items.Clear();
            foreach (var t in _tools) Tools.Items.Add(new ComboBoxItem { Content = t.Title.Length > 0 ? $"{t.Title} ({t.Name})" : t.Name, Tag = t.Name });
            Tools.SelectedIndex = _tools.Count > 0 ? 0 : -1;
            Status.Text = _tools.Count == 0 ? "The server offers no tools." : McpRules.SaidListed(_tools.Count);
        });
    }

    private void FillTool()
    {
        var tool = Tools.SelectedItem is ComboBoxItem { Tag: string name } ? _tools.FirstOrDefault(t => t.Name == name) : null;
        About.Text = tool?.Description ?? "";
        Arguments.Text = tool is null ? "" : McpRules.ArgumentsFor(tool, _selection);
        Reset();
    }

    internal void Preview()
    {
        if (Server is not { } server || Tools.SelectedItem is not ComboBoxItem { Tag: string tool }) { Status.Text = Server is null ? McpRules.SaidNoServer : McpRules.SaidChooseFirst; return; }
        var (args, problem) = McpRules.ParseArguments(Arguments.Text);
        if (args is null) { Status.Text = problem!; return; }
        _ready = (server, tool, args);
        Shown.Text = McpRules.Preview(server, tool, args, ApiTarget.Address(server.Endpoint).Uri?.IdnHost ?? "");
        SendButton.IsEnabled = true; Status.Text = "Nothing has been sent yet.";
    }

    internal Task SendAsync()
    {
        if (_ready is not { } ready) return Task.CompletedTask;
        _ready = null; Shown.Text = "";            // what was shown is being sent; after a failure, a new look is needed
        return Attempt("Sending…", async ct =>
        {
            var result = await ClientFor(ready.Server).CallToolAsync(ready.Tool, ready.Args, ct);
            Answer.Text = result.Text; CopyButton.IsEnabled = result.Text.Length > 0;
            Status.Text = (result.ToolError ? McpRules.SaidToolFailed : McpRules.SaidAnswered) + " " + McpRules.SaidNotKept;
        });
    }
}
