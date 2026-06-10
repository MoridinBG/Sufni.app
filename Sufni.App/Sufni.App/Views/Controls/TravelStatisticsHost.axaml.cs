using Avalonia;
using Avalonia.Controls;
using Sufni.App.ExtensionHost.RecordedSessions;
using Sufni.Telemetry;

namespace Sufni.App.Views.Controls;

public partial class TravelStatisticsHost : StatisticsHostBase
{
    public static readonly StyledProperty<TravelHistogramMode> TravelHistogramModeProperty =
        AvaloniaProperty.Register<TravelStatisticsHost, TravelHistogramMode>(nameof(TravelHistogramMode));

    public static readonly StyledProperty<bool> HasDynamicStatisticsProperty =
        AvaloniaProperty.Register<TravelStatisticsHost, bool>(nameof(HasDynamicStatistics), true);

    public static readonly StyledProperty<bool> ShowFrequencyHistogramProperty =
        AvaloniaProperty.Register<TravelStatisticsHost, bool>(nameof(ShowFrequencyHistogram));

    public static readonly StyledProperty<string?> StaticSourceProperty =
        AvaloniaProperty.Register<TravelStatisticsHost, string?>(nameof(StaticSource));

    public static readonly StyledProperty<double> PlotHeightProperty =
        AvaloniaProperty.Register<TravelStatisticsHost, double>(nameof(PlotHeight), double.NaN);

    public static readonly StyledProperty<Thickness> PlaceholderMarginProperty =
        AvaloniaProperty.Register<TravelStatisticsHost, Thickness>(nameof(PlaceholderMargin));

    public static readonly StyledProperty<object?> HeaderContentProperty =
        AvaloniaProperty.Register<TravelStatisticsHost, object?>(nameof(HeaderContent));

    public static readonly StyledProperty<RecordedSessionExtensionSlots?> ExtensionSlotsProperty =
        AvaloniaProperty.Register<TravelStatisticsHost, RecordedSessionExtensionSlots?>(
            nameof(ExtensionSlots));

    public TravelHistogramMode TravelHistogramMode
    {
        get => GetValue(TravelHistogramModeProperty);
        set => SetValue(TravelHistogramModeProperty, value);
    }

    public bool HasDynamicStatistics
    {
        get => GetValue(HasDynamicStatisticsProperty);
        set => SetValue(HasDynamicStatisticsProperty, value);
    }

    public bool ShowFrequencyHistogram
    {
        get => GetValue(ShowFrequencyHistogramProperty);
        set => SetValue(ShowFrequencyHistogramProperty, value);
    }

    public string? StaticSource
    {
        get => GetValue(StaticSourceProperty);
        set => SetValue(StaticSourceProperty, value);
    }

    public double PlotHeight
    {
        get => GetValue(PlotHeightProperty);
        set => SetValue(PlotHeightProperty, value);
    }

    public Thickness PlaceholderMargin
    {
        get => GetValue(PlaceholderMarginProperty);
        set => SetValue(PlaceholderMarginProperty, value);
    }

    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    public RecordedSessionExtensionSlots? ExtensionSlots
    {
        get => GetValue(ExtensionSlotsProperty);
        set => SetValue(ExtensionSlotsProperty, value);
    }

    public TravelStatisticsHost()
    {
        InitializeComponent();
    }
}
