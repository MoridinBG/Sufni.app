using System.Diagnostics;
using Avalonia;
using Sufni.App.Plots;
using Sufni.Telemetry;

namespace Sufni.App.DesktopViews.Plots;

public class BalancePlotDesktopView : SufniTelemetryPlotView
{
    public static readonly StyledProperty<BalanceType> BalanceTypeProperty =
        AvaloniaProperty.Register<BalancePlotDesktopView, BalanceType>("BalanceType");

    public BalanceType BalanceType
    {
        get => GetValue(BalanceTypeProperty);
        set => SetValue(BalanceTypeProperty, value);
    }

    protected override void CreatePlot()
    {
        SetPlotModel(new BalancePlot(PlotControl.Plot, BalanceType));
    }
}