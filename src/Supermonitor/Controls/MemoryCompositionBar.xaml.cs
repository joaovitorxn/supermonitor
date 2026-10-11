using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Supermonitor.Appearance;
using Supermonitor.Core.Dashboard;
using Supermonitor.Core.Metrics;

namespace Supermonitor.Controls;

public sealed partial class MemoryCompositionBar : UserControl
{
    public MemoryCompositionBar()
    {
        InitializeComponent();
    }

    public void Update(MemoryComposition composition, UnitOptions options)
    {
        InUseRect.Fill = new SolidColorBrush(AccentPalette.Current);
        InUseColumn.Width = new GridLength(composition.InUse, GridUnitType.Star);
        ModifiedColumn.Width = new GridLength(composition.Modified, GridUnitType.Star);
        StandbyColumn.Width = new GridLength(composition.Standby, GridUnitType.Star);
        FreeColumn.Width = new GridLength(composition.Free, GridUnitType.Star);

        string F(double bytes) => UnitFormatter.Format(bytes, MetricUnit.Bytes, options);
        Legend.Text = $"In use {F(composition.InUse)} · Modified {F(composition.Modified)} · Standby {F(composition.Standby)} · Free {F(composition.Free)}";
    }
}
