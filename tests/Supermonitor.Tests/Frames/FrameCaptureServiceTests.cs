using Microsoft.Extensions.Time.Testing;
using Supermonitor.Core.Frames;
using Supermonitor.Core.Sensors;

namespace Supermonitor.Tests.Frames;

public class FrameCaptureServiceTests
{
    private const int GamePid = 100;
    private const int OtherPid = 200;
    private const int OwnPid = 1;

    private sealed class FakeSource : IPresentSource
    {
        public event Action<int, double>? Presented;

        public Exception? FailOnStart { get; init; }

        public bool Started { get; private set; }

        public bool Disposed { get; private set; }

        public void Start()
        {
            if (FailOnStart is not null)
            {
                throw FailOnStart;
            }

            Started = true;
        }

        public void Raise(int pid, double timestampMs) => Presented?.Invoke(pid, timestampMs);

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeProbe : IForegroundProbe
    {
        public int Pid { get; set; } = GamePid;

        public int GetForegroundProcessId() => Pid;
    }

    private sealed class Rig : IDisposable
    {
        private readonly Queue<Func<FakeSource>> _plans = new();

        public Rig(bool runLoop = false)
        {
            Service = new FrameCaptureService(Demand, () =>
            {
                var source = _plans.Count > 0 ? _plans.Dequeue()() : new FakeSource();
                Sources.Add(source);
                return source;
            }, Probe, Time, OwnPid, runLoop);
        }

        public DemandTracker Demand { get; } = new();

        public FakeTimeProvider Time { get; } = new();

        public FakeProbe Probe { get; } = new();

        public List<FakeSource> Sources { get; } = [];

        public FrameCaptureService Service { get; }

        public void Plan(Func<FakeSource> source) => _plans.Enqueue(source);

        // Entrega `count` frames de 10 ms do processo (count + 1 presents), começando em `start`; devolve o último instante.
        public double Feed(int pid, double start, int count)
        {
            var timestamp = start;
            for (var i = 0; i <= count; i++)
            {
                Sources[^1].Raise(pid, timestamp);
                timestamp += 10;
            }

            return timestamp - 10;
        }

        public void Dispose() => Service.Dispose();
    }

    [Fact]
    public void Nothing_starts_without_demand()
    {
        using var rig = new Rig();

        rig.Service.Poll();

        Assert.Empty(rig.Sources);
        Assert.Equal(FrameCaptureState.Stopped, rig.Service.State);
        Assert.False(rig.Service.Current.HasData);
    }

    [Fact]
    public void Demand_starts_the_source_and_targets_the_foreground_process()
    {
        using var rig = new Rig();
        using var lease = rig.Demand.Acquire(Demand.FrameCapture);

        rig.Service.Poll();

        Assert.True(Assert.Single(rig.Sources).Started);
        Assert.Equal(FrameCaptureState.Running, rig.Service.State);
        Assert.Equal(GamePid, rig.Service.TargetProcessId);
    }

    [Fact]
    public void Only_presents_of_the_target_are_aggregated()
    {
        using var rig = new Rig();
        using var lease = rig.Demand.Acquire(Demand.FrameCapture);
        rig.Service.Poll();

        rig.Feed(GamePid, 1000, 10);
        rig.Feed(OtherPid, 1000, 50);

        var stats = rig.Service.Current;
        Assert.True(stats.HasData);
        Assert.Equal(10, stats.FrameCount);
        Assert.Equal(100, stats.Fps, 0.5);
    }

    [Fact]
    public void Changing_the_foreground_process_restarts_the_aggregation()
    {
        using var rig = new Rig();
        using var lease = rig.Demand.Acquire(Demand.FrameCapture);
        rig.Service.Poll();
        rig.Feed(GamePid, 1000, 10);

        rig.Probe.Pid = OtherPid;
        rig.Service.Poll();

        Assert.Equal(OtherPid, rig.Service.TargetProcessId);
        Assert.False(rig.Service.Current.HasData);

        rig.Feed(GamePid, 2000, 5);
        Assert.False(rig.Service.Current.HasData);

        rig.Feed(OtherPid, 3000, 5);
        Assert.Equal(5, rig.Service.Current.FrameCount);
    }

    [Fact]
    public void The_own_process_in_the_foreground_means_no_target()
    {
        using var rig = new Rig();
        using var lease = rig.Demand.Acquire(Demand.FrameCapture);
        rig.Probe.Pid = OwnPid;

        rig.Service.Poll();
        rig.Feed(OwnPid, 1000, 10);

        Assert.Equal(0, rig.Service.TargetProcessId);
        Assert.False(rig.Service.Current.HasData);
    }

    [Fact]
    public void No_foreground_window_means_no_target()
    {
        using var rig = new Rig();
        using var lease = rig.Demand.Acquire(Demand.FrameCapture);
        rig.Probe.Pid = 0;

        rig.Service.Poll();
        rig.Feed(0, 1000, 10);

        Assert.Equal(0, rig.Service.TargetProcessId);
        Assert.False(rig.Service.Current.HasData);
    }

    [Fact]
    public void Stats_go_idle_after_two_seconds_without_presents_and_return_with_the_next_one()
    {
        using var rig = new Rig();
        using var lease = rig.Demand.Acquire(Demand.FrameCapture);
        rig.Service.Poll();
        var last = rig.Feed(GamePid, 1000, 10);

        rig.Time.Advance(FrameCaptureService.IdleAfter - TimeSpan.FromMilliseconds(1));
        Assert.True(rig.Service.Current.HasData);

        rig.Time.Advance(TimeSpan.FromMilliseconds(2));
        Assert.False(rig.Service.Current.HasData);

        rig.Sources[^1].Raise(GamePid, last + 10);
        Assert.True(rig.Service.Current.HasData);
    }

    [Fact]
    public void Releasing_the_demand_stops_the_source_and_clears_the_stats()
    {
        using var rig = new Rig();
        var lease = rig.Demand.Acquire(Demand.FrameCapture);
        rig.Service.Poll();
        rig.Feed(GamePid, 1000, 10);

        lease.Dispose();
        rig.Service.Poll();

        Assert.True(rig.Sources[0].Disposed);
        Assert.Equal(FrameCaptureState.Stopped, rig.Service.State);
        Assert.Equal(0, rig.Service.TargetProcessId);
        Assert.False(rig.Service.Current.HasData);
    }

    [Fact]
    public void A_new_demand_after_a_stop_creates_a_new_source()
    {
        using var rig = new Rig();
        var lease = rig.Demand.Acquire(Demand.FrameCapture);
        rig.Service.Poll();
        lease.Dispose();
        rig.Service.Poll();

        using var again = rig.Demand.Acquire(Demand.FrameCapture);
        rig.Service.Poll();

        Assert.Equal(2, rig.Sources.Count);
        Assert.Equal(FrameCaptureState.Running, rig.Service.State);
    }

    [Fact]
    public void A_failed_start_is_reported_and_retried_every_thirty_seconds()
    {
        using var rig = new Rig();
        rig.Plan(() => new FakeSource { FailOnStart = new UnauthorizedAccessException("needs administrator") });
        using var lease = rig.Demand.Acquire(Demand.FrameCapture);

        rig.Service.Poll();

        Assert.Equal(FrameCaptureState.Failed, rig.Service.State);
        Assert.Equal("needs administrator", rig.Service.FailureReason);
        Assert.True(rig.Sources[0].Disposed);
        Assert.False(rig.Service.Current.HasData);

        rig.Time.Advance(FrameCaptureService.RetryInterval - TimeSpan.FromSeconds(1));
        rig.Service.Poll();
        Assert.Single(rig.Sources);

        rig.Time.Advance(TimeSpan.FromSeconds(2));
        rig.Service.Poll();
        Assert.Equal(2, rig.Sources.Count);
        Assert.Equal(FrameCaptureState.Running, rig.Service.State);
        Assert.Null(rig.Service.FailureReason);
    }

    [Fact]
    public void A_factory_that_throws_is_also_a_failed_start()
    {
        var demand = new DemandTracker();
        using var service = new FrameCaptureService(demand, () => throw new InvalidOperationException("no etw"), new FakeProbe(), new FakeTimeProvider(), OwnPid, runLoop: false);
        using var lease = demand.Acquire(Demand.FrameCapture);

        service.Poll();

        Assert.Equal(FrameCaptureState.Failed, service.State);
        Assert.Equal("no etw", service.FailureReason);
    }

    [Fact]
    public void Dropping_the_demand_after_a_failure_returns_to_stopped()
    {
        using var rig = new Rig();
        rig.Plan(() => new FakeSource { FailOnStart = new InvalidOperationException("boom") });
        var lease = rig.Demand.Acquire(Demand.FrameCapture);
        rig.Service.Poll();

        lease.Dispose();
        rig.Service.Poll();

        Assert.Equal(FrameCaptureState.Stopped, rig.Service.State);
        Assert.Null(rig.Service.FailureReason);
    }

    [Fact]
    public void Disposing_the_service_disposes_the_source()
    {
        var rig = new Rig();
        using var lease = rig.Demand.Acquire(Demand.FrameCapture);
        rig.Service.Poll();

        rig.Dispose();

        Assert.True(rig.Sources[0].Disposed);
    }

    [Fact]
    public void The_loop_starts_the_source_when_demand_appears_and_stops_it_when_it_ends()
    {
        using var rig = new Rig(runLoop: true);

        var lease = rig.Demand.Acquire(Demand.FrameCapture);
        Assert.True(SpinWait.SpinUntil(() => rig.Service.State == FrameCaptureState.Running, TimeSpan.FromSeconds(5)));

        lease.Dispose();
        SpinWait.SpinUntil(() =>
        {
            rig.Time.Advance(FrameCaptureService.PollInterval);
            return rig.Service.State == FrameCaptureState.Stopped;
        }, TimeSpan.FromSeconds(5));

        Assert.Equal(FrameCaptureState.Stopped, rig.Service.State);
        Assert.True(rig.Sources[0].Disposed);
    }
}
