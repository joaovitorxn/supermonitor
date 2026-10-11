using Supermonitor.Core.Sensors.Disk;

namespace Supermonitor.Tests.Sensors;

public class DiskInstanceNameTests
{
    [Theory]
    [InlineData("0 C:", 0, "C:")]
    [InlineData("1 D: E:", 1, "D: E:")]
    [InlineData("2", 2, "")]
    public void Parses_index_and_label(string name, int expectedIndex, string expectedLabel)
    {
        Assert.True(DiskInstanceName.TryParse(name, out var index, out var label));
        Assert.Equal(expectedIndex, index);
        Assert.Equal(expectedLabel, label.ToString());
    }

    [Theory]
    [InlineData("_Total")]
    [InlineData("")]
    [InlineData("C:")]
    public void Rejects_total_and_malformed_names(string name)
    {
        Assert.False(DiskInstanceName.TryParse(name, out _, out _));
    }
}
