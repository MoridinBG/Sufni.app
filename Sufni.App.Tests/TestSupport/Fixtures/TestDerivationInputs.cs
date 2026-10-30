using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.Telemetry;

namespace Sufni.App.Tests.TestSupport.Fixtures;

internal static class TestDerivationInputs
{
    public static RecordedSessionDerivationInput Create(
        RecordedSessionDomainSnapshot domain,
        RecordedSessionSource source,
        TelemetryProcessingOptions options) =>
        RecordedSessionDerivationInput.Create(domain.Session, domain.Setup!, domain.Bike!, source, options);
}
