using System.IO;
using Avalonia.Platform.Storage;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

using Sufni.App.Infrastructure;
using Sufni.App.Shared.Models;
using Sufni.Telemetry;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Stores;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.MapsAndTracks.Coordinators;

public class TrackCoordinatorTests : IDisposable
{
    private readonly ITrackRepository trackRepository = Substitute.For<ITrackRepository>();
    private readonly ISynchronizableRepository<Track> trackEntityRepository = Substitute.For<ISynchronizableRepository<Track>>();
    private readonly ISessionRepository sessionRepository = Substitute.For<ISessionRepository>();
    private readonly ISessionTelemetryWriter sessionTelemetryWriter = Substitute.For<ISessionTelemetryWriter>();
    private readonly SessionStore sessionStore = new(new StorePublication(new InlineUiThreadDispatcher()));
    private readonly IFilesService filesService = Substitute.For<IFilesService>();
    private readonly IBackgroundTaskRunner backgroundTaskRunner = new InlineBackgroundTaskRunner();
    private readonly ISessionTrackReader sessionTrackReader = Substitute.For<ISessionTrackReader>();
    private readonly IFullTrackPointReader fullTrackPointReader = Substitute.For<IFullTrackPointReader>();

    public void Dispose() => sessionStore.Dispose();

    private TrackCoordinator CreateCoordinator() => new(
        trackRepository,
        trackEntityRepository,
        sessionRepository,
        sessionStore,
        filesService,
        backgroundTaskRunner,
        sessionTrackReader,
        fullTrackPointReader,
        sessionTelemetryWriter);

    [Fact]
    public async Task ImportGpxAsync_ImportsSelectedFiles()
    {
        var file = Substitute.For<IStorageFile>();
        file.OpenReadAsync().Returns(Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(ValidGpx()))));
        filesService.OpenGpxFilesAsync().Returns([file]);
        trackRepository.FindTrackByTimeRangeAsync(Arg.Any<long>(), Arg.Any<long>())
            .Returns(Task.FromResult<Guid?>(null));

        var result = await CreateCoordinator().ImportGpxAsync(cancellationToken: TestContext.Current.CancellationToken);

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

        var result = await CreateCoordinator().ImportGpxAsync(cancellationToken: TestContext.Current.CancellationToken);

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

        await Assert.ThrowsAsync<System.Xml.XmlException>(() => CreateCoordinator().ImportGpxAsync(cancellationToken: TestContext.Current.CancellationToken));
        await trackEntityRepository.DidNotReceive().PutAsync(Arg.Any<Track>());
    }

    [Fact]
    public async Task ImportGpxAsync_Throws_WhenGpxContainsNoValidTrackPoints()
    {
        var file = Substitute.For<IStorageFile>();
        file.OpenReadAsync().Returns(Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(EmptyTrackGpx()))));
        filesService.OpenGpxFilesAsync().Returns([file]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateCoordinator().ImportGpxAsync(cancellationToken: TestContext.Current.CancellationToken));
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
        sessionRepository.GetSessionAsync(sessionId).Returns(new Session(
            sessionId,
            "session",
            "",
            setup: null,
            timestamp: telemetry.Metadata.Timestamp)
        {
            FullTrack = fullTrackId,
            Updated = 99,
            TrackProjectionRevision = 99,
        });
        fullTrackPointReader.GetTrackPointsAsync(fullTrackId, Arg.Any<CancellationToken>())
            .Returns(fullTrack.Points);
        sessionTrackReader.GetSessionTrackAsync(sessionId, 99, Arg.Any<CancellationToken>())
            .Returns(existingTrack);

        var result = await CreateCoordinator().LoadSessionTrackAsync(sessionId, fullTrackId, telemetry, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(fullTrackId, result.FullTrackId);
        Assert.Same(fullTrack.Points, result.FullTrackPoints);
        Assert.Same(existingTrack, result.TrackPoints);
        Assert.Equal(400.0, result.MediaColumnWidth);
        await trackEntityRepository.DidNotReceive().GetAsync(fullTrackId);
        await sessionRepository.DidNotReceive().GetSessionTrackAsync(sessionId);
        await sessionTelemetryWriter.DidNotReceive().PatchSessionTrackAsync(
            Arg.Any<Guid>(),
            Arg.Any<List<TrackPoint>>(),
            Arg.Any<double?>());
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
        var result = await CreateCoordinator().LoadSessionTrackAsync(sessionId, null, telemetry, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(result.FullTrackId);
        Assert.Null(result.TrackPoints);
        await sessionTelemetryWriter.DidNotReceive().PatchSessionTrackAsync(
            Arg.Any<Guid>(),
            Arg.Any<List<TrackPoint>>(),
            Arg.Any<double?>());
    }

    [Fact]
    public async Task UpdateSessionGpsOffsetAsync_RegeneratesSessionTrackAndPersistsOffset()
    {
        var (session, track, telemetry) = PrepareAlignment();
        var sessionId = session.Id;
        var fullTrackId = track.Id;
        const double offsetSeconds = 2.5;

        var result = await CreateCoordinator().UpdateSessionGpsOffsetAsync(
            sessionId,
            fullTrackId,
            telemetry,
            offsetSeconds, cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<GpsOffsetResult.Applied>(result);
        Assert.Equal(2.5, sessionStore.Get(session.Id)!.GpsOffsetSeconds);
        await sessionTelemetryWriter.Received(1).PatchSessionTrackAsync(
            sessionId,
            Arg.Is<List<TrackPoint>>(points =>
                points.Count > 0 &&
                Math.Abs(points[0].Time - (telemetry.Metadata.Timestamp + offsetSeconds)) < 0.000001),
            Arg.Is<double?>(value => value == offsetSeconds));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateSessionGpsOffsetAsync_DistinguishesMissingSessionFromPersistenceFailure(bool missing)
    {
        var (session, track, telemetry) = PrepareAlignment();
        var original = SessionSnapshot.From(session);
        await sessionStore.ReplaceAllAsync([original]);
        if (missing) sessionRepository.GetSessionAsync(session.Id).Returns((Session?)null);
        else sessionTelemetryWriter.PatchSessionTrackAsync(session.Id, Arg.Any<List<TrackPoint>>(), Arg.Any<double?>())
            .ThrowsAsync(new IOException("disk full"));

        var result = await CreateCoordinator().UpdateSessionGpsOffsetAsync(session.Id, track.Id, telemetry, 2.5, TestContext.Current.CancellationToken);

        if (missing) Assert.IsType<GpsOffsetResult.SessionMissing>(result);
        else Assert.Equal(GpsOffsetFailureReason.Save, Assert.IsType<GpsOffsetResult.Failed>(result).Reason);
        Assert.Equal(original, sessionStore.Get(session.Id));
    }

    [Fact]
    public async Task UpdateSessionGpsOffsetAsync_ReturnsNoMatchingTrack_WithoutWriting()
    {
        var (session, track, telemetry) = PrepareAlignment();

        var result = await CreateCoordinator().UpdateSessionGpsOffsetAsync(session.Id, track.Id, telemetry, 100, TestContext.Current.CancellationToken);

        Assert.IsType<GpsOffsetResult.NoMatchingTrack>(result);
        await sessionTelemetryWriter.DidNotReceive().PatchSessionTrackAsync(Arg.Any<Guid>(), Arg.Any<List<TrackPoint>>(), Arg.Any<double?>());
    }

    private (Session Session, Track Track, TelemetryData Telemetry) PrepareAlignment()
    {
        var telemetry = TestTelemetryData.CreateProcessed();
        telemetry.Metadata.Duration = 3;
        var track = new Track
        {
            Id = Guid.NewGuid(),
            Points = Enumerable.Range(0, 7).Select(index => new TrackPoint(telemetry.Metadata.Timestamp + index, index, index, 100)).ToList()
        };
        var session = new Session(Guid.NewGuid(), "session", "", null, telemetry.Metadata.Timestamp)
        {
            FullTrack = track.Id, HasProcessedData = true, Updated = 5
        };
        trackEntityRepository.GetAsync(track.Id).Returns(track);
        sessionRepository.GetSessionAsync(session.Id).Returns(session);
        sessionTelemetryWriter.PatchSessionTrackAsync(session.Id, Arg.Any<List<TrackPoint>>(), Arg.Any<double?>())
            .Returns(call =>
            {
                session.GpsOffsetSeconds = call.ArgAt<double?>(2)!.Value;
                session.Updated = 9;
                return Task.CompletedTask;
            });
        return (session, track, telemetry);
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
