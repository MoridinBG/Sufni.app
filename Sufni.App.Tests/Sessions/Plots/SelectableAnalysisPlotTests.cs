using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Sessions.Analysis.Services;
using ScottPlot;
using Sufni.Telemetry;

using Sufni.App.Infrastructure.Theming;
using Sufni.App.Sessions.Plots;
namespace Sufni.App.Tests.Sessions.Plots;

public class SelectableAnalysisPlotTests
{
    [Fact]
    public void StrokeLengthDistribution_SelectedPrimaryBarReturnsStrokeLengthSelection()
    {
        var telemetry = CreateStrokeTelemetry();
        var data = TelemetryStatistics.CalculateStrokeLengthHistogram(
            telemetry,
            SuspensionType.Front,
            BalanceType.Compression);
        var index = FindPositiveIndex(data);
        var plot = new Plot();
        var sut = new StrokeLengthDistributionPlot(plot, SuspensionType.Front, BalanceType.Compression);
        sut.LoadAnalysisData((StrokeLengthDistributionAnalysisResult)RecordedSessionAnalysisComputer.ComputePlot(
            PlotTestHelpers.CreateAnalysisKey(RecordedSessionAnalysisFamily.StrokeLengthDistribution), telemetry));

        var selected = sut.TryGetRangeSelection(data.Bins[index], data.Values[index] / 2.0, out var selection);
        var strokeSelection = Assert.IsType<StrokeLengthRangeSelection>(selection);

        Assert.True(selected);
        Assert.Equal(SuspensionType.Front, strokeSelection.SuspensionType);
        Assert.Equal(BalanceType.Compression, strokeSelection.StrokeKind);
        Assert.Equal(TelemetryRangeSelection.BinRange.FromBins(data.Bins, index), strokeSelection.Bin);
    }

    [Fact]
    public void StrokeLengthDistribution_ClickAbovePrimaryBarReturnsStrokeLengthSelectionAtMouseX()
    {
        var telemetry = CreateStrokeTelemetry();
        var data = TelemetryStatistics.CalculateStrokeLengthHistogram(
            telemetry,
            SuspensionType.Front,
            BalanceType.Compression);
        var index = FindPositiveIndex(data);
        var plot = new Plot();
        var sut = new StrokeLengthDistributionPlot(plot, SuspensionType.Front, BalanceType.Compression);
        sut.LoadAnalysisData((StrokeLengthDistributionAnalysisResult)RecordedSessionAnalysisComputer.ComputePlot(
            PlotTestHelpers.CreateAnalysisKey(RecordedSessionAnalysisFamily.StrokeLengthDistribution), telemetry));

        var selected = sut.TryGetRangeSelection(data.Bins[index], AboveBarY(data, index), out var selection);
        var strokeSelection = Assert.IsType<StrokeLengthRangeSelection>(selection);

        Assert.True(selected);
        Assert.Equal(TelemetryRangeSelection.BinRange.FromBins(data.Bins, index), strokeSelection.Bin);
    }

    [Fact]
    public void StrokeLengthDistribution_ClickBetweenBarsReturnsFalse()
    {
        var telemetry = CreateStrokeTelemetry();
        var data = TelemetryStatistics.CalculateStrokeLengthHistogram(
            telemetry,
            SuspensionType.Front,
            BalanceType.Compression);
        var index = FindPositiveIndexBeforeGap(data);
        var step = data.Bins[1] - data.Bins[0];
        var plot = new Plot();
        var sut = new StrokeLengthDistributionPlot(plot, SuspensionType.Front, BalanceType.Compression);
        sut.LoadAnalysisData((StrokeLengthDistributionAnalysisResult)RecordedSessionAnalysisComputer.ComputePlot(
            PlotTestHelpers.CreateAnalysisKey(RecordedSessionAnalysisFamily.StrokeLengthDistribution), telemetry));

        var selected = sut.TryGetRangeSelection(
            data.Bins[index] + step * 0.45,
            AboveBarY(data, index),
            out _);

        Assert.False(selected);
    }

    [Fact]
    public void StrokeSpeedDistribution_SelectedPrimaryBarReturnsStrokeSpeedSelectionWithClickedBounds()
    {
        var telemetry = CreateStrokeTelemetry();
        var data = TelemetryStatistics.CalculateStrokeSpeedHistogram(
            telemetry,
            SuspensionType.Front,
            BalanceType.Compression);
        var index = FindPositiveIndex(data);
        var plot = new Plot();
        var sut = new StrokeSpeedDistributionPlot(plot, SuspensionType.Front, BalanceType.Compression);
        sut.LoadAnalysisData((StrokeSpeedDistributionAnalysisResult)RecordedSessionAnalysisComputer.ComputePlot(
            PlotTestHelpers.CreateAnalysisKey(RecordedSessionAnalysisFamily.StrokeSpeedDistribution), telemetry));

        var selected = sut.TryGetRangeSelection(data.Bins[index], data.Values[index] / 2.0, out var selection);
        var strokeSelection = Assert.IsType<StrokeSpeedRangeSelection>(selection);

        Assert.True(selected);
        Assert.Equal(SuspensionType.Front, strokeSelection.SuspensionType);
        Assert.Equal(BalanceType.Compression, strokeSelection.StrokeKind);
        Assert.Equal(TelemetryRangeSelection.BinRange.FromBins(data.Bins, index), strokeSelection.Bin);
    }

    [Fact]
    public void StrokeSpeedDistribution_ClickAbovePrimaryBarReturnsStrokeSpeedSelectionAtMouseX()
    {
        var telemetry = CreateStrokeTelemetry();
        var data = TelemetryStatistics.CalculateStrokeSpeedHistogram(
            telemetry,
            SuspensionType.Front,
            BalanceType.Compression);
        var index = FindPositiveIndex(data);
        var plot = new Plot();
        var sut = new StrokeSpeedDistributionPlot(plot, SuspensionType.Front, BalanceType.Compression);
        sut.LoadAnalysisData((StrokeSpeedDistributionAnalysisResult)RecordedSessionAnalysisComputer.ComputePlot(
            PlotTestHelpers.CreateAnalysisKey(RecordedSessionAnalysisFamily.StrokeSpeedDistribution), telemetry));

        var selected = sut.TryGetRangeSelection(data.Bins[index], AboveBarY(data, index), out var selection);
        var strokeSelection = Assert.IsType<StrokeSpeedRangeSelection>(selection);

        Assert.True(selected);
        Assert.Equal(TelemetryRangeSelection.BinRange.FromBins(data.Bins, index), strokeSelection.Bin);
    }

    [Fact]
    public void DeepTravelDistribution_SelectedPrimaryBarReturnsDeepTravelSelectionWithClickedBounds()
    {
        var telemetry = CreateStrokeTelemetry();
        var data = TelemetryStatistics.CalculateDeepTravelHistogram(telemetry, SuspensionType.Front);
        var index = FindPositiveIndex(data);
        var plot = new Plot();
        var sut = new DeepTravelDistributionPlot(plot, SuspensionType.Front);
        sut.LoadAnalysisData((DeepTravelDistributionAnalysisResult)RecordedSessionAnalysisComputer.ComputePlot(
            PlotTestHelpers.CreateAnalysisKey(RecordedSessionAnalysisFamily.DeepTravelDistribution), telemetry));

        var selected = sut.TryGetRangeSelection(data.Bins[index], data.Values[index] / 2.0, out var selection);
        var deepTravelSelection = Assert.IsType<DeepTravelRangeSelection>(selection);

        Assert.True(selected);
        Assert.Equal(SuspensionType.Front, deepTravelSelection.SuspensionType);
        Assert.Equal(TelemetryRangeSelection.BinRange.FromBins(data.Bins, index), deepTravelSelection.Bin);
    }

    [Fact]
    public void DeepTravelDistribution_ClickAbovePrimaryBarReturnsDeepTravelSelectionAtMouseX()
    {
        var telemetry = CreateStrokeTelemetry();
        var data = TelemetryStatistics.CalculateDeepTravelHistogram(telemetry, SuspensionType.Front);
        var index = FindPositiveIndex(data);
        var plot = new Plot();
        var sut = new DeepTravelDistributionPlot(plot, SuspensionType.Front);
        sut.LoadAnalysisData((DeepTravelDistributionAnalysisResult)RecordedSessionAnalysisComputer.ComputePlot(
            PlotTestHelpers.CreateAnalysisKey(RecordedSessionAnalysisFamily.DeepTravelDistribution), telemetry));

        var selected = sut.TryGetRangeSelection(data.Bins[index], AboveBarY(data, index), out var selection);
        var deepTravelSelection = Assert.IsType<DeepTravelRangeSelection>(selection);

        Assert.True(selected);
        Assert.Equal(TelemetryRangeSelection.BinRange.FromBins(data.Bins, index), deepTravelSelection.Bin);
    }

    private static int FindPositiveIndex(HistogramData data)
    {
        var index = data.Values.FindIndex(value => value > 0);
        Assert.True(index >= 0, "Expected a non-empty histogram bin.");
        return index;
    }

    private static int FindPositiveIndexBeforeGap(HistogramData data)
    {
        for (var index = 0; index < data.Values.Count - 1; index++)
        {
            if (data.Values[index] > 0)
            {
                return index;
            }
        }

        Assert.Fail("Expected a non-empty histogram bin before a following gap.");
        return 0;
    }

    private static double AboveBarY(HistogramData data, int index)
    {
        var top = Math.Max(1, data.Values.Max()) / 0.9;
        return data.Values[index] + (top - data.Values[index]) / 2.0;
    }

    private static TelemetryData CreateStrokeTelemetry()
    {
        return new TelemetryData
        {
            Metadata = new Metadata { SampleRate = 1000, Duration = 0.005 },
            Front = CreateStrokeSuspension(),
            Rear = CreateEmptySuspension(),
            Airtimes = [],
            Markers = [],
        };
    }

    private static Suspension CreateStrokeSuspension()
    {
        return new Suspension
        {
            Present = true,
            MaxTravel = 100,
            Travel = [0, 20, 20, 30, 0],
            Velocity = [0, 700, 0, -600, 0],
            TravelBins = [.. (HistogramBuilder.Linspace(0, 100, 11))],
            VelocityBins = [-1000, 0, 1000],
            Strokes = new Strokes
            {
                Compressions =
                [
                    CreateStroke(
                        0,
                        1,
                        maxVelocity: 700,
                        maxTravel: 95,
                        sumVelocity: 700,
                        digitizedTravel: [],
                        digitizedVelocity: []),
                ],
                Rebounds =
                [
                    CreateStroke(
                        3,
                        4,
                        maxVelocity: -600,
                        maxTravel: 30,
                        sumVelocity: -600,
                        digitizedTravel: [],
                        digitizedVelocity: []),
                ],
            },
        };
    }

    private static Suspension CreateEmptySuspension()
    {
        return new Suspension
        {
            Present = false,
            Travel = [],
            Velocity = [],
            TravelBins = [0, 1],
            VelocityBins = [0, 1],
            Strokes = new Strokes
            {
                Compressions = [],
                Rebounds = [],
            },
        };
    }

    private static Stroke CreateStroke(
        int start,
        int end,
        double maxVelocity,
        double maxTravel,
        double sumVelocity,
        int[] digitizedTravel,
        int[] digitizedVelocity)
    {
        return new Stroke
        {
            Start = start,
            End = end,
            Stat = new StrokeStat
            {
                SumVelocity = sumVelocity,
                MaxVelocity = maxVelocity,
                MaxTravel = maxTravel,
                Count = end - start + 1,
            },
            DigitizedTravel = [.. (digitizedTravel)],
            DigitizedVelocity = [.. (digitizedVelocity)],
        };
    }
}
