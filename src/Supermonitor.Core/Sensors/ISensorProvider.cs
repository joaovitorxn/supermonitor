using Supermonitor.Core.Metrics;

namespace Supermonitor.Core.Sensors;

public interface ISensorProvider : IDisposable
{
    string Name { get; }

    TimeSpan MinInterval { get; }

    void Initialize(MetricStore store);

    void Sample(MetricStore store, TimeSpan elapsed);
}

public sealed record ProviderStatus(string Name, bool IsAvailable, string? UnavailableReason);
