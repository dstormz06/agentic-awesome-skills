using Avalonia;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

public class PlacementTests
{
    private static readonly PixelRect Screen = new(0, 0, 1920, 1040);   // work area (taskbar excluded)

    [Fact]
    public void Prefers_below_right_of_the_cursor()
    {
        var p = Placement.Near(new PixelPoint(100, 100), 400, 500, Screen);
        Assert.Equal(new PixelPoint(110, 114), p);
    }

    [Fact]
    public void Flips_left_when_there_is_no_room_on_the_right()
    {
        var p = Placement.Near(new PixelPoint(1800, 100), 400, 500, Screen);
        Assert.Equal(1800 - 400 - 10, p.X);
        Assert.Equal(114, p.Y);
    }

    [Fact]
    public void Flips_above_when_there_is_no_room_below()
    {
        var p = Placement.Near(new PixelPoint(100, 900), 400, 500, Screen);
        Assert.Equal(110, p.X);
        Assert.Equal(900 - 500 - 14, p.Y);
    }

    [Fact]
    public void Tray_corner_case_stays_fully_inside()
    {
        // Clicking the tray icon puts the cursor in the bottom-right corner.
        var size = (W: 492, H: 760);
        var p = Placement.Near(new PixelPoint(1919, 1039), size.W, size.H, Screen);
        AssertInside(p, size.W, size.H, Screen);
    }

    [Fact]
    public void Works_on_a_second_monitor_with_a_negative_origin()
    {
        var left = new PixelRect(-1920, 0, 1920, 1040);
        var p = Placement.Near(new PixelPoint(-30, 1000), 424, 650, left);
        AssertInside(p, 424, 650, left);
    }

    [Fact]
    public void A_popup_larger_than_the_screen_pins_to_the_top_left_instead_of_going_off()
    {
        var small = new PixelRect(0, 0, 1024, 600);
        var p = Placement.Near(new PixelPoint(500, 300), 500, 900, small);
        Assert.Equal(0, p.Y);
    }

    [Fact]
    public void Clamp_pulls_a_grown_window_back_on_screen()
    {
        var p = Placement.Clamp(new PixelPoint(1700, 800), 492, 760, Screen);
        AssertInside(p, 492, 760, Screen);
    }

    [Fact]
    public void Property_sweep_every_cursor_position_yields_an_on_screen_window()
    {
        foreach (var (w, h) in new[] { (372, 420), (424, 650), (492, 760), (300, 60) })
            for (int x = 0; x <= 1919; x += 97)
                for (int y = 0; y <= 1039; y += 83)
                    AssertInside(Placement.Near(new PixelPoint(x, y), w, h, Screen), w, h, Screen);
    }

    private static void AssertInside(PixelPoint p, int w, int h, PixelRect area)
    {
        Assert.True(p.X >= area.X && p.Y >= area.Y && p.X + w <= area.Right && p.Y + h <= area.Bottom,
            $"({p.X},{p.Y}) {w}x{h} escapes {area}");
    }
}
