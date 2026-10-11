using System.Text.Json;
using Supermonitor.Core.Settings;

namespace Supermonitor.Core.Overlay;

public static class OverlayEditor
{
    public static bool AddItem(OverlayStyle style, OverlayMetric metric)
    {
        if (style.Items.Any(i => i.Metric == metric))
        {
            return false;
        }

        style.Items.Add(new OverlayItemSettings { Metric = metric });
        return true;
    }

    public static bool RemoveItem(OverlayStyle style, OverlayMetric metric)
    {
        var index = style.Items.FindIndex(i => i.Metric == metric);
        if (index < 0)
        {
            return false;
        }

        style.Items.RemoveAt(index);
        return true;
    }

    public static int MoveItem(OverlayStyle style, OverlayMetric metric, int delta)
    {
        var index = style.Items.FindIndex(i => i.Metric == metric);
        if (index < 0)
        {
            return -1;
        }

        var target = Math.Clamp(index + delta, 0, style.Items.Count - 1);
        var item = style.Items[index];
        style.Items.RemoveAt(index);
        style.Items.Insert(target, item);
        return target;
    }

    public static void ApplyPreset(OverlaySettings overlay, OverlayPreset preset) => overlay.Style = preset.Style.Clone();

    public static bool SavePreset(OverlaySettings overlay, string name)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length == 0 || OverlayPresets.BuiltInNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        overlay.UserPresets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        overlay.UserPresets.Add(new OverlayPreset { Name = name, Style = overlay.Style.Clone() });
        return true;
    }

    public static bool DeletePreset(OverlaySettings overlay, string name) =>
        overlay.UserPresets.RemoveAll(p => string.Equals(p.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase)) > 0;

    public static IReadOnlyList<OverlayPreset> AllPresets(OverlaySettings overlay) =>
        [.. OverlayPresets.BuiltIn(), .. overlay.UserPresets];

    // Nome do preset cujo estilo é idêntico ao atual, ou null quando o usuário mexeu em algo.
    public static string? MatchingPresetName(OverlaySettings overlay)
    {
        var current = Serialize(overlay.Style);
        return AllPresets(overlay).FirstOrDefault(p => Serialize(p.Style) == current)?.Name;
    }

    private static string Serialize(OverlayStyle style) =>
        JsonSerializer.Serialize(style, SettingsJsonContext.Default.OverlayStyle);
}
