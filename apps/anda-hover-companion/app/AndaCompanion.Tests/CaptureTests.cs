using AndaCompanion.Services;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

public class CaptureTests
{
    [Theory]
    [InlineData("ConsoleWindowClass")]
    [InlineData("CASCADIA_HOSTING_WINDOW_CLASS")]
    [InlineData("PuTTY")]
    [InlineData("mintty")]
    [InlineData("VirtualConsoleClass")]
    [InlineData("Alacritty")]
    [InlineData("org.wezfurlong.wezterm")]
    [InlineData("consolewindowclass")]            // class names are matched case-insensitively
    public void Terminals_are_never_sent_a_keystroke(string windowClass) =>
        Assert.True(TerminalGuard.IsTerminal(windowClass));

    [Theory]
    [InlineData("Notepad")]
    [InlineData("OpusApp")]                        // Word
    [InlineData("Chrome_WidgetWin_1")]
    [InlineData("")]
    [InlineData(null)]
    public void Everything_else_is_allowed(string? windowClass) =>
        Assert.False(TerminalGuard.IsTerminal(windowClass));

    [Fact]
    public void Outcomes_that_need_a_word_to_the_user_have_a_notice_and_the_rest_are_silent()
    {
        Assert.Contains("terminals", new CaptureResult(CaptureOutcome.TerminalInFront).Notice);
        Assert.Contains("Release Ctrl and Alt", new CaptureResult(CaptureOutcome.ModifiersHeld).Notice);
        Assert.Contains("busy", new CaptureResult(CaptureOutcome.ClipboardBusy).Notice);
        Assert.Contains("too large", new CaptureResult(CaptureOutcome.ClipboardTooLarge).Notice);
        Assert.Null(new CaptureResult(CaptureOutcome.Captured, "216482").Notice);
        Assert.Null(new CaptureResult(CaptureOutcome.NothingSelected).Notice);
    }

    [Fact]
    public void Notices_are_short_enough_to_read_at_a_glance()
    {
        foreach (var outcome in Enum.GetValues<CaptureOutcome>())
            if (new CaptureResult(outcome).Notice is { } notice)
                Assert.True(notice.Length <= 50, $"{outcome}: '{notice}' is {notice.Length} chars");
    }

    [Fact]
    public void Notice_lifetime_scales_with_length_within_sane_bounds()
    {
        Assert.Equal(TimeSpan.FromSeconds(2.5), ChipWindow.NoticeLifetime("Hi"));
        Assert.True(ChipWindow.NoticeLifetime("Paused in terminals. Type the number instead") > TimeSpan.FromSeconds(3.5));
        Assert.Equal(TimeSpan.FromSeconds(8), ChipWindow.NoticeLifetime(new string('x', 500)));
    }

    [Fact]
    public void Capture_is_unsupported_off_windows_rather_than_throwing()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Equal(CaptureOutcome.Unsupported, TextCaptureService.CaptureSelection().Outcome);
    }

    [Fact]
    public void Plain_memory_formats_can_be_replayed_and_handle_formats_cannot()
    {
        Assert.False(ClipboardNative.IsHandleOnly(1));      // CF_TEXT
        Assert.False(ClipboardNative.IsHandleOnly(13));     // CF_UNICODETEXT
        Assert.False(ClipboardNative.IsHandleOnly(8));      // CF_DIB
        Assert.True(ClipboardNative.IsHandleOnly(2));       // CF_BITMAP (GDI handle)
        Assert.True(ClipboardNative.IsHandleOnly(3));       // CF_METAFILEPICT
        Assert.True(ClipboardNative.IsHandleOnly(14));      // CF_ENHMETAFILE
        Assert.True(ClipboardNative.IsHandleOnly(0x200));   // private range
    }

    [Fact]
    public void Self_test_arguments_parse()
    {
        Assert.Null(SelfTestOptions.Parse(Array.Empty<string>()));
        Assert.Null(SelfTestOptions.Parse(new[] { "--other" }));
        Assert.EndsWith("selftest-out", SelfTestOptions.Parse(new[] { "--selftest" })!.OutDir);
        Assert.Equal(Path.GetFullPath("out dir"), SelfTestOptions.Parse(new[] { "--selftest", "out dir" })!.OutDir);
        Assert.Equal(Path.GetFullPath("x"), SelfTestOptions.Parse(new[] { "--SELFTEST=x" })!.OutDir);
        Assert.EndsWith("selftest-out", SelfTestOptions.Parse(new[] { "--selftest", "--verbose" })!.OutDir);
    }
}
