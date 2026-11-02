using Sufni.Telemetry;
using Sufni.App.Shared.Common;

using Sufni.App.Sessions.Presentation;
using Sufni.App.Sessions.Analysis.Services;
namespace Sufni.App.Tests.Sessions.Presentation;

public class AnalysisSurfaceStateTests
{
    [Fact]
    public void ForSuspension_HidesRecordedSurface_WhenSuspensionIsAbsent()
    {
        var state = AnalysisSurfaceState.ForSuspension(AnalysisDataAvailability.Absent);

        Assert.Equal(SurfaceStateKind.Hidden, state.Kind);
    }

    [Fact]
    public void ForSuspension_WaitsForLiveSurface_WhenExpectedTelemetryIsMissing()
    {
        var state = AnalysisSurfaceState.ForSuspension(
            expected: true,
            telemetry: null,
            suspensionType: SuspensionType.Front);

        Assert.Equal(SurfaceStateKind.WaitingForData, state.Kind);
    }

    [Fact]
    public void ForSuspension_ShowsNoDataForRecordedSurface_WhenTravelHasNoStrokes()
    {
        var state = AnalysisSurfaceState.ForSuspension(AnalysisDataAvailability.Empty);

        Assert.Equal(SurfaceStateKind.NoData, state.Kind);
        Assert.Equal(SurfaceIndicatorKind.None, state.Indicator);
        Assert.Equal("No analysis data.", state.Message);
    }

    [Fact]
    public void ForSuspension_ShowsRangeSpecificNoDataForRecordedSurface_WhenSelectedRangeHasNoStrokes()
    {
        var state = AnalysisSurfaceState.ForSuspension(
            AnalysisDataAvailability.Empty,
            new TelemetryTimeRange(0, 0.5));

        Assert.Equal(SurfaceStateKind.NoData, state.Kind);
        Assert.Equal(SurfaceIndicatorKind.None, state.Indicator);
        Assert.Equal("No analysis data for the selected range.", state.Message);
    }

    [Fact]
    public void ForBalance_HidesLiveSurface_WhenBalanceIsNotExpected()
    {
        var state = AnalysisSurfaceState.ForBalance(
            expected: false,
            telemetry: null,
            balanceType: BalanceType.Compression);

        Assert.Equal(SurfaceStateKind.Hidden, state.Kind);
    }

    [Fact]
    public void ForVibration_ShowsNoDataForRecordedSurface_WhenImuExistsButStrokeDataIsMissing()
    {
        var state = AnalysisSurfaceState.ForVibration(
            AnalysisDataAvailability.Empty);

        Assert.Equal(SurfaceStateKind.NoData, state.Kind);
        Assert.Equal(SurfaceIndicatorKind.None, state.Indicator);
    }
}
