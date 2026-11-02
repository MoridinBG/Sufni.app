using Sufni.Telemetry;

using Sufni.App.Sessions.Analysis.ViewModels.Editors;
namespace Sufni.App.Tests.Sessions.Analysis.ViewModels.Editors;

public class AnalysisSelectionControllerTests
{
    [Fact]
    public void Select_DampingRange_SetsSelectionAndTogglesOffRepeatedSelection()
    {
        var selection = CreateDampingSelection(
            SuspensionType.Front,
            VelocityAverageMode.SampleAveraged);
        var sut = new AnalysisSelectionController();

        Assert.True(sut.Select(selection));

        Assert.Equal(selection, sut.ActiveFrontAnalysisSelection);
        Assert.Null(sut.ActiveRearAnalysisSelection);

        Assert.True(sut.Select(selection));

        Assert.Null(sut.ActiveFrontAnalysisSelection);
        Assert.Null(sut.ActiveRearAnalysisSelection);
    }

    [Fact]
    public void Select_StrokeSelection_ClearsDampingSelections()
    {
        var rearDampingSelection = CreateDampingSelection(
            SuspensionType.Rear,
            VelocityAverageMode.SampleAveraged);
        var frontStrokeSelection = CreateStrokeLengthSelection(
            SuspensionType.Front,
            BalanceType.Compression);
        var sut = new AnalysisSelectionController();

        Assert.True(sut.Select(rearDampingSelection));
        Assert.True(sut.Select(frontStrokeSelection));

        Assert.Equal(frontStrokeSelection, sut.ActiveFrontAnalysisSelection);
        Assert.Null(sut.ActiveRearAnalysisSelection);
    }

    [Fact]
    public void Select_DampingSelection_ClearsStrokeSelections()
    {
        var frontStrokeSelection = CreateStrokeLengthSelection(
            SuspensionType.Front,
            BalanceType.Compression);
        var rearDampingSelection = CreateDampingSelection(
            SuspensionType.Rear,
            VelocityAverageMode.SampleAveraged);
        var sut = new AnalysisSelectionController();

        Assert.True(sut.Select(frontStrokeSelection));
        Assert.True(sut.Select(rearDampingSelection));

        Assert.Null(sut.ActiveFrontAnalysisSelection);
        Assert.Equal(rearDampingSelection, sut.ActiveRearAnalysisSelection);
    }

    [Fact]
    public void ClearDampingRangeSelections_ClearsOnlyDampingSelections()
    {
        var frontDampingSelection = CreateDampingSelection(
            SuspensionType.Front,
            VelocityAverageMode.SampleAveraged);
        var rearDampingSelection = CreateDampingSelection(
            SuspensionType.Rear,
            VelocityAverageMode.SampleAveraged);
        var sut = new AnalysisSelectionController();

        Assert.True(sut.Select(frontDampingSelection));
        Assert.True(sut.Select(rearDampingSelection));
        Assert.True(sut.ClearDampingRangeSelections());

        Assert.Null(sut.ActiveFrontAnalysisSelection);
        Assert.Null(sut.ActiveRearAnalysisSelection);
    }

    [Fact]
    public void ClearDampingRangeSelections_WithStrokeSelection_ReturnsFalse()
    {
        var strokeSelection = CreateStrokeLengthSelection(
            SuspensionType.Front,
            BalanceType.Compression);
        var sut = new AnalysisSelectionController();

        Assert.True(sut.Select(strokeSelection));
        Assert.False(sut.ClearDampingRangeSelections());

        Assert.Equal(strokeSelection, sut.ActiveFrontAnalysisSelection);
        Assert.Null(sut.ActiveRearAnalysisSelection);
    }

    private static DampingRangeSelection CreateDampingSelection(SuspensionType suspension, VelocityAverageMode averageMode) =>
        new(suspension, averageMode, 0, 0, 0);

    private static StrokeLengthRangeSelection CreateStrokeLengthSelection(SuspensionType suspension, BalanceType strokeKind) =>
        new(suspension, strokeKind, TelemetryRangeSelection.BinRange.FromBins([0, 20], 0));
}
