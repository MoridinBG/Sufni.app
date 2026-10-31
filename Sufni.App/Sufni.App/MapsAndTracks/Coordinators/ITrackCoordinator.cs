using System;
using System.Threading;
using System.Threading.Tasks;
using Sufni.Telemetry;

using Sufni.App.Sessions.Processing.SessionDetails;
namespace Sufni.App.MapsAndTracks.Coordinators;

public interface ITrackCoordinator
{
    Task<GpxImportResult> ImportGpxAsync(CancellationToken cancellationToken = default);

    Task<SessionTrackPresentationData> LoadSessionTrackAsync(
        Guid sessionId,
        TelemetryData telemetryData,
        CancellationToken cancellationToken = default);

}
