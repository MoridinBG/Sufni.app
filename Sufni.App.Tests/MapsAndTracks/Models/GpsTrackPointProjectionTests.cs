using Sufni.Telemetry;
using Sufni.App.MapsAndTracks.Models;

namespace Sufni.App.Tests.MapsAndTracks.Models;

public class GpsTrackPointProjectionTests
{
    [Fact]
    public void ProjectAll_FiltersInvalidPoints_OrdersTrackPoints_AndCalculatesSpeed()
    {
        var points = GpsTrackPointProjection.ProjectAll(
        [
            new GpsRecord(
                Timestamp: new DateTime(2026, 1, 2, 3, 4, 8, DateTimeKind.Utc),
                Latitude: 42.6978,
                Longitude: 23.3220,
                Altitude: 601,
                Speed: 11,
                Heading: 91,
                FixMode: 3,
                Satellites: 12,
                Epe2d: 0.5f,
                Epe3d: 0.8f),
            new GpsRecord(
                Timestamp: new DateTime(2026, 1, 2, 3, 4, 7, DateTimeKind.Utc),
                Latitude: 42.6977,
                Longitude: 23.3219,
                Altitude: 600,
                Speed: 10,
                Heading: 90,
                FixMode: 3,
                Satellites: 12,
                Epe2d: 0.5f,
                Epe3d: 0.8f),
            new GpsRecord(
                Timestamp: new DateTime(2026, 1, 2, 3, 4, 9, DateTimeKind.Utc),
                Latitude: double.NaN,
                Longitude: 23.3221,
                Altitude: 602,
                Speed: 12,
                Heading: 92,
                FixMode: 3,
                Satellites: 12,
                Epe2d: 0.5f,
                Epe3d: 0.8f),
            new GpsRecord(
                Timestamp: new DateTime(2026, 1, 2, 3, 4, 10, DateTimeKind.Utc),
                Latitude: 42.6979,
                Longitude: 23.3222,
                Altitude: 603,
                Speed: 13,
                Heading: 93,
                FixMode: 0,
                Satellites: 0,
                Epe2d: 0.5f,
                Epe3d: 0.8f),
        ]);

        Assert.Equal(2, points.Count);
        Assert.True(points[0].Time < points[1].Time);
        Assert.Equal(600, points[0].Elevation);
        Assert.Equal(601, points[1].Elevation);
        Assert.Equal((byte)3, points[0].FixMode);
        Assert.Equal((byte)12, points[0].Satellites);
        Assert.Equal(0.5f, points[0].Epe2d);
        Assert.Equal(0.8f, points[0].Epe3d);
        Assert.Equal((byte)3, points[1].FixMode);
        Assert.Equal((byte)12, points[1].Satellites);
        Assert.Equal(0.5f, points[1].Epe2d);
        Assert.Equal(0.8f, points[1].Epe3d);
        Assert.NotNull(points[1].Speed);
        Assert.True(points[1].Speed > 0);
        Assert.Equal(points[1].Speed, points[0].Speed);
    }

    [Fact]
    public void ProjectAll_SetsSpeedNull_ForDuplicateTimestamp()
    {
        var timestamp = new DateTime(2026, 1, 2, 3, 4, 7, DateTimeKind.Utc);

        var points = GpsTrackPointProjection.ProjectAll(
        [
            CreateGpsRecord(timestamp, 42.6977, 23.3219, 600),
            CreateGpsRecord(timestamp, 42.6978, 23.3220, 601),
            CreateGpsRecord(timestamp.AddSeconds(1), 42.6979, 23.3221, 602),
        ]);

        Assert.Null(points[0].Speed);
        Assert.Null(points[1].Speed);
        Assert.NotNull(points[2].Speed);
    }

    [Fact]
    public void ProjectAll_ReturnsEmpty_WhenNoValidPointsRemain()
    {
        var points = GpsTrackPointProjection.ProjectAll(
        [
            new GpsRecord(
                Timestamp: new DateTime(2026, 1, 2, 3, 4, 10, DateTimeKind.Utc),
                Latitude: double.NaN,
                Longitude: 23.3222,
                Altitude: 603,
                Speed: 13,
                Heading: 93,
                FixMode: 3,
                Satellites: 12,
                Epe2d: 0.5f,
                Epe3d: 0.8f),
            new GpsRecord(
                Timestamp: new DateTime(2026, 1, 2, 3, 4, 11, DateTimeKind.Utc),
                Latitude: 42.6979,
                Longitude: 23.3223,
                Altitude: 604,
                Speed: 14,
                Heading: 94,
                FixMode: 0,
                Satellites: 0,
                Epe2d: 0.5f,
                Epe3d: 0.8f),
        ]);

        Assert.Empty(points);
    }

    private static GpsRecord CreateGpsRecord(DateTime timestamp, double latitude, double longitude, float altitude)
    {
        return new GpsRecord(
            Timestamp: timestamp,
            Latitude: latitude,
            Longitude: longitude,
            Altitude: altitude,
            Speed: 0,
            Heading: 0,
            FixMode: 3,
            Satellites: 12,
            Epe2d: 0.5f,
            Epe3d: 0.8f);
    }
}
