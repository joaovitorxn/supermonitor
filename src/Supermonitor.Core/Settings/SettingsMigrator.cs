using System.Text.Json.Nodes;

namespace Supermonitor.Core.Settings;

public static class SettingsMigrator
{
    public const int CurrentVersion = 1;

    public static JsonObject Migrate(JsonObject root)
    {
        var version = root["version"]?.GetValue<int>() ?? 0;
        if (version < 1)
        {
            // v0: arquivos sem número de versão. Nenhum campo mudou.
            root["version"] = 1;
        }

        return root;
    }
}
