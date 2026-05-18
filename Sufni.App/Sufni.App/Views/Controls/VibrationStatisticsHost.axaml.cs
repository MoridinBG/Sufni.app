using Avalonia;
using Avalonia.Controls;
using Sufni.App.Presentation;
using Sufni.Telemetry;

namespace Sufni.App.Views.Controls;

public partial class VibrationStatisticsHost : UserControl
{
    public static readonly StyledProperty<string?> HostNameProperty =
        AvaloniaProperty.Register<VibrationStatisticsHost, string?>(nameof(HostName));

    public static readonly StyledProperty<SurfacePresentationState> PresentationStateProperty =
        AvaloniaProperty.Register<VibrationStatisticsHost, SurfacePresentationState>(
            nameof(PresentationState),
            SurfacePresentationState.Hidden);

    public static readonly StyledProperty<TelemetryTimeRange?> AnalysisRangeProperty =
        AvaloniaProperty.Register<VibrationStatisticsHost, TelemetryTimeRange?>(nameof(AnalysisRange));

    public static readonly StyledProperty<TelemetryData?> TelemetryProperty =
        AvaloniaProperty.Register<VibrationStatisticsHost, TelemetryData?>(nameof(Telemetry));

    public static readonly StyledProperty<SuspensionType> SuspensionTypeProperty =
        AvaloniaProperty.Register<VibrationStatisticsHost, SuspensionType>(nameof(SuspensionType));

    public static readonly StyledProperty<ImuLocation> ImuLocationProperty =
        AvaloniaProperty.Register<VibrationStatisticsHost, ImuLocation>(nameof(ImuLocation));

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<VibrationStatisticsHost, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<double> MinCardHeightProperty =
        AvaloniaProperty.Register<VibrationStatisticsHost, double>(nameof(MinCardHeight));

    public static readonly StyledProperty<GridLength> PlotRowHeightProperty =
        AvaloniaProperty.Register<VibrationStatisticsHost, GridLength>(
            nameof(PlotRowHeight),
            new GridLength(1, GridUnitType.Star));

    public string? HostName
    {
        get => GetValue(HostNameProperty);
        set => SetValue(HostNameProperty, value);
    }

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

    public ImuLocation ImuLocation
    {
        get => GetValue(ImuLocationProperty);
        set => SetValue(ImuLocationProperty, value);
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

    public GridLength PlotRowHeight
    {
        get => GetValue(PlotRowHeightProperty);
        set => SetValue(PlotRowHeightProperty, value);
    }

    public VibrationStatisticsHost()
    {
        InitializeComponent();
    }
}
