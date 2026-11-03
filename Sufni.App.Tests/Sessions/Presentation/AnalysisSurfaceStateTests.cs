using Sufni.Telemetry;
using Sufni.App.Shared.Common;

using Sufni.App.Sessions.Presentation;
using Sufni.App.Sessions.Analysis.Services;
namespace Sufni.App.Tests.Sessions.Presentation;

public class AnalysisSurfaceStateTests
{
    [Fact]
    public void ForSuspension_WaitsForLiveSurface_WhenExpectedTelemetryIsMissing()
    {
        var state = AnalysisSurfaceState.ForSuspension(
            expected: true,
            telemetry: null,
            suspensionType: SuspensionType.Front);

        Assert.Equal(SurfaceStateKind.WaitingForData, state.Kind);
    }


}
