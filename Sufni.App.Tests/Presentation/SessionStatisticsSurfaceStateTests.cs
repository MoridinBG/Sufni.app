using Sufni.App.Presentation;
using Sufni.App.Tests.Infrastructure;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Presentation;

public class SessionStatisticsSurfaceStateTests
{
    [Fact]
    public void ForSuspension_HidesRecordedSurface_WhenSuspensionIsAbsent()
    {
        var telemetry = TestTelemetryData.CreateMinimal();
        telemetry.Rear.Present = false;

        var state = SessionStatisticsSurfaceState.ForSuspension(telemetry, SuspensionType.Rear);

        Assert.Equal(SurfaceStateKind.Hidden, state.Kind);
    }

    [Fact]
    public void ForSuspension_WaitsForLiveSurface_WhenExpectedTelemetryIsMissing()
    {
        var state = SessionStatisticsSurfaceState.ForSuspension(
            expected: true,
            telemetry: null,
            suspensionType: SuspensionType.Front);

        Assert.Equal(SurfaceStateKind.WaitingForData, state.Kind);
    }

    [Fact]
    public void ForBalance_HidesLiveSurface_WhenBalanceIsNotExpected()
    {
        var state = SessionStatisticsSurfaceState.ForBalance(
            expected: false,
            telemetry: null,
            balanceType: BalanceType.Compression);

        Assert.Equal(SurfaceStateKind.Hidden, state.Kind);
    }

    [Fact]
    public void ForVibration_Waits_WhenImuExistsButStrokeDataIsMissing()
    {
        var telemetry = TestTelemetryData.CreateWithImu(activeLocations: [(byte)ImuLocation.Fork]);

        var state = SessionStatisticsSurfaceState.ForVibration(
            telemetry,
            SuspensionType.Front,
            ImuLocation.Fork);

        Assert.Equal(SurfaceStateKind.WaitingForData, state.Kind);
    }
}
