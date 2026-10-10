using Supermonitor.Core.Metrics;

namespace Supermonitor.Tests.Metrics;

public class MetricStoreTests
{
    private static MetricDescriptor Descriptor(string id) => new(id, id, MetricCategory.System, MetricUnit.Count);

    [Fact]
    public void Register_same_id_twice_returns_same_handle_and_raises_event_once()
    {
        var store = new MetricStore(10);
        var raised = 0;
        store.MetricsChanged += () => raised++;

        var first = store.Register(Descriptor("a"), "p");
        var second = store.Register(Descriptor("a"), "p");

        Assert.Equal(first, second);
        Assert.Equal(1, raised);
        Assert.Single(store.GetDescriptors());
    }

    [Fact]
    public void Written_value_is_visible_only_after_commit()
    {
        var store = new MetricStore(10);
        var handle = store.Register(Descriptor("a"), "p");

        store.Write(handle, 5);
        Assert.True(float.IsNaN(store.GetLatest("a")));

        store.CommitTick();
        Assert.Equal(5, store.GetLatest("a"));
    }

    [Fact]
    public void Commit_without_new_write_repeats_last_value()
    {
        var store = new MetricStore(10);
        var handle = store.Register(Descriptor("a"), "p");
        store.Write(handle, 3);
        store.CommitTick();
        store.CommitTick();

        var history = new float[5];
        Assert.Equal(2, store.CopyHistory("a", history));
        Assert.Equal(3, history[0]);
        Assert.Equal(3, history[1]);
    }

    [Fact]
    public void InvalidateOwner_sets_only_that_owners_series_to_nan()
    {
        var store = new MetricStore(10);
        var a = store.Register(Descriptor("a"), "p1");
        var b = store.Register(Descriptor("b"), "p2");
        store.Write(a, 1);
        store.Write(b, 2);

        store.InvalidateOwner("p1");
        store.CommitTick();

        Assert.True(float.IsNaN(store.GetLatest("a")));
        Assert.Equal(2, store.GetLatest("b"));
    }

    [Fact]
    public void Unknown_id_returns_nan_and_empty_history()
    {
        var store = new MetricStore(10);

        Assert.True(float.IsNaN(store.GetLatest("missing")));
        Assert.Equal(0, store.CopyHistory("missing", new float[3]));
        Assert.False(store.TryGetDescriptor("missing", out _));
    }

    [Fact]
    public void TryGetDescriptor_returns_registered_descriptor()
    {
        var store = new MetricStore(10);
        store.Register(new MetricDescriptor("cpu", "CPU usage", MetricCategory.Cpu, MetricUnit.Percent), "p");

        Assert.True(store.TryGetDescriptor("cpu", out var descriptor));
        Assert.Equal("CPU usage", descriptor!.DisplayName);
    }
}
