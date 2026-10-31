using Sufni.App.Bikes.Models;
using Sufni.App.Segments.Models;
using Sufni.App.Sessions.Models;
using System;
using System.Threading;
using System.Threading.Tasks;
using Sufni.Telemetry;

using Sufni.App.Sessions.Processing.SessionDetails;
namespace Sufni.App.MapsAndTracks.Coordinators;

public interface ITrackCoordinator
{
    Task StopAsync();

    Task<SegmentAnalysisComparison> BuildAnalysisComparisonAsync(
        SegmentComparison comparison, SegmentComparisonDetail detail,
        TravelDistributionMode travelMode, VelocityAverageMode velocityMode,
        DampingSpeedCutoffs cutoffs, SessionDampingPercentages currentPercentages,
        CancellationToken cancellationToken = default);

    Task<GpxImportResult> ImportGpxAsync(CancellationToken cancellationToken = default);

    Task<SessionTrackPresentationData> LoadSessionTrackAsync(
        Guid sessionId,
        TelemetryData telemetryData,
        CancellationToken cancellationToken = default);

}
