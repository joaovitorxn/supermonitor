using Supermonitor.Core.Overlay;

namespace Supermonitor.Tests.Overlay;

public class OverlayPlacementTests
{
    private static readonly PixelRect Monitor = new(100, 50, 1920, 1080);

    [Theory]
    [InlineData(OverlayAnchor.TopLeft, 110, 60)]
    [InlineData(OverlayAnchor.TopCenter, 970, 60)]
    [InlineData(OverlayAnchor.TopRight, 1810, 60)]
    [InlineData(OverlayAnchor.MiddleLeft, 110, 550)]
    [InlineData(OverlayAnchor.Center, 970, 550)]
    [InlineData(OverlayAnchor.MiddleRight, 1810, 550)]
    [InlineData(OverlayAnchor.BottomLeft, 110, 1020)]
    [InlineData(OverlayAnchor.BottomCenter, 970, 1020)]
    [InlineData(OverlayAnchor.BottomRight, 1810, 1020)]
    public void Anchors_place_the_overlay_with_the_offset_as_an_inner_margin(OverlayAnchor anchor, int x, int y)
    {
        // Overlay 200x100, offset 10 px. On right and bottom anchors the offset moves away from the edge; at center, it shifts right/down.
        var position = OverlayPlacement.Compute(anchor, 10, 10, Monitor, 200, 100);

        Assert.Equal((x, y), position);
    }

    [Fact]
    public void Center_offset_shifts_right_and_down()
    {
        var centered = OverlayPlacement.Compute(OverlayAnchor.Center, 0, 0, Monitor, 200, 100);
        var shifted = OverlayPlacement.Compute(OverlayAnchor.Center, 30, -20, Monitor, 200, 100);

        Assert.Equal((centered.X + 30, centered.Y - 20), shifted);
    }

    [Fact]
    public void Position_is_clamped_inside_the_monitor()
    {
        var farRight = OverlayPlacement.Compute(OverlayAnchor.TopLeft, 99999, 99999, Monitor, 200, 100);
        var farLeft = OverlayPlacement.Compute(OverlayAnchor.TopLeft, -99999, -99999, Monitor, 200, 100);

        Assert.Equal((Monitor.Right - 200, Monitor.Bottom - 100), farRight);
        Assert.Equal((Monitor.X, Monitor.Y), farLeft);
    }

    [Fact]
    public void An_overlay_larger_than_the_monitor_sticks_to_the_top_left()
    {
        var position = OverlayPlacement.Compute(OverlayAnchor.BottomRight, 0, 0, new PixelRect(0, 0, 100, 100), 400, 400);

        Assert.Equal((0, 0), position);
    }
}
