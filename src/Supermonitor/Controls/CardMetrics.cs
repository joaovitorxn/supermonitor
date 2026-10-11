using Microsoft.UI.Xaml;
using Supermonitor.Core.Settings;

namespace Supermonitor.Controls;

public static class CardMetrics
{
    public static double Width(CardSize size) => size switch
    {
        CardSize.Compact => 200,
        CardSize.Large => 572,
        _ => 280,
    };

    public static double Height(CardSize size) => size switch
    {
        CardSize.Compact => 96,
        CardSize.Large => 260,
        _ => 170,
    };

    public static Visibility ChartVisibility(CardSize size) => size == CardSize.Compact ? Visibility.Collapsed : Visibility.Visible;
}
