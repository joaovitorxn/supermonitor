using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
        foreach (var child in InItemOrder(context.Children))
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

    // Children segue a ordem de criação dos elementos; depois de ObservableCollection.Move só o índice do item muda.
    private static IEnumerable<UIElement> InItemOrder(IReadOnlyList<UIElement> children)
    {
        if (children.Count == 0 || VisualTreeHelper.GetParent(children[0]) is not ItemsRepeater repeater)
        {
            return children;
        }

        return children.OrderBy(repeater.GetElementIndex);
    }
}
