using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Supermonitor.Core.Dashboard;
using Supermonitor.Core.Settings;
using Supermonitor.Dialogs;
using Supermonitor.ViewModels;

namespace Supermonitor.Controls;

public sealed partial class MetricCard : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(MetricCardViewModel),
        typeof(MetricCard),
        new PropertyMetadata(null, OnViewModelChanged));

    public MetricCard()
    {
        InitializeComponent();
        CardBorder.PointerPressed += OnPointerPressed;
        CardBorder.PointerMoved += OnPointerMoved;
        CardBorder.PointerReleased += OnPointerReleased;
        CardBorder.PointerCaptureLost += (_, _) => EndReorder();
    }

    private bool _reordering;
    private Windows.Foundation.Point _dragStart;
    private System.Numerics.Vector3 _dragOffset;

    public MetricCardViewModel? ViewModel
    {
        get => (MetricCardViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var card = (MetricCard)d;
        if (e.OldValue is MetricCardViewModel old)
        {
            old.PropertyChanged -= card.OnViewModelPropertyChanged;
        }

        var vm = e.NewValue as MetricCardViewModel;
        card.Chart.Source = vm;
        card.CardBorder.ContextFlyout = vm?.Actions is null ? null : card.CardMenu;
        if (vm is not null)
        {
            vm.PropertyChanged += card.OnViewModelPropertyChanged;
        }

        card.ApplyState();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MetricCardViewModel.Level) or nameof(MetricCardViewModel.Chart) or nameof(MetricCardViewModel.IsEditing))
        {
            ApplyState();
        }
    }

    private void ApplyState()
    {
        var vm = ViewModel;
        Chart.Kind = vm?.Chart ?? ChartKind.Line;
        var editing = vm is { IsEditing: true, Actions: not null };
        RemoveButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;

        switch (vm?.Level ?? ThresholdLevel.Normal)
        {
            case ThresholdLevel.Warning:
                ValueText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
                break;
            case ThresholdLevel.Critical:
                ValueText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                break;
            default:
                ValueText.ClearValue(TextBlock.ForegroundProperty);
                break;
        }
    }

    private void OnMenuOpening(object? sender, object e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        SizeCompact.IsChecked = vm.Size == CardSize.Compact;
        SizeNormal.IsChecked = vm.Size == CardSize.Normal;
        SizeLarge.IsChecked = vm.Size == CardSize.Large;
        ChartLine.IsChecked = vm.Chart == ChartKind.Line;
        ChartArea.IsChecked = vm.Chart == ChartKind.Area;
        ChartBar.IsChecked = vm.Chart == ChartKind.Bar;
    }

    private void OnSizeClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { Actions: { } actions } vm && sender is FrameworkElement { Tag: string tag })
        {
            actions.SetSize(vm, Enum.Parse<CardSize>(tag));
        }
    }

    private void OnChartClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { Actions: { } actions } vm && sender is FrameworkElement { Tag: string tag })
        {
            actions.SetChart(vm, Enum.Parse<ChartKind>(tag));
        }
    }

    private async void OnThresholdsClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { Actions: { } actions } vm)
        {
            return;
        }

        var dialog = new ThresholdDialog(vm.Title, actions.GetThreshold(vm.MetricId)) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.None)
        {
            actions.SetThreshold(vm.MetricId, dialog.Result);
        }
    }

    private void OnMoveEarlierClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { Actions: { } actions } vm)
        {
            actions.Move(vm, -1);
        }
    }

    private void OnMoveLaterClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { Actions: { } actions } vm)
        {
            actions.Move(vm, 1);
        }
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { Actions: { } actions } vm)
        {
            actions.Remove(vm);
        }
    }

    // Arrastar do sistema (OLE) não funciona em processo elevado; reordenação feita com captura de ponteiro.
    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var mouseWithoutLeftButton = e.Pointer.PointerDeviceType == PointerDeviceType.Mouse
            && !e.GetCurrentPoint(CardBorder).Properties.IsLeftButtonPressed;
        if (ViewModel is not { IsEditing: true, Actions: not null } || mouseWithoutLeftButton)
        {
            return;
        }

        _reordering = CardBorder.CapturePointer(e.Pointer);
        if (_reordering)
        {
            _dragStart = e.GetCurrentPoint(null).Position;
            _dragOffset = System.Numerics.Vector3.Zero;
            ElementCompositionPreview.SetIsTranslationEnabled(this, true);
            ElementCompositionPreview.GetElementVisual(this).StopAnimation("Translation");
            Canvas.SetZIndex(this, 1);
            Opacity = 0.85;
            e.Handled = true;
        }
    }

    // O card segue o ponteiro; os demais só se reorganizam ao soltar.
    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_reordering)
        {
            return;
        }

        var position = e.GetCurrentPoint(null).Position;
        _dragOffset = new System.Numerics.Vector3((float)(position.X - _dragStart.X), (float)(position.Y - _dragStart.Y), 0);
        ElementCompositionPreview.GetElementVisual(this).Properties.InsertVector3("Translation", _dragOffset);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_reordering)
        {
            return;
        }

        var position = e.GetCurrentPoint(null).Position;
        CardBorder.ReleasePointerCapture(e.Pointer);
        EndReorder();
        e.Handled = true;

        if (ViewModel is not { Actions: { } actions } vm || XamlRoot?.Content is not UIElement root)
        {
            ElementCompositionPreview.GetElementVisual(this).Properties.InsertVector3("Translation", System.Numerics.Vector3.Zero);
            return;
        }

        var target = VisualTreeHelper.FindElementsInHostCoordinates(position, root)
            .OfType<MetricCard>()
            .FirstOrDefault(card => !ReferenceEquals(card, this) && card.ViewModel is not null);
        var visual = ElementCompositionPreview.GetElementVisual(this);
        visual.Properties.InsertVector3("Translation", System.Numerics.Vector3.Zero);
        if (target?.ViewModel is { } targetVm)
        {
            // O card reordenado nasce na posição onde foi solto e desliza até o novo lugar.
            if ((VisualTreeHelper.GetParent(this) as ItemsRepeater)?.Layout is CardWrapLayout layout)
            {
                layout.Nudge(vm.MetricId, _dragOffset.X, _dragOffset.Y);
            }

            actions.MoveTo(vm.MetricId, targetVm.MetricId);
        }
        else
        {
            var back = visual.Compositor.CreateVector3KeyFrameAnimation();
            back.InsertKeyFrame(0f, _dragOffset);
            back.InsertKeyFrame(1f, System.Numerics.Vector3.Zero, visual.Compositor.CreateCubicBezierEasingFunction(new(0.2f, 0f), new(0f, 1f)));
            back.Duration = TimeSpan.FromMilliseconds(200);
            visual.StartAnimation("Translation", back);
        }
    }

    private void EndReorder()
    {
        _reordering = false;
        Opacity = 1;
        Canvas.SetZIndex(this, 0);
    }
}
