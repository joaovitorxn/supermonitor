using Supermonitor.Core.Overlay;

namespace Supermonitor.Tests.Overlay;

public class OverlayEditorTests
{
    private static OverlayStyle Style(params OverlayMetric[] metrics) =>
        new() { Items = metrics.Select(m => new OverlayItemSettings { Metric = m }).ToList() };

    [Fact]
    public void Add_appends_a_new_metric_once()
    {
        var style = Style(OverlayMetric.Fps);

        Assert.True(OverlayEditor.AddItem(style, OverlayMetric.Ping));
        Assert.False(OverlayEditor.AddItem(style, OverlayMetric.Ping));
        Assert.Equal(new[] { OverlayMetric.Fps, OverlayMetric.Ping }, style.Items.Select(i => i.Metric));
    }

    [Fact]
    public void Remove_deletes_only_an_existing_metric()
    {
        var style = Style(OverlayMetric.Fps, OverlayMetric.Ping);

        Assert.True(OverlayEditor.RemoveItem(style, OverlayMetric.Fps));
        Assert.False(OverlayEditor.RemoveItem(style, OverlayMetric.Fps));
        Assert.Equal(new[] { OverlayMetric.Ping }, style.Items.Select(i => i.Metric));
    }

    [Fact]
    public void Move_shifts_an_item_and_clamps_at_the_ends()
    {
        var style = Style(OverlayMetric.Fps, OverlayMetric.FrameTime, OverlayMetric.Ping);

        Assert.Equal(2, OverlayEditor.MoveItem(style, OverlayMetric.Fps, 5));
        Assert.Equal(new[] { OverlayMetric.FrameTime, OverlayMetric.Ping, OverlayMetric.Fps }, style.Items.Select(i => i.Metric));
        Assert.Equal(0, OverlayEditor.MoveItem(style, OverlayMetric.Fps, -9));
        Assert.Equal(-1, OverlayEditor.MoveItem(style, OverlayMetric.Clock, 1));
    }

    [Fact]
    public void Apply_preset_copies_the_style_and_keeps_enabled_and_monitor()
    {
        var overlay = new OverlaySettings { Enabled = true, MonitorId = "M1" };
        var preset = new OverlayPreset { Name = "Mine", Style = OverlayPresets.Minimal() };

        OverlayEditor.ApplyPreset(overlay, preset);
        overlay.Style.Items.Clear();

        Assert.True(overlay.Enabled);
        Assert.Equal("M1", overlay.MonitorId);
        Assert.Single(preset.Style.Items);
        Assert.Equal(OverlayLayout.Compact, preset.Style.Layout);
    }

    [Fact]
    public void Save_preset_stores_a_copy_under_a_trimmed_name_and_replaces_by_name()
    {
        var overlay = new OverlaySettings { Style = Style(OverlayMetric.Fps) };

        Assert.True(OverlayEditor.SavePreset(overlay, "  Mine "));
        overlay.Style.Items.Add(new OverlayItemSettings { Metric = OverlayMetric.Ping });
        Assert.True(OverlayEditor.SavePreset(overlay, "mine"));

        var preset = Assert.Single(overlay.UserPresets);
        Assert.Equal("mine", preset.Name);
        Assert.Equal(2, preset.Style.Items.Count);
        overlay.Style.Items.Clear();
        Assert.Equal(2, preset.Style.Items.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Default")]
    [InlineData("minimal")]
    [InlineData("FULL")]
    public void Save_preset_rejects_blank_and_built_in_names(string name)
    {
        var overlay = new OverlaySettings();

        Assert.False(OverlayEditor.SavePreset(overlay, name));
        Assert.Empty(overlay.UserPresets);
    }

    [Fact]
    public void Delete_preset_removes_only_user_presets()
    {
        var overlay = new OverlaySettings();
        OverlayEditor.SavePreset(overlay, "Mine");

        Assert.False(OverlayEditor.DeletePreset(overlay, "Default"));
        Assert.True(OverlayEditor.DeletePreset(overlay, "MINE"));
        Assert.False(OverlayEditor.DeletePreset(overlay, "Mine"));
        Assert.Empty(overlay.UserPresets);
    }

    [Fact]
    public void All_presets_lists_built_in_first_then_user_presets()
    {
        var overlay = new OverlaySettings();
        OverlayEditor.SavePreset(overlay, "Mine");

        Assert.Equal(new[] { "Minimal", "Default", "Full", "Mine" }, OverlayEditor.AllPresets(overlay).Select(p => p.Name));
    }

    [Fact]
    public void Matching_preset_name_follows_the_current_style()
    {
        var overlay = new OverlaySettings { Style = OverlayPresets.Full() };
        Assert.Equal("Full", OverlayEditor.MatchingPresetName(overlay));

        overlay.Style.FontSize += 1;
        Assert.Null(OverlayEditor.MatchingPresetName(overlay));

        OverlayEditor.SavePreset(overlay, "Mine");
        Assert.Equal("Mine", OverlayEditor.MatchingPresetName(overlay));
    }
}
