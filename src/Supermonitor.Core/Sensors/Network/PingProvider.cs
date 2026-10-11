using Supermonitor.Core.Metrics;

namespace Supermonitor.Core.Sensors.Network;

public sealed class PingProvider(PingMonitor monitor) : ISensorProvider
{
    private MetricHandle _latency;
    private MetricHandle _jitter;
    private MetricHandle _loss;

    public string Name => "Ping";

    public TimeSpan MinInterval => TimeSpan.Zero;

    public void Initialize(MetricStore store)
    {
        _latency = store.Register(new MetricDescriptor(MetricIds.Ping, "Ping", MetricCategory.Network, MetricUnit.Milliseconds, "Latency"), Name);
        _jitter = store.Register(new MetricDescriptor(MetricIds.PingJitter, "Jitter", MetricCategory.Network, MetricUnit.Milliseconds, "Latency"), Name);
        _loss = store.Register(new MetricDescriptor(MetricIds.PingLoss, "Packet loss", MetricCategory.Network, MetricUnit.Percent, "Latency"), Name);
    }

    public void Sample(MetricStore store, TimeSpan elapsed)
    {
        var snapshot = monitor.Snapshot;
        store.Write(_latency, (float)snapshot.LatencyMs);
        store.Write(_jitter, (float)snapshot.JitterMs);
        store.Write(_loss, (float)snapshot.LossPercent);
    }

    public void Dispose()
    {
    }
}
