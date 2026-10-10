using Microsoft.Extensions.Time.Testing;
using Supermonitor.Core.Settings;

namespace Supermonitor.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("smtest");
    private readonly FakeTimeProvider _time = new();

    private string FilePath => Path.Combine(_dir.FullName, "settings.json");

    private SettingsStore CreateStore() => new(FilePath, _time, TimeSpan.FromMilliseconds(500));

    public void Dispose() => _dir.Delete(recursive: true);

    [Fact]
    public void Load_without_file_returns_defaults()
    {
        using var store = CreateStore();

        Assert.Equal(SettingsLoadResult.CreatedDefault, store.Load());
        Assert.Equal(1000, store.Current.SampleIntervalMs);
    }

    [Fact]
    public void RequestSave_is_debounced_and_round_trips()
    {
        using (var store = CreateStore())
        {
            store.Load();
            store.Current.PingHost = "8.8.4.4";
            store.RequestSave();

            _time.Advance(TimeSpan.FromMilliseconds(400));
            Assert.False(File.Exists(FilePath));

            store.Current.SampleIntervalMs = 500;
            store.RequestSave();
            _time.Advance(TimeSpan.FromMilliseconds(400));
            Assert.False(File.Exists(FilePath));

            _time.Advance(TimeSpan.FromMilliseconds(100));
            Assert.True(File.Exists(FilePath));
        }

        using var reloaded = CreateStore();
        Assert.Equal(SettingsLoadResult.Loaded, reloaded.Load());
        Assert.Equal("8.8.4.4", reloaded.Current.PingHost);
        Assert.Equal(500, reloaded.Current.SampleIntervalMs);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void Flush_writes_pending_changes_immediately()
    {
        using var store = CreateStore();
        store.Load();
        store.Current.StartMinimized = true;
        store.RequestSave();

        store.Flush();

        Assert.Contains("\"startMinimized\": true", File.ReadAllText(FilePath));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{ \"sampleIntervalMs\": \"fast\" }")]
    public void Corrupt_file_is_moved_to_bak_and_defaults_are_used(string content)
    {
        File.WriteAllText(FilePath, content);
        using var store = CreateStore();

        Assert.Equal(SettingsLoadResult.RecoveredFromCorrupt, store.Load());
        Assert.Equal(1000, store.Current.SampleIntervalMs);
        Assert.Equal(content, File.ReadAllText(FilePath + ".bak"));
    }

    [Fact]
    public void Out_of_range_values_are_normalized_on_load()
    {
        File.WriteAllText(FilePath, "{ \"version\": 1, \"sampleIntervalMs\": 7, \"pingHost\": \"\" }");
        using var store = CreateStore();

        Assert.Equal(SettingsLoadResult.Loaded, store.Load());
        Assert.Equal(1000, store.Current.SampleIntervalMs);
        Assert.Equal("1.1.1.1", store.Current.PingHost);
    }
}
