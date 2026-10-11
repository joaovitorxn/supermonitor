using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Supermonitor.Controls;

// Fluxo da esquerda para a direita; quebra de linha quando o próximo card não cabe.
public sealed partial class CardWrapLayout : NonVirtualizingLayout
{
    public double Spacing { get; set; } = 12;

    protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize) =>
        Place(context, availableSize.Width, arrange: false);

    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize) =>
        Place(context, finalSize.Width, arrange: true);

    private Size Place(NonVirtualizingLayoutContext context, double width, bool arrange)
    {
        double x = 0, y = 0, rowHeight = 0, usedWidth = 0;
        foreach (var child in context.Children)
        {
            if (!arrange)
            {
                child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            }

            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > width)
            {
                x = 0;
                y += rowHeight + Spacing;
                rowHeight = 0;
            }

            if (arrange)
            {
                child.Arrange(new Rect(x, y, size.Width, size.Height));
            }

            x += size.Width + Spacing;
            rowHeight = Math.Max(rowHeight, size.Height);
            usedWidth = Math.Max(usedWidth, x - Spacing);
        }

        return new Size(double.IsInfinity(width) ? usedWidth : width, y + rowHeight);
    }
}
