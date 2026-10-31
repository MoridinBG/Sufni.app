using Sufni.App.MapsAndTracks.Stores;
using System.IO;
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
using Microsoft.Extensions.Time.Testing;
using Sufni.App.Tests.TestSupport.Persistence;
using Sufni.App.Sessions.Models;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.MapsAndTracks.Coordinators;

public class TrackCoordinatorTests
{
    private readonly ITrackRepository trackRepository = Substitute.For<ITrackRepository>();
    private readonly InMemorySynchronizableRepository<Track> trackEntityRepository = new(new FakeTimeProvider());
    private readonly ISessionRepository sessionRepository = Substitute.For<ISessionRepository>();
    private readonly IFilesService filesService = Substitute.For<IFilesService>();
    private readonly IBackgroundTaskRunner backgroundTaskRunner = new InlineBackgroundTaskRunner();
    private readonly ISessionTrackReader sessionTrackReader = Substitute.For<ISessionTrackReader>();
    private readonly IFullTrackPointReader fullTrackPointReader = Substitute.For<IFullTrackPointReader>();

    private readonly StorePublication publication = new(new InlineUiThreadDispatcher());
    private readonly ISessionStoreWriter sessionStore = Substitute.For<ISessionStoreWriter>();
    private readonly ITrackStoreWriter trackStore = Substitute.For<ITrackStoreWriter>();

    private TrackCoordinator CreateCoordinator(ITrackStoreWriter? tracks = null) => new(
        trackRepository,
        trackEntityRepository,
        sessionRepository,
        filesService,
        backgroundTaskRunner,
        sessionTrackReader,
        fullTrackPointReader,
        new GpxTrackReader(),
        tracks ?? trackStore, sessionStore, new InlineUiThreadDispatcher(), publication);

    [Fact]
    public async Task ImportGpxAsync_IndependentInstallationsChooseTheSameContentIdentity()
    {
        var identities = new List<Guid>();
        foreach (var name in new[] { "device-one.gpx", "device-two.gpx" })
        {
            var device = new TrackCoordinatorTests();
            device.filesService.OpenGpxFilesAsync().Returns([TestStorageItems.PickedTextFile(name, ValidGpx())]);
            using var coordinator = device.CreateCoordinator();

            var result = Assert.IsType<GpxImportResult.Completed>(await coordinator.ImportGpxAsync(TestContext.Current.CancellationToken));

            Assert.Equal(1, result.ImportedCount);
            identities.Add(Assert.Single(await device.trackEntityRepository.GetAllAsync()).Id);
        }
        Assert.Equal(identities[0], identities[1]);
    }

    [Fact]
    public async Task ImportGpxAsync_PublishesPersistedTrackMetadata()
    {
        using var store = new TrackStore(new StorePublication(new InlineUiThreadDispatcher()));
        filesService.OpenGpxFilesAsync().Returns([TestStorageItems.PickedTextFile("ride.gpx", ValidGpx())]);
        trackRepository.GetTrackSnapshotsAsync(Arg.Any<IReadOnlyCollection<Guid>>()).Returns(async call =>
        {
            var track = (await trackEntityRepository.GetAsync(call.Arg<IReadOnlyCollection<Guid>>().Single()))!;
            return (IReadOnlyList<TrackSnapshot>)new[] { new TrackSnapshot(track.Id, track.StartTime, track.EndTime, 7) };
        });

        var result = Assert.IsType<GpxImportResult.Completed>(await CreateCoordinator(store)
            .ImportGpxAsync(TestContext.Current.CancellationToken));

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(0, result.AlreadyImportedCount);
        var persisted = Assert.Single(await trackEntityRepository.GetAllAsync());
        Assert.Equal(new TrackSnapshot(persisted.Id, persisted.StartTime, persisted.EndTime, 7), store.Get(persisted.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportGpxAsync_ReusesContentId_AndRestoresDeletedTrack(bool deleted)
    {
        filesService.OpenGpxFilesAsync().Returns([TestStorageItems.PickedTextFile("ride.gpx", ValidGpx())]);
        var coordinator = CreateCoordinator();
        await coordinator.ImportGpxAsync(TestContext.Current.CancellationToken);
        var original = Assert.Single(await trackEntityRepository.GetAllAsync());
        if (deleted) await trackEntityRepository.DeleteAsync(original.Id);
        else trackEntityRepository.WriteFailure = new IOException("duplicate import must not write");
        filesService.OpenGpxFilesAsync().Returns([TestStorageItems.PickedTextFile("renamed.gpx", ValidGpx())]);

        var result = Assert.IsType<GpxImportResult.Completed>(
            await coordinator.ImportGpxAsync(TestContext.Current.CancellationToken));

        Assert.Equal(deleted ? 1 : 0, result.ImportedCount);
        Assert.Equal(deleted ? 0 : 1, result.AlreadyImportedCount);
        Assert.Equal(original.Id, Assert.Single(await trackEntityRepository.GetAllAsync()).Id);
    }

    [Fact]
    public async Task ImportGpxAsync_PreservesDifferentContentWithSameTimeRange()
    {
        filesService.OpenGpxFilesAsync().Returns([
            TestStorageItems.PickedTextFile("first.gpx", ValidGpx()),
            TestStorageItems.PickedTextFile("second.gpx", ValidGpx().Replace("42.0001", "42.0002"))]);

        var result = Assert.IsType<GpxImportResult.Completed>(
            await CreateCoordinator().ImportGpxAsync(TestContext.Current.CancellationToken));

        Assert.Equal(2, result.ImportedCount);
        Assert.Equal(2, (await trackEntityRepository.GetAllAsync()).Select(track => track.Id).Distinct().Count());
    }

    [Fact]
    public async Task ImportGpxAsync_ReturnsInvalidFile_WhenGpxIsInvalid()
    {
        var file = TestStorageItems.PickedTextFile("ride.gpx", "not gpx");
        filesService.OpenGpxFilesAsync().Returns([file]);

        var result = await CreateCoordinator().ImportGpxAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(GpxImportFailureReason.InvalidFile, Assert.IsType<GpxImportResult.Failed>(result).Reason);
        Assert.Empty(await trackEntityRepository.GetAllAsync());
    }

    [Fact]
    public async Task ImportGpxAsync_ReturnsInvalidFile_WhenGpxContainsNoValidTrackPoints()
    {
        var file = TestStorageItems.PickedTextFile("ride.gpx", EmptyTrackGpx());
        filesService.OpenGpxFilesAsync().Returns([file]);

        var result = await CreateCoordinator().ImportGpxAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(GpxImportFailureReason.InvalidFile, Assert.IsType<GpxImportResult.Failed>(result).Reason);
        Assert.Empty(await trackEntityRepository.GetAllAsync());
    }

    [Fact]
    public async Task ImportGpxAsync_StopsAtInvalidFile_AndReportsAlreadyCommittedTracks()
    {
        List<PickedFile> files = [
            TestStorageItems.PickedTextFile("first.gpx", ValidGpx()),
            TestStorageItems.PickedTextFile("broken.gpx", "not gpx"),
            TestStorageItems.PickedTextFile("last.gpx", ValidGpx())];
        filesService.OpenGpxFilesAsync().Returns(files);

        var result = Assert.IsType<GpxImportResult.Failed>(
            await CreateCoordinator().ImportGpxAsync(TestContext.Current.CancellationToken));

        Assert.Equal(GpxImportFailureReason.InvalidFile, result.Reason);
        Assert.Equal(1, result.ImportedCount);
        Assert.Single(await trackEntityRepository.GetAllAsync());
    }

    [Fact]
    public async Task ImportGpxAsync_ReturnsFailure_WhenPersistenceFails()
    {
        var file = TestStorageItems.PickedTextFile("ride.gpx", ValidGpx());
        filesService.OpenGpxFilesAsync().Returns([file]);
        trackEntityRepository.WriteFailure = new IOException("disk full");

        var result = Assert.IsType<GpxImportResult.Failed>(
            await CreateCoordinator().ImportGpxAsync(TestContext.Current.CancellationToken));

        Assert.Equal(GpxImportFailureReason.Import, result.Reason);
        Assert.Equal(0, result.ImportedCount);
        Assert.Empty(await trackEntityRepository.GetAllAsync());
    }

    [Fact]
    public async Task ImportGpxAsync_ReturnsFailure_WhenPickerFails()
    {
        filesService.OpenGpxFilesAsync().ThrowsAsync(new IOException("picker failed"));

        var result = await CreateCoordinator().ImportGpxAsync(TestContext.Current.CancellationToken);

        Assert.Equal(GpxImportFailureReason.Import, Assert.IsType<GpxImportResult.Failed>(result).Reason);
        Assert.Empty(await trackEntityRepository.GetAllAsync());
    }

    [Fact]
    public async Task ImportGpxAsync_ReturnsCanceled_WhenPickerReturnsNoFiles()
    {
        filesService.OpenGpxFilesAsync().Returns([]);

        var result = await CreateCoordinator().ImportGpxAsync(TestContext.Current.CancellationToken);

        Assert.IsType<GpxImportResult.Canceled>(result);
        Assert.Empty(await trackEntityRepository.GetAllAsync());
    }

    [Fact]
    public async Task ImportGpxAsync_PropagatesCancellation_WithoutWriting()
    {
        var file = TestStorageItems.PickedTextFile("ride.gpx", ValidGpx());
        filesService.OpenGpxFilesAsync().Returns([file]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateCoordinator().ImportGpxAsync(cancellation.Token));

        Assert.Empty(await trackEntityRepository.GetAllAsync());
    }

    [Fact]
    public async Task LoadSessionTrackAsync_SelectsFullerImportedTrackOnNextReadWithoutWritingSession()
    {
        using var tracks = new TrackStore(new StorePublication(new InlineUiThreadDispatcher()));
        var sessionId = Guid.NewGuid();
        var telemetry = TestTelemetryData.CreateProcessed();
        telemetry.Metadata.Timestamp = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        telemetry.Metadata.Duration = 2;
        var timestamp = telemetry.Metadata.Timestamp;
        var first = new Track([new TrackPoint(timestamp, 0, 0, 0), new TrackPoint(timestamp + 0.5, 1, 1, 1)]);
        trackEntityRepository.Seed(first);
        await tracks.PublishTracksChangedAsync([new TrackSnapshot(first.Id, first.StartTime, first.EndTime, 1)]);
        var session = new Session(sessionId, "ride", "notes", null, timestamp) { DurationSeconds = 2 };
        sessionRepository.GetSessionAsync(sessionId).Returns(session);
        fullTrackPointReader.GetTrackPointsExactAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(async call => (IReadOnlyList<TrackPoint>?)(await trackEntityRepository.GetAsync(call.Arg<Guid>()))?.Points);
        sessionTrackReader.GetSessionTrackAsync(Arg.Any<SessionTrackKey>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<TrackPoint>)[new TrackPoint(timestamp, 0, 0, 0)]);
        trackRepository.GetTrackSnapshotsAsync(Arg.Any<IReadOnlyCollection<Guid>>()).Returns(async call =>
        {
            var track = (await trackEntityRepository.GetAsync(call.Arg<IReadOnlyCollection<Guid>>().Single()))!;
            return (IReadOnlyList<TrackSnapshot>)new[] { new TrackSnapshot(track.Id, track.StartTime, track.EndTime, 1) };
        });
        var coordinator = CreateCoordinator(tracks);
        var before = await coordinator.LoadSessionTrackAsync(sessionId, telemetry, TestContext.Current.CancellationToken);
        filesService.OpenGpxFilesAsync().Returns([TestStorageItems.PickedTextFile("fuller.gpx", ValidGpx())]);

        Assert.IsType<GpxImportResult.Completed>(await coordinator.ImportGpxAsync(TestContext.Current.CancellationToken));
        var after = await coordinator.LoadSessionTrackAsync(sessionId, telemetry, TestContext.Current.CancellationToken);

        Assert.Equal(first.Id, before.FullTrackId);
        Assert.NotNull(after.FullTrackId);
        Assert.NotEqual(first.Id, after.FullTrackId);

        Assert.Equal("notes", session.Description);
        await sessionRepository.DidNotReceive().PutSessionAsync(Arg.Any<Session>());
        await sessionRepository.DidNotReceive().UpdateGpsAlignmentAsync(
            Arg.Any<Guid>(), Arg.Any<double>());
    }

    [Fact]
    public async Task LoadSessionTrackAsync_ReturnsNoTrackWhenNothingOverlaps()
    {
        var sessionId = Guid.NewGuid();
        var telemetry = TestTelemetryData.CreateProcessed();
        sessionRepository.GetSessionAsync(sessionId).Returns(new Session(sessionId, "ride", "", null, telemetry.Metadata.Timestamp));
        trackStore.GetAll().Returns([]);

        var result = await CreateCoordinator().LoadSessionTrackAsync(sessionId, telemetry, TestContext.Current.CancellationToken);

        Assert.Null(result.FullTrackId);
        Assert.Null(result.TrackPoints);
        await fullTrackPointReader.DidNotReceive().GetTrackPointsExactAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
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
