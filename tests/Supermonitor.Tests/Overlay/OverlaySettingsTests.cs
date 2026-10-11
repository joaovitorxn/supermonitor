using System.Text.Json;
using Supermonitor.Core.Overlay;
using Supermonitor.Core.Settings;

namespace Supermonitor.Tests.Overlay;

public class OverlaySettingsTests
{
    [Fact]
    public void Defaults_are_disabled_with_the_default_preset_items()
    {
        var settings = new AppSettings();

        Assert.False(settings.Overlay.Enabled);
        Assert.Null(settings.Overlay.MonitorId);
        Assert.Equal(OverlayAnchor.TopLeft, settings.Overlay.Style.Anchor);
        Assert.Equal(4, settings.Overlay.Style.RefreshHz);
        Assert.Contains(settings.Overlay.Style.Items, i => i.Metric == OverlayMetric.Fps);
        Assert.Equal(3, settings.Version);
    }

    [Fact]
    public void Built_in_presets_are_already_normalized()
    {
        foreach (var preset in OverlayPresets.BuiltIn())
        {
            var before = JsonSerializer.Serialize(preset.Style, SettingsJsonContext.Default.OverlayStyle);
            preset.Style.Normalize();
            var after = JsonSerializer.Serialize(preset.Style, SettingsJsonContext.Default.OverlayStyle);

            Assert.Equal(before, after);
        }
    }

    [Fact]
    public void Built_in_presets_are_minimal_default_and_full()
    {
        var presets = OverlayPresets.BuiltIn();

        Assert.Equal(new[] { "Minimal", "Default", "Full" }, presets.Select(p => p.Name));
        Assert.Equal(new[] { OverlayMetric.Fps }, presets[0].Style.Items.Select(i => i.Metric));
        Assert.Equal(Enum.GetValues<OverlayMetric>().Length, presets[2].Style.Items.Count);
    }

    [Fact]
    public void Built_in_presets_are_independent_copies()
    {
        var first = OverlayPresets.Default();
        first.Items.Clear();

        Assert.NotEmpty(OverlayPresets.Default().Items);
    }

    [Fact]
    public void Normalize_replaces_invalid_values_with_defaults()
    {
        var style = new OverlayStyle
        {
            Anchor = (OverlayAnchor)99,
            Layout = (OverlayLayout)99,
            FontFamily = "  ",
            FontSize = float.NaN,
            LabelColor = "blue",
            ValueColor = "#12",
            BackgroundColor = null!,
            BackgroundOpacity = 7,
            CornerRadius = 500,
            Padding = -3,
            GraphWidth = 1,
            GraphHeight = 9999,
            RefreshHz = 3,
            OffsetX = 1_000_000,
            OffsetY = -1_000_000,
        };

        style.Normalize();

        Assert.Equal(OverlayAnchor.TopLeft, style.Anchor);
        Assert.Equal(OverlayLayout.Vertical, style.Layout);
        Assert.Equal("Segoe UI", style.FontFamily);
        Assert.Equal(14f, style.FontSize);
        Assert.Equal("#B0B0B0", style.LabelColor);
        Assert.Equal("#FFFFFF", style.ValueColor);
        Assert.Equal("#000000", style.BackgroundColor);
        Assert.Equal(1f, style.BackgroundOpacity);
        Assert.Equal(32, style.CornerRadius);
        Assert.Equal(0, style.Padding);
        Assert.Equal(40, style.GraphWidth);
        Assert.Equal(200, style.GraphHeight);
        Assert.Equal(4, style.RefreshHz);
        Assert.Equal(4000, style.OffsetX);
        Assert.Equal(-4000, style.OffsetY);
    }

    [Fact]
    public void Normalize_uppercases_colors_and_clamps_the_font_size()
    {
        var style = new OverlayStyle { LabelColor = "#aabbcc", FontSize = 400 };

        style.Normalize();

        Assert.Equal("#AABBCC", style.LabelColor);
        Assert.Equal(48f, style.FontSize);
    }

    [Fact]
    public void Normalize_drops_duplicate_and_unknown_items_and_bad_item_values()
    {
        var style = new OverlayStyle
        {
            Items =
            [
                new() { Metric = OverlayMetric.Fps, ValueColor = "red" },
                new() { Metric = OverlayMetric.Fps },
                new() { Metric = (OverlayMetric)99 },
                null!,
                new() { Metric = OverlayMetric.CpuTemperature, LabelColor = "#ff0000", Threshold = new() { Warning = 90, Critical = 80 } },
                new() { Metric = OverlayMetric.GpuTemperature, Threshold = new() { Warning = float.NaN, Critical = 10 } },
            ],
        };

        style.Normalize();

        Assert.Equal(new[] { OverlayMetric.Fps, OverlayMetric.CpuTemperature, OverlayMetric.GpuTemperature }, style.Items.Select(i => i.Metric));
        Assert.Null(style.Items[0].ValueColor);
        Assert.Equal("#FF0000", style.Items[1].LabelColor);
        Assert.Equal(80, style.Items[1].Threshold!.Warning);
        Assert.Equal(90, style.Items[1].Threshold!.Critical);
        Assert.Null(style.Items[2].Threshold);
    }

    [Fact]
    public void Normalize_restores_null_collections()
    {
        var overlay = new OverlaySettings { Style = null!, UserPresets = null!, MonitorId = "  " };

        overlay.Normalize();

        Assert.NotNull(overlay.Style);
        Assert.Empty(overlay.UserPresets);
        Assert.Null(overlay.MonitorId);
    }

    [Fact]
    public void Normalize_drops_user_presets_that_are_unnamed_duplicated_or_shadow_a_built_in()
    {
        var overlay = new OverlaySettings
        {
            UserPresets =
            [
                new() { Name = " Mine ", Style = new OverlayStyle() },
                new() { Name = "mine", Style = new OverlayStyle() },
                new() { Name = "", Style = new OverlayStyle() },
                new() { Name = "default", Style = new OverlayStyle() },
                new() { Name = "Broken", Style = null! },
            ],
        };

        overlay.Normalize();

        Assert.Equal(new[] { "Mine" }, overlay.UserPresets.Select(p => p.Name));
    }

    [Fact]
    public void App_settings_normalize_covers_the_overlay()
    {
        var settings = new AppSettings { Overlay = null! };

        settings.Normalize();

        Assert.NotNull(settings.Overlay);
    }

    [Fact]
    public void Overlay_settings_round_trip_through_json_with_string_enums()
    {
        var settings = new AppSettings();
        settings.Overlay.Enabled = true;
        settings.Overlay.MonitorId = @"\\.\DISPLAY2";
        settings.Overlay.Style.Anchor = OverlayAnchor.BottomRight;
        settings.Overlay.Style.Items[0].Threshold = new MetricThreshold { Warning = 30, Critical = 20 };
        settings.Overlay.UserPresets.Add(new OverlayPreset { Name = "Mine", Style = OverlayPresets.Minimal() });

        var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
        var loaded = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)!;

        Assert.Contains("\"anchor\": \"BottomRight\"", json);
        Assert.Contains("\"metric\": \"Fps\"", json);
        Assert.True(loaded.Overlay.Enabled);
        Assert.Equal(@"\\.\DISPLAY2", loaded.Overlay.MonitorId);
        Assert.Equal(OverlayAnchor.BottomRight, loaded.Overlay.Style.Anchor);
        Assert.Equal(30, loaded.Overlay.Style.Items[0].Threshold!.Warning);
        Assert.Equal("Mine", loaded.Overlay.UserPresets.Single().Name);
    }

    [Fact]
    public void Unknown_enum_names_in_the_file_are_normalized_without_losing_the_rest()
    {
        var dir = Directory.CreateTempSubdirectory("smtest");
        try
        {
            var path = Path.Combine(dir.FullName, "settings.json");
            File.WriteAllText(path, """
                {
                  "version": 3,
                  "pingHost": "9.9.9.9",
                  "overlay": {
                    "enabled": true,
                    "style": {
                      "anchor": "Bogus",
                      "layout": "Sideways",
                      "items": [ { "metric": "LimiterState" }, { "metric": "Ping" } ]
                    }
                  }
                }
                """);
            using var store = new SettingsStore(path, TimeProvider.System, TimeSpan.FromMilliseconds(500));

            var result = store.Load();

            Assert.Equal(SettingsLoadResult.Loaded, result);
            Assert.Equal("9.9.9.9", store.Current.PingHost);
            Assert.True(store.Current.Overlay.Enabled);
            Assert.Equal(OverlayAnchor.TopLeft, store.Current.Overlay.Style.Anchor);
            Assert.Equal(OverlayLayout.Vertical, store.Current.Overlay.Style.Layout);
            Assert.Equal(new[] { OverlayMetric.Ping }, store.Current.Overlay.Style.Items.Select(i => i.Metric));
            Assert.False(File.Exists(path + ".bak"));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Normalize_keeps_frame_rate_thresholds_in_descending_order_and_sorts_the_others_ascending()
    {
        var style = new OverlayStyle
        {
            Items =
            [
                new() { Metric = OverlayMetric.Fps, Threshold = new MetricThreshold { Warning = 30, Critical = 60 } },
                new() { Metric = OverlayMetric.Low1Percent, Threshold = new MetricThreshold { Warning = 60, Critical = 30 } },
                new() { Metric = OverlayMetric.CpuTemperature, Threshold = new MetricThreshold { Warning = 90, Critical = 80 } },
            ],
        };

        style.Normalize();

        Assert.Equal((60f, 30f), (style.Items[0].Threshold!.Warning, style.Items[0].Threshold!.Critical));
        Assert.Equal((60f, 30f), (style.Items[1].Threshold!.Warning, style.Items[1].Threshold!.Critical));
        Assert.Equal((80f, 90f), (style.Items[2].Threshold!.Warning, style.Items[2].Threshold!.Critical));
    }
}
