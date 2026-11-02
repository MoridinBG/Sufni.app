using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.Shared.Models;
using Microsoft.Extensions.Time.Testing;
using Sufni.App.Shared.Stores;
using Sufni.App.Tests.TestSupport.Persistence;
using System.Reactive.Linq;
using System.IO;
using Sufni.App.Tests.TestSupport.Async;
using System.Net;
using System.Threading;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sufni.App.Acquisition.Coordinators;
using Sufni.App.Acquisition.Models;
using Sufni.App.Acquisition.Services;
using Sufni.App.Acquisition.Services.Management;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Tests.TestSupport.Acquisition;
using static Sufni.App.Tests.TestSupport.Fixtures.TestTelemetrySources;

namespace Sufni.App.Tests.Acquisition.Coordinators;

public class ImportSessionsCoordinatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportAsync_UnavailableSourceEndsWithExpectedStateAndLeavesFilesUntouched(bool busy)
    {
        var harness = new ImportWorkflowHarness();
        using var coordinator = harness.CreateCoordinator();
        var source = CreateDataStore();
        var holder = new DaqTransportHolder.Streaming();
        source.OpenImportBatchAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<TelemetryImportBatchAcquisition>(busy
            ? new TelemetryImportBatchAcquisition.Busy(holder) : new TelemetryImportBatchAcquisition.Unavailable()));
        var item = CreateImportItem();

        await coordinator.ImportAsync(source, [item], Guid.NewGuid(), TestContext.Current.CancellationToken);

        if (busy) Assert.Equal(holder, Assert.IsType<SessionImportState.SourceBusy>(coordinator.State).Holder);
        else Assert.IsType<SessionImportState.SourceUnavailable>(coordinator.State);
        await item.File.DidNotReceive().ReadSourceAsync(Arg.Any<CancellationToken>());
        await item.File.DidNotReceive().OnImported();
    }

    [Fact]
    public async Task ImportAsync_PublishesRecordingAndSourceTogether()
    {
        var harness = new ImportWorkflowHarness();
        var (setup, _) = harness.SeedSetupAndBike();
        var repository = new InMemorySessionRepository(new FakeTimeProvider());
        using var sessions = new SessionStore(harness.Publication);
        using var sources = new RecordedSessionSourceStore(harness.Publication);
        using var coordinator = harness.CreateCoordinator(repository.Repository, sessions, sources);
        var file = CreateImportItem();
        using var bytes = new TelemetryFileSource("file.SST", [1, 2, 3]);
        var id = RecordedSessionSourceFactory.CreateImportedSst(bytes).SessionId;
        var observed = new List<(SessionSnapshot? Session, RecordedSessionSourceSnapshot? Source)>();
        using var subscription = harness.Publication.Committed.Subscribe(_ => observed.Add((sessions.Get(id), sources.Get(id))));

        await coordinator.ImportAsync(CreateDataStore(), [file], setup.Id, TestContext.Current.CancellationToken);

        var committed = Assert.Single(observed);
        Assert.Equal(id, committed.Session!.Id);
        Assert.Equal(id, committed.Source!.SessionId);
    }

    [Fact]
    public async Task ImportAsync_RenamedRecording_ReusesPersistedSessionAndAcknowledgesFile()
    {
        var harness = new ImportWorkflowHarness();
        var (setup, _) = harness.SeedSetupAndBike();
        var repository = new InMemorySessionRepository(new FakeTimeProvider());
        var publication = harness.Publication;
        using var sessions = new SessionStore(publication);
        using var sources = new RecordedSessionSourceStore(publication);
        using var coordinator = harness.CreateCoordinator(repository.Repository, sessions, sources);
        await coordinator.ImportAsync(CreateDataStore(), [CreateImportItem(name: "first", description: "notes")], setup.Id);
        var original = Assert.Single((await repository.Repository.GetSessionsAsync()));
        var renamed = CreateImportItem(name: "renamed", description: "other notes");

        await coordinator.ImportAsync(CreateDataStore(), [renamed], setup.Id);

        var completed = Assert.IsType<SessionImportState.Completed>(coordinator.State);
        Assert.Empty(completed.Result.Imported);
        Assert.Empty(completed.Result.Failures);
        Assert.Equal(original.Id, Assert.Single(completed.Events.OfType<SessionImportEvent.AlreadyImported>()).SessionId);
        Assert.Equal("first", Assert.Single(await repository.Repository.GetSessionsAsync()).Name);
        Assert.Equal("notes", sessions.Get(original.Id)!.Description);
        Assert.Equal("first.SST", sources.Get(original.Id)!.SourceName);
        await renamed.File.Received(1).OnImported();
    }

    [Fact]
    public async Task ImportAsync_PublicationRetryFromNewCoordinator_UsesPersistedCheckpointWithoutProcessingOrWritingAgain()
    {
        var harness = new ImportWorkflowHarness();
        var (setup, _) = harness.SeedSetupAndBike();
        var repository = new InMemorySessionRepository(new FakeTimeProvider());
        using var sessions = new SessionStore(harness.Publication);
        using var sources = new RecordedSessionSourceStore(harness.Publication);
        using var tracks = new TrackStore(harness.Publication);
        var failingSourceWriter = Substitute.For<IRecordedSessionSourceStoreWriter>();
        failingSourceWriter.PublishSourcesChangedAsync(Arg.Any<IReadOnlyCollection<RecordedSessionSourceSnapshot>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("publication diagnostic"));
        var firstFile = CreateImportItem(name: "first");
        using (var first = harness.CreateCoordinator(repository.Repository, sessions, failingSourceWriter, tracks))
        {
            await first.ImportAsync(CreateDataStore(), [firstFile], setup.Id, TestContext.Current.CancellationToken);
            Assert.Equal(SessionImportFailureOperation.Publish,
                Assert.Single(Assert.IsType<SessionImportState.Completed>(first.State).Result.Failures).Operation);
        }
        await firstFile.File.DidNotReceive().OnImported();
        var persisted = Assert.Single(await repository.Repository.GetSessionsAsync());
        var track = new TrackSnapshot(Guid.NewGuid(), 100, 200, 1);
        harness.TrackRepository.GetTrackSnapshotsAsync(null).Returns(Task.FromResult<IReadOnlyList<TrackSnapshot>>([track]));
        repository.WriteFailure = new IOException("retry must not write again");
        harness.Reprocessor.ReprocessAsync(Arg.Any<RecordedSessionDerivationInput>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("retry must not reprocess"));
        using var retry = harness.CreateCoordinator(repository.Repository, sessions, sources, tracks);
        var renamed = CreateImportItem(name: "renamed");

        await retry.ImportAsync(CreateDataStore(), [renamed], setup.Id, TestContext.Current.CancellationToken);

        var completed = Assert.IsType<SessionImportState.Completed>(retry.State);
        Assert.Empty(completed.Result.Failures);
        Assert.Empty(completed.Result.Imported);
        Assert.Equal(persisted.Id, Assert.Single(completed.Events.OfType<SessionImportEvent.AlreadyImported>()).SessionId);
        Assert.Single(await repository.Repository.GetSessionsAsync());
        Assert.Equal("first", sessions.Get(persisted.Id)!.Name);
        Assert.Equal("first.SST", sources.Get(persisted.Id)!.SourceName);
        Assert.Equal(track, Assert.Single(tracks.GetAll()));
        await renamed.File.Received(1).OnImported();
    }

    [Fact]
    public async Task ImportAsync_AcknowledgementFailure_KeepsCommittedRecordingAndDoesNotCountAsImportFailure()
    {
        var harness = new ImportWorkflowHarness();
        var (setup, _) = harness.SeedSetupAndBike();
        var repository = new InMemorySessionRepository(new FakeTimeProvider());
        using var sessions = new SessionStore(harness.Publication);
        using var sources = new RecordedSessionSourceStore(harness.Publication);
        using var coordinator = harness.CreateCoordinator(repository.Repository, sessions, sources);
        var file = CreateImportItem(name: "ride");
        file.File.OnImported().ThrowsAsync(new IOException("acknowledgement diagnostic"));

        await coordinator.ImportAsync(CreateDataStore(), [file], setup.Id, TestContext.Current.CancellationToken);

        var completed = Assert.IsType<SessionImportState.Completed>(coordinator.State);
        var imported = Assert.Single(completed.Result.Imported);
        Assert.Empty(completed.Result.Failures);
        Assert.Equal(imported.Id, Assert.Single(await repository.Repository.GetSessionsAsync()).Id);
        Assert.NotNull(sessions.Get(imported.Id));
        Assert.NotNull(sources.Get(imported.Id));
        Assert.Single(completed.Events.OfType<SessionImportEvent.Imported>());
        var acknowledgement = Assert.Single(completed.Events.OfType<SessionImportEvent.AcknowledgementFailed>());
        Assert.Equal("ride", acknowledgement.FileName);
        Assert.Contains("acknowledgement diagnostic", acknowledgement.Details);
        Assert.Empty(completed.Events.OfType<SessionImportEvent.Failed>());
    }

    [Fact]
    public async Task ImportAsync_ProcessingFailure_KeepsSourceUnacknowledgedAndReportsReason()
    {
        var harness = new ImportWorkflowHarness();
        var (setup, _) = harness.SeedSetupAndBike();
        var file = CreateImportItem(name: "ride", action: ImportFileAction.Import,
            source: new TelemetryFileSource("ride.SST", [1, 2, 3]));
        harness.Reprocessor.ReprocessAsync(Arg.Any<RecordedSessionDerivationInput>(), Arg.Any<CancellationToken>())
            .Returns(new RecordedSessionReprocessResult.Failed(SessionProcessingFailureReason.InvalidCalibration, "calibration diagnostic"));
        using var coordinator = harness.CreateCoordinator();

        await coordinator.ImportAsync(CreateDataStore(), [file], setup.Id, TestContext.Current.CancellationToken);

        var completed = Assert.IsType<SessionImportState.Completed>(coordinator.State);
        Assert.Empty(completed.Result.Imported);
        var failure = Assert.IsType<SessionImportFailure.ProcessingFailed>(Assert.Single(completed.Result.Failures));
        Assert.Equal(SessionProcessingFailureReason.InvalidCalibration, failure.Reason);
        Assert.Equal("ride", failure.FileName);
        Assert.Equal(failure, Assert.Single(completed.Events.OfType<SessionImportEvent.Failed>()).Failure);
        await file.File.DidNotReceive().OnImported();
        await harness.SessionRepository.DidNotReceive().ImportProcessedSessionAsync(
            Arg.Any<Session>(), Arg.Any<Track?>(), Arg.Any<RecordedSessionSource>());
    }

    [Fact]
    public async Task ImportAsync_ImportsSourcePublishesStoresBeforeAcknowledgementAndReportsProgress()
    {
        var harness = new ImportWorkflowHarness();
        using var coordinator = harness.CreateCoordinator();
        var (setup, _) = harness.SeedSetupAndBike();
        var startTime = new DateTime(2025, 6, 1, 12, 34, 56, DateTimeKind.Utc);
        var telemetrySource = new TelemetryFileSource("ride-01.SST", [1, 2, 3]);
        var file = CreateImportItem(
            name: "ride-01",
            description: "morning lap",
            startTime: startTime,
            action: ImportFileAction.Import,
            source: telemetrySource);
        var sessionsPublished = false;
        var sourcesPublished = false;
        harness.SessionStore.PublishSessionsChangedAsync(
                Arg.Any<IReadOnlyCollection<SessionSnapshot>>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                sessionsPublished = true;
                return Task.CompletedTask;
            });
        harness.SourceStore.PublishSourcesChangedAsync(
                Arg.Any<IReadOnlyCollection<RecordedSessionSourceSnapshot>>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                sourcesPublished = true;
                return Task.CompletedTask;
            });
        file.File.OnImported().Returns(_ =>
        {
            Assert.True(sessionsPublished);
            Assert.True(sourcesPublished);
            Assert.Throws<ObjectDisposedException>(() => telemetrySource.SstBytes);
            return Task.CompletedTask;
        });
        var states = new List<SessionImportState>();
        using var observation = coordinator.Observe().Subscribe(states.Add);

        await coordinator.ImportAsync(
            CreateDataStore(), [file],
            setup.Id);
        var result = Assert.IsType<SessionImportState.Completed>(coordinator.State).Result;

        var imported = Assert.Single(result.Imported);
        Assert.Empty(result.Failures);
        Assert.Equal("ride-01", imported.Name);
        Assert.Equal("morning lap", imported.Description);
        Assert.Equal(new DateTimeOffset(startTime).ToUnixTimeSeconds(), imported.Timestamp);
        await harness.SessionRepository.Received(1).ImportProcessedSessionAsync(
            Arg.Is<Session>(session =>
                session.Name == "ride-01" &&
                session.Description == "morning lap" &&
                session.Setup == setup.Id),
            null,
            Arg.Is<RecordedSessionSource>(source =>
                source.SourceKind == RecordedSessionSourceKind.ImportedSst &&
                source.SourceName == "ride-01.SST" &&
                RecordedSessionSourcePayloadCodec.DecompressImportedSst(source.Payload).SequenceEqual(new byte[] { 1, 2, 3 }) &&
                RecordedSessionSourceHash.Matches(source)));
        await harness.SessionStore.Received(1).PublishSessionsChangedAsync(
            Arg.Is<IReadOnlyCollection<SessionSnapshot>>(ids => ids.Count == 1 && ids.Select(snapshot => snapshot.Id).Contains(imported.Id)),
            Arg.Any<CancellationToken>());
        await harness.SourceStore.Received(1).PublishSourcesChangedAsync(
            Arg.Is<IReadOnlyCollection<RecordedSessionSourceSnapshot>>(ids => ids.Count == 1 && ids.Select(snapshot => snapshot.SessionId).Contains(imported.Id)),
            Arg.Any<CancellationToken>());
        await file.File.Received(1).OnImported();
        Assert.Contains(states, state => state is SessionImportState.Running { Current: 1, Total: 1 });
        Assert.Contains(coordinator.State.Events, e => e is SessionImportEvent.Imported importedEvent && importedEvent.Snapshot.Id == imported.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportAsync_DoesNotAcknowledgeCommittedFiles_WhenPublicationFails(
        bool sourcePublicationFails)
    {
        var harness = new ImportWorkflowHarness();
        using var coordinator = harness.CreateCoordinator();
        var (setup, _) = harness.SeedSetupAndBike();
        var file = CreateImportItem(name: "ride", action: ImportFileAction.Import);
        if (sourcePublicationFails)
        {
            harness.SourceStore.PublishSourcesChangedAsync(
                    Arg.Any<IReadOnlyCollection<RecordedSessionSourceSnapshot>>(),
                    Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("source publish failed"));
        }
        else
        {
            harness.SessionStore.PublishSessionsChangedAsync(
                    Arg.Any<IReadOnlyCollection<SessionSnapshot>>(),
                    Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("session publish failed"));
        }
        var states = new List<SessionImportState>();
        using var observation = coordinator.Observe().Subscribe(states.Add);

        await coordinator.ImportAsync(
            CreateDataStore(), [file],
            setup.Id);
        var result = Assert.IsType<SessionImportState.Completed>(coordinator.State).Result;

        Assert.Single(result.Imported);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(SessionImportFailureOperation.Publish, failure.Operation);
        await file.File.DidNotReceive().OnImported();
        Assert.Contains(
            coordinator.State.Events,
            importEvent => importEvent is SessionImportEvent.Failed { Failure: SessionImportFailure.OperationFailed { FileName: "ride", Reason: SessionImportFailureReason.Publish } });
        Assert.DoesNotContain(coordinator.State.Events, importEvent => importEvent is SessionImportEvent.Imported);
    }

    [Theory]
    [InlineData(ImportFileAction.Import)]
    [InlineData(ImportFileAction.Trash)]
    [InlineData(ImportFileAction.Ignore)]
    public async Task ImportAsync_RoutesImportTrashAndIgnoreActions(
        ImportFileAction action)
    {
        var harness = new ImportWorkflowHarness();
        using var coordinator = harness.CreateCoordinator();
        var (setup, _) = harness.SeedSetupAndBike();
        var file = CreateImportItem(name: "candidate", action: action);

        await coordinator.ImportAsync(CreateDataStore(), [file], setup.Id);
        var result = Assert.IsType<SessionImportState.Completed>(coordinator.State).Result;

        switch (action)
        {
            case ImportFileAction.Import:
                Assert.Single(result.Imported);
                Assert.Empty(result.Failures);
                await file.File.Received(1).ReadSourceAsync(Arg.Any<CancellationToken>());
                await file.File.Received(1).OnImported();
                await file.File.DidNotReceive().OnTrashed();
                break;
            case ImportFileAction.Trash:
                Assert.Empty(result.Imported);
                Assert.Empty(result.Failures);
                await file.File.Received(1).OnTrashed();
                await file.File.DidNotReceive().ReadSourceAsync(Arg.Any<CancellationToken>());
                await file.File.DidNotReceive().OnImported();
                break;
            case ImportFileAction.Ignore:
                Assert.Empty(result.Imported);
                Assert.Empty(result.Failures);
                await file.File.DidNotReceive().ReadSourceAsync(Arg.Any<CancellationToken>());
                await file.File.DidNotReceive().OnImported();
                await file.File.DidNotReceive().OnTrashed();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    [Fact]
    public async Task ImportAsync_SkipsMalformedFileUnlessItIsExplicitlyImportable()
    {
        var harness = new ImportWorkflowHarness();
        using var coordinator = harness.CreateCoordinator();
        var (setup, _) = harness.SeedSetupAndBike();
        var rejectedSource = new TelemetryFileSource("bad.SST", [1, 2, 3]);
        var importableSource = new TelemetryFileSource("trimmed.SST", [4, 5, 6]);
        var rejected = CreateImportItem(
            name: "bad",
            action: ImportFileAction.Import,
            malformedMessage: "invalid telemetry payload",
            source: rejectedSource);
        var importable = CreateImportItem(
            name: "trimmed",
            action: ImportFileAction.Import,
            malformedMessage: "trailing chunk was trimmed",
            canImport: true,
            source: importableSource);
        var states = new List<SessionImportState>();
        using var observation = coordinator.Observe().Subscribe(states.Add);

        await coordinator.ImportAsync(
            CreateDataStore(), [rejected, importable],
            setup.Id);
        var result = Assert.IsType<SessionImportState.Completed>(coordinator.State).Result;

        Assert.Equal("trimmed", Assert.Single(result.Imported).Name);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("bad", failure.FileName);
        Assert.Equal(SessionImportFailureOperation.Import, failure.Operation);
        await rejected.File.Received(1).ReadSourceAsync(Arg.Any<CancellationToken>());
        await rejected.File.DidNotReceive().OnImported();
        await importable.File.Received(1).OnImported();
        await harness.Reprocessor.Received(1).ReprocessAsync(Arg.Is<RecordedSessionDerivationInput>(domain => domain.Session.Name == "trimmed"), Arg.Any<CancellationToken>());
        Assert.Throws<ObjectDisposedException>(() => rejectedSource.SstBytes);
        Assert.Throws<ObjectDisposedException>(() => importableSource.SstBytes);
        Assert.Contains(coordinator.State.Events, e => e is SessionImportEvent.Failed { Failure.FileName: "bad" });
    }

    [Theory]
    [InlineData(ImportFailurePoint.ReadSource)]
    [InlineData(ImportFailurePoint.Reprocess)]
    [InlineData(ImportFailurePoint.Persistence)]
    public async Task ImportAsync_ContinuesAfterPerFileReadReprocessOrPersistenceFailure(ImportFailurePoint failurePoint)
    {
        var harness = new ImportWorkflowHarness();
        using var coordinator = harness.CreateCoordinator();
        var (setup, _) = harness.SeedSetupAndBike();
        var brokenSource = failurePoint is ImportFailurePoint.ReadSource
            ? null
            : new TelemetryFileSource("broken.SST", [1, 2, 3]);
        var goodSource = new TelemetryFileSource("ok.SST", [4, 5, 6]);
        var broken = CreateImportItem(name: "broken", action: ImportFileAction.Import, source: brokenSource);
        var good = CreateImportItem(name: "ok", action: ImportFileAction.Import, source: goodSource);
        if (failurePoint is ImportFailurePoint.ReadSource)
        {
            broken.File.ReadSourceAsync(Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("read"));
        }
        else if (failurePoint is ImportFailurePoint.Reprocess)
        {
            harness.Reprocessor
                .ReprocessAsync(Arg.Any<RecordedSessionDerivationInput>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    var domain = callInfo.ArgAt<RecordedSessionDerivationInput>(0);
                    var source = domain.Source;
                    return source.SourceName == "broken.SST"
                        ? Task.FromException<RecordedSessionReprocessResult>(new InvalidOperationException("reprocess"))
                        : Task.FromResult(ImportWorkflowHarness.CreateReprocessResult(domain));
                });
        }
        else
        {
            harness.SessionRepository
                .ImportProcessedSessionAsync(
                    Arg.Is<Session>(session => session.Name == "broken"),
                    Arg.Any<Track?>(),
                    Arg.Any<RecordedSessionSource>())
                .ThrowsAsync(new InvalidOperationException("persistence"));
        }

        await coordinator.ImportAsync(CreateDataStore(), [broken, good], setup.Id);
        var result = Assert.IsType<SessionImportState.Completed>(coordinator.State).Result;

        Assert.Equal("ok", Assert.Single(result.Imported).Name);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("broken", failure.FileName);
        Assert.Equal(SessionImportFailureOperation.Import, failure.Operation);
        await harness.SessionRepository.Received(1).ImportProcessedSessionAsync(
            Arg.Is<Session>(session => session.Name == "ok"),
            Arg.Any<Track?>(),
            Arg.Any<RecordedSessionSource>());
        Assert.Throws<ObjectDisposedException>(() => goodSource.SstBytes);
        if (brokenSource is not null)
        {
            Assert.Throws<ObjectDisposedException>(() => brokenSource.SstBytes);
        }
    }

    [Fact]
    public async Task ImportAsync_HoldsSourceBatchUntilFileWorkFinishes_ThenDisposesIt()
    {
        var harness = new ImportWorkflowHarness();
        using var coordinator = harness.CreateCoordinator();
        var (setup, _) = harness.SeedSetupAndBike();
        var source = CreateDataStore();
        var batch = Substitute.For<IAsyncDisposable>();
        source.OpenImportBatchAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TelemetryImportBatchAcquisition>(new TelemetryImportBatchAcquisition.Acquired(batch)));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = CreateImportItem("first", action: ImportFileAction.Trash);
        var second = CreateImportItem("second", action: ImportFileAction.Trash);
        first.File.OnTrashed().Returns(_ => { entered.SetResult(); return release.Task; });

        var run = coordinator.ImportAsync(source, [first, second], setup.Id, TestContext.Current.CancellationToken);
        await entered.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        await batch.DidNotReceive().DisposeAsync();
        release.SetResult();
        await run.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(Assert.IsType<SessionImportState.Completed>(coordinator.State).Result.Failures);
        await second.File.Received(1).OnTrashed();
        await batch.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ImportAsync_OverlapsDownloadWithProcessing()
    {
        var harness = new ImportWorkflowHarness();
        using var coordinator = harness.CreateCoordinator();
        var (setup, _) = harness.SeedSetupAndBike();
        var first = CreateImportItem(name: "first", action: ImportFileAction.Import);
        var second = CreateImportItem(name: "second", action: ImportFileAction.Import);
        var secondReadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstProcessingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstProcessing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        second.File.ReadSourceAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                secondReadStarted.TrySetResult();
                return Task.FromResult(new TelemetryFileSource("second.SST", [1, 2, 3]));
            });
        harness.Reprocessor
            .ReprocessAsync(Arg.Any<RecordedSessionDerivationInput>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var domain = callInfo.ArgAt<RecordedSessionDerivationInput>(0);
                var source = domain.Source;
                if (source.SourceName == "first.SST")
                {
                    firstProcessingStarted.TrySetResult();
                    await releaseFirstProcessing.Task;
                }

                return ImportWorkflowHarness.CreateReprocessResult(domain);
            });

        var importTask = coordinator.ImportAsync(CreateDataStore(), [first, second], setup.Id);

        await firstProcessingStarted.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        await secondReadStarted.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        releaseFirstProcessing.SetResult();
        await importTask;
        var result = Assert.IsType<SessionImportState.Completed>(coordinator.State).Result;

        Assert.Equal(2, result.Imported.Count);
    }

    [Fact]
    public async Task ImportAsync_JoinsActiveBatch_AndReplaysProgressToNewObservers()
    {
        var harness = new ImportWorkflowHarness();
        var (setup, _) = harness.SeedSetupAndBike();
        using var coordinator = harness.CreateCoordinator();
        var source = new TaskCompletionSource<TelemetryFileSource>(TaskCreationOptions.RunContinuationsAsynchronously);
        var file = CreateImportItem("first", action: ImportFileAction.Import);
        file.File.ReadSourceAsync(Arg.Any<CancellationToken>()).Returns(source.Task);
        var anotherFile = CreateImportItem("second", action: ImportFileAction.Import);

        var firstRun = coordinator.ImportAsync(CreateDataStore(), [file], setup.Id);
        var secondRun = coordinator.ImportAsync(CreateDataStore(), [anotherFile], setup.Id);
        SessionImportState? observed = null;
        using var observation = coordinator.Observe().Subscribe(value => observed = value);

        Assert.IsType<SessionImportState.Running>(observed);
        Assert.False(firstRun.IsCompleted);
        Assert.False(secondRun.IsCompleted);
        source.SetResult(new TelemetryFileSource("first.SST", [1, 2, 3]));
        await Task.WhenAll(firstRun, secondRun).AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        var completed = Assert.IsType<SessionImportState.Completed>(observed);
        Assert.Equal("first", Assert.Single(completed.Result.Imported).Name);
        Assert.Contains(completed.Events, message => message is SessionImportEvent.Imported);
        await anotherFile.File.DidNotReceive().ReadSourceAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportAsync_PublishesFailure_AndAllowsAnotherRun()
    {
        var harness = new ImportWorkflowHarness();
        using var coordinator = harness.CreateCoordinator();
        var setupId = Guid.NewGuid();
        harness.SetupRepository.GetAsync(setupId).ThrowsAsync(new InvalidOperationException("unavailable"));

        await coordinator.ImportAsync(CreateDataStore(), [], setupId);

        Assert.IsType<SessionImportState.Failed>(coordinator.State);
        var (setup, _) = harness.SeedSetupAndBike();
        await coordinator.ImportAsync(CreateDataStore(), [CreateImportItem("recovered", action: ImportFileAction.Import)], setup.Id);

        var completed = Assert.IsType<SessionImportState.Completed>(coordinator.State);
        Assert.Equal("recovered", Assert.Single(completed.Result.Imported).Name);
    }

    [Fact]
    public async Task Stop_CancelsIntake_AndWaitsForQueuedImportsToPublishAndAcknowledge()
    {
        var harness = new ImportWorkflowHarness();
        var (setup, _) = harness.SeedSetupAndBike();
        using var coordinator = harness.CreateCoordinator();
        var processing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Reprocessor.ReprocessAsync(Arg.Any<RecordedSessionDerivationInput>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            processing.SetResult();
            await release.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
            return ImportWorkflowHarness.CreateReprocessResult(call.Arg<RecordedSessionDerivationInput>());
        });
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = CreateImportItem("queued", action: ImportFileAction.Import);
        var second = CreateImportItem("reading", action: ImportFileAction.Import);
        var third = CreateImportItem("unread", action: ImportFileAction.Import);
        second.File.ReadSourceAsync(Arg.Any<CancellationToken>()).Returns(call =>
        {
            reading.SetResult();
            return new TaskCompletionSource<TelemetryFileSource>().Task.WaitAsync(call.Arg<CancellationToken>());
        });
        var run = coordinator.ImportAsync(CreateDataStore(), [first, second, third], setup.Id);
        await Task.WhenAll(reading.Task, processing.Task).AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        var stop = coordinator.StopAsync();
        Assert.False(stop.IsCompleted);
        release.SetResult();
        await Task.WhenAll(run, stop).AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        var result = Assert.IsType<SessionImportState.Completed>(coordinator.State).Result;
        Assert.Equal("queued", Assert.Single(result.Imported).Name);
        Assert.Empty(result.Failures);
        await first.File.Received(1).OnImported();
        await third.File.DidNotReceive().ReadSourceAsync(Arg.Any<CancellationToken>());
    }

    public enum ImportFailurePoint
    {
        ReadSource,
        Reprocess,
        Persistence
    }
}
