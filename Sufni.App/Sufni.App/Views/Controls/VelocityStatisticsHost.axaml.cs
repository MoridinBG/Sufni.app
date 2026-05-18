using Avalonia;
using Avalonia.Controls;
using Sufni.App.Presentation;
using Sufni.Telemetry;

namespace Sufni.App.Views.Controls;

public partial class VelocityStatisticsHost : UserControl
{
    public static readonly StyledProperty<SurfacePresentationState> PresentationStateProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, SurfacePresentationState>(
            nameof(PresentationState),
            SurfacePresentationState.Hidden);

    public static readonly StyledProperty<TelemetryTimeRange?> AnalysisRangeProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, TelemetryTimeRange?>(nameof(AnalysisRange));

    public static readonly StyledProperty<TelemetryData?> TelemetryProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, TelemetryData?>(nameof(Telemetry));

    public static readonly StyledProperty<SuspensionType> SuspensionTypeProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, SuspensionType>(nameof(SuspensionType));

    public static readonly StyledProperty<VelocityAverageMode> VelocityAverageModeProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, VelocityAverageMode>(nameof(VelocityAverageMode));

    public static readonly StyledProperty<bool> HasDynamicStatisticsProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, bool>(nameof(HasDynamicStatistics), true);

    public static readonly StyledProperty<bool> ShowTravelLegendProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, bool>(nameof(ShowTravelLegend));

    public static readonly StyledProperty<string?> StaticSourceProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, string?>(nameof(StaticSource));

    public static readonly StyledProperty<double?> HscPercentageProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, double?>(nameof(HscPercentage));

    public static readonly StyledProperty<double?> HsrPercentageProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, double?>(nameof(HsrPercentage));

    public static readonly StyledProperty<double?> LscPercentageProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, double?>(nameof(LscPercentage));

    public static readonly StyledProperty<double?> LsrPercentageProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, double?>(nameof(LsrPercentage));

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<double> MinCardHeightProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, double>(nameof(MinCardHeight));

    public static readonly StyledProperty<double> PlotHeightProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, double>(nameof(PlotHeight), double.NaN);

    public static readonly StyledProperty<Thickness> PlaceholderMarginProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, Thickness>(nameof(PlaceholderMargin));

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

    public VelocityAverageMode VelocityAverageMode
    {
        get => GetValue(VelocityAverageModeProperty);
        set => SetValue(VelocityAverageModeProperty, value);
    }

    public bool HasDynamicStatistics
    {
        get => GetValue(HasDynamicStatisticsProperty);
        set => SetValue(HasDynamicStatisticsProperty, value);
    }

    public bool ShowTravelLegend
    {
        get => GetValue(ShowTravelLegendProperty);
        set => SetValue(ShowTravelLegendProperty, value);
    }

    public string? StaticSource
    {
        get => GetValue(StaticSourceProperty);
        set => SetValue(StaticSourceProperty, value);
    }

    public double? HscPercentage
    {
        get => GetValue(HscPercentageProperty);
        set => SetValue(HscPercentageProperty, value);
    }

    public double? HsrPercentage
    {
        get => GetValue(HsrPercentageProperty);
        set => SetValue(HsrPercentageProperty, value);
    }

    public double? LscPercentage
    {
        get => GetValue(LscPercentageProperty);
        set => SetValue(LscPercentageProperty, value);
    }

    public double? LsrPercentage
    {
        get => GetValue(LsrPercentageProperty);
        set => SetValue(LsrPercentageProperty, value);
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

    public VelocityStatisticsHost()
    {
        InitializeComponent();
    }
}
