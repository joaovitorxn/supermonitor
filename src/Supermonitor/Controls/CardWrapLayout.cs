using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Supermonitor.Controls;

// Fluxo da esquerda para a direita; quebra de linha quando o próximo card não cabe.
public sealed partial class CardWrapLayout : NonVirtualizingLayout
{
    public double Spacing { get; set; } = 12;

    // Última posição por card; o elemento é recriado no reordenamento, então a chave é o MetricId.
    private readonly Dictionary<string, Point> _lastPositions = [];
    private string[] _lastOrder = [];

    protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize) =>
        Place(context, availableSize.Width, arrange: false);

    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize) =>
        Place(context, finalSize.Width, arrange: true);

    private Size Place(NonVirtualizingLayoutContext context, double width, bool arrange)
    {
        double x = 0, y = 0, rowHeight = 0, usedWidth = 0;
        var ordered = InItemOrder(context.Children).ToArray();
        var order = arrange ? ordered.Select(IdOf).ToArray() : _lastOrder;
        // Só anima quando a ordem muda (reordenação); redimensionar a janela reposiciona sem deslizar.
        var animate = arrange && !order.SequenceEqual(_lastOrder);
        foreach (var child in ordered)
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
                SlideFromPrevious(child, new Point(x, y), animate);
            }

            x += size.Width + Spacing;
            rowHeight = Math.Max(rowHeight, size.Height);
            usedWidth = Math.Max(usedWidth, x - Spacing);
        }

        if (arrange)
        {
            // Cards removidos saem do dicionário; reordenar mantém o conjunto de ids e preserva as posições.
            if (order.Length != _lastOrder.Length)
            {
                var present = order.ToHashSet();
                foreach (var stale in _lastPositions.Keys.Where(k => !present.Contains(k)).ToList())
                {
                    _lastPositions.Remove(stale);
                }
            }

            _lastOrder = order;
        }

        return new Size(double.IsInfinity(width) ? usedWidth : width, y + rowHeight);
    }

    // Faz a próxima animação do card partir de onde ele foi solto, não do seu lugar de origem.
    public bool Nudge(string metricId, float dx, float dy)
    {
        if (!_lastPositions.TryGetValue(metricId, out var p))
        {
            return false;
        }

        _lastPositions[metricId] = new Point(p.X + dx, p.Y + dy);
        return true;
    }

    private static string IdOf(UIElement child) => child is MetricCard { ViewModel: { } vm } ? vm.MetricId : string.Empty;

    private void SlideFromPrevious(UIElement child, Point position, bool animate)
    {
        if (child is not MetricCard { ViewModel: { } vm })
        {
            return;
        }

        if (animate && _lastPositions.TryGetValue(vm.MetricId, out var previous)
            && (Math.Abs(previous.X - position.X) > 0.5 || Math.Abs(previous.Y - position.Y) > 0.5))
        {
            ElementCompositionPreview.SetIsTranslationEnabled(child, true);
            var visual = ElementCompositionPreview.GetElementVisual(child);
            var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
            animation.InsertKeyFrame(0f, new System.Numerics.Vector3((float)(previous.X - position.X), (float)(previous.Y - position.Y), 0));
            animation.InsertKeyFrame(1f, System.Numerics.Vector3.Zero, visual.Compositor.CreateCubicBezierEasingFunction(new(0.2f, 0f), new(0f, 1f)));
            animation.Duration = TimeSpan.FromMilliseconds(250);
            visual.StartAnimation("Translation", animation);
        }

        _lastPositions[vm.MetricId] = position;
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
