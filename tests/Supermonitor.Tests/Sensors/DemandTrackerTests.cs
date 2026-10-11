using Supermonitor.Core.Sensors;

namespace Supermonitor.Tests.Sensors;

public class DemandTrackerTests
{
    [Fact]
    public void Demand_is_active_while_a_lease_is_held()
    {
        var tracker = new DemandTracker();
        var lease = tracker.Acquire(Demand.Processes);

        Assert.True(tracker.IsActive(Demand.Processes));
        Assert.False(tracker.IsActive(Demand.HardwareSensors));

        lease.Dispose();
        Assert.False(tracker.IsActive(Demand.Processes));
    }

    [Fact]
    public void Demand_stays_active_until_every_lease_is_released()
    {
        var tracker = new DemandTracker();
        var first = tracker.Acquire(Demand.HardwareSensors);
        var second = tracker.Acquire(Demand.HardwareSensors);

        first.Dispose();
        Assert.True(tracker.IsActive(Demand.HardwareSensors));

        second.Dispose();
        Assert.False(tracker.IsActive(Demand.HardwareSensors));
    }

    [Fact]
    public void Disposing_a_lease_twice_releases_it_once()
    {
        var tracker = new DemandTracker();
        var lease = tracker.Acquire(Demand.Processes);
        lease.Dispose();
        lease.Dispose();

        using var other = tracker.Acquire(Demand.Processes);

        Assert.True(tracker.IsActive(Demand.Processes));
    }

    [Fact]
    public void Changed_fires_when_the_first_lease_is_taken_and_the_last_is_released()
    {
        var tracker = new DemandTracker();
        var events = new List<(Demand, bool)>();
        tracker.Changed += (demand, active) => events.Add((demand, active));

        var first = tracker.Acquire(Demand.FrameCapture);
        var second = tracker.Acquire(Demand.FrameCapture);
        first.Dispose();
        Assert.Equal(new[] { (Demand.FrameCapture, true) }, events);

        second.Dispose();
        second.Dispose();

        Assert.Equal(new[] { (Demand.FrameCapture, true), (Demand.FrameCapture, false) }, events);
    }

    [Fact]
    public void Changed_reports_each_demand_separately()
    {
        var tracker = new DemandTracker();
        var events = new List<(Demand, bool)>();
        tracker.Changed += (demand, active) => events.Add((demand, active));

        using var processes = tracker.Acquire(Demand.Processes);
        using var hardware = tracker.Acquire(Demand.HardwareSensors);

        Assert.Equal(new[] { (Demand.Processes, true), (Demand.HardwareSensors, true) }, events);
    }
}
