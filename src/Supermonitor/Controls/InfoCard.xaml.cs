using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Supermonitor.ViewModels;

namespace Supermonitor.Controls;

public sealed partial class InfoCard : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(InfoCardViewModel),
        typeof(InfoCard),
        new PropertyMetadata(null, (d, _) => ((InfoCard)d).Bindings.Update()));

    public InfoCard()
    {
        InitializeComponent();
    }

    public InfoCardViewModel? ViewModel
    {
        get => (InfoCardViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private Visibility BarVisibility => ViewModel is { HasBar: true } ? Visibility.Visible : Visibility.Collapsed;
}
