using Microsoft.UI.Xaml.Controls;
using Supermonitor.Core.Settings;

namespace Supermonitor.Dialogs;

public sealed partial class ThresholdDialog : ContentDialog
{
    private readonly NumberBox _warning;
    private readonly NumberBox _critical;
    private bool _cleared;

    public ThresholdDialog(string metricName, MetricThreshold? current)
    {
        Title = $"Thresholds — {metricName}";
        PrimaryButtonText = "Save";
        SecondaryButtonText = "Clear";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        _warning = new NumberBox { Header = "Warning at", Value = current?.Warning ?? double.NaN, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        _critical = new NumberBox { Header = "Critical at", Value = current?.Critical ?? double.NaN, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        Content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Values use the metric's base unit (°C, %, bytes).", TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
                _warning,
                _critical,
            },
        };

        PrimaryButtonClick += (_, args) =>
        {
            if (double.IsNaN(_warning.Value) || double.IsNaN(_critical.Value))
            {
                args.Cancel = true;
            }
        };
        SecondaryButtonClick += (_, _) => _cleared = true;
    }

    public MetricThreshold? Result => _cleared
        ? null
        : new MetricThreshold
        {
            Warning = (float)Math.Min(_warning.Value, _critical.Value),
            Critical = (float)Math.Max(_warning.Value, _critical.Value),
        };
}
