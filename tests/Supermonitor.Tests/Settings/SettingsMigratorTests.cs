using System.Text.Json.Nodes;
using Supermonitor.Core.Settings;

namespace Supermonitor.Tests.Settings;

public class SettingsMigratorTests
{
    [Fact]
    public void Version_one_overview_cards_become_v2_cards()
    {
        var root = JsonNode.Parse("""{ "version": 1, "overviewCards": ["cpu.usage", 5, "net.ping"] }""")!.AsObject();

        var migrated = SettingsMigrator.Migrate(root);

        Assert.Equal(2, migrated["version"]!.GetValue<int>());
        Assert.Null(migrated["overviewCards"]);
        var cards = migrated["cards"]!.AsArray();
        Assert.Equal(new[] { "cpu.usage", "net.ping" }, cards.Select(c => c!["metricId"]!.GetValue<string>()));
    }

    [Fact]
    public void Unversioned_document_is_migrated_to_current_version()
    {
        var root = new JsonObject { ["pingHost"] = "9.9.9.9" };

        var migrated = SettingsMigrator.Migrate(root);

        Assert.Equal(SettingsMigrator.CurrentVersion, migrated["version"]!.GetValue<int>());
        Assert.Equal("9.9.9.9", migrated["pingHost"]!.GetValue<string>());
    }

    [Fact]
    public void Current_version_document_is_left_as_is()
    {
        var root = JsonNode.Parse("""{ "version": 2, "cards": [ { "metricId": "cpu.usage" } ] }""")!.AsObject();

        var migrated = SettingsMigrator.Migrate(root);

        Assert.Single(migrated["cards"]!.AsArray());
    }
}
