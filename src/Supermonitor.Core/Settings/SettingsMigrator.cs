using System.Text.Json;
using System.Text.Json.Nodes;

namespace Supermonitor.Core.Settings;

public static class SettingsMigrator
{
    public const int CurrentVersion = 3;

    public static JsonObject Migrate(JsonObject root)
    {
        var version = root["version"]?.GetValue<int>() ?? 0;

        // v0 → v1: files without a version number. No field changed.
        if (version < 1)
        {
            version = 1;
        }

        // v1 → v2: "overviewCards": ["id"] becomes "cards": [{ "metricId": "id" }].
        if (version < 2)
        {
            if (root["overviewCards"] is JsonArray ids)
            {
                var cards = new JsonArray();
                foreach (var id in ids)
                {
                    if (id?.GetValueKind() == JsonValueKind.String)
                    {
                        cards.Add(new JsonObject { ["metricId"] = id.GetValue<string>() });
                    }
                }

                root["cards"] = cards;
            }

            root.Remove("overviewCards");
            version = 2;
        }

        // v2 → v3: the "overlay" block is new; when absent, AppSettings uses defaults. No field changed.
        if (version < 3)
        {
            version = 3;
        }

        root["version"] = version;
        return root;
    }
}
