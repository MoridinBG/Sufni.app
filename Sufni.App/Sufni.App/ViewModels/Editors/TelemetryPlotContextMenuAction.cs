using System.Windows.Input;
using Sufni.Telemetry;

namespace Sufni.App.ViewModels.Editors;

public sealed record TelemetryPlotContextMenuContext(
    string RowId,
    double ClickSeconds,
    double DurationSeconds,
    TelemetryTimeRange? AnalysisRange)
{
    public bool IsClickInsideAnalysisRange =>
        AnalysisRange is { } range &&
        ClickSeconds >= range.StartSeconds &&
        ClickSeconds <= range.EndSeconds;
}

public sealed record TelemetryPlotContextMenuAction(
    string Id,
    string Label,
    ICommand Command);
