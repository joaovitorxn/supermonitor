namespace Supermonitor.Core.Shell;

public static class AppPaths
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Supermonitor");

    public static string LogDirectory { get; } = Path.Combine(DataDirectory, "logs");

    public static string SettingsFile { get; } = Path.Combine(DataDirectory, "settings.json");

    public static string IconPath { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "Supermonitor.ico");
}
