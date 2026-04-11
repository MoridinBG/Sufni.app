using System.Linq;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Plots;
using Sufni.App.Tests.Infrastructure;
using Sufni.Telemetry;
using static Sufni.App.Tests.Infrastructure.TestTelemetryFactories;

namespace Sufni.App.Tests.Plots;

public class ImuPlotTests
{
    [Fact]
    public void LoadTelemetryData_AddsOneSignalPerActiveLocationWithMetadata()
    {
        var plot = new Plot();
        var sut = new ImuPlot(plot);

        sut.LoadTelemetryData(CreateTelemetryDataWithImu());

        Assert.NotNull(sut.CursorLine);
        Assert.Equal("IMU Acceleration (g)", plot.Axes.Title.Label.Text);
        Assert.Equal(2, plot.PlottableList.OfType<Signal>().Count());
        Assert.Equal(2, plot.Axes.Rules.Count);
    }

    [Fact]
    public void LoadTelemetryData_SkipsLocationsWithoutMetadata()
    {
        var plot = new Plot();
        var sut = new ImuPlot(plot);

        var telemetry = CreateTelemetryDataWithImu(
            activeLocations: [0, 1],
            meta: [new ImuMetaEntry(0, 1.0f, 1.0f)],
            records:
            [
                new ImuRecord(1, 0, 1, 0, 0, 0),
                new ImuRecord(2, 0, 1, 0, 0, 0),
                new ImuRecord(3, 0, 1, 0, 0, 0),
                new ImuRecord(4, 0, 1, 0, 0, 0)
            ]);

        sut.LoadTelemetryData(telemetry);

        Assert.Single(plot.PlottableList.OfType<Signal>());
    }
}