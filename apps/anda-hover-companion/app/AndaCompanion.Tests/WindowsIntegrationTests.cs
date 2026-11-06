using AndaCompanion.Services;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// Real Win32, no fakes. These run only on Windows (the CI "Windows box"); on Linux they are
/// reported as skipped, not passed. Filter them out locally with: --filter "Category!=Windows".
/// </summary>
[Trait("Category", "Windows")]
[Collection("Global state")]
public class WindowsIntegrationTests
{
    [WindowsFact]
    public void Clipboard_snapshot_restores_every_plain_format_and_refuses_oversized_content()
    {
        var result = SelfTestChecks.ClipboardRoundTrip();
        Assert.True(result.Status == CheckStatus.Pass, result.Detail);
    }

    [WindowsFact]
    public void Clipboard_owner_window_is_created_once_and_is_stable()
    {
        IntPtr first = ClipboardOwner.Handle;
        Assert.NotEqual(IntPtr.Zero, first);
        Assert.Equal(first, ClipboardOwner.Handle);
    }

    [WindowsFact]
    public void Ole_bookkeeping_formats_are_not_replayed_but_html_is()
    {
        Assert.True(ClipboardNative.IsHandleOnly(ClipboardNative.RegisterFormat("DataObject")));
        Assert.True(ClipboardNative.IsHandleOnly(ClipboardNative.RegisterFormat("Ole Private Data")));
        Assert.False(ClipboardNative.IsHandleOnly(ClipboardNative.RegisterFormat("HTML Format")));
        Assert.Equal("HTML Format", ClipboardNative.FormatName(ClipboardNative.RegisterFormat("HTML Format")));
    }

    [WindowsFact]
    public void Real_hotkey_registration_succeeds_then_second_copy_is_told_they_are_taken()
    {
        using var first = new HotkeyService(_ => { });
        first.Start();
        Assert.True(first.WaitReady(TimeSpan.FromSeconds(5)));
        Assert.All(first.Status, s => Assert.True(s.Registered, HotkeyService.Describe(s) ?? "registered"));

        var problems = new List<string>();
        using var second = new HotkeyService(_ => { }, problems.Add);
        second.Start();
        Assert.True(second.WaitReady(TimeSpan.FromSeconds(5)));
        Assert.All(second.Status, s =>
        {
            Assert.False(s.Registered);
            Assert.Equal(1409, s.Win32Error);
        });

        // the problem callback fires after WaitReady; give the hotkey thread a moment
        int expected = second.Status.Count;   // one message per shortcut (three now: peek, lookup, review a draft)
        Assert.Equal(3, expected);
        SpinWait.SpinUntil(() => problems.Count >= expected, TimeSpan.FromSeconds(2));
        Assert.Equal(expected, problems.Count);
        Assert.All(problems, m => Assert.Contains("already used by another program", m));
    }

    [WindowsFact]
    public void The_edge_connection_round_trips_in_the_current_user_registry_hive()
    {
        const string testBase = @"Software\AndaHoverCompanionTests\NativeMessagingHosts";
        string dir = Temp.NewDir();
        Environment.SetEnvironmentVariable(NativeHostRegistration.DirEnvVar, dir);
        try
        {
            Assert.False(NativeHostRegistration.Status(testBase).Registered);
            var (ok, message) = NativeHostRegistration.Install(@"C:\Apps\AndaCompanion.exe", NativeHostRegistration.DefaultExtensionId, testBase);
            Assert.True(ok, message);
            var status = NativeHostRegistration.Status(testBase);
            Assert.True(status.Registered, status.Message);
            Assert.Contains(NativeHostRegistration.DefaultExtensionId, status.Message);
            Assert.True(File.Exists(NativeHostRegistration.ManifestPath));
            Assert.True(NativeHostRegistration.Uninstall(testBase).Ok);
            Assert.False(NativeHostRegistration.Status(testBase).Registered);
            Assert.False(File.Exists(NativeHostRegistration.ManifestPath));
            Assert.False(NativeHostRegistration.Install(@"C:\x", "bad id", testBase).Ok);
        }
        finally
        {
            Environment.SetEnvironmentVariable(NativeHostRegistration.DirEnvVar, null);
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\AndaHoverCompanionTests", throwOnMissingSubKey: false);
        }
    }

    [WindowsFact]
    public void Disposing_the_service_releases_the_hotkeys()
    {
        var first = new HotkeyService(_ => { });
        first.Start();
        Assert.True(first.WaitReady(TimeSpan.FromSeconds(5)));
        first.Dispose();

        using var again = new HotkeyService(_ => { });
        again.Start();
        Assert.True(again.WaitReady(TimeSpan.FromSeconds(5)));
        Assert.All(again.Status, s => Assert.True(s.Registered, HotkeyService.Describe(s) ?? "registered"));
    }

    [WindowsFact]
    public void Foreground_window_class_can_be_read_without_throwing() =>
        Assert.NotNull(TextCaptureService.ForegroundWindowClass());

    [WindowsFact]
    public void Cursor_position_is_a_real_point()
    {
        var (x, y) = TextCaptureService.GetCursorPosition();
        Assert.InRange(x, -20000, 20000);
        Assert.InRange(y, -20000, 20000);
    }
}
