namespace Supermonitor.Core.Overlay;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;
}

public static class OverlayPlacement
{
    // The offset is an inward margin from the anchor edge; at center it shifts right/down.
    // The result always fits the monitor (or sticks to the top-left corner if the overlay is larger).
    public static (int X, int Y) Compute(OverlayAnchor anchor, int offsetX, int offsetY, PixelRect monitor, int width, int height)
    {
        var column = (int)anchor % 3;
        var row = (int)anchor / 3;

        var x = column switch
        {
            0 => monitor.X + offsetX,
            1 => monitor.X + (monitor.Width - width) / 2 + offsetX,
            _ => monitor.Right - width - offsetX,
        };
        var y = row switch
        {
            0 => monitor.Y + offsetY,
            1 => monitor.Y + (monitor.Height - height) / 2 + offsetY,
            _ => monitor.Bottom - height - offsetY,
        };

        var maxX = Math.Max(monitor.X, monitor.Right - width);
        var maxY = Math.Max(monitor.Y, monitor.Bottom - height);
        return (Math.Clamp(x, monitor.X, maxX), Math.Clamp(y, monitor.Y, maxY));
    }
}
