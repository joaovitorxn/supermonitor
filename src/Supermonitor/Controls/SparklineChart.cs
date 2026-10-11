using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace Supermonitor.Controls;

public sealed partial class SparklineChart : UserControl
{
    private CanvasControl? _canvas;
    private ISeriesSource? _source;
    private float[] _buffer = new float[120];
    private Color _stroke;

    public SparklineChart()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public ISeriesSource? Source
    {
        get => _source;
        set
        {
            if (_source is not null && _canvas is not null)
            {
                _source.Refreshed -= Invalidate;
            }

            _source = value;
            if (_source is not null && _canvas is not null)
            {
                _source.Refreshed += Invalidate;
            }

            Invalidate();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _stroke = (Color)Application.Current.Resources["SystemAccentColor"];
        _canvas = new CanvasControl();
        _canvas.Draw += OnDraw;
        Content = _canvas;
        if (_source is not null)
        {
            _source.Refreshed += Invalidate;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_source is not null)
        {
            _source.Refreshed -= Invalidate;
        }

        if (_canvas is null)
        {
            return;
        }

        _canvas.Draw -= OnDraw;
        _canvas.RemoveFromVisualTree();
        _canvas = null;
        Content = null;
    }

    private void Invalidate() => _canvas?.Invalidate();

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_source is null)
        {
            return;
        }

        var count = Math.Max(2, _source.PointCount);
        if (_buffer.Length < count)
        {
            _buffer = new float[count];
        }

        var points = _buffer.AsSpan(0, count);
        var n = _source.CopyHistory(points);
        if (n < 2)
        {
            return;
        }

        var max = _source.FixedMaximum ?? MaxFinite(points[..n]) * 1.1f;
        if (!(max > 0))
        {
            max = 1;
        }

        var width = (float)sender.ActualWidth;
        var height = (float)sender.ActualHeight;
        var step = width / (count - 1);
        var offset = count - n;
        for (var i = 1; i < n; i++)
        {
            float a = points[i - 1], b = points[i];
            if (!float.IsFinite(a) || !float.IsFinite(b))
            {
                continue;
            }

            var x0 = (offset + i - 1) * step;
            var x1 = (offset + i) * step;
            var y0 = height - Math.Clamp(a / max, 0, 1) * height;
            var y1 = height - Math.Clamp(b / max, 0, 1) * height;
            args.DrawingSession.DrawLine(x0, y0, x1, y1, _stroke, 1.5f);
        }
    }

    private static float MaxFinite(ReadOnlySpan<float> values)
    {
        var max = 0f;
        foreach (var value in values)
        {
            if (float.IsFinite(value) && value > max)
            {
                max = value;
            }
        }

        return max;
    }
}
