using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>Auto-hide only ever takes a card away when it is idle AND the user is not using it.</summary>
public class AutoHideTests
{
    [Theory]
    [InlineData(25, 20, false, false, false, true)]     // idle long enough, nothing holding it open → hide
    [InlineData(19.9, 20, false, false, false, false)]  // not idle long enough
    [InlineData(20, 20, false, false, false, true)]     // boundary: exactly the limit hides
    [InlineData(99, 20, true, false, false, false)]     // pointer over the card → never
    [InlineData(99, 20, false, true, false, false)]     // pinned → never
    [InlineData(99, 20, false, false, true, false)]     // drawer open → never
    [InlineData(99, 0, false, false, false, false)]     // Off → never
    public void Policy(double idle, double limit, bool inside, bool pinned, bool expanded, bool expected) =>
        Assert.Equal(expected, AutoHidePolicy.ShouldHide(idle, limit, inside, pinned, expanded));

    [Fact]
    public void The_offered_choices_are_off_plus_three_and_the_default_is_one_of_them()
    {
        Assert.Equal(new[] { 0, 10, 20, 40 }, AutoHidePolicy.Choices);
        Assert.Contains(AutoHidePolicy.Default, AutoHidePolicy.Choices);
        Assert.Equal("Off", AutoHidePolicy.Label(0));
        Assert.Equal("20 s", AutoHidePolicy.Label(20));
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(10, 10)] [InlineData(40, 40)] [InlineData(7, 20)] [InlineData(-1, 20)] [InlineData(3600, 20)]
    public void Normalize(int input, int expected) => Assert.Equal(expected, AutoHidePolicy.Normalize(input));

    private sealed class Rig
    {
        public double Now;
        public bool Pinned, Expanded;
        public int Hidden;
        public AutoHideController C;
        public Rig() => C = new AutoHideController(() => Pinned, () => Expanded, () => Hidden++, () => Now);
    }

    [Fact]
    public void The_controller_hides_once_after_the_idle_period()
    {
        var r = new Rig();
        r.C.Start(10);
        r.Now = 9; r.C.Tick();
        Assert.Equal(0, r.Hidden);
        r.Now = 10.5; r.C.Tick();
        Assert.Equal(1, r.Hidden);
    }

    [Fact]
    public void Activity_restarts_the_period_and_hovering_pauses_it()
    {
        var r = new Rig();
        r.C.Start(10);
        r.Now = 8; r.C.Touch();                       // the user moved: the clock restarts at 8
        r.Now = 15; r.C.Tick();
        Assert.Equal(0, r.Hidden);                    // only 7 s idle
        r.C.PointerInside = true;
        r.Now = 100; r.C.Tick();
        Assert.Equal(0, r.Hidden);                    // hovering pauses indefinitely
        r.C.PointerInside = false; r.C.Touch();       // leaving restarts the full period
        r.Now = 105; r.C.Tick();
        Assert.Equal(0, r.Hidden);
        r.Now = 111; r.C.Tick();
        Assert.Equal(1, r.Hidden);
    }

    [Fact]
    public void Pinned_and_expanded_cards_are_never_hidden_and_resume_when_released()
    {
        var r = new Rig();
        r.C.Start(10);
        r.Pinned = true; r.Now = 500; r.C.Tick();
        Assert.Equal(0, r.Hidden);
        r.Pinned = false; r.Expanded = true; r.C.Tick();
        Assert.Equal(0, r.Hidden);
        r.Expanded = false; r.C.Tick();
        Assert.Equal(1, r.Hidden);
    }

    [Fact]
    public void Off_never_starts_a_timer()
    {
        var r = new Rig();
        r.C.Start(0);
        r.Now = 1000; r.C.Tick();
        Assert.Equal(0, r.Hidden);
    }
}
