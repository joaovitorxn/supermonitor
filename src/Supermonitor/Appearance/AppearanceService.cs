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

        // Trocar o backdrop durante uma transição de tema derruba o CoreMessaging; só troca quando o tipo muda.
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
        AccentPalette.Set(ResolveAccent(settings.AccentColor));
    }

    // Único ajuste que depende do tema efetivo; seguro para chamar de ActualThemeChanged.
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
