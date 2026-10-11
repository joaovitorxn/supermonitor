using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    }

    public MetricCardViewModel? ViewModel
    {
        get => (MetricCardViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var card = (MetricCard)d;
        card.Chart.Source = e.NewValue as MetricCardViewModel;
    }
}
