using ScottPlot;
using Sufni.App.Theming;
using Sufni.Telemetry;

using Sufni.App.Infrastructure.Theming;
using Sufni.App.Sessions.Plots;
namespace Sufni.App.Tests.Plots;

public class SelectableStatisticsPlotTests
{
    [Fact]
    public void SelectableStatisticsBarHitTester_InsidePointReturnsBarAndOutsidePointsReturnFalse()
    {
        var sut = new SelectableStatisticsBarHitTester();
        var first = new Bar
        {
            Position = 10,
            Size = 4,
            ValueBase = 0,
            Value = 20,
            Orientation = Orientation.Vertical,
        };
        var second = new Bar
        {
            Position = 20,
            Size = 4,
            ValueBase = 0,
            Value = 30,
            Orientation = Orientation.Vertical,
        };
        var firstBin = new TelemetryRangeSelection.BinRange(0, 0, 10, true, false);
        var secondBin = new TelemetryRangeSelection.BinRange(1, 10, 20, false, true);
        sut.Register(first, firstBin, 20, 0);
        sut.Register(second, secondBin, 30, 1);

        Assert.True(sut.TryHit(10, 12, out var hit));
        Assert.Same(first, hit.Bar);
        Assert.Equal(firstBin, hit.Bin);
        Assert.False(sut.TryHit(15, 12, out _));
        Assert.False(sut.TryHit(10, 25, out _));
    }

    [Fact]
    public void SelectableStatisticsBarHitTester_VerticalBarAtXReturnsVisibleBarAndIgnoresGapsAndEmptyBars()
    {
        var sut = new SelectableStatisticsBarHitTester();
        var visible = new Bar
        {
            Position = 10,
            Size = 4,
            ValueBase = 0,
            Value = 20,
            Orientation = Orientation.Vertical,
        };
        var empty = new Bar
        {
            Position = 20,
            Size = 4,
            ValueBase = 0,
            Value = 0,
            Orientation = Orientation.Vertical,
        };
        var visibleBin = new TelemetryRangeSelection.BinRange(0, 0, 10, true, false);
        var emptyBin = new TelemetryRangeSelection.BinRange(1, 10, 20, false, true);
        sut.Register(visible, visibleBin, 20, 0);
        sut.Register(empty, emptyBin, 0, 1);

        Assert.True(sut.TryHitVerticalBarAtX(10, out var hit));
        Assert.Same(visible, hit.Bar);
        Assert.Equal(visibleBin, hit.Bin);
        Assert.False(sut.TryHitVerticalBarAtX(15, out _));
        Assert.False(sut.TryHitVerticalBarAtX(20, out _));
    }

    [Fact]
    public void VelocityHistogram_SelectedYBinEdgeUsesHistogramDigitizeSemantics()
    {
        var telemetry = CreateVelocityTelemetry();
        var plot = new Plot();
        var sut = new VelocityHistogramPlot(plot, SuspensionType.Front);
        sut.LoadTelemetryData(telemetry);
        var y = telemetry.Front.VelocityBins[3];

        var selected = sut.TryGetRangeSelection(90, y, out var selection);
        var dampingSelection = Assert.IsType<DampingRangeSelection>(selection);

        Assert.True(selected);
        Assert.Equal(HistogramBuilder.DigitizeValue(y, telemetry.Front.VelocityBins), dampingSelection.VelocityBinIndex);
        Assert.Equal(0, dampingSelection.TravelBinStartIndex);
        Assert.Equal(TelemetryData.TravelBinsForVelocityHistogram - 1, dampingSelection.TravelBinEndIndex);
    }

    [Fact]
    public void VelocityHistogram_SelectedAllTravelBucketsHighlightsPrimarySegmentsInBin()
    {
        var plot = new Plot();
        var sut = new VelocityHistogramPlot(plot, SuspensionType.Front);
        sut.LoadTelemetryData(CreateVelocityTelemetry());

        sut.SetSelectedRangeSelection(
            new DampingRangeSelection(
                SuspensionType.Front,
                VelocityAverageMode.SampleAveraged,
                3,
                0,
                TelemetryData.TravelBinsForVelocityHistogram - 1));

        var highlighted = GetBars(plot)
            .Where(bar => bar.Orientation == Orientation.Horizontal && bar.LineWidth == 3.0f)
            .ToArray();
        Assert.NotEmpty(highlighted);
        Assert.All(highlighted, AssertSelectedOutline);
    }

    [Fact]
    public void StrokeLengthHistogram_SelectedPrimaryBarReturnsStrokeLengthSelection()
    {
        var telemetry = CreateStrokeTelemetry();
        var data = TelemetryStatistics.CalculateStrokeLengthHistogram(
            telemetry,
            SuspensionType.Front,
            BalanceType.Compression);
        var index = FindPositiveIndex(data);
        var plot = new Plot();
        var sut = new StrokeLengthHistogramPlot(plot, SuspensionType.Front, BalanceType.Compression);
        sut.LoadTelemetryData(telemetry);

        var selected = sut.TryGetRangeSelection(data.Bins[index], data.Values[index] / 2.0, out var selection);
        var strokeSelection = Assert.IsType<StrokeLengthRangeSelection>(selection);

        Assert.True(selected);
        Assert.Equal(SuspensionType.Front, strokeSelection.SuspensionType);
        Assert.Equal(BalanceType.Compression, strokeSelection.StrokeKind);
        Assert.Equal(TelemetryRangeSelection.BinRange.FromBins(data.Bins, index), strokeSelection.Bin);
    }

    [Fact]
    public void StrokeLengthHistogram_ClickAbovePrimaryBarReturnsStrokeLengthSelectionAtMouseX()
    {
        var telemetry = CreateStrokeTelemetry();
        var data = TelemetryStatistics.CalculateStrokeLengthHistogram(
            telemetry,
            SuspensionType.Front,
            BalanceType.Compression);
        var index = FindPositiveIndex(data);
        var plot = new Plot();
        var sut = new StrokeLengthHistogramPlot(plot, SuspensionType.Front, BalanceType.Compression);
        sut.LoadTelemetryData(telemetry);

        var selected = sut.TryGetRangeSelection(data.Bins[index], AboveBarY(data, index), out var selection);
        var strokeSelection = Assert.IsType<StrokeLengthRangeSelection>(selection);

        Assert.True(selected);
        Assert.Equal(TelemetryRangeSelection.BinRange.FromBins(data.Bins, index), strokeSelection.Bin);
    }

    [Fact]
    public void StrokeLengthHistogram_ClickBetweenBarsReturnsFalse()
    {
        var telemetry = CreateStrokeTelemetry();
        var data = TelemetryStatistics.CalculateStrokeLengthHistogram(
            telemetry,
            SuspensionType.Front,
            BalanceType.Compression);
        var index = FindPositiveIndexBeforeGap(data);
        var step = data.Bins[1] - data.Bins[0];
        var plot = new Plot();
        var sut = new StrokeLengthHistogramPlot(plot, SuspensionType.Front, BalanceType.Compression);
        sut.LoadTelemetryData(telemetry);

        var selected = sut.TryGetRangeSelection(
            data.Bins[index] + step * 0.45,
            AboveBarY(data, index),
            out _);

        Assert.False(selected);
    }

    [Fact]
    public void StrokeSpeedHistogram_SelectedPrimaryBarReturnsStrokeSpeedSelectionWithClickedBounds()
    {
        var telemetry = CreateStrokeTelemetry();
        var data = TelemetryStatistics.CalculateStrokeSpeedHistogram(
            telemetry,
            SuspensionType.Front,
            BalanceType.Compression);
        var index = FindPositiveIndex(data);
        var plot = new Plot();
        var sut = new StrokeSpeedHistogramPlot(plot, SuspensionType.Front, BalanceType.Compression);
        sut.LoadTelemetryData(telemetry);

        var selected = sut.TryGetRangeSelection(data.Bins[index], data.Values[index] / 2.0, out var selection);
        var strokeSelection = Assert.IsType<StrokeSpeedRangeSelection>(selection);

        Assert.True(selected);
        Assert.Equal(SuspensionType.Front, strokeSelection.SuspensionType);
        Assert.Equal(BalanceType.Compression, strokeSelection.StrokeKind);
        Assert.Equal(TelemetryRangeSelection.BinRange.FromBins(data.Bins, index), strokeSelection.Bin);
    }

    [Fact]
    public void StrokeSpeedHistogram_ClickAbovePrimaryBarReturnsStrokeSpeedSelectionAtMouseX()
    {
        var telemetry = CreateStrokeTelemetry();
        var data = TelemetryStatistics.CalculateStrokeSpeedHistogram(
            telemetry,
            SuspensionType.Front,
            BalanceType.Compression);
        var index = FindPositiveIndex(data);
        var plot = new Plot();
        var sut = new StrokeSpeedHistogramPlot(plot, SuspensionType.Front, BalanceType.Compression);
        sut.LoadTelemetryData(telemetry);

        var selected = sut.TryGetRangeSelection(data.Bins[index], AboveBarY(data, index), out var selection);
        var strokeSelection = Assert.IsType<StrokeSpeedRangeSelection>(selection);

        Assert.True(selected);
        Assert.Equal(TelemetryRangeSelection.BinRange.FromBins(data.Bins, index), strokeSelection.Bin);
    }

    [Fact]
    public void DeepTravelHistogram_SelectedPrimaryBarReturnsDeepTravelSelectionWithClickedBounds()
    {
        var telemetry = CreateStrokeTelemetry();
        var data = TelemetryStatistics.CalculateDeepTravelHistogram(telemetry, SuspensionType.Front);
        var index = FindPositiveIndex(data);
        var plot = new Plot();
        var sut = new DeepTravelHistogramPlot(plot, SuspensionType.Front);
        sut.LoadTelemetryData(telemetry);

        var selected = sut.TryGetRangeSelection(data.Bins[index], data.Values[index] / 2.0, out var selection);
        var deepTravelSelection = Assert.IsType<DeepTravelRangeSelection>(selection);

        Assert.True(selected);
        Assert.Equal(SuspensionType.Front, deepTravelSelection.SuspensionType);
        Assert.Equal(TelemetryRangeSelection.BinRange.FromBins(data.Bins, index), deepTravelSelection.Bin);
    }

    [Fact]
    public void DeepTravelHistogram_ClickAbovePrimaryBarReturnsDeepTravelSelectionAtMouseX()
    {
        var telemetry = CreateStrokeTelemetry();
        var data = TelemetryStatistics.CalculateDeepTravelHistogram(telemetry, SuspensionType.Front);
        var index = FindPositiveIndex(data);
        var plot = new Plot();
        var sut = new DeepTravelHistogramPlot(plot, SuspensionType.Front);
        sut.LoadTelemetryData(telemetry);

        var selected = sut.TryGetRangeSelection(data.Bins[index], AboveBarY(data, index), out var selection);
        var deepTravelSelection = Assert.IsType<DeepTravelRangeSelection>(selection);

        Assert.True(selected);
        Assert.Equal(TelemetryRangeSelection.BinRange.FromBins(data.Bins, index), deepTravelSelection.Bin);
    }

    [Fact]
    public void StrokeSpeedHistogram_SelectedPrimaryBarReceivesHighlightedOutline()
    {
        var telemetry = CreateStrokeTelemetry();
        var data = TelemetryStatistics.CalculateStrokeSpeedHistogram(
            telemetry,
            SuspensionType.Front,
            BalanceType.Compression);
        var index = FindPositiveIndex(data);
        var plot = new Plot();
        var sut = new StrokeSpeedHistogramPlot(plot, SuspensionType.Front, BalanceType.Compression);
        sut.LoadTelemetryData(telemetry);
        var selection = new StrokeSpeedRangeSelection(
            SuspensionType.Front,
            BalanceType.Compression,
            TelemetryRangeSelection.BinRange.FromBins(data.Bins, index));

        sut.SetSelectedRangeSelection(selection);

        var highlighted = GetBars(plot).Where(bar => bar.LineWidth == 3.0f).ToArray();
        var bar = Assert.Single(highlighted);
        AssertSelectedOutline(bar);
    }

    [Fact]
    public void StrokeSpeedHistogram_StaleSelectedBoundsDoNotHighlightMatchingIndex()
    {
        var telemetry = CreateStrokeTelemetry();
        var data = TelemetryStatistics.CalculateStrokeSpeedHistogram(
            telemetry,
            SuspensionType.Front,
            BalanceType.Compression);
        var index = FindPositiveIndex(data);
        var currentBin = TelemetryRangeSelection.BinRange.FromBins(data.Bins, index);
        var staleBin = currentBin with
        {
            Start = currentBin.Start + 1,
            End = currentBin.End + 1,
        };
        var plot = new Plot();
        var sut = new StrokeSpeedHistogramPlot(plot, SuspensionType.Front, BalanceType.Compression);
        sut.LoadTelemetryData(telemetry);

        sut.SetSelectedRangeSelection(
            new StrokeSpeedRangeSelection(SuspensionType.Front, BalanceType.Compression, staleBin));

        Assert.DoesNotContain(GetBars(plot), bar => bar.LineWidth == 3.0f);
    }

    private static void AssertSelectedOutline(Bar bar)
    {
        Assert.Equal(SufniThemes.Dark.Plot.Marker.DampingSelectionOutline.ToScottPlotColor(), bar.LineColor);
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

    private static Bar[] GetBars(Plot plot)
    {
        var bars = new List<Bar>();
        foreach (var plottable in plot.PlottableList)
        {
            if (plottable is Bar bar)
            {
                bars.Add(bar);
                continue;
            }

            if (plottable.GetType().GetProperty("Bars")?.GetValue(plottable) is IEnumerable<Bar> nestedBars)
            {
                bars.AddRange(nestedBars);
            }
        }

        return [.. bars];
    }

    private static TelemetryData CreateVelocityTelemetry()
    {
        return new TelemetryData
        {
            Metadata = new Metadata { SampleRate = 1000, Duration = 0.005 },
            Front = CreateVelocitySuspension(),
            Rear = CreateEmptySuspension(),
            Airtimes = [],
            Markers = [],
        };
    }

    private static Suspension CreateVelocitySuspension()
    {
        return new Suspension
        {
            Present = true,
            MaxTravel = 100,
            Travel = [0, 10, 20, 10, 0],
            Velocity = [100, 120, 80, -60, -90],
            TravelBins = HistogramBuilder.Linspace(0, 100, Parameters.TravelHistBins + 1),
            VelocityBins = [-200, -100, 0, 100, 200],
            FineVelocityBins = [-200, -100, 0, 100, 200],
            Strokes = new Strokes
            {
                Compressions =
                [
                    CreateStroke(
                        0,
                        2,
                        maxVelocity: 120,
                        maxTravel: 20,
                        sumVelocity: 300,
                        digitizedTravel: [0, 2, 4],
                        digitizedVelocity: [3, 3, 2]),
                ],
                Rebounds =
                [
                    CreateStroke(
                        3,
                        4,
                        maxVelocity: -90,
                        maxTravel: 10,
                        sumVelocity: -150,
                        digitizedTravel: [2, 0],
                        digitizedVelocity: [1, 1]),
                ],
            },
        };
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
            TravelBins = HistogramBuilder.Linspace(0, 100, 11),
            VelocityBins = [-1000, 0, 1000],
            FineVelocityBins = [-1000, 0, 1000],
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
            FineVelocityBins = [0, 1],
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
            DigitizedTravel = digitizedTravel,
            DigitizedVelocity = digitizedVelocity,
            FineDigitizedVelocity = [],
        };
    }
}
