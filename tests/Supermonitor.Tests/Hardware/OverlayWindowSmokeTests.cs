using Supermonitor.Overlay;

namespace Supermonitor.Tests.Hardware;

[Trait("Category", "Hardware")]
public class OverlayWindowSmokeTests
{
    [Fact]
    public void At_least_one_monitor_is_found_and_one_is_primary_with_a_plausible_scale()
    {
        var monitors = MonitorCatalog.Enumerate();

        Assert.NotEmpty(monitors);
        var primary = Assert.Single(monitors, m => m.IsPrimary);
        Assert.InRange(primary.Scale, 1f, 4f);
        Assert.True(primary.Bounds.Width > 0 && primary.Bounds.Height > 0);
        Assert.All(monitors, m => Assert.False(string.IsNullOrWhiteSpace(m.Id)));
        Assert.Equal(monitors.Count, monitors.Select(m => m.Id).Distinct().Count());
    }
}
