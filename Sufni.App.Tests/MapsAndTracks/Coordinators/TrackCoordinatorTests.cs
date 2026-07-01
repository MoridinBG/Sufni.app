using System.IO;
using Avalonia.Platform.Storage;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.MapsAndTracks.Coordinators;

public class TrackCoordinatorTests
{
    private readonly ITrackRepository trackRepository = Substitute.For<ITrackRepository>();
    private readonly ISynchronizableRepository<Track> trackEntityRepository = Substitute.For<ISynchronizableRepository<Track>>();
    private readonly ISessionRepository sessionRepository = Substitute.For<ISessionRepository>();
    private readonly ISessionTelemetryWriter sessionTelemetryWriter = Substitute.For<ISessionTelemetryWriter>();
    private readonly ISessionStoreWriter sessionStore = Substitute.For<ISessionStoreWriter>();
    private readonly IFilesService filesService = Substitute.For<IFilesService>();
    private readonly IBackgroundTaskRunner backgroundTaskRunner = new InlineBackgroundTaskRunner();

    private TrackCoordinator CreateCoordinator() => new(trackRepository, trackEntityRepository, sessionRepository, sessionTelemetryWriter, sessionStore, filesService, backgroundTaskRunner);

    [Fact]
    public async Task ImportGpxAsync_ImportsSelectedFiles()
    {
        var file = Substitute.For<IStorageFile>();
        file.OpenReadAsync().Returns(Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(ValidGpx()))));
        filesService.OpenGpxFilesAsync().Returns([file]);
        trackRepository.FindTrackByTimeRangeAsync(Arg.Any<long>(), Arg.Any<long>())
            .Returns(Task.FromResult<Guid?>(null));

        var result = await CreateCoordinator().ImportGpxAsync();

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(0, result.AlreadyImportedCount);
        await trackEntityRepository.Received(1).PutAsync(Arg.Is<Track>(track => track.Points.Count == 2));
    }

    [Fact]
    public async Task ImportGpxAsync_SkipsFile_WhenTrackTimeRangeAlreadyExists()
    {
        var file = Substitute.For<IStorageFile>();
        file.OpenReadAsync().Returns(Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(ValidGpx()))));
        filesService.OpenGpxFilesAsync().Returns([file]);
        trackRepository.FindTrackByTimeRangeAsync(Arg.Any<long>(), Arg.Any<long>())
            .Returns(Task.FromResult<Guid?>(Guid.NewGuid()));

        var result = await CreateCoordinator().ImportGpxAsync();

        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(1, result.AlreadyImportedCount);
        await trackEntityRepository.DidNotReceive().PutAsync(Arg.Any<Track>());
    }

    [Fact]
    public async Task ImportGpxAsync_Throws_WhenGpxIsInvalid()
    {
        var file = Substitute.For<IStorageFile>();
        file.OpenReadAsync().Returns(Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("not gpx"))));
        filesService.OpenGpxFilesAsync().Returns([file]);

        await Assert.ThrowsAsync<System.Xml.XmlException>(() => CreateCoordinator().ImportGpxAsync());
        await trackEntityRepository.DidNotReceive().PutAsync(Arg.Any<Track>());
    }

    [Fact]
    public async Task ImportGpxAsync_Throws_WhenGpxContainsNoValidTrackPoints()
    {
        var file = Substitute.For<IStorageFile>();
        file.OpenReadAsync().Returns(Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(EmptyTrackGpx()))));
        filesService.OpenGpxFilesAsync().Returns([file]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateCoordinator().ImportGpxAsync());
        await trackEntityRepository.DidNotReceive().PutAsync(Arg.Any<Track>());
    }

    [Fact]
    public async Task LoadSessionTrackAsync_ReturnsExistingSessionTrackWithoutPatch()
    {
        var sessionId = Guid.NewGuid();
        var fullTrackId = Guid.NewGuid();
        var telemetry = TestTelemetryData.CreateProcessed();
        var existingTrack = new List<TrackPoint>
        {
            new(telemetry.Metadata.Timestamp, 1, 1, 0),
            new(telemetry.Metadata.Timestamp + 1, 2, 2, 0),
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
        trackEntityRepository.GetAsync(fullTrackId).Returns(fullTrack);
        sessionRepository.GetSessionTrackAsync(sessionId).Returns(existingTrack);

        var result = await CreateCoordinator().LoadSessionTrackAsync(sessionId, fullTrackId, telemetry);

        Assert.Equal(fullTrackId, result.FullTrackId);
        Assert.Same(fullTrack.Points, result.FullTrackPoints);
        Assert.Same(existingTrack, result.TrackPoints);
        Assert.Equal(400.0, result.MediaColumnWidth);
        await sessionTelemetryWriter.DidNotReceive().PatchSessionTrackAsync(Arg.Any<Guid>(), Arg.Any<List<TrackPoint>>());
    }

    [Fact]
    public async Task LoadSessionTrackAsync_ReturnsUnassociatedWithoutWriting_WhenSessionHasNoFullTrack()
    {
        var sessionId = Guid.NewGuid();
        var telemetry = TestTelemetryData.CreateProcessed();
        telemetry.Metadata.Duration = 3.0;

        // Load is read-only: with no full_track_id the coordinator neither
        // associates a track nor persists anything. Track association is owned by
        // the processed-write path, not the load path.
        var result = await CreateCoordinator().LoadSessionTrackAsync(sessionId, null, telemetry);

        Assert.Null(result.FullTrackId);
        Assert.Null(result.TrackPoints);
        await sessionTelemetryWriter.DidNotReceive().PatchSessionTrackAsync(
            Arg.Any<Guid>(),
            Arg.Any<List<TrackPoint>>());
        await sessionTelemetryWriter.DidNotReceive().PatchSessionTrackAsync(
            Arg.Any<Guid>(),
            Arg.Any<List<TrackPoint>>(),
            Arg.Any<double?>());
    }

    [Fact]
    public async Task UpdateSessionGpsOffsetAsync_RegeneratesSessionTrackAndPersistsOffset()
    {
        var sessionId = Guid.NewGuid();
        var fullTrackId = Guid.NewGuid();
        var telemetry = TestTelemetryData.CreateProcessed();
        telemetry.Metadata.Duration = 3.0;
        var offsetSeconds = 2.5;
        var updatedSession = new Session(
            sessionId,
            "session",
            "",
            setup: null,
            timestamp: telemetry.Metadata.Timestamp)
        {
            FullTrack = fullTrackId,
            HasProcessedData = true,
            GpsOffsetSeconds = offsetSeconds,
            Updated = 9,
        };
        var fullTrack = new Track
        {
            Id = fullTrackId,
            Points =
            [
                new TrackPoint(telemetry.Metadata.Timestamp + 2, 1, 1, 100, 10),
                new TrackPoint(telemetry.Metadata.Timestamp + 3, 2, 2, 110, 20),
                new TrackPoint(telemetry.Metadata.Timestamp + 4, 3, 3, 120, 30),
                new TrackPoint(telemetry.Metadata.Timestamp + 5, 4, 4, 130, 40),
                new TrackPoint(telemetry.Metadata.Timestamp + 6, 5, 5, 140, 50),
            ]
        };
        trackEntityRepository.GetAsync(fullTrackId).Returns(fullTrack);
        sessionRepository.GetSessionAsync(sessionId).Returns(updatedSession);

        var result = await CreateCoordinator().UpdateSessionGpsOffsetAsync(
            sessionId,
            fullTrackId,
            telemetry,
            offsetSeconds);

        // The GPS-offset write is one-way: it persists the regenerated session
        // window + offset and upserts the store so the editor refreshes through its
        // watch reaction. It reports success instead of pushing a result snapshot.
        Assert.True(result);
        await sessionTelemetryWriter.Received(1).PatchSessionTrackAsync(
            sessionId,
            Arg.Is<List<TrackPoint>>(points =>
                points.Count > 0 &&
                Math.Abs(points[0].Time - (telemetry.Metadata.Timestamp + offsetSeconds)) < 0.000001),
            Arg.Is<double?>(value => value == offsetSeconds));
        sessionStore.Received(1).Upsert(Arg.Is<SessionSnapshot>(snapshot =>
            snapshot.Id == sessionId &&
            snapshot.GpsOffsetSeconds == offsetSeconds));
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

    private static string EmptyTrackGpx()
    {
        return """
                             <?xml version="1.0" encoding="UTF-8"?>
                             <gpx version="1.1" creator="tests" xmlns="http://www.topografix.com/GPX/1/1">
                                 <trk>
                                     <name>Example</name>
                                     <trkseg>
                                         <trkpt lat="42.0" lon="23.0"><ele>600</ele></trkpt>
                                     </trkseg>
                                 </trk>
                             </gpx>
                             """;
    }
}
