using Supermonitor.Core.Sensors.Cpu;

namespace Supermonitor.Tests.Sensors;

public class CpuInstanceNameTests
{
    [Theory]
    [InlineData("0,0", 0)]
    [InlineData("0,15", 15)]
    [InlineData("1,3", 1027)]
    public void Parses_group_and_core_number(string name, int expected)
    {
        Assert.True(CpuInstanceName.TryParse(name, out var key));
        Assert.Equal(expected, key);
    }

    [Theory]
    [InlineData("_Total")]
    [InlineData("0,_Total")]
    [InlineData("")]
    [InlineData("abc")]
    public void Rejects_totals_and_malformed_names(string name)
    {
        Assert.False(CpuInstanceName.TryParse(name, out _));
    }
}
