using Supermonitor.Core.Frames;
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Overlay;

namespace Supermonitor.Overlay;

// Reads the latest value of each metric from MetricStore and the FPS numbers from FrameCaptureService.
public sealed class LiveOverlayValues(MetricStore store, FrameCaptureService frames, TimeProvider time) : IOverlayValues
{
    public DateTime Now => time.GetLocalNow().DateTime;

    public long FrameVersion => frames.FrameVersion;

    public double Get(OverlayMetric metric) => metric switch
    {
        OverlayMetric.Fps => frames.Current.Fps,
        OverlayMetric.FrameTime => frames.Current.FrameTimeMs,
        OverlayMetric.Low1Percent => frames.Current.Low1PercentFps,
        _ => OverlayMetricCatalog.StoreMetricId(metric) is { } id ? store.GetLatest(id) : double.NaN,
    };

    public int CopyFrameTimes(Span<float> destination) => frames.CopyFrameTimes(destination);
}
