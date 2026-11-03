using Avalonia.Controls;
using Avalonia.Input;
using AndaCompanion.Data;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

/// <summary>Manage the person's own quick replies: templates the card fills from its record. No AI and no network.</summary>
public partial class QuickRepliesWindow : Window
{
    private readonly QuickReplyService _replies;
    private readonly Action _onChanged;

    public QuickRepliesWindow(QuickReplyService replies, Action onChanged)
    {
        _replies = replies;
        _onChanged = onChanged;
        InitializeComponent();
        PlaceholderHelp.Text = "Placeholders: " + string.Join("  ", QuickReplyFill.Placeholders.Select(p => "{" + p.Name + "}"));

        ReplyList.SelectionChanged += (_, _) =>
        {
            if (ReplyList.SelectedItem is string name && _replies.Replies.FirstOrDefault(r => r.Name == name) is { } r)
            { NameBox.Text = r.Name; TextBox1.Text = r.Text; StatusText.Text = ""; Preview(); }
        };
        TextBox1.TextChanged += (_, _) => Preview();
        NewBtn.Click += (_, _) => { ReplyList.SelectedItem = null; NameBox.Text = ""; TextBox1.Text = ""; StatusText.Text = ""; Preview(); };
        SaveBtn.Click += (_, _) =>
        {
            string? refused = _replies.Upsert(new QuickReply(NameBox.Text ?? "", TextBox1.Text ?? ""));
            StatusText.Text = refused ?? "Saved.";
            if (refused is null) { Reload(select: (NameBox.Text ?? "").Trim()); _onChanged(); }
        };
        DeleteBtn.Click += (_, _) =>
        {
            if (NameBox.Text?.Trim() is { Length: > 0 } name)
            {
                _replies.Delete(name); Reload(select: null);
                NameBox.Text = ""; TextBox1.Text = ""; StatusText.Text = "Deleted."; Preview(); _onChanged();
            }
        };
        CloseBtn.Click += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Reload(select: null);
    }

    private void Preview()
    {
        var demo = new MockAndaRepository().Find("216482");
        PreviewText.Text = demo is null || string.IsNullOrWhiteSpace(TextBox1.Text) ? ""
            : QuickReplyFill.Fill(TextBox1.Text, demo, DateOnly.FromDateTime(DateTime.Now));
    }

    private void Reload(string? select)
    {
        ReplyList.ItemsSource = _replies.Replies.Select(r => r.Name).ToList();
        if (select is { Length: > 0 }) ReplyList.SelectedItem = _replies.Replies.FirstOrDefault(r => r.Name.Equals(select, StringComparison.OrdinalIgnoreCase))?.Name;
    }
}
