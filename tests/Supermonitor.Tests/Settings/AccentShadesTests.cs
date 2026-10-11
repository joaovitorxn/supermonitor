using Supermonitor.Core.Settings;

namespace Supermonitor.Tests.Settings;

public class AccentShadesTests
{
    [Fact]
    public void Produces_three_lighter_and_three_darker_shades()
    {
        var shades = AccentShades.Compute(100, 50, 200);

        Assert.Equal((100, 50, 200), shades.Base);
        Assert.Equal((131, 91, 211), shades.Light1);
        Assert.Equal((162, 132, 222), shades.Light2);
        Assert.Equal((193, 173, 233), shades.Light3);
        Assert.Equal((75, 38, 150), shades.Dark1);
        Assert.Equal((50, 25, 100), shades.Dark2);
        Assert.Equal((25, 13, 50), shades.Dark3);
    }

    [Fact]
    public void White_and_black_stay_in_range()
    {
        var white = AccentShades.Compute(255, 255, 255);
        var black = AccentShades.Compute(0, 0, 0);

        Assert.Equal((255, 255, 255), white.Light3);
        Assert.Equal((0, 0, 0), black.Dark3);
    }
}
