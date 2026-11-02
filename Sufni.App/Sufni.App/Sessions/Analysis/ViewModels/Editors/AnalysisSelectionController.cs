using Sufni.Telemetry;

namespace Sufni.App.Sessions.Analysis.ViewModels.Editors;

internal sealed class AnalysisSelectionController
{
    public AnalysisSelectionController()
    {
    }

    public AnalysisSelectionController(
        TelemetryRangeSelection? activeFrontAnalysisSelection,
        TelemetryRangeSelection? activeRearAnalysisSelection)
    {
        ActiveFrontAnalysisSelection = activeFrontAnalysisSelection;
        ActiveRearAnalysisSelection = activeRearAnalysisSelection;
    }

    public TelemetryRangeSelection? ActiveFrontAnalysisSelection { get; private set; }

    public TelemetryRangeSelection? ActiveRearAnalysisSelection { get; private set; }

    public bool ClearDampingRangeSelections()
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

        return true;
    }

    public bool Select(
        TelemetryRangeSelection? selection)
    {
        if (selection is null)
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

}
