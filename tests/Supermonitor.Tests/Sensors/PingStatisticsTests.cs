using Supermonitor.Core.Sensors.Network;

namespace Supermonitor.Tests.Sensors;

public class PingStatisticsTests
{
    [Fact]
    public void Empty_statistics_are_not_available()
    {
        var snapshot = new PingStatistics(10).GetSnapshot();

        Assert.True(double.IsNaN(snapshot.LatencyMs));
        Assert.True(double.IsNaN(snapshot.JitterMs));
        Assert.True(double.IsNaN(snapshot.LossPercent));
    }

    [Fact]
    public void Replies_produce_latest_latency_and_mean_jitter()
    {
        var stats = new PingStatistics(10);
        stats.AddReply(20);
        stats.AddReply(30);
        stats.AddReply(25);

        var snapshot = stats.GetSnapshot();

        Assert.Equal(25, snapshot.LatencyMs);
        Assert.Equal(7.5, snapshot.JitterMs);
        Assert.Equal(0, snapshot.LossPercent);
    }

    [Fact]
    public void Losses_count_toward_loss_and_are_skipped_for_jitter()
    {
        var stats = new PingStatistics(10);
        stats.AddReply(20);
        stats.AddLoss();
        stats.AddReply(30);

        var snapshot = stats.GetSnapshot();

        Assert.Equal(30, snapshot.LatencyMs);
        Assert.Equal(10, snapshot.JitterMs);
        Assert.Equal(33.33, snapshot.LossPercent, 2);
    }

    [Fact]
    public void Latest_loss_makes_latency_not_available()
    {
        var stats = new PingStatistics(10);
        stats.AddReply(20);
        stats.AddLoss();

        var snapshot = stats.GetSnapshot();

        Assert.True(double.IsNaN(snapshot.LatencyMs));
        Assert.Equal(50, snapshot.LossPercent);
    }

    [Fact]
    public void Window_drops_oldest_samples()
    {
        var stats = new PingStatistics(2);
        stats.AddLoss();
        stats.AddReply(10);
        stats.AddReply(12);

        Assert.Equal(0, stats.GetSnapshot().LossPercent);
    }
}
