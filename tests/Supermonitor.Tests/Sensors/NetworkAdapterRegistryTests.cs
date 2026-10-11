using Supermonitor.Core.Sensors.Network;

namespace Supermonitor.Tests.Sensors;

public class NetworkAdapterRegistryTests
{
    [Fact]
    public void New_adapters_get_increasing_indexes_and_known_ones_keep_theirs()
    {
        var registry = new NetworkAdapterRegistry();

        var first = registry.AddNew([(10UL, "Ethernet"), (20UL, "Wi-Fi")]);
        var second = registry.AddNew([(20UL, "Wi-Fi"), (30UL, "VPN"), (10UL, "Ethernet")]);

        Assert.Equal(new[] { (10UL, 0, "Ethernet"), (20UL, 1, "Wi-Fi") }, first);
        Assert.Equal(new[] { (30UL, 2, "VPN") }, second);
    }

    [Fact]
    public void Adapter_that_disappears_and_returns_keeps_its_index()
    {
        var registry = new NetworkAdapterRegistry();
        registry.AddNew([(10UL, "Ethernet"), (20UL, "Wi-Fi")]);

        registry.AddNew([(10UL, "Ethernet")]);
        var back = registry.AddNew([(20UL, "Wi-Fi"), (10UL, "Ethernet")]);

        Assert.Empty(back);
        Assert.Equal(1, registry.IndexOf(20UL));
    }
}
