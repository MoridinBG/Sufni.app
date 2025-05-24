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
        Debug.Assert(AvaPlot != null, nameof(AvaPlot) + " != null");
        Plot = new VelocityHistogramPlot(AvaPlot.Plot, SuspensionType);
    }
}