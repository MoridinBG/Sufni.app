using System.Threading;
using Sufni.App.Models;
using Sufni.App.SessionDetails;
using Sufni.Telemetry;

namespace Sufni.App.Services;

public interface ISessionPresentationService
{
    SessionDamperPercentages CalculateDamperPercentages(
        TelemetryData telemetryData,
        TelemetryTimeRange? range = null);

    SessionCachePresentationData BuildCachePresentation(
        TelemetryData telemetryData,
        SessionPresentationDimensions dimensions,
        CancellationToken cancellationToken = default);
}