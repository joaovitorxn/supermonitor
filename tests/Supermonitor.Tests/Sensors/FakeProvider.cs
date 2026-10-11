using Supermonitor.Core.Metrics;
using Supermonitor.Core.Sensors;

namespace Supermonitor.Tests.Sensors;

internal sealed class FakeProvider(string name) : ISensorProvider
{
    private MetricHandle _handle;

    public string Name { get; } = name;

    public TimeSpan MinInterval { get; init; } = TimeSpan.Zero;

    public bool FailInitialize { get; init; }

    public int FailuresRemaining { get; set; }

    public float Value { get; set; } = 1;

    public bool SkipWrite { get; set; }

    public ManualResetEventSlim? InitializeGate { get; init; }

    public List<TimeSpan> Elapsed { get; } = [];

    public bool Disposed { get; private set; }

    public string MetricId => $"{Name}.value";

    public void Initialize(MetricStore store)
    {
        InitializeGate?.Wait();
        if (FailInitialize)
        {
            throw new InvalidOperationException("no hardware");
        }

        _handle = store.Register(new MetricDescriptor(MetricId, Name, MetricCategory.System, MetricUnit.Count), Name);
    }

    public void Sample(MetricStore store, TimeSpan elapsed)
    {
        Elapsed.Add(elapsed);
        if (FailuresRemaining > 0)
        {
            FailuresRemaining--;
            throw new InvalidOperationException("boom");
        }

        if (!SkipWrite)
        {
            store.Write(_handle, Value);
        }
    }

    public void Dispose() => Disposed = true;
}
