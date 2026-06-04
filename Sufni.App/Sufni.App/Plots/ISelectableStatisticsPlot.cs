using System.Diagnostics.CodeAnalysis;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public interface ISelectableStatisticsPlot
{
    bool TryGetRangeSelection(
        double x,
        double y,
        [NotNullWhen(true)] out TelemetryRangeSelection? selection);

    void SetSelectedRangeSelection(TelemetryRangeSelection? selection);
}
