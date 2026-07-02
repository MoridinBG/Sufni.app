using System.Diagnostics.CodeAnalysis;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Plots;

public interface ISelectableAnalysisPlot
{
    bool TryGetRangeSelection(
        double x,
        double y,
        [NotNullWhen(true)] out TelemetryRangeSelection? selection);

    void SetActiveAnalysisSelection(TelemetryRangeSelection? selection);
}
