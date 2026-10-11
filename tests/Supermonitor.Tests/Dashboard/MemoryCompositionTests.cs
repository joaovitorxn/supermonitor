using Supermonitor.Core.Dashboard;

namespace Supermonitor.Tests.Dashboard;

public class MemoryCompositionTests
{
    [Fact]
    public void Splits_used_memory_into_in_use_and_modified()
    {
        var composition = MemoryComposition.From(total: 32, used: 14, modified: 1, standby: 12, free: 6);

        Assert.Equal(new MemoryComposition(13, 1, 12, 6), composition);
    }

    [Fact]
    public void Without_memory_lists_everything_available_is_free()
    {
        var composition = MemoryComposition.From(total: 32, used: 14, modified: double.NaN, standby: double.NaN, free: double.NaN);

        Assert.Equal(new MemoryComposition(14, 0, 0, 18), composition);
    }

    [Fact]
    public void Invalid_total_yields_empty_composition()
    {
        Assert.Equal(default, MemoryComposition.From(double.NaN, 1, 0, 0, 0));
    }
}
