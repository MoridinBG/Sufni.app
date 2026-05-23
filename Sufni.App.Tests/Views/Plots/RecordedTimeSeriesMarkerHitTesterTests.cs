using Sufni.App.DesktopViews.Plots;
using Sufni.App.Tests.Infrastructure;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Views.Plots;

public class RecordedTimeSeriesMarkerHitTesterTests
{
    [Fact]
    public void TryGetHitMarkerSeconds_ReturnsMarkerInsideThreshold()
    {
        var telemetry = TestTelemetryData.CreateProcessed();
        telemetry.Metadata.Duration = 2;
        telemetry.Markers = [new MarkerData(0.5), new MarkerData(1.5)];

        var hit = RecordedTimeSeriesMarkerHitTester.TryGetHitMarkerSeconds(
            telemetry,
            pointerSeconds: 0.52,
            thresholdSeconds: 0.03,
            out var markerSeconds);

        Assert.True(hit);
        Assert.Equal(0.5, markerSeconds);
    }

    [Fact]
    public void TryGetHitMarkerSeconds_ClampsMarkersToTelemetryDuration()
    {
        var telemetry = TestTelemetryData.CreateProcessed();
        telemetry.Metadata.Duration = 2;
        telemetry.Markers = [new MarkerData(3)];

        var hit = RecordedTimeSeriesMarkerHitTester.TryGetHitMarkerSeconds(
            telemetry,
            pointerSeconds: 2,
            thresholdSeconds: 0.01,
            out var markerSeconds);

        Assert.True(hit);
        Assert.Equal(2, markerSeconds);
    }

    [Fact]
    public void TryGetHitMarkerSeconds_IgnoresInvalidAndOutOfRangeMarkers()
    {
        var telemetry = TestTelemetryData.CreateProcessed();
        telemetry.Metadata.Duration = 2;
        telemetry.Markers = [new MarkerData(double.NaN), new MarkerData(1.5)];

        var hit = RecordedTimeSeriesMarkerHitTester.TryGetHitMarkerSeconds(
            telemetry,
            pointerSeconds: 0.5,
            thresholdSeconds: 0.03,
            out _);

        Assert.False(hit);
    }
}
