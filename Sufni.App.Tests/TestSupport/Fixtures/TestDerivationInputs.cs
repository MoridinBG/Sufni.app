using Sufni.App.Sessions.Models;
using Sufni.App.Bikes.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.Telemetry;

namespace Sufni.App.Tests.TestSupport.Fixtures;

internal static class TestDerivationInputs
{
    public static RecordedSessionDerivationInput Create(
        RecordedSessionDomainSnapshot domain,
        RecordedSessionSource source,
        TelemetryProcessingOptions options) =>
        RecordedSessionDerivationInput.Create(
            domain.Session with { VelocityFilterWindowMilliseconds = options.ClampedVelocityFilterWindowMilliseconds },
            domain.Setup!, domain.Bike!, source);
    public static SessionProcessingInputBundle DatabaseInputs(RecordedSessionDomainSnapshot domain) => new(
        new SessionProcessingInput(domain.Session.Id, domain.Setup!.Id, domain.Session.DerivationWindow,
            domain.Session.VelocityFilterWindowMilliseconds),
        new SetupProcessingInput(domain.Setup.Id, domain.Setup.BikeId,
            domain.Setup.FrontSensorConfigurationJson, domain.Setup.RearSensorConfigurationJson),
        new BikeProcessingInput(domain.Bike!.Id, domain.Bike.HeadAngle, domain.Bike.ForkStroke,
            domain.Bike.ShockStroke, RearSuspensionJsonCodec.Serialize(domain.Bike.RearSuspension)),
        domain.Source!);

}
