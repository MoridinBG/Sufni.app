using Sufni.App.LiveDaq.Plots;
using Sufni.App.LiveDaq.Services.Imu;
using Sufni.App.Shared.Plots;
using Sufni.Telemetry;

namespace Sufni.App.LiveDaq.Views.Plots;

public class FramePitchRollPlotView : RecordedImuPlotViewBase
{
    protected override void CreatePlot()
    {
        SetPlotModel(new FramePitchRollPlot(PlotControl.Plot, CurrentTheme));
        InitializeCursorReadoutInteractions();
    }

    protected override void LoadProjection(
        TelemetryPlot plotModel,
        TelemetryData telemetry,
        RecordedImuDisplaySeries projection)
    {
        ((FramePitchRollPlot)plotModel).LoadProjection(telemetry, projection);
    }
}
