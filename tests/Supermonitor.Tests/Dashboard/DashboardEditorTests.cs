using Supermonitor.Core.Dashboard;
using Supermonitor.Core.Settings;

namespace Supermonitor.Tests.Dashboard;

public class DashboardEditorTests
{
    private static List<CardSettings> Cards(params string[] ids) => ids.Select(id => new CardSettings { MetricId = id }).ToList();

    private static string[] Ids(List<CardSettings> cards) => cards.Select(c => c.MetricId).ToArray();

    [Fact]
    public void Add_appends_new_ids_only()
    {
        var cards = Cards("a");

        Assert.True(DashboardEditor.Add(cards, "b"));
        Assert.False(DashboardEditor.Add(cards, "a"));
        Assert.Equal(new[] { "a", "b" }, Ids(cards));
    }

    [Fact]
    public void Remove_works_for_metrics_missing_on_this_machine()
    {
        var cards = Cards("cpu.usage", "gpu0.temp");

        Assert.True(DashboardEditor.Remove(cards, "gpu0.temp"));
        Assert.False(DashboardEditor.Remove(cards, "gpu0.temp"));
        Assert.Equal(new[] { "cpu.usage" }, Ids(cards));
    }

    [Fact]
    public void MoveTo_reorders_and_clamps()
    {
        var cards = Cards("a", "b", "c");

        Assert.Equal(0, DashboardEditor.MoveTo(cards, "c", 0));
        Assert.Equal(new[] { "c", "a", "b" }, Ids(cards));
        Assert.Equal(2, DashboardEditor.MoveTo(cards, "c", 99));
        Assert.Equal(new[] { "a", "b", "c" }, Ids(cards));
        Assert.Equal(-1, DashboardEditor.MoveTo(cards, "x", 0));
    }

    [Fact]
    public void MoveBy_moves_relative_to_current_position()
    {
        var cards = Cards("a", "b", "c");

        Assert.Equal(2, DashboardEditor.MoveBy(cards, "a", 2));
        Assert.Equal(new[] { "b", "c", "a" }, Ids(cards));
        Assert.Equal(0, DashboardEditor.MoveBy(cards, "c", -5));
        Assert.Equal(new[] { "c", "b", "a" }, Ids(cards));
    }
}
