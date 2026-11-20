using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

// Round 40 in Settings: the MCP servers. Each row is one server: whether it is used, its name, its address, the token's header and prefix,
// and the NAME of the environment variable that holds the token. Round 41: or a program on this PC, as IT approved it: its full path, its
// SHA-256 and its arguments. Test asks the server for its list of tools (a program only after a second press that names it, McpStartConfirm);
// nothing else is sent from here.
public partial class SettingsWindow
{
    /// <summary>The first press of Test on a program, waiting for the second; any edit asks again.</summary>
    internal McpStartConfirm McpConfirm { get; } = new();

    private void BuildMcp()
    {
        McpHint.Text = McpRules.Hint;
        void Add(McpServerSettings added) { _settings.McpServers = McpRules.Normalize(_settings.McpServers.Append(added)); Apply(); PaintMcp(); }
        McpAdd.Click += (_, _) => Add(new McpServerSettings { Name = $"MCP server {_settings.McpServers.Count + 1}" });
        McpAddLocal.Click += (_, _) => Add(new McpServerSettings { Name = $"Program {_settings.McpServers.Count + 1}", IsProgram = true });
        PaintMcp();
    }

    private void PaintMcp()
    {
        McpList.Children.Clear(); McpConfirm.Clear();
        McpAdd.IsEnabled = McpAddLocal.IsEnabled = _settings.McpServers.Count < McpRules.MaxServers;
        for (int i = 0; i < _settings.McpServers.Count; i++) McpList.Children.Add(McpRow(_settings.McpServers[i], i + 1));
    }

    /// <summary>One row: Use, Name, the fields of a server or of a program, Test and Remove, and the line that says what happened.</summary>
    private StackPanel McpRow(McpServerSettings s, int n)
    {
        var status = new TextBlock { Classes = { "hint" } };
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var use = new CheckBox { Content = s.IsProgram ? "Use this program" : "Use this server", IsChecked = s.Enabled };
        AutomationProperties.SetName(use, $"Use MCP server {n}");
        TextBox Field(string label, string watermark, string value, Action<string> set, bool lines = false)
        {
            var box = new TextBox { Text = value, Watermark = watermark, AcceptsReturn = lines, TextWrapping = lines ? Avalonia.Media.TextWrapping.Wrap : Avalonia.Media.TextWrapping.NoWrap, MinHeight = lines ? 56 : 0 };
            AutomationProperties.SetName(box, $"{label} of MCP server {n}");
            box.LostFocus += (_, _) =>
            {
                set(lines ? box.Text ?? "" : (box.Text ?? "").Trim());
                McpConfirm.Clear();
                // the rule of McpRules.Normalize, shown at once: the box is cleared and the reason said
                if (s.Enabled && McpRules.Problem(s) is { } why) { s.Enabled = false; use.IsChecked = false; status.Text = why; }
                Apply();
            };
            return box;
        }
        use.IsCheckedChanged += (_, _) =>
        {
            if (use.IsChecked == true && McpRules.Problem(s) is { } problem) { use.IsChecked = false; status.Text = problem; return; }
            s.Enabled = use.IsChecked == true; Apply();
        };
        var test = new Button { Content = "Test" };
        AutomationProperties.SetName(test, $"Test MCP server {n}");
        test.Click += async (_, _) => await TestMcpAsync(s, test, status);
        var remove = new Button { Content = "Remove" };
        AutomationProperties.SetName(remove, $"Remove MCP server {n}");
        remove.Click += (_, _) => { _settings.McpServers.Remove(s); Apply(); PaintMcp(); };
        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(test); buttons.Children.Add(remove);

        var row = new StackPanel { Spacing = 6 };
        row.Children.Add(use);
        row.Children.Add(Field("Name", "A name, for example Division search", s.Name, v => s.Name = v));
        foreach (var c in s.IsProgram ? ProgramFields(s, Field) : ServerFields(s, Field)) row.Children.Add(c);
        row.Children.Add(buttons); row.Children.Add(status);
        return row;
    }

    private delegate TextBox McpField(string label, string watermark, string value, Action<string> set, bool lines = false);

    private static IEnumerable<Control> ProgramFields(McpServerSettings s, McpField field)
    {
        yield return field("Program", @"Full path of the .exe IT approved, for example C:\Program Files\Example\mcp-server.exe", s.Program, v => s.Program = McpRules.ProgramPath(v));
        yield return field("SHA-256", "The program's SHA-256, exactly as IT gives it (64 letters and digits)", s.ProgramSha256, v => s.ProgramSha256 = v.ToLowerInvariant());
        yield return field("Arguments", "Arguments IT names, one per line (often none)", string.Join("\n", s.ProgramArguments),
            v => s.ProgramArguments = v.Split('\n').Select(a => a.Trim()).Where(a => a.Length > 0).Take(McpRules.MaxArguments).ToList(), lines: true);
        yield return new TextBlock { Classes = { "hint" }, Text = McpRules.ProgramHint, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    }

    private static IEnumerable<Control> ServerFields(McpServerSettings s, McpField field)
    {
        yield return field("Address", "https://… (the server's MCP address)", s.Endpoint, v => s.Endpoint = v);
        var tokenRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*") };
        var header = field("Token header", "Header, for example Authorization", s.AuthHeader, v => s.AuthHeader = v);
        var scheme = field("Before the token", "Before the token, for example Bearer", s.AuthScheme, v => s.AuthScheme = v);
        Grid.SetColumn(scheme, 2); tokenRow.Children.Add(header); tokenRow.Children.Add(scheme);
        yield return tokenRow;
        yield return field("Token variable", "NAME of the environment variable that holds the token (never the token)", s.SecretEnvVar, v => s.SecretEnvVar = v);
    }

    /// <summary>Test: a server's list of tools. A program first says what it starts, and starts on the second press for the same program.</summary>
    internal async Task TestMcpAsync(McpServerSettings s, Button test, TextBlock status, Func<McpServerSettings, McpClient>? client = null)
    {
        if (McpRules.Problem(s) is { } problem && s.IsProgram) { McpConfirm.Clear(); status.Text = problem; return; }
        if (s.IsProgram && !McpConfirm.Confirm(s)) { status.Text = McpRules.SaidStartsProgram(McpRules.ProgramName(s.Program)); return; }
        test.IsEnabled = false; status.Text = "Asking the server for its tools…";
        using var c = client?.Invoke(s) ?? new McpClient(s, McpRules.WithheldVariables(_settings));
        c.PermitStart();
        try { status.Text = McpRules.SaidTested((await c.ListToolsAsync(CancellationToken.None)).Select(t => t.Name).ToList()); }
        catch (McpException ex) { status.Text = ex.Message; }
        catch (Exception) { status.Text = McpRules.SaidUnexpected; }      // round 40 review: nothing a server sends may close the app
        finally { await c.EndAsync(); test.IsEnabled = true; }
    }
}
