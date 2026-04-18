using System.Diagnostics;
using Avalonia;
using Sufni.App.Plots;
using Sufni.Telemetry;

namespace Sufni.App.DesktopViews.Plots;

public class VelocityHistogramDesktopView : SufniTelemetryPlotView
{
    public static readonly StyledProperty<SuspensionType> SuspensionTypeProperty =
        AvaloniaProperty.Register<VelocityHistogramDesktopView, SuspensionType>("SuspensionType");

    public SuspensionType SuspensionType
    {
        get => GetValue(SuspensionTypeProperty);
        set => SetValue(SuspensionTypeProperty, value);
    }

    protected override void CreatePlot()
    {
        SetPlotModel(new VelocityHistogramPlot(PlotControl.Plot, SuspensionType));
    }
}