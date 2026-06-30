using Sufni.App.Tests.TestSupport;
using Sufni.Telemetry;

using Sufni.App.Sessions.Statistics.ViewModels.Editors;
namespace Sufni.App.Tests.ViewModels.Editors;

public class StatisticsSelectionControllerTests
{
    [Fact]
    public void Select_DampingRange_SetsSelectionAndTogglesOffRepeatedSelection()
    {
        var telemetry = TestTelemetryData.CreateProcessed();
        var selection = CreateDampingSelection(
            telemetry,
            SuspensionType.Front,
            VelocityAverageMode.SampleAveraged);
        var sut = new StatisticsSelectionController();

        Assert.True(sut.Select(selection, telemetry, null));

        Assert.Equal(selection, sut.SelectedFrontRangeSelection);
        Assert.Null(sut.SelectedRearRangeSelection);
        Assert.True(sut.HasSelection);
        Assert.NotEmpty(sut.HighlightRanges);
        Assert.All(sut.HighlightRanges, range => Assert.Equal(SuspensionType.Front, range.SuspensionType));

        Assert.True(sut.Select(selection, telemetry, null));

        Assert.Null(sut.SelectedFrontRangeSelection);
        Assert.Null(sut.SelectedRearRangeSelection);
        Assert.False(sut.HasSelection);
        Assert.Empty(sut.HighlightRanges);
    }

    [Fact]
    public void Select_StrokeSelection_ClearsDampingSelections()
    {
        var telemetry = TestTelemetryData.CreateProcessed();
        var rearDampingSelection = CreateDampingSelection(
            telemetry,
            SuspensionType.Rear,
            VelocityAverageMode.SampleAveraged);
        var frontStrokeSelection = CreateStrokeLengthSelection(
            telemetry,
            SuspensionType.Front,
            BalanceType.Compression);
        var sut = new StatisticsSelectionController();

        Assert.True(sut.Select(rearDampingSelection, telemetry, null));
        Assert.True(sut.Select(frontStrokeSelection, telemetry, null));

        Assert.Equal(frontStrokeSelection, sut.SelectedFrontRangeSelection);
        Assert.Null(sut.SelectedRearRangeSelection);
        Assert.True(sut.HasSelection);
        Assert.All(sut.HighlightRanges, range => Assert.Equal(SuspensionType.Front, range.SuspensionType));
    }

    [Fact]
    public void Select_DampingSelection_ClearsStrokeSelections()
    {
        var telemetry = TestTelemetryData.CreateProcessed();
        var frontStrokeSelection = CreateStrokeLengthSelection(
            telemetry,
            SuspensionType.Front,
            BalanceType.Compression);
        var rearDampingSelection = CreateDampingSelection(
            telemetry,
            SuspensionType.Rear,
            VelocityAverageMode.SampleAveraged);
        var sut = new StatisticsSelectionController();

        Assert.True(sut.Select(frontStrokeSelection, telemetry, null));
        Assert.True(sut.Select(rearDampingSelection, telemetry, null));

        Assert.Null(sut.SelectedFrontRangeSelection);
        Assert.Equal(rearDampingSelection, sut.SelectedRearRangeSelection);
        Assert.True(sut.HasSelection);
        Assert.All(sut.HighlightRanges, range => Assert.Equal(SuspensionType.Rear, range.SuspensionType));
    }

    [Fact]
    public void ClearDampingRangeSelections_ClearsOnlyDampingSelections()
    {
        var telemetry = TestTelemetryData.CreateProcessed();
        var frontDampingSelection = CreateDampingSelection(
            telemetry,
            SuspensionType.Front,
            VelocityAverageMode.SampleAveraged);
        var rearDampingSelection = CreateDampingSelection(
            telemetry,
            SuspensionType.Rear,
            VelocityAverageMode.SampleAveraged);
        var sut = new StatisticsSelectionController();

        Assert.True(sut.Select(frontDampingSelection, telemetry, null));
        Assert.True(sut.Select(rearDampingSelection, telemetry, null));
        Assert.True(sut.ClearDampingRangeSelections(telemetry, null));

        Assert.Null(sut.SelectedFrontRangeSelection);
        Assert.Null(sut.SelectedRearRangeSelection);
        Assert.False(sut.HasSelection);
        Assert.Empty(sut.HighlightRanges);
    }

    [Fact]
    public void ClearDampingRangeSelections_WithStrokeSelection_ReturnsFalse()
    {
        var telemetry = TestTelemetryData.CreateProcessed();
        var strokeSelection = CreateStrokeLengthSelection(
            telemetry,
            SuspensionType.Front,
            BalanceType.Compression);
        var sut = new StatisticsSelectionController();

        Assert.True(sut.Select(strokeSelection, telemetry, null));
        Assert.False(sut.ClearDampingRangeSelections(telemetry, null));

        Assert.Equal(strokeSelection, sut.SelectedFrontRangeSelection);
        Assert.Null(sut.SelectedRearRangeSelection);
        Assert.True(sut.HasSelection);
        Assert.All(sut.HighlightRanges, range => Assert.Equal(SuspensionType.Front, range.SuspensionType));
    }

    private static DampingRangeSelection CreateDampingSelection(
        TelemetryData telemetry,
        SuspensionType suspension,
        VelocityAverageMode averageMode)
    {
        var histogram = TelemetryStatistics.CalculateVelocityHistogram(
            telemetry,
            suspension,
            new VelocityStatisticsOptions(null, averageMode));

        for (var velocityBinIndex = 0; velocityBinIndex < histogram.Values.Count; velocityBinIndex++)
        {
            var travelValues = histogram.Values[velocityBinIndex];
            for (var travelBinIndex = 0; travelBinIndex < travelValues.Length; travelBinIndex++)
            {
                if (travelValues[travelBinIndex] > 0)
                {
                    return new DampingRangeSelection(
                        suspension,
                        averageMode,
                        velocityBinIndex,
                        travelBinIndex,
                        travelBinIndex);
                }
            }
        }

        Assert.Fail($"Expected a non-empty {suspension} damping histogram bin.");
        return default!;
    }

    private static StrokeLengthRangeSelection CreateStrokeLengthSelection(
        TelemetryData telemetry,
        SuspensionType suspension,
        BalanceType strokeKind)
    {
        var histogram = TelemetryStatistics.CalculateStrokeLengthHistogram(telemetry, suspension, strokeKind);
        var index = FindPositiveIndex(histogram.Values);
        return new StrokeLengthRangeSelection(
            suspension,
            strokeKind,
            TelemetryRangeSelection.BinRange.FromBins(histogram.Bins, index));
    }

    private static int FindPositiveIndex(IReadOnlyList<double> values)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] > 0)
            {
                return index;
            }
        }

        Assert.Fail("Expected a non-empty stroke histogram bin.");
        return -1;
    }
}
