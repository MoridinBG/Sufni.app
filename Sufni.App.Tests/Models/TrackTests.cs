using Sufni.App.Models;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Models;

public class TrackTests
{
    [Fact]
    public void FromGpx_CalculatesSpeed_AndCopiesSecondSpeedToFirstPoint()
    {
        var track = Track.FromGpx(
            """
            <gpx version="1.1" creator="test" xmlns="http://www.topografix.com/GPX/1/1">
              <trk>
                <trkseg>
                  <trkpt lat="42.697700" lon="23.321900">
                    <ele>600</ele>
                    <time>2026-01-02T03:04:07Z</time>
                  </trkpt>
                  <trkpt lat="42.697800" lon="23.322000">
                    <ele>601</ele>
                    <time>2026-01-02T03:04:08Z</time>
                  </trkpt>
                </trkseg>
              </trk>
            </gpx>
            """);

        Assert.NotNull(track);
        Assert.Equal(2, track!.Points.Count);
        Assert.NotNull(track.Points[1].Speed);
        Assert.True(track.Points[1].Speed > 0);
        Assert.Equal(track.Points[1].Speed, track.Points[0].Speed);
        Assert.Equal(600, track.Points[0].Elevation);
        Assert.Equal(601, track.Points[1].Elevation);
    }

    [Fact]
    public void FromGpx_LeavesElevationNull_WhenElevationIsMalformed()
    {
        var track = Track.FromGpx(
            """
            <gpx version="1.1" creator="test" xmlns="http://www.topografix.com/GPX/1/1">
              <trk>
                <trkseg>
                  <trkpt lat="42.697700" lon="23.321900">
                    <ele>not-a-number</ele>
                    <time>2026-01-02T03:04:07Z</time>
                  </trkpt>
                  <trkpt lat="42.697800" lon="23.322000">
                    <time>2026-01-02T03:04:08Z</time>
                  </trkpt>
                </trkseg>
              </trk>
            </gpx>
            """);

        Assert.NotNull(track);
        Assert.Equal(2, track!.Points.Count);
        Assert.Null(track.Points[0].Elevation);
        Assert.Null(track.Points[1].Elevation);
    }

    [Fact]
    public void FromGpsRecords_FiltersInvalidPoints_OrdersTrackPoints_AndCalculatesSpeed()
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
        Assert.NotNull(track.Points[1].Speed);
        Assert.True(track.Points[1].Speed > 0);
        Assert.Equal(track.Points[1].Speed, track.Points[0].Speed);
    }

    [Fact]
    public void FromGpsRecords_SetsSpeedNull_ForDuplicateTimestamp()
    {
        var timestamp = new DateTime(2026, 1, 2, 3, 4, 7, DateTimeKind.Utc);

        var track = Track.FromGpsRecords(
        [
            CreateGpsRecord(timestamp, 42.6977, 23.3219, 600),
            CreateGpsRecord(timestamp, 42.6978, 23.3220, 601),
            CreateGpsRecord(timestamp.AddSeconds(1), 42.6979, 23.3221, 602),
        ]);

        Assert.NotNull(track);
        Assert.Null(track!.Points[0].Speed);
        Assert.Null(track.Points[1].Speed);
        Assert.NotNull(track.Points[2].Speed);
    }

    [Fact]
    public void GenerateSessionTrack_InterpolatesElevationAndSpeed_AndUsesAbsoluteUnixTime()
    {
        const long start = 1_767_312_247;
        var track = new Track
        {
            Points =
            [
                new TrackPoint(start, 0, 0, 100, 10),
                new TrackPoint(start + 1, 10, 10, 110, 20),
                new TrackPoint(start + 2, 20, 20, 120, 30),
            ]
        };

        var sessionTrack = track.GenerateSessionTrack(start, start + 2);

        Assert.NotEmpty(sessionTrack);
        Assert.Equal(start, sessionTrack[0].Time);
        Assert.True(sessionTrack[^1].Time > start);
        Assert.Equal(start + 2, sessionTrack[^1].Time);
        Assert.Equal(100, sessionTrack[0].Elevation);
        Assert.Equal(10, sessionTrack[0].Speed);
        Assert.Equal(120, sessionTrack[^1].Elevation);
        Assert.Equal(30, sessionTrack[^1].Speed);
    }

    [Fact]
    public void GenerateSessionTrack_LeavesSpeedNull_WhenSourceHasNoSpeedSeries()
    {
        const long start = 1_767_312_247;
        var track = new Track
        {
            Points =
            [
                new TrackPoint(start, 0, 0, 100),
                new TrackPoint(start + 1, 10, 10, 110),
                new TrackPoint(start + 2, 20, 20, 120),
            ]
        };

        var sessionTrack = track.GenerateSessionTrack(start, start + 2);

        Assert.NotEmpty(sessionTrack);
        Assert.All(sessionTrack, point => Assert.Null(point.Speed));
        Assert.All(sessionTrack, point => Assert.NotNull(point.Elevation));
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
