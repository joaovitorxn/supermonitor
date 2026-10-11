using Supermonitor.Core.Collections;

namespace Supermonitor.Core.Sensors.Network;

public readonly record struct PingSnapshot(double LatencyMs, double JitterMs, double LossPercent);

public sealed class PingStatistics(int window)
{
    private readonly RingBuffer<float> _samples = new(window);

    public void AddReply(double milliseconds) => _samples.Add((float)milliseconds);

    public void AddLoss() => _samples.Add(float.NaN);

    public PingSnapshot GetSnapshot()
    {
        if (_samples.Count == 0)
        {
            return new PingSnapshot(double.NaN, double.NaN, double.NaN);
        }

        var lost = 0;
        double jitterSum = 0;
        var jitterCount = 0;
        var previous = float.NaN;
        for (var i = 0; i < _samples.Count; i++)
        {
            var sample = _samples[i];
            if (float.IsNaN(sample))
            {
                lost++;
                continue;
            }

            if (!float.IsNaN(previous))
            {
                jitterSum += Math.Abs(sample - previous);
                jitterCount++;
            }

            previous = sample;
        }

        return new PingSnapshot(
            _samples.Latest,
            jitterCount == 0 ? double.NaN : jitterSum / jitterCount,
            lost * 100.0 / _samples.Count);
    }
}
