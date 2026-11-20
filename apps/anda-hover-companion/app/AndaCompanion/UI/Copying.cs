using Avalonia.Controls;

namespace AndaCompanion.UI;

/// <summary>
/// Text put on the clipboard when the person presses Copy: true when it got there. Windows may refuse for a moment (another program holds
/// the clipboard), so a refusal is an answer, never an error. A test passes its own writer.
/// </summary>
public static class Copying
{
    public static async Task<bool> TextAsync(TopLevel window, string text, Func<string, Task<bool>>? writer = null)
    {
        try
        {
            if (writer is not null) return await writer(text);
            if (window.Clipboard is not { } clipboard) return false;
            await clipboard.SetTextAsync(text);
            return true;
        }
        catch (Exception) { return false; }
    }
}
