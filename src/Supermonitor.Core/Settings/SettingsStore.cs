using System.Text.Json;
using System.Text.Json.Nodes;
using Supermonitor.Core.Diagnostics;

namespace Supermonitor.Core.Settings;

public enum SettingsLoadResult
{
    Loaded,
    CreatedDefault,
    RecoveredFromCorrupt,
    Unreadable,
}

public sealed class SettingsStore : IDisposable
{
    private readonly string _path;
    private readonly TimeSpan _debounce;
    private readonly ITimer _timer;
    private readonly Lock _gate = new();
    private string? _pendingJson;

    public SettingsStore(string filePath, TimeProvider time, TimeSpan debounce)
    {
        _path = filePath;
        _debounce = debounce;
        _timer = time.CreateTimer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public AppSettings Current { get; private set; } = new();

    public SettingsLoadResult Load()
    {
        if (!File.Exists(_path))
        {
            Current = new AppSettings();
            return SettingsLoadResult.CreatedDefault;
        }

        string text;
        try
        {
            text = File.ReadAllText(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Settings file could not be read; using defaults", ex);
            Current = new AppSettings();
            return SettingsLoadResult.Unreadable;
        }

        try
        {
            var root = JsonNode.Parse(text) as JsonObject
                ?? throw new JsonException("Settings root is not an object.");
            var migrated = SettingsMigrator.Migrate(root);
            Current = migrated.Deserialize(SettingsJsonContext.Default.AppSettings)
                ?? throw new JsonException("Settings deserialized to null.");
            Current.Normalize();
            return SettingsLoadResult.Loaded;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Warn("Settings file is corrupt; moving it to .bak and using defaults", ex);
            TryMoveToBackup();
            Current = new AppSettings();
            return SettingsLoadResult.RecoveredFromCorrupt;
        }
    }

    private void TryMoveToBackup()
    {
        try
        {
            File.Move(_path, _path + ".bak", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not move the corrupt settings file to .bak", ex);
        }
    }

    public void RequestSave()
    {
        var json = JsonSerializer.Serialize(Current, SettingsJsonContext.Default.AppSettings);
        lock (_gate)
        {
            _pendingJson = json;
        }

        _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
    }

    public void Flush()
    {
        string? json;
        lock (_gate)
        {
            json = _pendingJson;
            _pendingJson = null;
        }

        if (json is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Failed to save settings", ex);
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        Flush();
    }
}
