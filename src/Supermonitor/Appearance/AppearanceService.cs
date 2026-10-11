using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Supermonitor.Core.Settings;
using Windows.UI;

namespace Supermonitor.Appearance;

public static class AppearanceService
{
    private static readonly string[] AccentKeys =
    [
        "SystemAccentColor",
        "SystemAccentColorLight1",
        "SystemAccentColorLight2",
        "SystemAccentColorLight3",
        "SystemAccentColorDark1",
        "SystemAccentColorDark2",
        "SystemAccentColorDark3",
    ];

    private static string? _appliedAccent;
    private static bool _accentApplied;

    public static event Action? Changed;

    public static void NotifyChanged() => Changed?.Invoke();

    public static void Apply(Window window, Panel root, AppSettings settings)
    {
        root.RequestedTheme = settings.Theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        window.AppWindow.TitleBar.PreferredTheme = settings.Theme switch
        {
            AppTheme.Light => TitleBarTheme.Light,
            AppTheme.Dark => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode,
        };

        // Swapping the backdrop during a theme transition crashes CoreMessaging; only swap when the type changes.
        if (KindOf(window.SystemBackdrop) != settings.Backdrop)
        {
            window.SystemBackdrop = settings.Backdrop switch
            {
                BackdropKind.MicaAlt => new MicaBackdrop { Kind = MicaKind.BaseAlt },
                BackdropKind.Acrylic => new DesktopAcrylicBackdrop(),
                BackdropKind.Solid => null,
                _ => new MicaBackdrop(),
            };
        }

        ApplySolidBackground(root, settings);
        if (ApplyAccentResources(settings.AccentColor))
        {
            // ThemeResource is only re-evaluated on a theme change: toggle and revert so controls pick up the new color.
            var requested = root.RequestedTheme;
            root.RequestedTheme = root.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
            root.RequestedTheme = requested;
        }

        AccentPalette.Set(ResolveAccent(settings.AccentColor));
    }

    // Overrides the WinUI accent colors at app level; null restores the Windows color.
    public static bool ApplyAccentResources(string? hex)
    {
        if (_accentApplied && _appliedAccent == hex)
        {
            return false;
        }

        var firstTime = !_accentApplied;
        _accentApplied = true;
        _appliedAccent = hex;
        var resources = Application.Current.Resources;
        if (!ColorText.TryParseRgb(hex, out var r, out var g, out var b))
        {
            foreach (var key in AccentKeys)
            {
                resources.Remove(key);
            }

            return !firstTime;
        }

        var shades = AccentShades.Compute(r, g, b);
        (byte R, byte G, byte B)[] values = [shades.Base, shades.Light1, shades.Light2, shades.Light3, shades.Dark1, shades.Dark2, shades.Dark3];
        for (var i = 0; i < AccentKeys.Length; i++)
        {
            resources[AccentKeys[i]] = Color.FromArgb(255, values[i].R, values[i].G, values[i].B);
        }

        return !firstTime;
    }

    // Only adjustment that depends on the effective theme; safe to call from ActualThemeChanged.
    public static void ApplySolidBackground(Panel root, AppSettings settings)
    {
        if (settings.Backdrop == BackdropKind.Solid)
        {
            root.Background = new SolidColorBrush(root.ActualTheme == ElementTheme.Dark
                ? Color.FromArgb(255, 0x20, 0x20, 0x20)
                : Color.FromArgb(255, 0xF3, 0xF3, 0xF3));
        }
        else
        {
            root.ClearValue(Panel.BackgroundProperty);
        }
    }

    private static BackdropKind KindOf(SystemBackdrop? backdrop) => backdrop switch
    {
        MicaBackdrop { Kind: MicaKind.BaseAlt } => BackdropKind.MicaAlt,
        MicaBackdrop => BackdropKind.Mica,
        DesktopAcrylicBackdrop => BackdropKind.Acrylic,
        _ => BackdropKind.Solid,
    };

    public static Color ResolveAccent(string? hex) =>
        ColorText.TryParseRgb(hex, out var r, out var g, out var b)
            ? Color.FromArgb(255, r, g, b)
            : (Color)Application.Current.Resources["SystemAccentColor"];
}
