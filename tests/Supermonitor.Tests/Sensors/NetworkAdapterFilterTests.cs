using Supermonitor.Core.Sensors.Network;

namespace Supermonitor.Tests.Sensors;

public class NetworkAdapterFilterTests
{
    private const uint Ethernet = 6;
    private const byte Hardware = 0x1;
    private const byte Filter = 0x2;

    [Fact]
    public void Hardware_adapter_that_is_up_is_monitored()
    {
        Assert.True(NetworkAdapterFilter.IsMonitored(Ethernet, NetworkAdapterFilter.OperStatusUp, Hardware));
    }

    [Theory]
    [InlineData(Ethernet, 2, Hardware)]
    [InlineData(Ethernet, 1, 0)]
    [InlineData(Ethernet, 1, Hardware | Filter)]
    [InlineData(24u, 1, Hardware)]
    [InlineData(131u, 1, Hardware)]
    public void Down_virtual_filter_loopback_and_tunnel_adapters_are_ignored(uint type, int operStatus, byte flags)
    {
        Assert.False(NetworkAdapterFilter.IsMonitored(type, operStatus, flags));
    }
}
