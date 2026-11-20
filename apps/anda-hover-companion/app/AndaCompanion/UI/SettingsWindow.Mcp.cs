using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

// Round 40 in Settings: the MCP servers. Each row is one server: whether it is used, its name, its address, the token's header and prefix,
// and the NAME of the environment variable that holds the token. Test asks the server for its list of tools; nothing else is sent from here.
public partial class SettingsWindow
{
    private void BuildMcp()
    {
        McpHint.Text = McpRules.Hint;
        McpAdd.Click += (_, _) =>
        {
            _settings.McpServers = McpRules.Normalize(_settings.McpServers.Append(new McpServerSettings { Name = $"MCP server {_settings.McpServers.Count + 1}" }));
            Apply(); PaintMcp();
        };
        PaintMcp();
    }

    private void PaintMcp()
    {
        McpList.Children.Clear();
        McpAdd.IsEnabled = _settings.McpServers.Count < McpRules.MaxServers;
        for (int i = 0; i < _settings.McpServers.Count; i++)
        {
            var s = _settings.McpServers[i]; int n = i + 1;
            var status = new TextBlock { Classes = { "hint" } };
            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
            var use = new CheckBox { Content = "Use this server", IsChecked = s.Enabled };
            AutomationProperties.SetName(use, $"Use MCP server {n}");
            TextBox Field(string label, string watermark, string value, Action<string> set)
            {
                var box = new TextBox { Text = value, Watermark = watermark };
                AutomationProperties.SetName(box, $"{label} of MCP server {n}");
                box.LostFocus += (_, _) =>
                {
                    set((box.Text ?? "").Trim());
                    // the rule of McpRules.Normalize (no address, not used), shown at once: the box is cleared and the reason said
                    if (s.Endpoint.Length == 0 && s.Enabled) { s.Enabled = false; use.IsChecked = false; status.Text = "No address, so this server is not used."; }
                    Apply();
                };
                return box;
            }
            use.IsCheckedChanged += (_, _) =>
            {
                if (use.IsChecked == true && ApiTarget.Address(s.Endpoint).Problem is { } problem) { use.IsChecked = false; status.Text = problem; return; }
                s.Enabled = use.IsChecked == true; Apply();
            };
            var test = new Button { Content = "Test" };
            AutomationProperties.SetName(test, $"Test MCP server {n}");
            test.Click += async (_, _) =>
            {
                test.IsEnabled = false; status.Text = "Asking the server for its tools…";
                using var client = new McpClient(s);
                try { status.Text = McpRules.SaidTested((await client.ListToolsAsync(CancellationToken.None)).Select(t => t.Name).ToList()); }
                catch (McpException ex) { status.Text = ex.Message; }
                catch (Exception) { status.Text = McpRules.SaidUnexpected; }      // round 40 review: nothing a server sends may close the app
                finally { await client.EndAsync(); test.IsEnabled = true; }
            };
            var remove = new Button { Content = "Remove" };
            AutomationProperties.SetName(remove, $"Remove MCP server {n}");
            remove.Click += (_, _) => { _settings.McpServers.Remove(s); Apply(); PaintMcp(); };
            var tokenRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*") };
            var header = Field("Token header", "Header, for example Authorization", s.AuthHeader, v => s.AuthHeader = v);
            var scheme = Field("Before the token", "Before the token, for example Bearer", s.AuthScheme, v => s.AuthScheme = v);
            Grid.SetColumn(scheme, 2); tokenRow.Children.Add(header); tokenRow.Children.Add(scheme);
            var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
            buttons.Children.Add(test); buttons.Children.Add(remove);
            var row = new StackPanel { Spacing = 6 };
            foreach (var c in new Control[] { use, Field("Name", "A name, for example Division search", s.Name, v => s.Name = v),
                         Field("Address", "https://… (the server's MCP address)", s.Endpoint, v => s.Endpoint = v), tokenRow,
                         Field("Token variable", "NAME of the environment variable that holds the token (never the token)", s.SecretEnvVar, v => s.SecretEnvVar = v), buttons, status })
                row.Children.Add(c);
            McpList.Children.Add(row);
        }
    }
}
