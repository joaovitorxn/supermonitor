using Supermonitor.Core.Collections;

namespace Supermonitor.Core.Frames;

public readonly record struct FrameStats(bool HasData, double Fps, double FrameTimeMs, double Low1PercentFps, long FrameCount)
{
    public static FrameStats None { get; } = new(false, double.NaN, double.NaN, double.NaN, 0);
}

// Receives present timestamps (ms) of the target process and computes FPS, frametime and 1% low.
// Written by the ETW thread, read by the overlay and dashboard threads; all under a short lock.
public sealed class FrameTimeAggregator
{
    public const int Capacity = 16384;
    public const double FpsWindowMs = 1000;
    public const double LowWindowMs = 10_000;
    public const double MaxGapMs = 5000;

    private readonly Lock _gate = new();
    private readonly RingBuffer<double> _timestamps = new(Capacity);
    private readonly RingBuffer<float> _frameTimes = new(Capacity);
    private readonly float[] _scratch = new float[Capacity];
    private double _last = double.NaN;
    private long _frameCount;
    private long _version;

    public long Version => Interlocked.Read(ref _version);

    public void AddPresent(double timestampMs)
    {
        lock (_gate)
        {
            if (double.IsNaN(_last))
            {
                _last = timestampMs;
                return;
            }

            var delta = timestampMs - _last;
            if (delta <= 0)
            {
                return;
            }

            if (delta > MaxGapMs)
            {
                // Long pause (loading, alt-tab, minimized game): restarts the sequence without recording the interval.
                _timestamps.Clear();
                _frameTimes.Clear();
                _last = timestampMs;
                _version++;
                return;
            }

            _timestamps.Add(timestampMs);
            _frameTimes.Add((float)delta);
            _last = timestampMs;
            _frameCount++;
            _version++;
        }
    }

    public FrameStats GetStats()
    {
        lock (_gate)
        {
            var count = _frameTimes.Count;
            if (count == 0)
            {
                return FrameStats.None;
            }

            var latest = _timestamps[count - 1];
            double sum = 0;
            var inFpsWindow = 0;
            var inLowWindow = 0;
            for (var i = count - 1; i >= 0; i--)
            {
                var age = latest - _timestamps[i];
                if (age >= LowWindowMs)
                {
                    break;
                }

                var frameTime = _frameTimes[i];
                _scratch[inLowWindow++] = frameTime;
                if (age < FpsWindowMs)
                {
                    sum += frameTime;
                    inFpsWindow++;
                }
            }

            var fps = sum > 0 ? inFpsWindow * 1000d / sum : double.NaN;
            var p99 = Percentile.NearestRank(_scratch.AsSpan(0, inLowWindow), 99);
            var low = p99 > 0 ? 1000d / p99 : double.NaN;
            return new FrameStats(true, fps, _frameTimes.Latest, low, _frameCount);
        }
    }

    public int CopyFrameTimes(Span<float> destination)
    {
        lock (_gate)
        {
            return _frameTimes.CopyLast(destination);
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _timestamps.Clear();
            _frameTimes.Clear();
            _last = double.NaN;
            _frameCount = 0;
            _version++;
        }
    }
}
