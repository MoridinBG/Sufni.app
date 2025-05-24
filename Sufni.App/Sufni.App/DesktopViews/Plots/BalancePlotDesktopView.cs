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
        Debug.Assert(AvaPlot != null, nameof(AvaPlot) + " != null");
        Plot = new BalancePlot(AvaPlot.Plot, BalanceType);
    }
}