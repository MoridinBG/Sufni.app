using System.IO;
using Avalonia.Platform.Storage;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Tests.Infrastructure;

namespace Sufni.App.Tests.Coordinators;

public class TrackCoordinatorTests
{
    private readonly IDatabaseService database = Substitute.For<IDatabaseService>();
    private readonly IFilesService filesService = Substitute.For<IFilesService>();
    private readonly IBackgroundTaskRunner backgroundTaskRunner = new InlineBackgroundTaskRunner();

    private TrackCoordinator CreateCoordinator() => new(database, filesService, backgroundTaskRunner);

    [Fact]
    public async Task ImportGpxAsync_ImportsSelectedFiles()
    {
        var file = Substitute.For<IStorageFile>();
        file.OpenReadAsync().Returns(Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(ValidGpx()))));
        filesService.OpenGpxFilesAsync().Returns([file]);

        await CreateCoordinator().ImportGpxAsync();

        await database.Received(1).PutAsync(Arg.Is<Track>(track => track.Points.Count == 2));
    }

    [Fact]
    public async Task ImportGpxAsync_Throws_WhenGpxIsInvalid()
    {
        var file = Substitute.For<IStorageFile>();
        file.OpenReadAsync().Returns(Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("not gpx"))));
        filesService.OpenGpxFilesAsync().Returns([file]);

        await Assert.ThrowsAsync<System.Xml.XmlException>(() => CreateCoordinator().ImportGpxAsync());
        await database.DidNotReceive().PutAsync(Arg.Any<Track>());
    }

    [Fact]
    public async Task LoadSessionTrackAsync_ReturnsExistingSessionTrackWithoutPatch()
    {
        var sessionId = Guid.NewGuid();
        var fullTrackId = Guid.NewGuid();
        var telemetry = TestTelemetryData.Create();
        var existingTrack = new List<TrackPoint>
        {
            new(1, 1, 1, 0),
            new(2, 2, 2, 0),
        };
        var fullTrack = new Track
        {
            Id = fullTrackId,
            Points =
            [
                new TrackPoint(telemetry.Metadata.Timestamp, 1, 1, 0),
                new TrackPoint(telemetry.Metadata.Timestamp + 1, 2, 2, 0),
                new TrackPoint(telemetry.Metadata.Timestamp + 2, 3, 3, 0),
            ]
        };
        database.GetAsync<Track>(fullTrackId).Returns(fullTrack);
        database.GetSessionTrackAsync(sessionId).Returns(existingTrack);

        var result = await CreateCoordinator().LoadSessionTrackAsync(sessionId, fullTrackId, telemetry);

        Assert.Equal(fullTrackId, result.FullTrackId);
        Assert.Same(fullTrack.Points, result.FullTrackPoints);
        Assert.Same(existingTrack, result.TrackPoints);
        Assert.Equal(400.0, result.MapVideoWidth);
        await database.DidNotReceive().PatchSessionTrackAsync(Arg.Any<Guid>(), Arg.Any<List<TrackPoint>>());
    }

    [Fact]
    public async Task LoadSessionTrackAsync_AssociatesGeneratesAndPersistsWhenSessionTrackMissing()
    {
        var sessionId = Guid.NewGuid();
        var fullTrackId = Guid.NewGuid();
        var telemetry = TestTelemetryData.Create();
        telemetry.Metadata.Duration = 3.0;
        var fullTrack = new Track
        {
            Id = fullTrackId,
            Points =
            [
                new TrackPoint(telemetry.Metadata.Timestamp - 1, 0, 0, 0),
                new TrackPoint(telemetry.Metadata.Timestamp, 1, 1, 0),
                new TrackPoint(telemetry.Metadata.Timestamp + 1, 2, 2, 0),
                new TrackPoint(telemetry.Metadata.Timestamp + 2, 3, 3, 0),
                new TrackPoint(telemetry.Metadata.Timestamp + 3, 4, 4, 0),
                new TrackPoint(telemetry.Metadata.Timestamp + 4, 5, 5, 0),
            ]
        };

        database.AssociateSessionWithTrackAsync(sessionId).Returns(fullTrackId);
        database.GetAsync<Track>(fullTrackId).Returns(fullTrack);
        database.GetSessionTrackAsync(sessionId).Returns((List<TrackPoint>?)null);

        var result = await CreateCoordinator().LoadSessionTrackAsync(sessionId, null, telemetry);

        await database.Received(1).AssociateSessionWithTrackAsync(sessionId);
        await database.Received(1).PatchSessionTrackAsync(sessionId, Arg.Is<List<TrackPoint>>(points => points.Count > 0));
        Assert.Equal(fullTrackId, result.FullTrackId);
        Assert.NotNull(result.TrackPoints);
        Assert.NotEmpty(result.TrackPoints!);
    }

    private static string ValidGpx()
    {
        return """
               <?xml version="1.0" encoding="UTF-8"?>
               <gpx version="1.1" creator="tests" xmlns="http://www.topografix.com/GPX/1/1">
                 <trk>
                   <name>Example</name>
                   <trkseg>
                     <trkpt lat="42.0" lon="23.0"><ele>600</ele><time>2025-06-01T12:00:00Z</time></trkpt>
                     <trkpt lat="42.0001" lon="23.0001"><ele>601</ele><time>2025-06-01T12:00:01Z</time></trkpt>
                   </trkseg>
                 </trk>
               </gpx>
               """;
    }
}