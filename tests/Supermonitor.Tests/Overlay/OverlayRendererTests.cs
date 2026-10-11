using Supermonitor.Core.Overlay;
using Supermonitor.Overlay;

namespace Supermonitor.Tests.Overlay;

public sealed class OverlayRendererTests : IDisposable
{
    private readonly OverlayRenderer _renderer = new();

    public void Dispose() => _renderer.Dispose();

    private static OverlayFrame Frame(int rows, float[]? graph = null)
    {
        var items = Enumerable.Range(0, rows)
            .Select(i => new OverlayRow($"Label {i}", $"{100 + i} ms", new Rgba(0xB0, 0xB0, 0xB0), new Rgba(0xFF, 0xFF, 0xFF)))
            .ToArray();
        return new OverlayFrame(items, graph ?? [], graph is null ? 0 : 1);
    }

    private static OverlayStyle Style(Action<OverlayStyle>? configure = null)
    {
        var style = new OverlayStyle { Padding = 10, CornerRadius = 0, BackgroundOpacity = 1, BackgroundColor = "#FF0000" };
        configure?.Invoke(style);
        return style;
    }

    private static (byte B, byte G, byte R, byte A) Pixel(OverlayBitmap bitmap, int x, int y)
    {
        var pixels = bitmap.Pixels;
        var index = (y * bitmap.Width + x) * 4;
        return (pixels[index], pixels[index + 1], pixels[index + 2], pixels[index + 3]);
    }

    [Fact]
    public void More_rows_make_a_vertical_overlay_taller_but_not_wider()
    {
        var one = _renderer.Render(Frame(1), Style(), 1);
        var oneWidth = one.Width;
        var oneHeight = one.Height;

        var three = _renderer.Render(Frame(3), Style(), 1);

        Assert.True(three.Height > oneHeight * 2, $"{three.Height} vs {oneHeight}");
        Assert.InRange(three.Width, oneWidth - 2, oneWidth + 2);
    }

    [Fact]
    public void Horizontal_layout_is_wider_and_shorter_than_vertical()
    {
        var vertical = _renderer.Render(Frame(3), Style(s => s.Layout = OverlayLayout.Vertical), 1);
        var (verticalWidth, verticalHeight) = (vertical.Width, vertical.Height);

        var horizontal = _renderer.Render(Frame(3), Style(s => s.Layout = OverlayLayout.Horizontal), 1);

        Assert.True(horizontal.Width > verticalWidth);
        Assert.True(horizontal.Height < verticalHeight);
    }

    [Fact]
    public void Compact_layout_is_narrower_than_horizontal()
    {
        var horizontal = _renderer.Render(Frame(3), Style(s => s.Layout = OverlayLayout.Horizontal), 1);
        var horizontalWidth = horizontal.Width;

        var compact = _renderer.Render(Frame(3), Style(s => s.Layout = OverlayLayout.Compact), 1);

        Assert.True(compact.Width < horizontalWidth);
    }

    [Fact]
    public void Padding_grows_the_bitmap_on_every_side()
    {
        var small = _renderer.Render(Frame(2), Style(s => s.Padding = 0), 1);
        var (smallWidth, smallHeight) = (small.Width, small.Height);

        var large = _renderer.Render(Frame(2), Style(s => s.Padding = 20), 1);

        Assert.Equal(smallWidth + 40, large.Width);
        Assert.Equal(smallHeight + 40, large.Height);
    }

    [Fact]
    public void Scale_multiplies_the_size()
    {
        var normal = _renderer.Render(Frame(2), Style(), 1);
        var (normalWidth, normalHeight) = (normal.Width, normal.Height);

        var doubled = _renderer.Render(Frame(2), Style(), 2);

        // A métrica do DirectWrite arredonda por glifo; 10% de tolerância cobre isso.
        Assert.InRange(doubled.Width, normalWidth * 2 * 0.9, normalWidth * 2 * 1.1);
        Assert.InRange(doubled.Height, normalHeight * 2 * 0.9, normalHeight * 2 * 1.1);
    }

    [Fact]
    public void Opaque_background_fills_the_padding_and_rounded_corners_stay_transparent()
    {
        var bitmap = _renderer.Render(Frame(1), Style(s => s.CornerRadius = 12), 1);

        var padding = Pixel(bitmap, bitmap.Width - 3, bitmap.Height / 2);
        var corner = Pixel(bitmap, 0, 0);

        Assert.Equal(((byte)0, (byte)0, (byte)255, (byte)255), padding);
        Assert.Equal(0, corner.A);
    }

    [Fact]
    public void Background_opacity_is_premultiplied()
    {
        var bitmap = _renderer.Render(Frame(1), Style(s => s.BackgroundOpacity = 0.5f), 1);

        var padding = Pixel(bitmap, bitmap.Width - 3, bitmap.Height / 2);

        Assert.InRange(padding.A, 126, 130);
        Assert.InRange(padding.R, 126, 130);
        Assert.Equal(0, padding.G);
        Assert.Equal(0, padding.B);
    }

    [Fact]
    public void Text_is_drawn_even_without_a_background()
    {
        var bitmap = _renderer.Render(Frame(2), Style(s => s.BackgroundOpacity = 0), 1);

        var drawn = 0;
        var pixels = bitmap.Pixels;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0)
            {
                drawn++;
            }
        }

        Assert.True(drawn > 30, $"only {drawn} pixels drawn");
        Assert.Equal(0, Pixel(bitmap, 0, 0).A);
    }

    [Fact]
    public void The_graph_adds_its_height_and_is_drawn()
    {
        var graph = Enumerable.Range(0, 80).Select(i => 6f + (i % 10)).ToArray();
        var without = _renderer.Render(Frame(2), Style(s => s.ShowFrameGraph = true), 1);
        var withoutHeight = without.Height;

        var with = _renderer.Render(Frame(2, graph), Style(s => { s.ShowFrameGraph = true; s.GraphWidth = 160; s.GraphHeight = 40; s.BackgroundOpacity = 0; }), 1);

        Assert.True(with.Height >= withoutHeight + 40, $"{with.Height} vs {withoutHeight}");
        Assert.True(with.Width >= 160 + 20);
        var drawnInGraphArea = 0;
        for (var y = with.Height - 10 - 40; y < with.Height - 10; y++)
        {
            for (var x = 10; x < 170; x++)
            {
                if (Pixel(with, x, y).A != 0)
                {
                    drawnInGraphArea++;
                }
            }
        }

        Assert.True(drawnInGraphArea > 100, $"only {drawnInGraphArea} pixels in the graph area");
    }

    [Fact]
    public void A_graph_with_fewer_than_two_samples_is_not_drawn()
    {
        var without = _renderer.Render(Frame(1), Style(s => s.ShowFrameGraph = true), 1);
        var withoutHeight = without.Height;

        var one = _renderer.Render(Frame(1, [10f]), Style(s => s.ShowFrameGraph = true), 1);

        Assert.Equal(withoutHeight, one.Height);
    }

    [Fact]
    public void An_empty_frame_renders_without_throwing()
    {
        var bitmap = _renderer.Render(Frame(0), Style(), 1);

        Assert.True(bitmap.Width >= 1);
        Assert.True(bitmap.Height >= 1);
    }

    [Fact]
    public void Rendering_repeatedly_with_changing_sizes_stays_valid()
    {
        for (var rows = 1; rows <= 6; rows++)
        {
            var bitmap = _renderer.Render(Frame(rows), Style(), 1);

            Assert.Equal(bitmap.Width * bitmap.Height * 4, bitmap.Pixels.Length);
            Assert.NotEqual(0, Pixel(bitmap, bitmap.Width - 3, bitmap.Height / 2).A);
        }
    }
}
