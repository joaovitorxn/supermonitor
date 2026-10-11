using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Supermonitor.Core.Overlay;
using Supermonitor.Core.Settings;

namespace Supermonitor.Dialogs;

public sealed class OverlayItemDialog : ContentDialog
{
    private readonly TextBox _name;
    private readonly TextBox _labelColor;
    private readonly TextBox _valueColor;
    private readonly NumberBox _warning;
    private readonly NumberBox _critical;
    private readonly TextBlock _error;
    private readonly OverlayItemSettings _item;

    public OverlayItemDialog(OverlayItemSettings item)
    {
        _item = item;
        Result = item.Clone();
        Title = $"{OverlayMetricCatalog.Label(item.Metric)} — label, colors and thresholds";
        PrimaryButtonText = "Save";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        _name = new TextBox { Header = "Label", PlaceholderText = OverlayMetricCatalog.Label(item.Metric), Text = item.CustomLabel ?? string.Empty, MaxLength = OverlayStyle.MaxLabelLength };
        _labelColor = new TextBox { Header = "Label color", PlaceholderText = "#RRGGBB — empty uses the overlay default", Text = item.LabelColor ?? string.Empty };
        _valueColor = new TextBox { Header = "Value color", PlaceholderText = "#RRGGBB — empty uses the overlay default", Text = item.ValueColor ?? string.Empty };
        _warning = new NumberBox { Header = "Warning at", Value = item.Threshold?.Warning ?? double.NaN, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        _critical = new NumberBox { Header = "Critical at", Value = item.Threshold?.Critical ?? double.NaN, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        _error = new TextBlock { Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"] };
        Content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                _name,
                _labelColor,
                _valueColor,
                new TextBlock { Text = "Thresholds use the metric's base unit (°C, %, bytes, ms). Leave both empty to use the dashboard thresholds.", TextWrapping = TextWrapping.Wrap },
                _warning,
                _critical,
                _error,
            },
        };

        PrimaryButtonClick += OnSave;
    }

    public OverlayItemSettings Result { get; private set; }

    private void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var label = _labelColor.Text.Trim();
        var value = _valueColor.Text.Trim();
        if ((label.Length > 0 && !ColorText.TryParseRgb(label, out _, out _, out _))
            || (value.Length > 0 && !ColorText.TryParseRgb(value, out _, out _, out _)))
        {
            Fail(args, "Colors must look like #RRGGBB.");
            return;
        }

        var hasWarning = !double.IsNaN(_warning.Value);
        var hasCritical = !double.IsNaN(_critical.Value);
        if (hasWarning != hasCritical)
        {
            Fail(args, "Fill both thresholds or leave both empty.");
            return;
        }

        var result = _item.Clone();
        var name = _name.Text.Trim();
        result.CustomLabel = name.Length == 0 ? null : name;
        result.LabelColor = label.Length == 0 ? null : label.ToUpperInvariant();
        result.ValueColor = value.Length == 0 ? null : value.ToUpperInvariant();
        result.Threshold = hasWarning ? new MetricThreshold { Warning = (float)_warning.Value, Critical = (float)_critical.Value } : null;
        Result = result;
    }

    private void Fail(ContentDialogButtonClickEventArgs args, string message)
    {
        args.Cancel = true;
        _error.Text = message;
        _error.Visibility = Visibility.Visible;
    }
}
