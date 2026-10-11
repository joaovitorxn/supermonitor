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

        Assert.Equal(SettingsMigrator.CurrentVersion, migrated["version"]!.GetValue<int>());
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

    [Fact]
    public void Version_two_document_is_bumped_to_three_and_keeps_its_fields()
    {
        var root = JsonNode.Parse("""{ "version": 2, "pingHost": "9.9.9.9", "cards": [ { "metricId": "cpu.usage" } ] }""")!.AsObject();

        var migrated = SettingsMigrator.Migrate(root);

        Assert.Equal(3, migrated["version"]!.GetValue<int>());
        Assert.Equal("9.9.9.9", migrated["pingHost"]!.GetValue<string>());
        Assert.Single(migrated["cards"]!.AsArray());
        Assert.Null(migrated["overlay"]);
    }

    [Fact]
    public void Version_two_file_loads_with_default_overlay_settings()
    {
        var dir = Directory.CreateTempSubdirectory("smtest");
        try
        {
            var path = Path.Combine(dir.FullName, "settings.json");
            File.WriteAllText(path, """{ "version": 2, "pingHost": "9.9.9.9" }""");
            using var store = new SettingsStore(path, TimeProvider.System, TimeSpan.FromMilliseconds(500));

            var result = store.Load();

            Assert.Equal(SettingsLoadResult.Loaded, result);
            Assert.Equal("9.9.9.9", store.Current.PingHost);
            Assert.False(store.Current.Overlay.Enabled);
            Assert.Equal(3, store.Current.Version);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
