using System;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.SessionDetails;
using Sufni.Telemetry;

namespace Sufni.App.Coordinators;

public interface ITrackCoordinator
{
    Task<GpxImportResult> ImportGpxAsync(CancellationToken cancellationToken = default);

    Task<SessionTrackPresentationData> LoadSessionTrackAsync(
        Guid sessionId,
        Guid? fullTrackId,
        TelemetryData telemetryData,
        CancellationToken cancellationToken = default);

    Task<SessionGpsOffsetUpdateResult?> UpdateSessionGpsOffsetAsync(
        Guid sessionId,
        Guid? fullTrackId,
        TelemetryData telemetryData,
        double gpsOffsetSeconds,
        CancellationToken cancellationToken = default);
}
