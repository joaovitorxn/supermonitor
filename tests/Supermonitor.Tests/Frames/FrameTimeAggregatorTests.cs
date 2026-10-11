using Supermonitor.Core.Frames;

namespace Supermonitor.Tests.Frames;

public class FrameTimeAggregatorTests
{
    // The first present yields no frametime (no previous one); each interval becomes a frametime.
    private static FrameTimeAggregator Feed(params double[] frameTimesMs)
    {
        var aggregator = new FrameTimeAggregator();
        var timestamp = 1000.0;
        aggregator.AddPresent(timestamp);
        foreach (var frameTime in frameTimesMs)
        {
            timestamp += frameTime;
            aggregator.AddPresent(timestamp);
        }

        return aggregator;
    }

    [Fact]
    public void Steady_sixty_fps_reports_sixty()
    {
        var aggregator = Feed(Enumerable.Repeat(1000.0 / 60, 120).ToArray());

        var stats = aggregator.GetStats();

        Assert.True(stats.HasData);
        Assert.Equal(60, stats.Fps, 0.5);
        Assert.Equal(1000.0 / 60, stats.FrameTimeMs, 0.01);
        Assert.Equal(60, stats.Low1PercentFps, 0.5);
        Assert.Equal(120, stats.FrameCount);
    }

    [Fact]
    public void New_aggregator_has_no_data()
    {
        var stats = new FrameTimeAggregator().GetStats();

        Assert.False(stats.HasData);
        Assert.True(double.IsNaN(stats.Fps));
    }

    [Fact]
    public void A_single_present_has_no_frame_time_yet()
    {
        var stats = Feed().GetStats();

        Assert.False(stats.HasData);
    }

    [Fact]
    public void One_percent_low_comes_from_the_ninety_ninth_percentile_frame_time()
    {
        // 100 frames: 96 of 10 ms and 4 of 40 ms → rank 99 = 40 ms → 25 FPS.
        var frameTimes = Enumerable.Repeat(10.0, 100).ToArray();
        frameTimes[20] = frameTimes[40] = frameTimes[60] = frameTimes[80] = 40;

        var stats = Feed(frameTimes).GetStats();

        Assert.Equal(25, stats.Low1PercentFps, 1e-6);
        Assert.Equal(10, stats.FrameTimeMs, 1e-6);
    }

    [Fact]
    public void Fps_uses_only_the_last_second()
    {
        // 5 s at 50 FPS (20 ms) followed by 1.5 s at 100 FPS (10 ms).
        var frameTimes = Enumerable.Repeat(20.0, 250).Concat(Enumerable.Repeat(10.0, 150)).ToArray();

        var stats = Feed(frameTimes).GetStats();

        Assert.Equal(100, stats.Fps, 1);
    }

    [Fact]
    public void A_gap_longer_than_the_limit_restarts_the_sequence()
    {
        var aggregator = Feed(10, 10, 10);
        var last = 1000.0 + 30;

        aggregator.AddPresent(last + FrameTimeAggregator.MaxGapMs + 1);

        Assert.False(aggregator.GetStats().HasData);

        aggregator.AddPresent(last + FrameTimeAggregator.MaxGapMs + 11);
        var stats = aggregator.GetStats();
        Assert.True(stats.HasData);
        Assert.Equal(10, stats.FrameTimeMs, 1e-6);
        Assert.Equal(100, stats.Low1PercentFps, 1e-6);
    }

    [Fact]
    public void A_short_pause_is_kept_as_a_frame_time()
    {
        // 4 frames: rank 99 is the largest (800 ms) → 1.25 FPS 1% low; the current frametime stays the last one.
        var stats = Feed(10, 10, 800, 10).GetStats();

        Assert.Equal(1000.0 / 800, stats.Low1PercentFps, 1e-6);
        Assert.Equal(10, stats.FrameTimeMs, 1e-6);
    }

    [Fact]
    public void Duplicate_and_out_of_order_timestamps_are_ignored()
    {
        var aggregator = Feed(10, 10);
        var version = aggregator.Version;

        aggregator.AddPresent(1020);
        aggregator.AddPresent(1015);

        Assert.Equal(version, aggregator.Version);
        Assert.Equal(2, aggregator.GetStats().FrameCount);
    }

    [Fact]
    public void More_frames_than_the_capacity_do_not_overflow()
    {
        var aggregator = Feed(Enumerable.Repeat(1.0, FrameTimeAggregator.Capacity + 4000).ToArray());

        var stats = aggregator.GetStats();
        Span<float> buffer = new float[100];

        Assert.True(stats.HasData);
        Assert.Equal(1000, stats.Fps, 1);
        Assert.Equal(100, aggregator.CopyFrameTimes(buffer));
    }

    [Fact]
    public void Copy_returns_the_most_recent_frame_times_oldest_first()
    {
        var aggregator = Feed(1, 2, 3, 4, 5);
        Span<float> buffer = new float[3];

        var copied = aggregator.CopyFrameTimes(buffer);

        Assert.Equal(3, copied);
        Assert.Equal(new[] { 3f, 4f, 5f }, buffer.ToArray());
    }

    [Fact]
    public void Version_changes_with_every_accepted_frame_and_reset_clears_the_data()
    {
        var aggregator = Feed(10);
        var version = aggregator.Version;

        aggregator.AddPresent(1020);
        Assert.NotEqual(version, aggregator.Version);

        aggregator.Reset();
        Assert.False(aggregator.GetStats().HasData);
        Assert.Equal(0, aggregator.GetStats().FrameCount);
    }
}
