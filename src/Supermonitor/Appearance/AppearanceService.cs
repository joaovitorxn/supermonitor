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

        window.SystemBackdrop = settings.Backdrop switch
        {
            BackdropKind.MicaAlt => new MicaBackdrop { Kind = MicaKind.BaseAlt },
            BackdropKind.Acrylic => new DesktopAcrylicBackdrop(),
            BackdropKind.Solid => null,
            _ => new MicaBackdrop(),
        };

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

        AccentPalette.Set(ResolveAccent(settings.AccentColor));
    }

    public static Color ResolveAccent(string? hex) =>
        ColorText.TryParseRgb(hex, out var r, out var g, out var b)
            ? Color.FromArgb(255, r, g, b)
            : (Color)Application.Current.Resources["SystemAccentColor"];
}
