using System.Numerics;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Supermonitor.Appearance;
using Supermonitor.Core.Dashboard;
using Supermonitor.Core.Settings;
using Windows.UI;

namespace Supermonitor.Controls;

public sealed partial class TimeSeriesChart : UserControl
{
    private CanvasControl? _canvas;
    private ISeriesSource? _source;
    private ChartKind _kind;
    private float[] _history = new float[120];
    private float[] _plot = new float[256];

    public TimeSeriesChart()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public ChartKind Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            Invalidate();
        }
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
        if (_canvas is not null)
        {
            return;
        }

        _canvas = new CanvasControl();
        _canvas.Draw += OnDraw;
        Content = _canvas;
        AccentPalette.Changed += Invalidate;
        if (_source is not null)
        {
            _source.Refreshed += Invalidate;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_canvas is null)
        {
            return;
        }

        if (_source is not null)
        {
            _source.Refreshed -= Invalidate;
        }

        AccentPalette.Changed -= Invalidate;
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

        var width = (float)sender.ActualWidth;
        var height = (float)sender.ActualHeight;
        var count = Math.Max(2, _source.PointCount);
        if (width < 2 || height < 2)
        {
            return;
        }

        if (_history.Length < count)
        {
            _history = new float[count];
        }

        var history = _history.AsSpan(0, count);
        var n = _source.CopyHistory(history);
        if (n < 2)
        {
            return;
        }

        // The whole window (count points) spans the width; the series is right-aligned.
        var slots = Math.Max(2, Math.Min(count, (int)width));
        var visibleSlots = Math.Max(2, (int)Math.Ceiling((double)n * slots / count));
        if (_plot.Length < visibleSlots)
        {
            _plot = new float[visibleSlots];
        }

        var plotted = ChartSampler.Downsample(history[..n], _plot.AsSpan(0, visibleSlots));
        var points = _plot.AsSpan(0, plotted);
        var max = ChartSampler.Maximum(points, _source.FixedMaximum);
        var step = width / (slots - 1);
        var offset = slots - plotted;
        var stroke = AccentPalette.Current;

        float X(int i) => (offset + i) * step;
        float Y(float v) => height - Math.Clamp(v / max, 0, 1) * height;

        var session = args.DrawingSession;
        switch (_kind)
        {
            case ChartKind.Bar:
                var barWidth = Math.Max(1, step - 1);
                for (var i = 0; i < plotted; i++)
                {
                    if (float.IsFinite(points[i]))
                    {
                        var y = Y(points[i]);
                        session.FillRectangle(X(i) - barWidth / 2, y, barWidth, height - y, stroke);
                    }
                }

                return;

            case ChartKind.Area:
                var fill = Color.FromArgb(0x50, stroke.R, stroke.G, stroke.B);
                var run = new List<Vector2>();
                for (var i = 0; i <= plotted; i++)
                {
                    if (i < plotted && float.IsFinite(points[i]))
                    {
                        run.Add(new Vector2(X(i), Y(points[i])));
                        continue;
                    }

                    if (run.Count >= 2)
                    {
                        run.Add(new Vector2(run[^1].X, height));
                        run.Add(new Vector2(run[0].X, height));
                        using var geometry = CanvasGeometry.CreatePolygon(sender, run.ToArray());
                        session.FillGeometry(geometry, fill);
                    }

                    run.Clear();
                }

                goto default;

            default:
                for (var i = 1; i < plotted; i++)
                {
                    float a = points[i - 1], b = points[i];
                    if (float.IsFinite(a) && float.IsFinite(b))
                    {
                        session.DrawLine(X(i - 1), Y(a), X(i), Y(b), stroke, 1.5f);
                    }
                }

                return;
        }
    }
}
