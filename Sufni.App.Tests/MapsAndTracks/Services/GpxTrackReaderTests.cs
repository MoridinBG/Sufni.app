using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Shared.Models;
using Sufni.App.Tests.TestSupport.Fixtures;

namespace Sufni.App.Tests.MapsAndTracks.Services;

public class GpxTrackReaderTests
{
    [Fact]
    public async Task ReadAsync_CalculatesSpeed_AndCopiesSecondSpeedToFirstPoint()
    {
        var track = Assert.IsType<GpxTrackReadResult.Read>(await new GpxTrackReader().ReadAsync(TestStorageItems.PickedTextFile("track.gpx",
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
            """), TestContext.Current.CancellationToken));

        Assert.Equal(2, track.Points.Count);
        Assert.NotNull(track.Points[1].Speed);
        Assert.True(track.Points[1].Speed > 0);
        Assert.Equal(track.Points[1].Speed, track.Points[0].Speed);
        Assert.Equal(600, track.Points[0].Elevation);
        Assert.Equal(601, track.Points[1].Elevation);
        Assert.All(track.Points, point =>
        {
            Assert.Null(point.FixMode);
            Assert.Null(point.Satellites);
            Assert.Null(point.Epe2d);
            Assert.Null(point.Epe3d);
        });
    }

    [Fact]
    public async Task ReadAsync_LeavesElevationNull_WhenElevationIsMalformed()
    {
        var track = Assert.IsType<GpxTrackReadResult.Read>(await new GpxTrackReader().ReadAsync(TestStorageItems.PickedTextFile("track.gpx",
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
            """), TestContext.Current.CancellationToken));

        Assert.Equal(2, track.Points.Count);
        Assert.Null(track.Points[0].Elevation);
        Assert.Null(track.Points[1].Elevation);
    }

    [Theory]
    [InlineData("not XML")]
    [InlineData("<gpx xmlns=\"http://www.topografix.com/GPX/1/1\" />")]
    public async Task ReadAsync_ReturnsInvalidFile_WhenXmlIsMalformedOrHasNoUsablePoints(string contents)
    {
        var result = await new GpxTrackReader().ReadAsync(TestStorageItems.PickedTextFile("track.gpx", contents), TestContext.Current.CancellationToken);

        Assert.Equal(GpxImportFailureReason.InvalidFile, Assert.IsType<GpxTrackReadResult.Failed>(result).Reason);
    }

    [Fact]
    public async Task ReadAsync_ReturnsImportFailure_WhenFileCannotBeOpened()
    {
        var file = new PickedFile("track.gpx", () => Task.FromException<Stream>(new IOException("unavailable")));

        var result = await new GpxTrackReader().ReadAsync(file, TestContext.Current.CancellationToken);

        Assert.Equal(GpxImportFailureReason.Import, Assert.IsType<GpxTrackReadResult.Failed>(result).Reason);
    }

    [Fact]
    public async Task ReadAsync_PropagatesCancellationBeforeOpeningTheFile()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var file = new PickedFile("track.gpx", () => throw new InvalidOperationException("The file should not be opened."));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GpxTrackReader().ReadAsync(file, cancellation.Token));
    }
}
