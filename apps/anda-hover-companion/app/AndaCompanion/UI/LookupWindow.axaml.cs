using Avalonia.Controls;
using Avalonia.Input;
using AndaCompanion.Data;

namespace AndaCompanion.UI;

public partial class LookupWindow : Window
{
    private readonly Action<string> _onValid;

    public LookupWindow(Action<string> onValid)
    {
        _onValid = onValid;
        InitializeComponent();
        GoBtn.Click += (_, _) => Submit();
        Input.KeyDown += (_, e) => { if (e.Key == Key.Enter) Submit(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Deactivated += (_, _) => Close();
        Opened += (_, _) => Input.Focus();
    }

    private void Submit()
    {
        var number = AndaValidator.Extract(Input.Text);
        if (number is null)
        {
            ErrorText.Text = "Not a recognized ANDA number.";
            ErrorText.IsVisible = true;
            return;
        }
        Close();
        _onValid(number);
    }
}
