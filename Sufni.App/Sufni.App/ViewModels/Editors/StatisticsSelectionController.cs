using System.Collections.Generic;
using System.Linq;
using Sufni.Telemetry;

namespace Sufni.App.ViewModels.Editors;

internal sealed class StatisticsSelectionController
{
    public TelemetryRangeSelection? SelectedFrontRangeSelection { get; private set; }

    public TelemetryRangeSelection? SelectedRearRangeSelection { get; private set; }

    public IReadOnlyList<TelemetryHighlightRange> HighlightRanges { get; private set; } = [];

    public bool HasSelection => HighlightRanges.Count > 0;

    public void Clear()
    {
        SelectedFrontRangeSelection = null;
        SelectedRearRangeSelection = null;
        HighlightRanges = [];
    }

    public bool ClearDampingRangeSelections(TelemetryData? telemetryData, TelemetryTimeRange? analysisRange)
    {
        var changed = false;
        if (SelectedFrontRangeSelection is DampingRangeSelection)
        {
            SelectedFrontRangeSelection = null;
            changed = true;
        }

        if (SelectedRearRangeSelection is DampingRangeSelection)
        {
            SelectedRearRangeSelection = null;
            changed = true;
        }

        if (!changed)
        {
            return false;
        }

        RecomputeHighlightRanges(telemetryData, analysisRange);
        return true;
    }

    public bool Select(
        TelemetryRangeSelection? selection,
        TelemetryData? telemetryData,
        TelemetryTimeRange? analysisRange)
    {
        if (selection is null || telemetryData is null)
        {
            return false;
        }

        var isClearingSelection = selection.SuspensionType == SuspensionType.Front
            ? SelectedFrontRangeSelection == selection
            : SelectedRearRangeSelection == selection;

        if (selection.SuspensionType == SuspensionType.Front)
        {
            SelectedFrontRangeSelection = isClearingSelection ? null : selection;
        }
        else
        {
            SelectedRearRangeSelection = isClearingSelection ? null : selection;
        }

        if (!isClearingSelection)
        {
            ClearSelectionsFromOtherStatistics(selection);
        }

        RecomputeHighlightRanges(telemetryData, analysisRange);
        return true;
    }

    private void ClearSelectionsFromOtherStatistics(TelemetryRangeSelection selection)
    {
        var clearDampingSelections = IsStrokeStatisticsSelection(selection);
        var clearStrokeSelections = selection is DampingRangeSelection;
        if (!clearDampingSelections && !clearStrokeSelections)
        {
            return;
        }

        if (ShouldClearStatisticsSelection(SelectedFrontRangeSelection, clearDampingSelections, clearStrokeSelections))
        {
            SelectedFrontRangeSelection = null;
        }

        if (ShouldClearStatisticsSelection(SelectedRearRangeSelection, clearDampingSelections, clearStrokeSelections))
        {
            SelectedRearRangeSelection = null;
        }
    }

    private void RecomputeHighlightRanges(TelemetryData? telemetryData, TelemetryTimeRange? analysisRange)
    {
        if (telemetryData is null)
        {
            HighlightRanges = [];
            return;
        }

        var ranges = new List<TelemetryHighlightRange>();
        if (SelectedFrontRangeSelection is { } frontSelection)
        {
            ranges.AddRange(CreateStatisticsHighlightRanges(telemetryData, frontSelection, analysisRange));
        }

        if (SelectedRearRangeSelection is { } rearSelection)
        {
            ranges.AddRange(CreateStatisticsHighlightRanges(telemetryData, rearSelection, analysisRange));
        }

        HighlightRanges = TelemetryStatistics.MergeHighlightRanges(ranges);
    }

    private static bool ShouldClearStatisticsSelection(
        TelemetryRangeSelection? selection,
        bool clearDampingSelections,
        bool clearStrokeSelections)
    {
        return (clearDampingSelections && selection is DampingRangeSelection) ||
               (clearStrokeSelections && IsStrokeStatisticsSelection(selection));
    }

    private static bool IsStrokeStatisticsSelection(TelemetryRangeSelection? selection)
    {
        return selection is StrokeLengthRangeSelection or StrokeSpeedRangeSelection or DeepTravelRangeSelection;
    }

    private static IEnumerable<TelemetryHighlightRange> CreateStatisticsHighlightRanges(
        TelemetryData telemetryData,
        TelemetryRangeSelection selection,
        TelemetryTimeRange? analysisRange)
    {
        var ranges = TelemetryStatistics.CalculateHighlightRanges(telemetryData, selection, analysisRange);
        return ranges.Select(range => range with { SuspensionType = selection.SuspensionType });
    }
}
