using Supermonitor.Core.Metrics;
using Supermonitor.Core.Settings;

namespace Supermonitor.Tests.Settings;

public class AppSettingsTests
{
    [Fact]
    public void Defaults_include_temperature_cards_and_thresholds()
    {
        var settings = new AppSettings();

        Assert.Equal(3, settings.Version);
        Assert.Contains(settings.Cards, c => c.MetricId == MetricIds.CpuTemperature);
        Assert.Contains(settings.Cards, c => c.MetricId == MetricIds.GpuTemperature(0));
        Assert.Equal(80, settings.Thresholds[MetricIds.CpuTemperature].Warning);
        Assert.Equal(90, settings.Thresholds[MetricIds.CpuTemperature].Critical);
    }

    [Fact]
    public void Normalize_replaces_invalid_scalars_with_defaults()
    {
        var settings = new AppSettings
        {
            SampleIntervalMs = 7,
            PingHost = "  ",
            HistoryMinutes = 7,
            Theme = (AppTheme)42,
            Backdrop = (BackdropKind)42,
            AccentColor = "blue",
            Cards = null!,
            Thresholds = null!,
        };

        settings.Normalize();

        Assert.Equal(1000, settings.SampleIntervalMs);
        Assert.Equal("1.1.1.1", settings.PingHost);
        Assert.Equal(1, settings.HistoryMinutes);
        Assert.Equal(AppTheme.System, settings.Theme);
        Assert.Equal(BackdropKind.Mica, settings.Backdrop);
        Assert.Null(settings.AccentColor);
        Assert.Equal(AppSettings.DefaultCards().Select(c => c.MetricId), settings.Cards.Select(c => c.MetricId));
        Assert.NotEmpty(settings.Thresholds);
    }

    [Fact]
    public void Normalize_cleans_cards()
    {
        var settings = new AppSettings
        {
            Cards =
            [
                null!,
                new CardSettings { MetricId = " " },
                new CardSettings { MetricId = "cpu.usage", Size = (CardSize)9, Chart = (ChartKind)9 },
                new CardSettings { MetricId = "cpu.usage", Size = CardSize.Large },
                new CardSettings { MetricId = "mem.usage", Size = CardSize.Compact, Chart = ChartKind.Bar },
            ],
        };

        settings.Normalize();

        Assert.Equal(new[] { "cpu.usage", "mem.usage" }, settings.Cards.Select(c => c.MetricId));
        Assert.Equal(CardSize.Normal, settings.Cards[0].Size);
        Assert.Equal(ChartKind.Line, settings.Cards[0].Chart);
        Assert.Equal(CardSize.Compact, settings.Cards[1].Size);
        Assert.Equal(ChartKind.Bar, settings.Cards[1].Chart);
    }

    [Fact]
    public void Normalize_fixes_thresholds()
    {
        var settings = new AppSettings
        {
            Thresholds = new()
            {
                ["a"] = new MetricThreshold { Warning = 90, Critical = 80 },
                ["b"] = new MetricThreshold { Warning = float.NaN, Critical = 10 },
                ["c"] = null!,
            },
        };

        settings.Normalize();

        Assert.Equal(80, settings.Thresholds["a"].Warning);
        Assert.Equal(90, settings.Thresholds["a"].Critical);
        Assert.False(settings.Thresholds.ContainsKey("b"));
        Assert.False(settings.Thresholds.ContainsKey("c"));
    }

    [Fact]
    public void Normalize_keeps_valid_accent()
    {
        var settings = new AppSettings { AccentColor = "#1E90FF", HistoryMinutes = 10 };

        settings.Normalize();

        Assert.Equal("#1E90FF", settings.AccentColor);
        Assert.Equal(10, settings.HistoryMinutes);
    }

    [Fact]
    public void Unit_options_follow_settings()
    {
        var settings = new AppSettings { NetworkInBits = true, Fahrenheit = true };

        Assert.Equal(new UnitOptions(NetworkInBits: true, Fahrenheit: true), settings.ToUnitOptions());
    }
}
