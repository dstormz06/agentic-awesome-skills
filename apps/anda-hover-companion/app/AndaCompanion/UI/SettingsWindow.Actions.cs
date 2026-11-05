using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

/// <summary>Settings: My actions, My agents and My to-do list. Every change is saved at once, like the rest of Settings.</summary>
public partial class SettingsWindow
{
    private static IBrush Brush(string key) => (IBrush)Application.Current!.Resources[key]!;

    private TextBox Field(string name, string watermark, string text, Action<string> save, bool multi = false, double height = 0, bool mono = false)
    {
        var box = new TextBox { Text = text, Watermark = watermark, AcceptsReturn = multi, TextWrapping = multi ? TextWrapping.Wrap : TextWrapping.NoWrap };
        if (multi && height > 0) box.Height = height;
        if (mono) { box.FontFamily = (FontFamily)Application.Current!.Resources["C.NumFont"]!; box.FontSize = 11.5; }
        AutomationProperties.SetName(box, name);
        box.LostFocus += (_, _) => { if (!_loading) save(box.Text ?? ""); };
        return box;
    }

    private ComboBox Choice(string name, IReadOnlyList<string> labels, int selected, Action<int> changed)
    {
        var box = new ComboBox { ItemsSource = labels.ToList(), SelectedIndex = selected, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        AutomationProperties.SetName(box, name);
        box.SelectionChanged += (_, _) => { if (!_loading && box.SelectedIndex >= 0) changed(box.SelectedIndex); };
        return box;
    }

    private static Control Card(params Control[] children)
    {
        var stack = new StackPanel { Spacing = 5 };
        foreach (var c in children) stack.Children.Add(c);
        return new Border
        {
            Child = stack, BorderBrush = Brush("C.Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10),
        };
    }

    private TextBlock Note(string text, bool bad = false, double size = 11.5) =>
        new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = Brush(bad ? "C.Bad" : "C.Ink2") };

    private void BuildActionSections()
    {
        BuildMyActions();
        BuildMyAgents();
        BuildMyTodo();
    }

    // ---------------- my actions ----------------

    private IEnumerable<(string Name, string Hotkey)> ShortcutsInUse(CustomAction? except) =>
        new[] { ("Select & Peek", _settings.PeekHotkey), ("Lookup box", _settings.LookupHotkey), ("Check a draft", _settings.DraftHotkey) }
            .Concat(_settings.MyActions.Where(x => !ReferenceEquals(x, except) && !string.IsNullOrWhiteSpace(x.Hotkey)).Select(x => (x.Name, x.Hotkey)));

    private void BuildMyActions()
    {
        ActionAdd.Click += (_, _) =>
        {
            if (_settings.MyActions.Count >= MyActionRules.MaxActions) { ActionsStatus.Text = $"You can keep up to {MyActionRules.MaxActions} actions."; return; }
            _settings.MyActions.Add(new CustomAction { Name = $"My action {_settings.MyActions.Count + 1}", Kind = ActionKind.AddToTodo, Template = "Follow up on ANDA {number}" });
            Apply(); RebuildActions();
        };
        RebuildActions();
    }

    internal void RebuildActions()
    {
        ActionsPanel.Children.Clear();
        if (_settings.MyActions.Count == 0) ActionsPanel.Children.Add(Note("No actions yet. Press Add an action to make one."));
        foreach (var a in _settings.MyActions.ToList()) ActionsPanel.Children.Add(ActionEditor(a));
    }

    private Control ActionEditor(CustomAction a)
    {
        var problem = Note("", bad: true);
        void Check()
        {
            string? p = MyActionRules.Validate(a, ShortcutsInUse(a));
            var unknown = MyActionRules.UnknownPlaceholders(a.Template);
            problem.Text = p ?? (unknown.Count > 0 ? $"Not a known placeholder: {string.Join(", ", unknown.Select(u => "{" + u + "}"))}. It will be left as typed." : "");
            problem.IsVisible = problem.Text.Length > 0;
        }

        var enabled = new CheckBox { IsChecked = a.Enabled, Content = new TextBlock { Text = "On" }, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        enabled.IsCheckedChanged += (_, _) => { if (_loading) return; a.Enabled = enabled.IsChecked == true; Apply(); };
        var name = Field("Action name", "A name, for example Add to my to-do list", a.Name, v => { a.Name = v.Trim(); Check(); Apply(); });
        var remove = new Button { Content = "Remove" };
        AutomationProperties.SetName(remove, "Remove the action " + a.Name);
        remove.Click += (_, _) => { _settings.MyActions.Remove(a); Apply(); RebuildActions(); };
        var head = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 8 };
        head.Children.Add(enabled);
        Grid.SetColumn(name, 1); head.Children.Add(name);
        Grid.SetColumn(remove, 2); head.Children.Add(remove);

        var kinds = Enum.GetValues<ActionKind>().Select(MyActionRules.KindLabel).ToList();
        var kind = Choice("What the action does", kinds, (int)a.Kind, i =>
        {
            a.Kind = (ActionKind)i;
            // An agent action starts on the first agent, so a new action is complete as soon as it is chosen.
            if (a.Kind == ActionKind.RunAgent && !_settings.Agents.Any(x => x.Id == a.AgentId) && _settings.Agents.Count > 0) a.AgentId = _settings.Agents[0].Id;
            Apply(); RebuildActions();
        });

        Control? extra = null;
        if (a.Kind == ActionKind.AddToTodo)
            extra = Choice("When it is due", MyActionRules.DueChoices.Select(d => d == 0 ? "Due today" : d == 1 ? "Due in 1 business day" : $"Due in {d} business days").ToList(),
                Math.Max(0, MyActionRules.DueChoices.ToList().IndexOf(a.DueBusinessDays)), i => { a.DueBusinessDays = MyActionRules.DueChoices[i]; Apply(); });
        else if (a.Kind == ActionKind.RunAgent)
        {
            var agents = _settings.Agents.ToList();
            extra = Choice("Which agent to run", agents.Select(x => x.Name).ToList(), agents.FindIndex(x => x.Id == a.AgentId), i => { a.AgentId = agents[i].Id; Check(); Apply(); });
        }
        var top = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 8 };
        top.Children.Add(kind);
        if (extra is not null) { Grid.SetColumn(extra, 1); top.Children.Add(extra); }

        var keyBox = new TextBox { Text = a.Hotkey, IsReadOnly = true, Watermark = "Click here, then press the keys (optional)" };
        AutomationProperties.SetName(keyBox, "Shortcut for " + a.Name);
        var keyClear = new Button { Content = "Clear" };
        var run = new Button { Content = "Run…" };
        AutomationProperties.SetName(run, "Run the action " + a.Name + " now");
        var keyRow = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 8 };
        keyRow.Children.Add(keyBox);
        Grid.SetColumn(keyClear, 1); keyRow.Children.Add(keyClear);
        Grid.SetColumn(run, 2); keyRow.Children.Add(run);
        keyBox.KeyDown += (_, e) =>
        {
            e.Handled = true;
            if (HotkeyCapture.FromKey(e.Key, e.KeyModifiers) is not { } combo) return;           // a modifier alone: keep waiting
            if (!HotkeyParser.TryParse(combo, out uint _m1, out uint _k1, out string norm, out string error)) { ActionsStatus.Text = $"{a.Name}: {error}"; return; }
            var owner = ShortcutsInUse(a).FirstOrDefault(x => HotkeyParser.TryParse(x.Hotkey, out uint _m2, out uint _k2, out string n, out string _e2) && n == norm);
            if (owner.Name is not null) { ActionsStatus.Text = $"{norm} is already used by {owner.Name}. Please choose another shortcut."; return; }
            a.Hotkey = norm; keyBox.Text = norm; Check(); Apply();
            ActionsStatus.Text = $"{a.Name}: {norm}. Checking with Windows…";
            DispatcherTimer.RunOnce(() => ActionsStatus.Text = _hooks?.HotkeyStatus() ?? "", TimeSpan.FromMilliseconds(500));
        };
        keyClear.Click += (_, _) => { a.Hotkey = ""; keyBox.Text = ""; Check(); Apply(); };
        run.Click += (_, _) => _hooks?.RunAction?.Invoke(a);

        string watermark = a.Kind == ActionKind.AddToTodo ? "The to-do title, for example: Follow up on ANDA {number}" : "Your prompt. The card's facts are filled in where you write a placeholder.";
        var template = Field("Text for " + a.Name, watermark, a.Template, v => { a.Template = v.Length > MyActionRules.MaxTemplateChars ? v[..MyActionRules.MaxTemplateChars] : v; Check(); Apply(); }, multi: true, height: 78);
        var placeholders = Note("Placeholders: " + string.Join(" ", ActionFill.Placeholders.Select(p => "{" + p.Name + "}")), size: 10.5);
        placeholders.Foreground = Brush("C.Ink3");

        var parts = new List<Control> { head, top, keyRow, template, placeholders };
        if (a.Kind is ActionKind.AskAi or ActionKind.RunAgent)
        {
            var direct = new CheckBox { IsChecked = a.SendWithoutPreview, Content = new TextBlock { Text = "Send as soon as I press the shortcut, without the preview step", TextWrapping = TextWrapping.Wrap } };
            direct.IsCheckedChanged += (_, _) => { if (_loading) return; a.SendWithoutPreview = direct.IsChecked == true; Apply(); };
            parts.Add(direct);
        }
        parts.Add(problem);
        Check();
        return Card(parts.ToArray());
    }

    // ---------------- my agents ----------------

    private void BuildMyAgents()
    {
        AgentAdd.Click += (_, _) =>
        {
            if (_settings.Agents.Count >= AgentRules.MaxAgents) { AgentsStatus.Text = $"You can keep up to {AgentRules.MaxAgents} agents."; return; }
            int n = _settings.Agents.Count + 1;
            string id = AgentRules.Slug($"my agent {n}"), unique = id; int k = 2;
            while (_settings.Agents.Any(x => x.Id == unique)) unique = $"{id}-{k++}";
            _settings.Agents.Add(new AgentSettings
            {
                Id = unique, Name = $"My agent {n}", Prompt = "Tell me what you know about ANDA {number} ({drug}). One finding per line, each starting with \"- \".",
                Api = new CustomApiSettings { Name = $"My agent {n}" },
            });
            Apply(); RebuildAgents();
        };
        RebuildAgents();
    }

    internal void RebuildAgents()
    {
        AgentsPanel.Children.Clear();
        foreach (var agent in _settings.Agents.ToList()) AgentsPanel.Children.Add(AgentEditor(agent));
    }

    private Control AgentEditor(AgentSettings a)
    {
        var status = Note("");
        var problem = Note("", bad: true);
        void Check()
        {
            string? setup = _hooks?.AgentProblem?.Invoke(a);
            status.Text = !a.Enabled ? "Off. Nothing is sent." : setup is null ? "On and set up." : "On, but not set up: " + setup;
            string? p = AgentRules.Validate(a);
            problem.Text = p ?? ""; problem.IsVisible = problem.Text.Length > 0;
        }
        bool preset = a.Id is AgentRules.AlertHubId or AgentRules.TriagingId;

        var enabled = new CheckBox { IsChecked = a.Enabled, Content = new TextBlock { Text = "On" }, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        enabled.IsCheckedChanged += (_, _) => { if (_loading) return; a.Enabled = enabled.IsChecked == true; Check(); Apply(); };
        var name = Field("Agent name", "A name for the agent", a.Name, v => { a.Name = v.Trim(); a.Api.Name = a.Name; Check(); Apply(); });
        var remove = new Button { Content = "Remove", IsEnabled = !preset };
        AutomationProperties.SetName(remove, "Remove the agent " + a.Name);
        remove.Click += (_, _) => { _settings.Agents.Remove(a); foreach (var act in _settings.MyActions.Where(x => x.AgentId == a.Id)) act.AgentId = ""; Apply(); RebuildAgents(); RebuildActions(); };
        var head = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 8 };
        head.Children.Add(enabled);
        Grid.SetColumn(name, 1); head.Children.Add(name);
        Grid.SetColumn(remove, 2); head.Children.Add(remove);

        var api = a.Api;
        var connection = new StackPanel { Spacing = 5, Margin = new Thickness(0, 4, 0, 0) };
        connection.Children.Add(Field("Agent address", "https://… (the address your IT team gives you)", api.Endpoint, v => { api.Endpoint = v.Trim(); Check(); Apply(); }));
        var hdr = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 8 };
        var h1 = Field("Token header", "Header, for example Authorization", api.AuthHeader, v => { api.AuthHeader = v.Trim(); Apply(); });
        var h2 = Field("Token prefix", "Before the token, for example Bearer", api.AuthScheme, v => { api.AuthScheme = v.Trim(); Apply(); });
        hdr.Children.Add(h1); Grid.SetColumn(h2, 1); hdr.Children.Add(h2);
        connection.Children.Add(hdr);
        connection.Children.Add(Field("Token variable name", "NAME of the environment variable that holds the token (never the token)", api.SecretEnvVar, v => { api.SecretEnvVar = v.Trim(); Check(); Apply(); }));
        connection.Children.Add(Field("Agent model", "Model or agent name, if the service wants one", api.Model, v => { api.Model = v.Trim(); Apply(); }));
        connection.Children.Add(Field("Request body", "Request body (JSON). Placeholders: {{user}} {{system}} {{model}} {{max_tokens}} and the card fields such as {{number}} {{drug}} {{goal}}",
            api.RequestTemplate, v => { api.RequestTemplate = string.IsNullOrWhiteSpace(v) ? CustomApiSettings.ExampleTemplate : v; Check(); Apply(); }, multi: true, height: 90, mono: true));
        connection.Children.Add(Field("Reply path", "Where the reply text is, for example choices[0].message.content", api.ResponsePath, v => { api.ResponsePath = v.Trim(); Check(); Apply(); }));
        connection.Children.Add(Choice("Wait at most", CustomApiSettings.TimeoutChoices.Select(t => $"Wait up to {t} seconds").ToList(),
            Math.Max(0, CustomApiSettings.TimeoutChoices.ToList().IndexOf(api.TimeoutSeconds)), i => { api.TimeoutSeconds = CustomApiSettings.TimeoutChoices[i]; Apply(); }));
        var expander = new Expander { Header = "Connection: the address, header and body of this agent", Content = connection, IsExpanded = !preset && string.IsNullOrWhiteSpace(api.Endpoint) };

        var prompt = Field("What this agent is asked", "What the agent is asked, with placeholders such as {number} and {drug}", a.Prompt,
            v => { a.Prompt = v.Length > AgentRules.MaxPromptChars ? v[..AgentRules.MaxPromptChars] : v; Check(); Apply(); }, multi: true, height: 70);

        var runChoices = Enum.GetValues<AgentRun>().ToList();
        var auto = Choice("When this agent runs", runChoices.Select(AgentRules.RunLabel).ToList(), runChoices.IndexOf(a.AutoRun), i =>
        {
            var previous = a.AutoRun; a.AutoRun = runChoices[i];
            if (AgentRules.Validate(a) is { } refused) { a.AutoRun = previous; AgentsStatus.Text = refused; RebuildAgents(); return; }
            AgentsStatus.Text = a.AutoRun == AgentRun.Never ? "" : $"{a.Name} will now run by itself {(a.AutoRun == AgentRun.OnFullCard ? "whenever a full card opens" : "when a full card opens for an at-risk ANDA")}. A peek never runs it. Only the fields its prompt and body name are sent.";
            Check(); Apply();
        });
        var cache = Choice("How long to reuse an answer", AgentRules.CacheChoices.Select(m => m == 0 ? "Always ask again" : $"Reuse an answer for {m} minutes").ToList(),
            Math.Max(0, AgentRules.CacheChoices.ToList().IndexOf(a.CacheMinutes)), i => { a.CacheMinutes = AgentRules.CacheChoices[i]; Apply(); });
        var opts = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 8 };
        opts.Children.Add(auto); Grid.SetColumn(cache, 1); opts.Children.Add(cache);

        Check();
        return Card(head, status, expander, prompt, opts, problem);
    }

    // ---------------- my to-do list ----------------

    private void BuildMyTodo()
    {
        TodoAutoCheck.IsChecked = _settings.TodoShowFromData;
        TodoAutoCheck.IsCheckedChanged += (_, _) => { if (_loading) return; _settings.TodoShowFromData = TodoAutoCheck.IsChecked == true; Apply(); };
        foreach (int seconds in Settings.TodoRefreshChoices)
        {
            var rb = new RadioButton
            {
                Content = seconds == 0 ? "never (only when I ask)" : seconds < 60 ? $"{seconds} s" : $"{seconds / 60} min", GroupName = "todorefresh",
                IsChecked = seconds == _settings.TodoRefreshSeconds, Margin = new Thickness(0, 0, 12, 2),
            };
            rb.IsCheckedChanged += (_, _) => { if (!_loading && rb.IsChecked == true) { _settings.TodoRefreshSeconds = seconds; Apply(); } };
            TodoRefreshPanel.Children.Add(rb);
        }
        TodoOpen.Click += (_, _) => _hooks?.OpenTodo?.Invoke();
        TodoClear.Click += (_, _) =>
        {
            int n = _hooks?.ClearFinished?.Invoke() ?? 0;
            TodoStatus.Text = n == 0 ? "Nothing finished to clear." : $"Cleared {n} finished {(n == 1 ? "item" : "items")}.";
        };
    }
}
