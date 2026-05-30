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
    public void ForSuspension_ShowsNoDataForRecordedSurface_WhenTravelHasNoStrokes()
    {
        var telemetry = TestTelemetryData.CreateMinimal();

        var state = SessionStatisticsSurfaceState.ForSuspension(telemetry, SuspensionType.Front);

        Assert.Equal(SurfaceStateKind.NoData, state.Kind);
        Assert.Equal(SurfaceIndicatorKind.None, state.Indicator);
        Assert.Equal("Not enough travel movement to calculate statistics.", state.Message);
    }

    [Fact]
    public void ForSuspension_ShowsRangeSpecificNoDataForRecordedSurface_WhenSelectedRangeHasNoStrokes()
    {
        var telemetry = TestTelemetryData.CreateMinimal(duration: 2, sampleRate: 2);
        telemetry.Front.Strokes = new Strokes
        {
            Compressions = [new Stroke { Start = 2, End = 3 }],
            Rebounds = [],
        };

        var state = SessionStatisticsSurfaceState.ForSuspension(
            telemetry,
            SuspensionType.Front,
            new TelemetryTimeRange(0, 0.5));

        Assert.Equal(SurfaceStateKind.NoData, state.Kind);
        Assert.Equal(SurfaceIndicatorKind.None, state.Indicator);
        Assert.Equal("Not enough travel movement in the selected range to calculate statistics.", state.Message);
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
    public void ForVibration_ShowsNoDataForRecordedSurface_WhenImuExistsButStrokeDataIsMissing()
    {
        var telemetry = TestTelemetryData.CreateWithImu(activeLocations: [(byte)ImuLocation.Fork]);

        var state = SessionStatisticsSurfaceState.ForVibration(
            telemetry,
            SuspensionType.Front,
            ImuLocation.Fork);

        Assert.Equal(SurfaceStateKind.NoData, state.Kind);
        Assert.Equal(SurfaceIndicatorKind.None, state.Indicator);
    }
}
