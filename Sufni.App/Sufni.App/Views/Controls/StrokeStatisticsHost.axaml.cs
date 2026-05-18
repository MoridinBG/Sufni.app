using Avalonia;
using Avalonia.Controls;
using Sufni.App.Presentation;
using Sufni.Telemetry;

namespace Sufni.App.Views.Controls;

public partial class StrokeStatisticsHost : UserControl
{
    public static readonly StyledProperty<SurfacePresentationState> PresentationStateProperty =
        AvaloniaProperty.Register<StrokeStatisticsHost, SurfacePresentationState>(
            nameof(PresentationState),
            SurfacePresentationState.Hidden);

    public static readonly StyledProperty<TelemetryTimeRange?> AnalysisRangeProperty =
        AvaloniaProperty.Register<StrokeStatisticsHost, TelemetryTimeRange?>(nameof(AnalysisRange));

    public static readonly StyledProperty<TelemetryData?> TelemetryProperty =
        AvaloniaProperty.Register<StrokeStatisticsHost, TelemetryData?>(nameof(Telemetry));

    public static readonly StyledProperty<SuspensionType> SuspensionTypeProperty =
        AvaloniaProperty.Register<StrokeStatisticsHost, SuspensionType>(nameof(SuspensionType));

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<StrokeStatisticsHost, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<double> MinCardHeightProperty =
        AvaloniaProperty.Register<StrokeStatisticsHost, double>(nameof(MinCardHeight));

    public static readonly StyledProperty<GridLength> CompressionLengthRowHeightProperty =
        AvaloniaProperty.Register<StrokeStatisticsHost, GridLength>(
            nameof(CompressionLengthRowHeight),
            new GridLength(220));

    public static readonly StyledProperty<GridLength> CompressionSpeedRowHeightProperty =
        AvaloniaProperty.Register<StrokeStatisticsHost, GridLength>(
            nameof(CompressionSpeedRowHeight),
            new GridLength(220));

    public static readonly StyledProperty<GridLength> ReboundLengthRowHeightProperty =
        AvaloniaProperty.Register<StrokeStatisticsHost, GridLength>(
            nameof(ReboundLengthRowHeight),
            new GridLength(220));

    public static readonly StyledProperty<GridLength> ReboundSpeedRowHeightProperty =
        AvaloniaProperty.Register<StrokeStatisticsHost, GridLength>(
            nameof(ReboundSpeedRowHeight),
            new GridLength(220));

    public static readonly StyledProperty<GridLength> DeepTravelRowHeightProperty =
        AvaloniaProperty.Register<StrokeStatisticsHost, GridLength>(
            nameof(DeepTravelRowHeight),
            new GridLength(180));

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

    public GridLength CompressionLengthRowHeight
    {
        get => GetValue(CompressionLengthRowHeightProperty);
        set => SetValue(CompressionLengthRowHeightProperty, value);
    }

    public GridLength CompressionSpeedRowHeight
    {
        get => GetValue(CompressionSpeedRowHeightProperty);
        set => SetValue(CompressionSpeedRowHeightProperty, value);
    }

    public GridLength ReboundLengthRowHeight
    {
        get => GetValue(ReboundLengthRowHeightProperty);
        set => SetValue(ReboundLengthRowHeightProperty, value);
    }

    public GridLength ReboundSpeedRowHeight
    {
        get => GetValue(ReboundSpeedRowHeightProperty);
        set => SetValue(ReboundSpeedRowHeightProperty, value);
    }

    public GridLength DeepTravelRowHeight
    {
        get => GetValue(DeepTravelRowHeightProperty);
        set => SetValue(DeepTravelRowHeightProperty, value);
    }

    public StrokeStatisticsHost()
    {
        InitializeComponent();
    }
}
