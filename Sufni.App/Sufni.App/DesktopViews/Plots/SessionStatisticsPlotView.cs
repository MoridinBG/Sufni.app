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
}

public class SessionStatisticsPlotView : SufniTelemetryPlotView
{
    public static readonly StyledProperty<PlotKind> PlotKindProperty =
        AvaloniaProperty.Register<SessionStatisticsPlotView, PlotKind>(nameof(PlotKind));

    public static readonly StyledProperty<SuspensionType> SuspensionTypeProperty =
        AvaloniaProperty.Register<SessionStatisticsPlotView, SuspensionType>(nameof(SuspensionType));

    public static readonly StyledProperty<BalanceType> BalanceTypeProperty =
        AvaloniaProperty.Register<SessionStatisticsPlotView, BalanceType>(nameof(BalanceType));

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

    protected override void CreatePlot()
    {
        SetPlotModel(PlotKind switch
        {
            PlotKind.TravelHistogram => new TravelHistogramPlot(PlotControl.Plot, SuspensionType),
            PlotKind.TravelFrequencyHistogram => new TravelFrequencyHistogramPlot(PlotControl.Plot, SuspensionType),
            PlotKind.VelocityHistogram => new VelocityHistogramPlot(PlotControl.Plot, SuspensionType),
            PlotKind.Balance => new BalancePlot(PlotControl.Plot, BalanceType),
            _ => throw new ArgumentOutOfRangeException()
        });
    }
}