using Sufni.App.Sessions.Analysis.Services;
using System.Linq;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.Telemetry;

using Sufni.App.Sessions.Plots;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Sessions.Plots;

public class BalancePlotTests
{
    [Fact]
    public void SetPointerPositionWithReadout_ShowsFrontAndRearBalanceValuesAtPointerX()
    {
        var telemetry = CreateTelemetryWithTwoBalanceSamplesPerSide();
        var plot = new Plot();
        var sut = new BalancePlot(plot, BalanceType.Compression);

        sut.LoadAnalysisData(RecordedSessionAnalysisComputer.CalculateBalance(
            PlotTestHelpers.CreateAnalysisKey(RecordedSessionAnalysisFamily.Balance, balanceMode: sut.DisplacementMode), telemetry));
        plot.GetSvgXml(500, 320);
        sut.SetPointerPositionWithReadout(7.5, 150);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        Assert.True(tooltip.IsVisible);
        Assert.Contains("Zenith: 7.5 %", tooltip.LabelText);
        Assert.Contains("Front peak speed: 150 mm/s", tooltip.LabelText);
        Assert.Contains("Rear peak speed: 125 mm/s", tooltip.LabelText);
    }

    [Fact]
    public void SetPointerPositionWithReadout_UsesStrokeTravelLabelInTravelMode()
    {
        var telemetry = CreateTelemetryWithTwoBalanceSamplesPerSide();
        var plot = new Plot();
        var sut = new BalancePlot(plot, BalanceType.Compression)
        {
            DisplacementMode = BalanceDisplacementMode.Travel,
        };

        sut.LoadAnalysisData(RecordedSessionAnalysisComputer.CalculateBalance(
            PlotTestHelpers.CreateAnalysisKey(RecordedSessionAnalysisFamily.Balance, balanceMode: sut.DisplacementMode), telemetry));
        plot.GetSvgXml(500, 320);
        sut.SetPointerPositionWithReadout(7.5, 125);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        Assert.True(tooltip.IsVisible);
        Assert.Contains("Stroke travel: 7.5 %", tooltip.LabelText);
        Assert.Contains("Front peak speed: 150 mm/s", tooltip.LabelText);
        Assert.Contains("Rear peak speed: 125 mm/s", tooltip.LabelText);
    }

    [Fact]
    public void SetPointerPositionWithReadout_UsesNearestPointInSpeedMode()
    {
        var telemetry = CreateTelemetryWithTwoBalanceSamplesPerSide();
        var plot = new Plot();
        var sut = new BalancePlot(plot, BalanceType.Compression)
        {
            DisplacementMode = BalanceDisplacementMode.Speed,
        };

        sut.LoadAnalysisData(RecordedSessionAnalysisComputer.CalculateBalance(
            PlotTestHelpers.CreateAnalysisKey(RecordedSessionAnalysisFamily.Balance, balanceMode: sut.DisplacementMode), telemetry));
        plot.GetSvgXml(500, 320);
        sut.SetPointerPositionWithReadout(10, 200);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        Assert.True(tooltip.IsVisible);
        Assert.Contains("Peak speed position: 10 %", tooltip.LabelText);
        Assert.Contains("Front peak speed: 200 mm/s", tooltip.LabelText);
    }

    private static TelemetryData CreateTelemetryWithTwoBalanceSamplesPerSide()
    {
        var telemetry = TestTelemetryData.CreateProcessed(frontPresent: true, rearPresent: true);

        telemetry = telemetry with { Front = telemetry.Front with { Travel = [0, 10, 0, 20] } };
        telemetry = telemetry with { Front = telemetry.Front with { Velocity = [0, 100, 0, 200] } };
        telemetry = telemetry with { Front = telemetry.Front with { Strokes = new Strokes
        {
            Compressions =
            [
                CreateStroke(0, 1, maxTravel: 10, maxVelocity: 100),
                CreateStroke(2, 3, maxTravel: 20, maxVelocity: 200),
            ],
            Rebounds = [],
        } } };

        telemetry = telemetry with { Rear = telemetry.Rear with { Travel = [0, 10, 0, 20] } };
        telemetry = telemetry with { Rear = telemetry.Rear with { Velocity = [0, 100, 0, 150] } };
        telemetry = telemetry with { Rear = telemetry.Rear with { Strokes = new Strokes
        {
            Compressions =
            [
                CreateStroke(0, 1, maxTravel: 10, maxVelocity: 100),
                CreateStroke(2, 3, maxTravel: 20, maxVelocity: 150),
            ],
            Rebounds = [],
        } } };

        return telemetry;
    }

    private static Stroke CreateStroke(int start, int end, double maxTravel, double maxVelocity)
    {
        return new Stroke
        {
            Start = start,
            End = end,
            Stat = new StrokeStat
            {
                Count = end - start + 1,
                MaxTravel = maxTravel,
                MaxVelocity = maxVelocity,
            },
            DigitizedTravel = [0],
            DigitizedVelocity = [0],
        };
    }
}
