using Supermonitor.Core.Settings;

namespace Supermonitor.Tests.Settings;

public class ColorTextTests
{
    [Fact]
    public void Parses_hex_rgb()
    {
        Assert.True(ColorText.TryParseRgb("#1E90ff", out var r, out var g, out var b));
        Assert.Equal((0x1E, 0x90, 0xFF), (r, g, b));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1E90FF")]
    [InlineData("#1E90F")]
    [InlineData("#GGGGGG")]
    [InlineData("#1E90FF00")]
    public void Rejects_other_formats(string? text)
    {
        Assert.False(ColorText.TryParseRgb(text, out _, out _, out _));
    }

    [Fact]
    public void Formats_hex_in_upper_case()
    {
        Assert.Equal("#0A0BFF", ColorText.ToHex(10, 11, 255));
    }
}
