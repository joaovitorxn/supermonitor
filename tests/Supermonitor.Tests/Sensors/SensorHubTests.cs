using Microsoft.Extensions.Time.Testing;
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Sensors;

namespace Supermonitor.Tests.Sensors;

public class SensorHubTests
{
    private static SensorHub CreateHub(MetricStore store, FakeTimeProvider time, params ISensorProvider[] providers)
    {
        var hub = new SensorHub(store, providers, time, TimeSpan.FromSeconds(1));
        hub.InitializeProviders();
        return hub;
    }

    [Fact]
    public void Initialize_failure_marks_provider_unavailable_and_others_still_sample()
    {
        var store = new MetricStore(10);
        var broken = new FakeProvider("broken") { FailInitialize = true };
        var good = new FakeProvider("good") { Value = 7 };
        var hub = CreateHub(store, new FakeTimeProvider(), broken, good);

        hub.Tick();

        var status = hub.GetStatuses().Single(s => s.Name == "broken");
        Assert.False(status.IsAvailable);
        Assert.Equal("no hardware", status.UnavailableReason);
        Assert.Empty(broken.Elapsed);
        Assert.Equal(7, store.GetLatest(good.MetricId));
    }

    [Fact]
    public void Consecutive_failures_mark_provider_unavailable_and_invalidate_its_metrics()
    {
        var store = new MetricStore(10);
        var provider = new FakeProvider("p");
        var hub = CreateHub(store, new FakeTimeProvider(), provider);
        hub.Tick();
        Assert.Equal(1, store.GetLatest(provider.MetricId));

        provider.FailuresRemaining = SensorHub.MaxConsecutiveFailures;
        for (var i = 0; i < SensorHub.MaxConsecutiveFailures; i++)
        {
            hub.Tick();
        }

        Assert.False(hub.GetStatuses().Single().IsAvailable);
        Assert.True(float.IsNaN(store.GetLatest(provider.MetricId)));

        var samplesBefore = provider.Elapsed.Count;
        hub.Tick();
        Assert.Equal(samplesBefore, provider.Elapsed.Count);
    }

    [Fact]
    public void Failures_below_threshold_followed_by_success_keep_provider_available()
    {
        var store = new MetricStore(10);
        var provider = new FakeProvider("p") { FailuresRemaining = SensorHub.MaxConsecutiveFailures - 1 };
        var hub = CreateHub(store, new FakeTimeProvider(), provider);

        for (var i = 0; i < SensorHub.MaxConsecutiveFailures; i++)
        {
            hub.Tick();
        }

        Assert.True(hub.GetStatuses().Single().IsAvailable);
        Assert.Equal(1, store.GetLatest(provider.MetricId));
    }

    [Fact]
    public void Metric_not_written_in_a_sample_becomes_nan()
    {
        var store = new MetricStore(10);
        var provider = new FakeProvider("p");
        var hub = CreateHub(store, new FakeTimeProvider(), provider);
        hub.Tick();
        Assert.Equal(1, store.GetLatest(provider.MetricId));

        provider.SkipWrite = true;
        hub.Tick();

        Assert.True(float.IsNaN(store.GetLatest(provider.MetricId)));
    }

    [Fact]
    public void Metric_of_provider_not_due_keeps_its_last_value()
    {
        var store = new MetricStore(10);
        var time = new FakeTimeProvider();
        var provider = new FakeProvider("slow") { MinInterval = TimeSpan.FromSeconds(5) };
        var hub = CreateHub(store, time, provider);
        hub.Tick();
        time.Advance(TimeSpan.FromSeconds(1));
        hub.Tick();

        Assert.Equal(1, store.GetLatest(provider.MetricId));
    }

    [Fact]
    public void MinInterval_skips_samples_until_due_and_reports_elapsed()
    {
        var time = new FakeTimeProvider();
        var provider = new FakeProvider("slow") { MinInterval = TimeSpan.FromSeconds(5) };
        var hub = CreateHub(new MetricStore(10), time, provider);

        hub.Tick();
        time.Advance(TimeSpan.FromSeconds(1));
        hub.Tick();
        time.Advance(TimeSpan.FromSeconds(4));
        hub.Tick();

        Assert.Equal(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(5) }, provider.Elapsed);
    }

    [Fact]
    public void Throwing_SampleReady_handler_does_not_stop_ticks()
    {
        var provider = new FakeProvider("p");
        var hub = CreateHub(new MetricStore(10), new FakeTimeProvider(), provider);
        var raised = 0;
        hub.SampleReady += () =>
        {
            raised++;
            throw new InvalidOperationException("ui bug");
        };

        hub.Tick();
        hub.Tick();

        Assert.Equal(2, raised);
        Assert.Equal(2, provider.Elapsed.Count);
    }

    [Fact]
    public void Throwing_SampleReady_handler_does_not_block_later_handlers()
    {
        var hub = CreateHub(new MetricStore(10), new FakeTimeProvider(), new FakeProvider("p"));
        var second = 0;
        hub.SampleReady += () => throw new InvalidOperationException("ui bug");
        hub.SampleReady += () => second++;

        hub.Tick();

        Assert.Equal(1, second);
    }

    [Fact]
    public void Unsubscribed_handler_is_not_called()
    {
        var hub = CreateHub(new MetricStore(10), new FakeTimeProvider(), new FakeProvider("p"));
        var calls = 0;
        Action handler = () => calls++;
        hub.SampleReady += handler;
        hub.SampleReady -= handler;

        hub.Tick();

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Start_returns_before_slow_provider_initialization_finishes()
    {
        using var gate = new ManualResetEventSlim();
        var provider = new FakeProvider("slow") { InitializeGate = gate };
        var hub = new SensorHub(new MetricStore(10), [provider], new FakeTimeProvider(), TimeSpan.FromSeconds(1));

        var started = Task.Run(hub.Start);
        var returned = started.Wait(TimeSpan.FromSeconds(2));
        gate.Set();
        await started;
        await hub.DisposeAsync();

        Assert.True(returned);
    }

    [Fact]
    public async Task DisposeAsync_disposes_providers()
    {
        var provider = new FakeProvider("p");
        var hub = CreateHub(new MetricStore(10), new FakeTimeProvider(), provider);

        await hub.DisposeAsync();

        Assert.True(provider.Disposed);
    }

    [Fact]
    public void Interval_below_minimum_throws()
    {
        var hub = CreateHub(new MetricStore(10), new FakeTimeProvider());

        Assert.Throws<ArgumentOutOfRangeException>(() => hub.Interval = TimeSpan.FromMilliseconds(50));
    }
}
