using Avalonia;
using Avalonia.Controls;
using Sufni.App.Presentation;
using Sufni.Telemetry;

namespace Sufni.App.Views.Controls;

public partial class TravelStatisticsHost : UserControl
{
    public static readonly StyledProperty<SurfacePresentationState> PresentationStateProperty =
        AvaloniaProperty.Register<TravelStatisticsHost, SurfacePresentationState>(
            nameof(PresentationState),
            SurfacePresentationState.Hidden);

    public static readonly StyledProperty<TelemetryTimeRange?> AnalysisRangeProperty =
        AvaloniaProperty.Register<TravelStatisticsHost, TelemetryTimeRange?>(nameof(AnalysisRange));

    public static readonly StyledProperty<TelemetryData?> TelemetryProperty =
        AvaloniaProperty.Register<TravelStatisticsHost, TelemetryData?>(nameof(Telemetry));

    public static readonly StyledProperty<SuspensionType> SuspensionTypeProperty =
        AvaloniaProperty.Register<TravelStatisticsHost, SuspensionType>(nameof(SuspensionType));

    public static readonly StyledProperty<TravelHistogramMode> TravelHistogramModeProperty =
        AvaloniaProperty.Register<TravelStatisticsHost, TravelHistogramMode>(nameof(TravelHistogramMode));

    public static readonly StyledProperty<bool> HasDynamicStatisticsProperty =
        AvaloniaProperty.Register<TravelStatisticsHost, bool>(nameof(HasDynamicStatistics), true);

    public static readonly StyledProperty<bool> ShowFrequencyHistogramProperty =
        AvaloniaProperty.Register<TravelStatisticsHost, bool>(nameof(ShowFrequencyHistogram));

    public static readonly StyledProperty<string?> StaticSourceProperty =
        AvaloniaProperty.Register<TravelStatisticsHost, string?>(nameof(StaticSource));

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<TravelStatisticsHost, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<double> MinCardHeightProperty =
        AvaloniaProperty.Register<TravelStatisticsHost, double>(nameof(MinCardHeight));

    public static readonly StyledProperty<double> PlotHeightProperty =
        AvaloniaProperty.Register<TravelStatisticsHost, double>(nameof(PlotHeight), double.NaN);

    public static readonly StyledProperty<Thickness> PlaceholderMarginProperty =
        AvaloniaProperty.Register<TravelStatisticsHost, Thickness>(nameof(PlaceholderMargin));

    public SurfacePresentationState PresentationState
    {
        get => GetValue(PresentationStateProperty);
        set => SetValue(PresentationStateProperty, value);
    }

    public TelemetryTimeRange? AnalysisRange
    {
        get => GetValue(AnalysisRangeProperty);
        set => SetValue(AnalysisRangeProperty, value);
    }

    public TelemetryData? Telemetry
    {
        get => GetValue(TelemetryProperty);
        set => SetValue(TelemetryProperty, value);
    }

    public SuspensionType SuspensionType
    {
        get => GetValue(SuspensionTypeProperty);
        set => SetValue(SuspensionTypeProperty, value);
    }

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

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public double MinCardHeight
    {
        get => GetValue(MinCardHeightProperty);
        set => SetValue(MinCardHeightProperty, value);
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

    public TravelStatisticsHost()
    {
        InitializeComponent();
    }
}
