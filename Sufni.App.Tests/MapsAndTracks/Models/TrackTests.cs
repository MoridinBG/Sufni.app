using Sufni.App.MapsAndTracks.Models;

namespace Sufni.App.Tests.MapsAndTracks.Models;

public class TrackTests
{
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
    public void GenerateSessionTrack_CopiesGpsQualityOnlyForExactSourceTimes()
    {
        const long start = 1_767_312_247;
        var track = new Track
        {
            Points =
            [
                new TrackPoint(start, 0, 0, 100, 10, 3, 12, 0.5f, 0.8f),
                new TrackPoint(start + 1, 10, 10, 110, 20, 3, 11, 0.6f, 0.9f),
                new TrackPoint(start + 2, 20, 20, 120, 30, 3, 10, 0.7f, 1.0f),
            ]
        };
        var sourceTimes = new HashSet<double> { start, start + 1, start + 2 };

        var sessionTrack = track.GenerateSessionTrack(start, start + 2);

        Assert.NotEmpty(sessionTrack);
        var middle = Assert.Single(sessionTrack, point => point.Time == start + 1);
        Assert.Equal(110, middle.Elevation);
        Assert.Equal((byte)11, middle.Satellites);
        Assert.Contains(sessionTrack, point =>
            !sourceTimes.Contains(point.Time) &&
            point.FixMode is null &&
            point.Satellites is null &&
            point.Epe2d is null &&
            point.Epe3d is null);

        foreach (var point in sessionTrack)
        {
            if (!sourceTimes.Contains(point.Time))
            {
                Assert.Null(point.FixMode);
                Assert.Null(point.Satellites);
                Assert.Null(point.Epe2d);
                Assert.Null(point.Epe3d);
                continue;
            }

            Assert.Equal((byte)3, point.FixMode);
            Assert.NotNull(point.Satellites);
            Assert.NotNull(point.Epe2d);
            Assert.NotNull(point.Epe3d);
        }
    }

    [Fact]
    public void GenerateSessionTrack_DoesNotCopyGpsQuality_WhenFirstPointIsPinnedToSessionStart()
    {
        const long start = 1_767_312_247;
        var track = new Track
        {
            Points =
            [
                new TrackPoint(start + 1, 10, 10, 110, 20, 3, 12, 0.5f, 0.8f),
                new TrackPoint(start + 2, 20, 20, 120, 30, 3, 11, 0.6f, 0.9f),
                new TrackPoint(start + 3, 30, 30, 130, 40, 3, 10, 0.7f, 1.0f),
            ]
        };

        var sessionTrack = track.GenerateSessionTrack(start, start + 3);

        Assert.NotEmpty(sessionTrack);
        Assert.Equal(start, sessionTrack[0].Time);
        Assert.Null(sessionTrack[0].FixMode);
        Assert.Null(sessionTrack[0].Satellites);
        Assert.Null(sessionTrack[0].Epe2d);
        Assert.Null(sessionTrack[0].Epe3d);
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
    public void GenerateSessionTrack_IgnoresDuplicateTimesBeforeInterpolating()
    {
        const long start = 1_767_312_247;
        var track = new Track
        {
            Points =
            [
                new TrackPoint(start, 0, 0, 100, 10),
                new TrackPoint(start, 100, 100, 200, 20),
                new TrackPoint(start + 1, 10, 10, 110, 11),
                new TrackPoint(start + 2, 20, 20, 120, 12),
            ]
        };

        var sessionTrack = track.GenerateSessionTrack(start, start + 2);

        Assert.NotEmpty(sessionTrack);
        Assert.Equal(start, sessionTrack[0].Time);
        Assert.Equal(0, sessionTrack[0].X);
        Assert.Equal(20, sessionTrack[^1].X);
    }

    [Fact]
    public void GenerateSessionTrack_IgnoresNonFiniteCoordinatesBeforeInterpolating()
    {
        const long start = 1_767_312_247;
        var track = new Track
        {
            Points =
            [
                new TrackPoint(start, 0, 0, 100, 10),
                new TrackPoint(start + 1, double.NaN, 10, 110, 11),
                new TrackPoint(start + 2, 20, 20, 120, 12),
            ]
        };

        var sessionTrack = track.GenerateSessionTrack(start, start + 2);

        Assert.NotEmpty(sessionTrack);
        Assert.All(sessionTrack, point =>
        {
            Assert.True(double.IsFinite(point.X));
            Assert.True(double.IsFinite(point.Y));
        });
        Assert.Equal(20, sessionTrack[^1].X);
    }

    [Fact]
    public void GenerateSessionTrack_ReturnsEmpty_WhenFewerThanTwoUsablePointsRemain()
    {
        const long start = 1_767_312_247;
        var track = new Track
        {
            Points =
            [
                new TrackPoint(start, double.NaN, 0, 100, 10),
                new TrackPoint(start + 1, 10, 10, 110, 11),
            ]
        };

        var sessionTrack = track.GenerateSessionTrack(start, start + 1);

        Assert.Empty(sessionTrack);
    }

}
