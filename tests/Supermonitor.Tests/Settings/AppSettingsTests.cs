using System.Text.Json.Nodes;
using Supermonitor.Core.Settings;

namespace Supermonitor.Tests.Settings;

public class AppSettingsTests
{
    [Fact]
    public void Normalize_replaces_invalid_values_with_defaults()
    {
        var settings = new AppSettings { SampleIntervalMs = 7, PingHost = "  ", OverviewCards = null! };

        settings.Normalize();

        Assert.Equal(1000, settings.SampleIntervalMs);
        Assert.Equal("1.1.1.1", settings.PingHost);
        Assert.Equal(AppSettings.DefaultOverviewCards, settings.OverviewCards);
    }

    [Fact]
    public void Normalize_keeps_valid_values()
    {
        var settings = new AppSettings { SampleIntervalMs = 250, PingHost = "8.8.8.8", OverviewCards = ["cpu.usage"] };

        settings.Normalize();

        Assert.Equal(250, settings.SampleIntervalMs);
        Assert.Equal("8.8.8.8", settings.PingHost);
        Assert.Equal(new[] { "cpu.usage" }, settings.OverviewCards);
    }

    [Fact]
    public void Normalize_removes_null_and_blank_card_ids()
    {
        var settings = new AppSettings { OverviewCards = [null!, " ", "cpu.usage"] };

        settings.Normalize();

        Assert.Equal(new[] { "cpu.usage" }, settings.OverviewCards);
    }

    [Fact]
    public void Migrator_adds_version_to_unversioned_document()
    {
        var root = new JsonObject { ["pingHost"] = "9.9.9.9" };

        var migrated = SettingsMigrator.Migrate(root);

        Assert.Equal(SettingsMigrator.CurrentVersion, migrated["version"]!.GetValue<int>());
        Assert.Equal("9.9.9.9", migrated["pingHost"]!.GetValue<string>());
    }
}
