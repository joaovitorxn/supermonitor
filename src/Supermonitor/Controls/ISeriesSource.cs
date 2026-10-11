namespace Supermonitor.Controls;

public interface ISeriesSource
{
    int PointCount { get; }

    float? FixedMaximum { get; }

    event Action? Refreshed;

    int CopyHistory(Span<float> destination);
}
