using Sufni.App.Models;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Models;

public class TrackTests
{
    [Fact]
    public void FromGpsRecords_FiltersInvalidPoints_AndOrdersTrackPoints()
    {
        var track = Track.FromGpsRecords(
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

        Assert.NotNull(track);
        Assert.Equal(2, track!.Points.Count);
        Assert.True(track.Points[0].Time < track.Points[1].Time);
        Assert.Equal(600, track.Points[0].Elevation);
        Assert.Equal(601, track.Points[1].Elevation);
    }

    [Fact]
    public void FromGpsRecords_ReturnsNull_WhenNoValidPointsRemain()
    {
        var track = Track.FromGpsRecords(
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

        Assert.Null(track);
    }

    [Fact]
    public void EmptyTrackCarrier_ReportsNoPoints_AndZeroTimeBounds()
    {
        var track = new Track { Points = [] };

        Assert.False(track.HasPoints);
        Assert.Equal(0, track.StartTime);
        Assert.Equal(0, track.EndTime);
    }
}