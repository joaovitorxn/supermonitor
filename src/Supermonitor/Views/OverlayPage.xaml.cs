using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Supermonitor.Core;
using Supermonitor.Core.Frames;
using Supermonitor.Core.Overlay;
using Supermonitor.Core.Settings;
using Supermonitor.Dialogs;
using Supermonitor.Overlay;
using Supermonitor.ViewModels;
using Windows.UI;

namespace Supermonitor.Views;

public sealed partial class OverlayPage : Page
{
    private const string CustomPreset = "Custom";

    private static readonly string[] Fonts = ["Segoe UI", "Segoe UI Variable Display", "Bahnschrift", "Calibri", "Consolas", "Cascadia Mono", "Arial", "Verdana"];

    private readonly DemoOverlayValues _demo = new();
    private readonly DispatcherQueueTimer _statusTimer;
    private AppHost? _host;
    private OverlayService? _overlay;
    private OverlayRenderer? _renderer;
    private WriteableBitmap? _bitmap;
    private IReadOnlyList<MonitorInfo> _monitors = [];
    private bool _loading;

    public OverlayPage()
    {
        InitializeComponent();
        _statusTimer = DispatcherQueue.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromSeconds(1);
        _statusTimer.Tick += (_, _) => UpdateCaptureStatus();
        Unloaded += OnUnloaded;
    }

    private OverlaySettings Overlay => _host!.Settings.Current.Overlay;

    private OverlayStyle Look => Overlay.Style;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _host = ((PageContext)e.Parameter).Host;
        _overlay = App.Current.Overlay;
        _renderer = new OverlayRenderer();
        if (_overlay is not null)
        {
            _overlay.Changed += OnOverlayChanged;
        }

        _monitors = MonitorCatalog.Enumerate();
        Reload();
        _statusTimer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _statusTimer.Stop();
        if (_overlay is not null)
        {
            _overlay.Changed -= OnOverlayChanged;
        }

        _renderer?.Dispose();
        _renderer = null;
    }

    private void OnOverlayChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        _loading = true;
        EnabledSwitch.IsOn = Overlay.Enabled;
        _loading = false;
        UpdateCaptureStatus();
    });

    // Copia as configurações para os controles.
    private void Reload()
    {
        _loading = true;
        EnabledSwitch.IsOn = Overlay.Enabled;
        FillMonitors();
        AnchorBox.SelectedIndex = (int)Look.Anchor;
        LayoutBox.SelectedIndex = (int)Look.Layout;
        OffsetXBox.Value = Look.OffsetX;
        OffsetYBox.Value = Look.OffsetY;
        FillFonts();
        FontSizeBox.Value = Look.FontSize;
        SetColor(LabelColorPicker, LabelSwatch, Look.LabelColor);
        SetColor(ValueColorPicker, ValueSwatch, Look.ValueColor);
        SetColor(BackgroundColorPicker, BackgroundSwatch, Look.BackgroundColor);
        OpacitySlider.Value = Math.Round(Look.BackgroundOpacity * 100);
        RadiusBox.Value = Look.CornerRadius;
        PaddingBox.Value = Look.Padding;
        GraphSwitch.IsOn = Look.ShowFrameGraph;
        GraphWidthBox.Value = Look.GraphWidth;
        GraphHeightBox.Value = Look.GraphHeight;
        GraphWidthBox.IsEnabled = GraphHeightBox.IsEnabled = Look.ShowFrameGraph;
        RefreshBox.SelectedIndex = Math.Max(0, OverlayStyle.AllowedRefreshHz.ToList().IndexOf(Look.RefreshHz));
        FillPresets();
        RebuildItems();
        _loading = false;
        UpdateCaptureStatus();
        RefreshPreview();
    }

    // Grava, aplica no overlay real e atualiza preview e seleção de preset.
    private void Commit()
    {
        _host!.Settings.RequestSave();
        _overlay?.Apply();
        var wasLoading = _loading;
        _loading = true;
        FillPresets();
        _loading = wasLoading;
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (_renderer is null || _host is null)
        {
            return;
        }

        var settings = _host.Settings.Current;
        var scale = (float)(XamlRoot?.RasterizationScale ?? 1.0);
        var frame = OverlayFrameBuilder.Build(Look, _demo, settings.ToUnitOptions(), settings.Thresholds);
        var bitmap = _renderer.Render(frame, Look, scale);

        if (_bitmap is null || _bitmap.PixelWidth != bitmap.Width || _bitmap.PixelHeight != bitmap.Height)
        {
            _bitmap = new WriteableBitmap(bitmap.Width, bitmap.Height);
        }

        using (var stream = _bitmap.PixelBuffer.AsStream())
        {
            stream.Position = 0;
            stream.Write(bitmap.Pixels);
        }

        _bitmap.Invalidate();
        PreviewImage.Source = _bitmap;
        PreviewImage.Width = bitmap.Width / scale;
        PreviewImage.Height = bitmap.Height / scale;
    }

    private void UpdateCaptureStatus()
    {
        var frames = _host?.Frames;
        var failed = Overlay.Enabled && frames is { State: FrameCaptureState.Failed };
        CaptureBar.IsOpen = failed;
        if (failed)
        {
            CaptureBar.Message = $"{frames!.FailureReason} FPS shows N/A and Supermonitor retries every 30 seconds.";
        }
    }

    private void OnEnabledToggled(object sender, RoutedEventArgs e)
    {
        if (_loading || _overlay is null)
        {
            return;
        }

        _loading = true;
        _overlay.SetEnabled(EnabledSwitch.IsOn);
        _loading = false;
        UpdateCaptureStatus();
    }

    // ----- presets -----

    private void FillPresets()
    {
        var names = OverlayEditor.AllPresets(Overlay).Select(p => p.Name).ToList();
        var matching = OverlayEditor.MatchingPresetName(Overlay);
        if (matching is null)
        {
            names.Insert(0, CustomPreset);
        }

        PresetBox.ItemsSource = names;
        PresetBox.SelectedItem = matching ?? CustomPreset;
        DeletePresetButton.IsEnabled = matching is not null && Overlay.UserPresets.Any(p => p.Name == matching);
    }

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || PresetBox.SelectedItem is not string name || name == CustomPreset)
        {
            return;
        }

        var preset = OverlayEditor.AllPresets(Overlay).FirstOrDefault(p => p.Name == name);
        if (preset is null)
        {
            return;
        }

        OverlayEditor.ApplyPreset(Overlay, preset);
        _host!.Settings.RequestSave();
        _overlay?.Apply();
        Reload();
    }

    private async void OnSavePresetClick(object sender, RoutedEventArgs e)
    {
        var name = new TextBox { Header = "Preset name", MaxLength = 40 };
        var error = new TextBlock
        {
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
        };
        var dialog = new ContentDialog
        {
            Title = "Save preset",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            Content = new StackPanel { Spacing = 8, Children = { name, error } },
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!OverlayEditor.SavePreset(Overlay, name.Text))
            {
                args.Cancel = true;
                error.Text = "Choose a name that is not empty and is not Minimal, Default or Full.";
                error.Visibility = Visibility.Visible;
            }
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            Commit();
        }
    }

    private void OnDeletePresetClick(object sender, RoutedEventArgs e)
    {
        if (PresetBox.SelectedItem is string name && OverlayEditor.DeletePreset(Overlay, name))
        {
            Commit();
        }
    }

    // ----- posição -----

    private void FillMonitors()
    {
        MonitorBox.Items.Clear();
        MonitorBox.Items.Add(new ComboBoxItem { Content = "Primary display", Tag = null });
        var selected = 0;
        foreach (var monitor in _monitors)
        {
            MonitorBox.Items.Add(new ComboBoxItem { Content = monitor.Name, Tag = monitor.Id });
            if (monitor.Id == Overlay.MonitorId)
            {
                selected = MonitorBox.Items.Count - 1;
            }
        }

        MonitorBox.SelectedIndex = selected;
    }

    private void OnMonitorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || MonitorBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        Overlay.MonitorId = item.Tag as string;
        Commit();
    }

    private void OnAnchorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || AnchorBox.SelectedIndex < 0)
        {
            return;
        }

        Look.Anchor = (OverlayAnchor)AnchorBox.SelectedIndex;
        Commit();
    }

    private void OnLayoutChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LayoutBox.SelectedIndex < 0)
        {
            return;
        }

        Look.Layout = (OverlayLayout)LayoutBox.SelectedIndex;
        Commit();
    }

    private void OnNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(sender.Value))
        {
            return;
        }

        var whole = (int)Math.Round(sender.Value);
        switch (sender.Tag as string)
        {
            case "offsetX":
                Look.OffsetX = whole;
                break;
            case "offsetY":
                Look.OffsetY = whole;
                break;
            case "fontSize":
                Look.FontSize = (float)sender.Value;
                break;
            case "radius":
                Look.CornerRadius = whole;
                break;
            case "padding":
                Look.Padding = whole;
                break;
            case "graphWidth":
                Look.GraphWidth = whole;
                break;
            case "graphHeight":
                Look.GraphHeight = whole;
                break;
            default:
                return;
        }

        Commit();
    }

    // ----- métricas -----

    private void RebuildItems()
    {
        ItemsPanel.Children.Clear();
        foreach (var item in Look.Items)
        {
            var row = new Grid { ColumnSpacing = 4 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < 4; i++)
            {
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }

            row.Children.Add(new TextBlock { Text = OverlayMetricCatalog.Label(item.Metric), VerticalAlignment = VerticalAlignment.Center });
            AddRowButton(row, 1, "", "Move up", item.Metric, OnItemUp);
            AddRowButton(row, 2, "", "Move down", item.Metric, OnItemDown);
            AddRowButton(row, 3, "", "Colors and thresholds", item.Metric, OnItemEdit);
            AddRowButton(row, 4, "", "Remove", item.Metric, OnItemRemove);
            ItemsPanel.Children.Add(row);
        }
    }

    private static void AddRowButton(Grid row, int column, string glyph, string tooltip, OverlayMetric metric, RoutedEventHandler handler)
    {
        var button = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 12 }, Tag = metric, Padding = new Thickness(8, 4, 8, 4) };
        ToolTipService.SetToolTip(button, tooltip);
        button.Click += handler;
        Grid.SetColumn(button, column);
        row.Children.Add(button);
    }

    private void OnItemUp(object sender, RoutedEventArgs e) => MoveItem(sender, -1);

    private void OnItemDown(object sender, RoutedEventArgs e) => MoveItem(sender, 1);

    private void MoveItem(object sender, int delta)
    {
        if (sender is FrameworkElement { Tag: OverlayMetric metric })
        {
            OverlayEditor.MoveItem(Look, metric, delta);
            RebuildItems();
            Commit();
        }
    }

    private void OnItemRemove(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: OverlayMetric metric } && OverlayEditor.RemoveItem(Look, metric))
        {
            RebuildItems();
            Commit();
        }
    }

    private async void OnItemEdit(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: OverlayMetric metric })
        {
            return;
        }

        var index = Look.Items.FindIndex(i => i.Metric == metric);
        if (index < 0)
        {
            return;
        }

        var dialog = new OverlayItemDialog(Look.Items[index]) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            Look.Items[index] = dialog.Result;
            Commit();
        }
    }

    private void OnAddItemMenuOpening(object sender, object e)
    {
        AddItemFlyout.Items.Clear();
        var used = Look.Items.Select(i => i.Metric).ToHashSet();
        foreach (var metric in Enum.GetValues<OverlayMetric>().Where(m => !used.Contains(m)))
        {
            var entry = new MenuFlyoutItem { Text = OverlayMetricCatalog.Label(metric), Tag = metric };
            entry.Click += OnAddItem;
            AddItemFlyout.Items.Add(entry);
        }

        if (AddItemFlyout.Items.Count == 0)
        {
            AddItemFlyout.Items.Add(new MenuFlyoutItem { Text = "All metrics are shown", IsEnabled = false });
        }
    }

    private void OnAddItem(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: OverlayMetric metric } && OverlayEditor.AddItem(Look, metric))
        {
            RebuildItems();
            Commit();
        }
    }

    // ----- texto, cores e fundo -----

    private void FillFonts()
    {
        FontBox.ItemsSource = Fonts;
        FontBox.SelectedItem = Fonts.FirstOrDefault(f => string.Equals(f, Look.FontFamily, StringComparison.OrdinalIgnoreCase));
        FontBox.Text = Look.FontFamily;
    }

    private void OnFontChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || FontBox.SelectedItem is not string family)
        {
            return;
        }

        Look.FontFamily = family;
        Commit();
    }

    private void OnFontSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        args.Handled = true;
        if (_loading || string.IsNullOrWhiteSpace(args.Text))
        {
            return;
        }

        Look.FontFamily = args.Text.Trim();
        Commit();
    }

    private static void SetColor(ColorPicker picker, Border swatch, string hex)
    {
        var color = ToColor(Rgba.Parse(hex, new Rgba(0xFF, 0xFF, 0xFF)));
        picker.Color = color;
        swatch.Background = new SolidColorBrush(color);
    }

    private static Color ToColor(Rgba rgba) => Color.FromArgb(0xFF, rgba.R, rgba.G, rgba.B);

    private void OnColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_loading)
        {
            return;
        }

        var hex = ColorText.ToHex(args.NewColor.R, args.NewColor.G, args.NewColor.B);
        switch (sender.Tag as string)
        {
            case "label":
                Look.LabelColor = hex;
                LabelSwatch.Background = new SolidColorBrush(args.NewColor);
                break;
            case "value":
                Look.ValueColor = hex;
                ValueSwatch.Background = new SolidColorBrush(args.NewColor);
                break;
            case "background":
                Look.BackgroundColor = hex;
                BackgroundSwatch.Background = new SolidColorBrush(args.NewColor);
                break;
            default:
                return;
        }

        Commit();
    }

    private void OnOpacityChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        Look.BackgroundOpacity = (float)(e.NewValue / 100d);
        Commit();
    }

    private void OnGraphToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        Look.ShowFrameGraph = GraphSwitch.IsOn;
        GraphWidthBox.IsEnabled = GraphHeightBox.IsEnabled = GraphSwitch.IsOn;
        Commit();
    }

    private void OnRefreshChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || RefreshBox.SelectedIndex < 0)
        {
            return;
        }

        Look.RefreshHz = OverlayStyle.AllowedRefreshHz[RefreshBox.SelectedIndex];
        Commit();
    }
}
