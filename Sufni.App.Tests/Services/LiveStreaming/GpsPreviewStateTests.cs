using Sufni.App.Services.LiveStreaming;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Services.LiveStreaming;

public class GpsPreviewStateTests
{
    [Fact]
    public void FromRecord_ReturnsNoFix_WhenRecordIsMissingOrHasNoFix()
    {
        Assert.Equal(GpsPreviewState.NoFix, GpsPreviewState.FromRecord(null));

        var noFix = new GpsRecord(
            Timestamp: new DateTime(2026, 4, 12, 12, 0, 0, DateTimeKind.Utc),
            Latitude: 0,
            Longitude: 0,
            Altitude: 0,
            Speed: 0,
            Heading: 0,
            FixMode: 0,
            Satellites: 0,
            Epe2d: 0,
            Epe3d: 0);

        Assert.Equal(GpsPreviewState.NoFix, GpsPreviewState.FromRecord(noFix));
    }

    [Fact]
    public void FromRecord_ReportsTwoDimensionalFix_AsNotReady()
    {
        var state = GpsPreviewState.FromRecord(new GpsRecord(
            Timestamp: new DateTime(2026, 4, 12, 12, 0, 0, DateTimeKind.Utc),
            Latitude: 48.2082,
            Longitude: 16.3738,
            Altitude: 180f,
            Speed: 6f,
            Heading: 90f,
            FixMode: 1,
            Satellites: 6,
            Epe2d: 2f,
            Epe3d: 4f));

        Assert.True(state.HasFix);
        Assert.False(state.IsReady);
        Assert.Equal(GpsFixKind.TwoDimensional, state.FixKind);
    }

    [Fact]
    public void FromRecord_ReportsThreeDimensionalFix_AsReady()
    {
        var state = GpsPreviewState.FromRecord(new GpsRecord(
            Timestamp: new DateTime(2026, 4, 12, 12, 0, 0, DateTimeKind.Utc),
            Latitude: 48.2082,
            Longitude: 16.3738,
            Altitude: 180f,
            Speed: 6f,
            Heading: 90f,
            FixMode: 2,
            Satellites: 9,
            Epe2d: 1.2f,
            Epe3d: 2.3f));

        Assert.True(state.HasFix);
        Assert.True(state.IsReady);
        Assert.Equal(GpsFixKind.ThreeDimensional, state.FixKind);
    }
}