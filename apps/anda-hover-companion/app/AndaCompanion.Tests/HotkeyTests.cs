using AndaCompanion.Services;
using Xunit;

namespace AndaCompanion.Tests;

public class HotkeyTests
{
    private sealed class FakeApi : IHotkeyApi
    {
        public readonly Dictionary<int, (uint Mod, uint Vk)> Registered = new();
        public readonly Dictionary<int, int> FailWith = new();
        public bool Register(int id, uint modifiers, uint virtualKey, out int win32Error)
        {
            if (FailWith.TryGetValue(id, out win32Error)) return false;
            Registered[id] = (modifiers, virtualKey);
            win32Error = 0;
            return true;
        }
        public void Unregister(int id) => Registered.Remove(id);
    }

    [Fact]
    public void Default_bindings_are_the_documented_three_and_do_not_collide()
    {
        Assert.Equal(new[] { "Ctrl+Alt+A", "Ctrl+Alt+Space", "Ctrl+Alt+Shift+R" }, HotkeyService.Bindings.Select(b => b.Combo));
        Assert.Equal(3, HotkeyService.Bindings.Select(b => b.Id).Distinct().Count());
        Assert.Equal(3, HotkeyService.Bindings.Select(b => (b.Modifiers, b.VirtualKey)).Distinct().Count());
    }

    [Fact]
    public void Every_binding_asks_for_ctrl_alt_and_no_auto_repeat()
    {
        foreach (var b in HotkeyService.Bindings)
        {
            Assert.True((b.Modifiers & 0x2) != 0, $"{b.Combo} needs Ctrl");
            Assert.True((b.Modifiers & 0x1) != 0, $"{b.Combo} needs Alt");
            Assert.True((b.Modifiers & 0x4000) != 0, $"{b.Combo} needs MOD_NOREPEAT so holding the keys cannot spam");
        }
        Assert.Equal(0x41u, HotkeyService.Bindings.Single(b => b.Id == HotkeyService.IdPeek).VirtualKey);
        Assert.Equal(0x20u, HotkeyService.Bindings.Single(b => b.Id == HotkeyService.IdLookup).VirtualKey);
        Assert.Equal(0x52u, HotkeyService.Bindings.Single(b => b.Id == HotkeyService.IdDraft).VirtualKey);   // R, with Shift: Ctrl+Alt+D is Word's own
        Assert.True((HotkeyService.Bindings.Single(b => b.Id == HotkeyService.IdDraft).Modifiers & 0x4) != 0, "Review a draft needs Shift");
    }

    [Fact]
    public void Custom_bindings_register_with_their_own_keys_and_failures_name_the_custom_combination()
    {
        var custom = HotkeyService.FromCombos("Ctrl+Shift+P", "Ctrl+Alt+Shift+L", "Ctrl+Alt+Shift+9");
        var api = new FakeApi();
        api.FailWith[HotkeyService.IdDraft] = 1409;
        var status = HotkeyService.RegisterAll(api, custom);
        Assert.Equal((0x2u | 0x4u | 0x4000u, 0x50u), api.Registered[HotkeyService.IdPeek]);
        Assert.Equal(0x4Cu, api.Registered[HotkeyService.IdLookup].Vk);
        var failed = status.Single(s => !s.Registered);
        Assert.Contains("Ctrl+Alt+Shift+9 is already used by another program", HotkeyService.Describe(failed));
    }

    [Fact]
    public void Clean_registration_reports_every_hotkey_live_and_says_nothing()
    {
        var api = new FakeApi();
        var status = HotkeyService.RegisterAll(api);
        Assert.All(status, s => Assert.True(s.Registered));
        Assert.All(status, s => Assert.Null(HotkeyService.Describe(s)));
        Assert.Equal(3, api.Registered.Count);
    }

    [Fact]
    public void A_taken_combination_is_reported_in_plain_words_with_the_fallback()
    {
        var api = new FakeApi();
        api.FailWith[HotkeyService.IdPeek] = 1409;     // ERROR_HOTKEY_ALREADY_REGISTERED
        var status = HotkeyService.RegisterAll(api);

        var peek = status.Single(s => s.Binding.Id == HotkeyService.IdPeek);
        Assert.False(peek.Registered);
        string message = HotkeyService.Describe(peek)!;
        Assert.Contains("Ctrl+Alt+A is already used by another program", message);
        Assert.Contains("choose another shortcut in Settings, or use the tray icon", message);

        Assert.True(status.Single(s => s.Binding.Id == HotkeyService.IdLookup).Registered);   // one failure must not block the other
    }

    [Fact]
    public void Other_failures_include_the_windows_error_number()
    {
        var api = new FakeApi();
        api.FailWith[HotkeyService.IdLookup] = 5;
        var lookup = HotkeyService.RegisterAll(api).Single(s => s.Binding.Id == HotkeyService.IdLookup);
        Assert.Contains("Windows error 5", HotkeyService.Describe(lookup));
    }

    [Fact]
    public void Off_windows_the_service_starts_and_stops_without_touching_win32()
    {
        if (OperatingSystem.IsWindows()) return;
        using var svc = new HotkeyService(_ => { }, api: new FakeApi());
        svc.Start();
        Assert.True(svc.WaitReady(TimeSpan.FromSeconds(1)));
        Assert.Empty(svc.Status);
    }
}
