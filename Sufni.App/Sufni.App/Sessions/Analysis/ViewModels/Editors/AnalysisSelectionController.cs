using System.Collections.Generic;
using System.Linq;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Analysis.ViewModels.Editors;

internal sealed class AnalysisSelectionController
{
    public AnalysisSelectionController()
    {
    }

    public AnalysisSelectionController(
        TelemetryRangeSelection? activeFrontAnalysisSelection,
        TelemetryRangeSelection? activeRearAnalysisSelection,
        IReadOnlyList<TelemetryHighlightRange> highlightRanges)
    {
        ActiveFrontAnalysisSelection = activeFrontAnalysisSelection;
        ActiveRearAnalysisSelection = activeRearAnalysisSelection;
        HighlightRanges = highlightRanges;
    }

    public TelemetryRangeSelection? ActiveFrontAnalysisSelection { get; private set; }

    public TelemetryRangeSelection? ActiveRearAnalysisSelection { get; private set; }

    public IReadOnlyList<TelemetryHighlightRange> HighlightRanges { get; private set; } = [];

    public bool HasSelection => HighlightRanges.Count > 0;

    public void Clear()
    {
        ActiveFrontAnalysisSelection = null;
        ActiveRearAnalysisSelection = null;
        HighlightRanges = [];
    }

    public bool ClearDampingRangeSelections(TelemetryData? telemetryData, TelemetryTimeRange? analysisRange)
    {
        var changed = false;
        if (ActiveFrontAnalysisSelection is DampingRangeSelection)
        {
            ActiveFrontAnalysisSelection = null;
            changed = true;
        }

        if (ActiveRearAnalysisSelection is DampingRangeSelection)
        {
            ActiveRearAnalysisSelection = null;
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
            ? ActiveFrontAnalysisSelection == selection
            : ActiveRearAnalysisSelection == selection;

        if (selection.SuspensionType == SuspensionType.Front)
        {
            ActiveFrontAnalysisSelection = isClearingSelection ? null : selection;
        }
        else
        {
            ActiveRearAnalysisSelection = isClearingSelection ? null : selection;
        }

        if (!isClearingSelection)
        {
            ClearSelectionsFromOtherAnalysis(selection);
        }

        RecomputeHighlightRanges(telemetryData, analysisRange);
        return true;
    }

    private void ClearSelectionsFromOtherAnalysis(TelemetryRangeSelection selection)
    {
        var clearDampingSelections = IsStrokeAnalysisSelection(selection);
        var clearStrokeSelections = selection is DampingRangeSelection;
        if (!clearDampingSelections && !clearStrokeSelections)
        {
            return;
        }

        if (ShouldClearAnalysisSelection(ActiveFrontAnalysisSelection, clearDampingSelections, clearStrokeSelections))
        {
            ActiveFrontAnalysisSelection = null;
        }

        if (ShouldClearAnalysisSelection(ActiveRearAnalysisSelection, clearDampingSelections, clearStrokeSelections))
        {
            ActiveRearAnalysisSelection = null;
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
        if (ActiveFrontAnalysisSelection is { } frontSelection)
        {
            ranges.AddRange(CreateAnalysisHighlightRanges(telemetryData, frontSelection, analysisRange));
        }

        if (ActiveRearAnalysisSelection is { } rearSelection)
        {
            ranges.AddRange(CreateAnalysisHighlightRanges(telemetryData, rearSelection, analysisRange));
        }

        HighlightRanges = TelemetryStatistics.MergeHighlightRanges(ranges);
    }

    private static bool ShouldClearAnalysisSelection(
        TelemetryRangeSelection? selection,
        bool clearDampingSelections,
        bool clearStrokeSelections)
    {
        return (clearDampingSelections && selection is DampingRangeSelection) ||
               (clearStrokeSelections && IsStrokeAnalysisSelection(selection));
    }

    private static bool IsStrokeAnalysisSelection(TelemetryRangeSelection? selection)
    {
        return selection is StrokeLengthRangeSelection or StrokeSpeedRangeSelection or DeepTravelRangeSelection;
    }

    private static IEnumerable<TelemetryHighlightRange> CreateAnalysisHighlightRanges(
        TelemetryData telemetryData,
        TelemetryRangeSelection selection,
        TelemetryTimeRange? analysisRange)
    {
        var ranges = TelemetryStatistics.CalculateHighlightRanges(telemetryData, selection, analysisRange);
        return ranges.Select(range => range with { SuspensionType = selection.SuspensionType });
    }
}
