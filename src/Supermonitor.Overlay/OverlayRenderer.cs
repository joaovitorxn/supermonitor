using System.Numerics;
using System.Runtime.InteropServices;
using Supermonitor.Core.Overlay;
using Supermonitor.Overlay.Interop;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using AlphaMode = Vortice.DCommon.AlphaMode;

namespace Supermonitor.Overlay;

public readonly record struct OverlayBitmap(nint Hdc, nint Bits, int Width, int Height)
{
    public unsafe ReadOnlySpan<byte> Pixels => new((void*)Bits, Width * Height * 4);
}

// Desenha o OverlayFrame com Direct2D/DirectWrite num DIB 32 bits premultiplicado (o formato do UpdateLayeredWindow).
// Não toca em janelas: o overlay apresenta o DIB e o preview do editor copia os pixels.
public sealed class OverlayRenderer : IDisposable
{
    private readonly ID2D1Factory1 _d2d = D2D1.D2D1CreateFactory<ID2D1Factory1>();
    private readonly IDWriteFactory _dwrite = DWrite.DWriteCreateFactory<IDWriteFactory>();
    private readonly nint _hdc = Gdi32.CreateCompatibleDC(0);
    private ID2D1DCRenderTarget? _target;
    private ID2D1SolidColorBrush? _brush;
    private IDWriteTextFormat? _format;
    private string _formatKey = string.Empty;
    private nint _dib;
    private nint _originalBitmap;
    private nint _bits;
    private int _width;
    private int _height;
    private bool _disposed;

    public OverlayBitmap Render(OverlayFrame frame, OverlayStyle style, float scale)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var fontSize = style.FontSize * scale;
        var padding = style.Padding * scale;
        var format = GetFormat(style.FontFamily, fontSize);
        var count = frame.Rows.Length;
        var labels = new IDWriteTextLayout[count];
        var values = new IDWriteTextLayout[count];
        try
        {
            for (var i = 0; i < count; i++)
            {
                labels[i] = _dwrite.CreateTextLayout(frame.Rows[i].Label, format, 4096f, fontSize * 3);
                values[i] = _dwrite.CreateTextLayout(frame.Rows[i].Value, format, 4096f, fontSize * 3);
            }

            var labelX = new float[count];
            var valueX = new float[count];
            var rowY = new float[count];
            var (textWidth, textHeight) = Arrange(style.Layout, labels, values, fontSize, labelX, valueX, rowY);

            var hasGraph = style.ShowFrameGraph && frame.Graph.Length >= 2;
            var graphWidth = style.GraphWidth * scale;
            var graphHeight = style.GraphHeight * scale;
            var graphGap = count == 0 ? 0 : fontSize * 0.5f;
            var contentWidth = Math.Max(textWidth, hasGraph ? graphWidth : 0);
            var contentHeight = textHeight + (hasGraph ? graphGap + graphHeight : 0);
            var width = Math.Max(1, (int)Math.Ceiling(contentWidth + 2 * padding));
            var height = Math.Max(1, (int)Math.Ceiling(contentHeight + 2 * padding));

            EnsureSurface(width, height);
            var target = EnsureTarget();
            target.BindDC(_hdc, new RectI(0, 0, width, height));
            target.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Grayscale;
            target.BeginDraw();
            target.Clear(new Color4(0f, 0f, 0f, 0f));

            if (style.BackgroundOpacity > 0)
            {
                SetBrush(Rgba.Parse(style.BackgroundColor, new Rgba(0, 0, 0)), style.BackgroundOpacity);
                var radius = Math.Min(style.CornerRadius * scale, Math.Min(width, height) / 2f);
                target.FillRoundedRectangle(new RoundedRectangle(new System.Drawing.RectangleF(0, 0, width, height), radius, radius), _brush!);
            }

            for (var i = 0; i < count; i++)
            {
                SetBrush(frame.Rows[i].LabelColor);
                target.DrawTextLayout(new Vector2(padding + labelX[i], padding + rowY[i]), labels[i], _brush!);
                SetBrush(frame.Rows[i].ValueColor);
                target.DrawTextLayout(new Vector2(padding + valueX[i], padding + rowY[i]), values[i], _brush!);
            }

            if (hasGraph)
            {
                DrawGraph(target, frame.Graph, padding, padding + textHeight + graphGap, graphWidth, graphHeight, scale, Rgba.Parse(style.ValueColor, new Rgba(0xFF, 0xFF, 0xFF)));
            }

            target.EndDraw();
            return new OverlayBitmap(_hdc, _bits, width, height);
        }
        finally
        {
            foreach (var layout in labels.Concat(values))
            {
                layout?.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _brush?.Dispose();
        _target?.Dispose();
        _format?.Dispose();
        if (_dib != 0)
        {
            Gdi32.SelectObject(_hdc, _originalBitmap);
            Gdi32.DeleteObject(_dib);
        }

        Gdi32.DeleteDC(_hdc);
        _dwrite.Dispose();
        _d2d.Dispose();
    }

    // Preenche as posições de cada linha e devolve o tamanho do bloco de texto.
    private static (float Width, float Height) Arrange(OverlayLayout layout, IDWriteTextLayout[] labels, IDWriteTextLayout[] values, float fontSize, float[] labelX, float[] valueX, float[] rowY)
    {
        var count = labels.Length;
        if (count == 0)
        {
            return (0, 0);
        }

        var lineHeight = 0f;
        var labelColumn = 0f;
        var valueColumn = 0f;
        for (var i = 0; i < count; i++)
        {
            var label = labels[i].Metrics;
            var value = values[i].Metrics;
            lineHeight = Math.Max(lineHeight, Math.Max(label.Height, value.Height));
            labelColumn = Math.Max(labelColumn, label.Width);
            valueColumn = Math.Max(valueColumn, value.Width);
        }

        if (layout == OverlayLayout.Vertical)
        {
            var gap = fontSize * 0.8f;
            var rowGap = fontSize * 0.15f;
            for (var i = 0; i < count; i++)
            {
                rowY[i] = i * (lineHeight + rowGap);
                labelX[i] = 0;
                valueX[i] = labelColumn + gap + (valueColumn - values[i].Metrics.Width);
            }

            return (labelColumn + gap + valueColumn, count * lineHeight + (count - 1) * rowGap);
        }

        var itemGap = layout == OverlayLayout.Compact ? fontSize * 0.7f : fontSize * 1.4f;
        var labelGap = fontSize * 0.35f;
        var x = 0f;
        for (var i = 0; i < count; i++)
        {
            labelX[i] = x;
            valueX[i] = x + labels[i].Metrics.Width + labelGap;
            rowY[i] = 0;
            x = valueX[i] + values[i].Metrics.Width + itemGap;
        }

        return (x - itemGap, lineHeight);
    }

    private void DrawGraph(ID2D1DCRenderTarget target, float[] samples, float x, float y, float width, float height, float scale, Rgba lineColor)
    {
        SetBrush(new Rgba(0xFF, 0xFF, 0xFF), 0.10f);
        target.FillRoundedRectangle(new RoundedRectangle(new System.Drawing.RectangleF(x, y, width, height), 0, 0), _brush!);

        var ceiling = Math.Max(16.7f, samples.Max() * 1.1f);
        var step = width / (samples.Length - 1);
        SetBrush(lineColor);
        var previous = default(Vector2);
        for (var i = 0; i < samples.Length; i++)
        {
            var point = new Vector2(x + i * step, y + height - Math.Clamp(samples[i] / ceiling, 0f, 1f) * height);
            if (i > 0)
            {
                target.DrawLine(previous, point, _brush!, Math.Max(1f, scale));
            }

            previous = point;
        }
    }

    private void SetBrush(Rgba color, float alpha = 1f) =>
        _brush!.Color = new Color4(color.R / 255f, color.G / 255f, color.B / 255f, alpha * color.A / 255f);

    private IDWriteTextFormat GetFormat(string family, float size)
    {
        var key = $"{family}|{size:0.##}";
        if (_format is null || key != _formatKey)
        {
            _format?.Dispose();
            _format = _dwrite.CreateTextFormat(family, FontWeight.SemiBold, Vortice.DirectWrite.FontStyle.Normal, FontStretch.Normal, size);
            _format.WordWrapping = WordWrapping.NoWrap;
            _formatKey = key;
        }

        return _format;
    }

    private ID2D1DCRenderTarget EnsureTarget()
    {
        if (_target is null)
        {
            var properties = new RenderTargetProperties(
                // Software: o overlay é pequeno, evita a leitura GPU→CPU a cada frame durante o jogo e não sofre perda de dispositivo (TDR).
                RenderTargetType.Software,
                new PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied),
                0,
                0,
                RenderTargetUsage.GdiCompatible,
                FeatureLevel.Default);
            _target = _d2d.CreateDCRenderTarget(properties);
            _brush = _target.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 1f));
        }

        return _target;
    }

    private void EnsureSurface(int width, int height)
    {
        if (_dib != 0 && width == _width && height == _height)
        {
            return;
        }

        var info = new Gdi32.BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<Gdi32.BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
        };
        var dib = Gdi32.CreateDIBSection(_hdc, ref info, 0, out var bits, 0, 0);
        if (dib == 0)
        {
            throw new InvalidOperationException("CreateDIBSection failed.");
        }

        var previous = Gdi32.SelectObject(_hdc, dib);
        if (_dib == 0)
        {
            _originalBitmap = previous;
        }
        else
        {
            Gdi32.DeleteObject(_dib);
        }

        _dib = dib;
        _bits = bits;
        _width = width;
        _height = height;
    }
}
