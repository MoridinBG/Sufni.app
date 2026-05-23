using System.Collections.Generic;
using System.Linq;
using ScottPlot;
using Sufni.App.Plots;
using Sufni.App.Services.LiveStreaming;

namespace Sufni.App.Tests.Plots;

public class LiveTravelPlotTests
{
    [Fact]
    public void Append_AppliesFloorPaddingAndKeepsRunningMaximum()
    {
        var plot = new Plot();
        var sut = new LiveTravelPlot(plot, travelMaximum: 120, hideRightAxis: false);

        AssertYRange(plot, expectedMinimum: 0, expectedMaximum: 5);

        sut.Append(CreateTravelBatch([1, 4]));
        AssertYRange(plot, expectedMinimum: 0, expectedMaximum: 5);

        sut.Append(CreateTravelBatch([10]));
        AssertYRange(plot, expectedMinimum: 0, expectedMaximum: 11);

        sut.Append(CreateTravelBatch([6]));
        AssertYRange(plot, expectedMinimum: 0, expectedMaximum: 11);
    }

    [Fact]
    public void Reset_RestoresFloorLimitAfterRunningMaximum()
    {
        var plot = new Plot();
        var sut = new LiveTravelPlot(plot, travelMaximum: 120, hideRightAxis: false);

        sut.Append(CreateTravelBatch([20]));
        AssertYRange(plot, expectedMinimum: 0, expectedMaximum: 22);

        sut.Reset();

        Assert.False(sut.HasTiming);
        AssertYRange(plot, expectedMinimum: 0, expectedMaximum: 5);
    }

    private static LiveGraphBatch CreateTravelBatch(IReadOnlyList<double> frontTravel)
    {
        var times = Enumerable.Range(0, frontTravel.Count)
            .Select(index => index * 0.01)
            .ToArray();

        return LiveGraphBatch.Empty with
        {
            Revision = 1,
            TravelTimes = times,
            FrontTravel = frontTravel,
        };
    }

    private static void AssertYRange(Plot plot, double expectedMinimum, double expectedMaximum)
    {
        Assert.Equal(expectedMinimum, plot.Axes.Left.Min, precision: 6);
        Assert.Equal(expectedMaximum, plot.Axes.Left.Max, precision: 6);
        Assert.Equal(expectedMinimum, plot.Axes.Right.Min, precision: 6);
        Assert.Equal(expectedMaximum, plot.Axes.Right.Max, precision: 6);
    }
}
