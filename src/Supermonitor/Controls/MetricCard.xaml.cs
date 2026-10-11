using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Supermonitor.Core.Dashboard;
using Supermonitor.Core.Settings;
using Supermonitor.Dialogs;
using Supermonitor.ViewModels;
using Windows.ApplicationModel.DataTransfer;

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
        DragStarting += OnDragStarting;
        DragOver += OnDragOver;
        Drop += OnDrop;
    }

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
        CanDrag = editing;
        AllowDrop = editing;
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

    private void OnDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (ViewModel is { } vm)
        {
            args.Data.SetText(vm.MetricId);
            args.Data.RequestedOperation = DataPackageOperation.Move;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.Text))
        {
            e.AcceptedOperation = DataPackageOperation.Move;
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (ViewModel is not { Actions: { } actions } vm || !e.DataView.Contains(StandardDataFormats.Text))
        {
            return;
        }

        var source = await e.DataView.GetTextAsync();
        if (source != vm.MetricId)
        {
            actions.MoveTo(source, vm.MetricId);
        }
    }
}
