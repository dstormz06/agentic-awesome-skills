using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

// Round 40 in Settings: the MCP servers. Each row is one server: whether it is used, its name, its address, the token's header and prefix,
// and the NAME of the environment variable that holds the token. Round 41: or a program on this PC, as IT approved it: its full path, its
// SHA-256 and its arguments. Test asks the server for its list of tools (a program only after a second press that names it); nothing else
// is sent from here.
public partial class SettingsWindow
{
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
        McpList.Children.Clear();
        McpAdd.IsEnabled = McpAddLocal.IsEnabled = _settings.McpServers.Count < McpRules.MaxServers;
        for (int i = 0; i < _settings.McpServers.Count; i++)
        {
            var s = _settings.McpServers[i]; int n = i + 1; bool local = McpRules.IsLocal(s);
            var status = new TextBlock { Classes = { "hint" } };
            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
            var use = new CheckBox { Content = local ? "Use this program" : "Use this server", IsChecked = s.Enabled };
            AutomationProperties.SetName(use, $"Use MCP server {n}");
            // what keeps a row from being used, said where it is typed: no address, or a program not named as IT approved it
            string? Problem() => local ? McpRules.ProgramProblem(s.Program, s.ProgramSha256) : ApiTarget.Address(s.Endpoint).Problem;
            TextBox Field(string label, string watermark, string value, Action<string> set, bool lines = false)
            {
                var box = new TextBox { Text = value, Watermark = watermark, AcceptsReturn = lines, TextWrapping = lines ? Avalonia.Media.TextWrapping.Wrap : Avalonia.Media.TextWrapping.NoWrap, MinHeight = lines ? 56 : 0 };
                AutomationProperties.SetName(box, $"{label} of MCP server {n}");
                box.LostFocus += (_, _) =>
                {
                    set(lines ? box.Text ?? "" : (box.Text ?? "").Trim());
                    // the rule of McpRules.Normalize, shown at once: the box is cleared and the reason said
                    if (s.Enabled && Problem() is { } why) { s.Enabled = false; use.IsChecked = false; status.Text = why; }
                    Apply();
                };
                return box;
            }
            use.IsCheckedChanged += (_, _) =>
            {
                if (use.IsChecked == true && Problem() is { } problem) { use.IsChecked = false; status.Text = problem; return; }
                s.Enabled = use.IsChecked == true; Apply();
            };
            bool armed = false;      // a program starts only on the second press, after the first has said what it starts
            var test = new Button { Content = "Test" };
            AutomationProperties.SetName(test, $"Test MCP server {n}");
            test.Click += async (_, _) =>
            {
                if (local && !armed && Problem() is null) { armed = true; status.Text = McpRules.SaidStartsProgram(McpRules.ProgramName(s.Program)); return; }
                armed = false; test.IsEnabled = false; status.Text = "Asking the server for its tools…";
                using var client = new McpClient(s);
                try { status.Text = McpRules.SaidTested((await client.ListToolsAsync(CancellationToken.None)).Select(t => t.Name).ToList()); }
                catch (McpException ex) { status.Text = ex.Message; }
                catch (Exception) { status.Text = McpRules.SaidUnexpected; }      // round 40 review: nothing a server sends may close the app
                finally { await client.EndAsync(); test.IsEnabled = true; }
            };
            var remove = new Button { Content = "Remove" };
            AutomationProperties.SetName(remove, $"Remove MCP server {n}");
            remove.Click += (_, _) => { _settings.McpServers.Remove(s); Apply(); PaintMcp(); };
            var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
            buttons.Children.Add(test); buttons.Children.Add(remove);
            var row = new StackPanel { Spacing = 6 };
            var fields = new List<Control> { use, Field("Name", "A name, for example Division search", s.Name, v => s.Name = v) };
            if (local)
            {
                fields.Add(Field("Program", @"Full path of the .exe IT approved, for example C:\Program Files\Example\mcp-server.exe", s.Program, v => s.Program = McpRules.ProgramPath(v)));
                fields.Add(Field("SHA-256", "The program's SHA-256, exactly as IT gives it (64 letters and digits)", s.ProgramSha256, v => s.ProgramSha256 = v.ToLowerInvariant()));
                fields.Add(Field("Arguments", "Arguments IT names, one per line (often none)", string.Join("\n", s.ProgramArguments),
                    v => s.ProgramArguments = v.Split('\n').Select(a => a.Trim()).Where(a => a.Length > 0).Take(McpRules.MaxArguments).ToList(), lines: true));
                fields.Add(new TextBlock { Classes = { "hint" }, Text = McpRules.ProgramHint, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            }
            else
            {
                var tokenRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*") };
                var header = Field("Token header", "Header, for example Authorization", s.AuthHeader, v => s.AuthHeader = v);
                var scheme = Field("Before the token", "Before the token, for example Bearer", s.AuthScheme, v => s.AuthScheme = v);
                Grid.SetColumn(scheme, 2); tokenRow.Children.Add(header); tokenRow.Children.Add(scheme);
                fields.Add(Field("Address", "https://… (the server's MCP address)", s.Endpoint, v => s.Endpoint = v));
                fields.Add(tokenRow);
                fields.Add(Field("Token variable", "NAME of the environment variable that holds the token (never the token)", s.SecretEnvVar, v => s.SecretEnvVar = v));
            }
            fields.Add(buttons); fields.Add(status);
            foreach (var c in fields) row.Children.Add(c);
            McpList.Children.Add(row);
        }
    }
}
