using System.Threading;
using Sufni.App.Models;
using Sufni.App.SessionDetails;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;

namespace Sufni.App.Services;

public interface ISessionPresentationService
{
    SessionDamperPercentages CalculateDamperPercentages(
        TelemetryData telemetryData,
        TelemetryTimeRange? range = null,
        VelocityAverageMode velocityAverageMode = VelocityAverageMode.SampleAveraged,
        DampingSpeedCutoffs? dampingSpeedCutoffs = null);

    SessionCachePresentationData BuildCachePresentation(
        TelemetryData telemetryData,
        SessionPresentationDimensions dimensions,
        CancellationToken cancellationToken = default,
        DampingSpeedCutoffs? dampingSpeedCutoffs = null);
}
