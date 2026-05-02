using System;
using Avalonia;
using Sufni.App.Plots;
using Sufni.Telemetry;

namespace Sufni.App.DesktopViews.Plots;

public enum PlotKind
{
    TravelHistogram,
    TravelFrequencyHistogram,
    VelocityHistogram,
    Balance,
    StrokeLengthHistogram,
    StrokeSpeedHistogram,
    DeepTravelHistogram,
    VibrationThirds,
}

public class SessionStatisticsPlotView : SufniTelemetryPlotView
{
    public static readonly StyledProperty<PlotKind> PlotKindProperty =
        AvaloniaProperty.Register<SessionStatisticsPlotView, PlotKind>(nameof(PlotKind));

    public static readonly StyledProperty<SuspensionType> SuspensionTypeProperty =
        AvaloniaProperty.Register<SessionStatisticsPlotView, SuspensionType>(nameof(SuspensionType));

    public static readonly StyledProperty<BalanceType> BalanceTypeProperty =
        AvaloniaProperty.Register<SessionStatisticsPlotView, BalanceType>(nameof(BalanceType));

    public static readonly StyledProperty<ImuLocation> ImuLocationProperty =
        AvaloniaProperty.Register<SessionStatisticsPlotView, ImuLocation>(nameof(ImuLocation));

    public static readonly StyledProperty<TravelHistogramMode> TravelHistogramModeProperty =
        AvaloniaProperty.Register<SessionStatisticsPlotView, TravelHistogramMode>(
            nameof(TravelHistogramMode),
            TravelHistogramMode.ActiveSuspension);

    public static readonly StyledProperty<BalanceDisplacementMode> BalanceDisplacementModeProperty =
        AvaloniaProperty.Register<SessionStatisticsPlotView, BalanceDisplacementMode>(
            nameof(BalanceDisplacementMode),
            BalanceDisplacementMode.Zenith);

    public static readonly StyledProperty<VelocityAverageMode> VelocityAverageModeProperty =
        AvaloniaProperty.Register<SessionStatisticsPlotView, VelocityAverageMode>(
            nameof(VelocityAverageMode),
            VelocityAverageMode.SampleAveraged);

    public PlotKind PlotKind
    {
        get => GetValue(PlotKindProperty);
        set => SetValue(PlotKindProperty, value);
    }

    public SuspensionType SuspensionType
    {
        get => GetValue(SuspensionTypeProperty);
        set => SetValue(SuspensionTypeProperty, value);
    }

    public BalanceType BalanceType
    {
        get => GetValue(BalanceTypeProperty);
        set => SetValue(BalanceTypeProperty, value);
    }

    public ImuLocation ImuLocation
    {
        get => GetValue(ImuLocationProperty);
        set => SetValue(ImuLocationProperty, value);
    }

    public TravelHistogramMode TravelHistogramMode
    {
        get => GetValue(TravelHistogramModeProperty);
        set => SetValue(TravelHistogramModeProperty, value);
    }

    public BalanceDisplacementMode BalanceDisplacementMode
    {
        get => GetValue(BalanceDisplacementModeProperty);
        set => SetValue(BalanceDisplacementModeProperty, value);
    }

    public VelocityAverageMode VelocityAverageMode
    {
        get => GetValue(VelocityAverageModeProperty);
        set => SetValue(VelocityAverageModeProperty, value);
    }

    public SessionStatisticsPlotView()
    {
        PropertyChanged += (_, e) =>
        {
            if (e.Property.Name is nameof(TravelHistogramMode) && PlotKind == PlotKind.TravelHistogram ||
                e.Property.Name is nameof(BalanceDisplacementMode) && PlotKind == PlotKind.Balance ||
                e.Property.Name is nameof(VelocityAverageMode) && PlotKind == PlotKind.VelocityHistogram)
            {
                if (!HasPlotModel)
                {
                    return;
                }

                ApplyModeToPlotModel(PlotModel);
                ReloadTelemetry();
            }
        };
    }

    protected override void CreatePlot()
    {
        TelemetryPlot plotModel = PlotKind switch
        {
            PlotKind.TravelHistogram => new TravelHistogramPlot(PlotControl.Plot, SuspensionType),
            PlotKind.TravelFrequencyHistogram => new TravelFrequencyHistogramPlot(PlotControl.Plot, SuspensionType),
            PlotKind.VelocityHistogram => new VelocityHistogramPlot(PlotControl.Plot, SuspensionType),
            PlotKind.Balance => new BalancePlot(PlotControl.Plot, BalanceType),
            PlotKind.StrokeLengthHistogram => new StrokeLengthHistogramPlot(PlotControl.Plot, SuspensionType, BalanceType),
            PlotKind.StrokeSpeedHistogram => new StrokeSpeedHistogramPlot(PlotControl.Plot, SuspensionType, BalanceType),
            PlotKind.DeepTravelHistogram => new DeepTravelHistogramPlot(PlotControl.Plot, SuspensionType),
            PlotKind.VibrationThirds => new VibrationThirdsPlot(PlotControl.Plot, SuspensionType, ImuLocation),
            _ => throw new ArgumentOutOfRangeException()
        };

        ApplyModeToPlotModel(plotModel);
        SetPlotModel(plotModel);
    }

    private void ApplyModeToPlotModel(TelemetryPlot plotModel)
    {
        switch (plotModel)
        {
            case TravelHistogramPlot travelHistogram:
                travelHistogram.HistogramMode = TravelHistogramMode;
                break;
            case BalancePlot balance:
                balance.DisplacementMode = BalanceDisplacementMode;
                break;
            case VelocityHistogramPlot velocityHistogram:
                velocityHistogram.AverageMode = VelocityAverageMode;
                break;
        }
    }
}