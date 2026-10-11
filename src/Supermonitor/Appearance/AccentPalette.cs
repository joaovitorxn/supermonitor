using Microsoft.UI.Xaml;
using Windows.UI;

namespace Supermonitor.Appearance;

public static class AccentPalette
{
    private static Color? _current;

    public static event Action? Changed;

    public static Color Current => _current ??= (Color)Application.Current.Resources["SystemAccentColor"];

    public static void Set(Color color)
    {
        _current = color;
        Changed?.Invoke();
    }
}
