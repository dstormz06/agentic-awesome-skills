using Avalonia;

namespace AndaCompanion.UI;

/// <summary>
/// Where a popup goes relative to the cursor. Pure geometry in physical pixels so it is
/// unit-testable on any OS. Rule: prefer below-right of the cursor; if that would leave the
/// work area, flip to the other side of the cursor; finally clamp inside the work area.
/// A popup larger than the work area pins to its top-left corner rather than going off-screen.
/// </summary>
public static class Placement
{
    public static PixelPoint Near(PixelPoint cursor, int width, int height, PixelRect area, int dx = 10, int dy = 14)
    {
        int x = cursor.X + dx, y = cursor.Y + dy;
        if (x + width > area.Right) x = cursor.X - width - dx;
        if (y + height > area.Bottom) y = cursor.Y - height - dy;
        return Clamp(new PixelPoint(x, y), width, height, area);
    }

    public static PixelPoint Clamp(PixelPoint p, int width, int height, PixelRect area) =>
        new(Math.Clamp(p.X, area.X, Math.Max(area.X, area.Right - width)),
            Math.Clamp(p.Y, area.Y, Math.Max(area.Y, area.Bottom - height)));
}
