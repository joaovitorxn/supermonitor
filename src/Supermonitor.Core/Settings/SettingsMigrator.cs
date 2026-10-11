using System.Text.Json;
using System.Text.Json.Nodes;

namespace Supermonitor.Core.Settings;

public static class SettingsMigrator
{
    public const int CurrentVersion = 3;

    public static JsonObject Migrate(JsonObject root)
    {
        var version = root["version"]?.GetValue<int>() ?? 0;

        // v0 → v1: arquivos sem número de versão. Nenhum campo mudou.
        if (version < 1)
        {
            version = 1;
        }

        // v1 → v2: "overviewCards": ["id"] vira "cards": [{ "metricId": "id" }].
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

        // v2 → v3: o bloco "overlay" é novo; ausente, o AppSettings usa os padrões. Nenhum campo mudou.
        if (version < 3)
        {
            version = 3;
        }

        root["version"] = version;
        return root;
    }
}
