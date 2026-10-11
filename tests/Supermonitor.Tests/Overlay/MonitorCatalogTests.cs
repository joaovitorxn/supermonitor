using Supermonitor.Core.Overlay;
using Supermonitor.Overlay;

namespace Supermonitor.Tests.Overlay;

public class MonitorCatalogTests
{
    private static readonly MonitorInfo Primary = new(@"\\.\DISPLAY1", "Display 1", new PixelRect(0, 0, 2560, 1440), true, 1.25f);
    private static readonly MonitorInfo Second = new(@"\\.\DISPLAY2", "Display 2", new PixelRect(2560, 0, 1920, 1080), false, 1f);

    [Fact]
    public void A_known_id_picks_that_monitor()
    {
        Assert.Equal(Second, MonitorCatalog.Resolve([Primary, Second], @"\\.\DISPLAY2"));
    }

    [Fact]
    public void No_id_picks_the_primary_monitor()
    {
        Assert.Equal(Primary, MonitorCatalog.Resolve([Second, Primary], null));
    }

    [Fact]
    public void An_unplugged_monitor_falls_back_to_the_primary()
    {
        Assert.Equal(Primary, MonitorCatalog.Resolve([Primary], @"\\.\DISPLAY9"));
    }

    [Fact]
    public void Without_a_primary_flag_the_first_monitor_is_used()
    {
        var other = Second with { IsPrimary = false };

        Assert.Equal(other, MonitorCatalog.Resolve([other], null));
    }

    [Fact]
    public void No_monitors_gives_null()
    {
        Assert.Null(MonitorCatalog.Resolve([], null));
    }
}
