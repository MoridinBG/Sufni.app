using System.Collections.Generic;

namespace Sufni.App.Shared.Views.Plots;

/// <summary>The selection and menu intents accepted by a time-series surface.</summary>
public interface ITimeSeriesPlotInteractions
{
    IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> SignalPlotContextMenuActionsBySignalRowId { get; }
    void SetAnalysisRange(double startSeconds, double endSeconds);
    void ClearAnalysisRange();
    void SetAnalysisRangeBoundary(double boundarySeconds);
}
