using Sufni.App.Shared.Models;
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
    [Fact]
    public async Task ImportAsync_ProcessingFailure_KeepsSourceUnacknowledgedAndReportsReason()
    {
        var harness = new ImportWorkflowHarness();
        var (setup, _) = harness.SeedSetupAndBike();
        var file = CreateTelemetryFile(name: "ride", shouldBeImported: true,
            source: new TelemetryFileSource("ride.SST", [1, 2, 3]));
        harness.Reprocessor.ReprocessAsync(Arg.Any<RecordedSessionDomainSnapshot>(), Arg.Any<RecordedSessionSource>(), Arg.Any<CancellationToken>())
            .Returns(new RecordedSessionReprocessResult.Failed(SessionProcessingFailureReason.InvalidCalibration, "calibration diagnostic"));
        using var coordinator = harness.CreateCoordinator();

        await coordinator.ImportAsync([file], setup.Id, TestContext.Current.CancellationToken);

        var completed = Assert.IsType<SessionImportState.Completed>(coordinator.State);
        Assert.Empty(completed.Result.Imported);
        var failure = Assert.IsType<SessionImportFailure.ProcessingFailed>(Assert.Single(completed.Result.Failures));
        Assert.Equal(SessionProcessingFailureReason.InvalidCalibration, failure.Reason);
        Assert.Equal("ride", failure.FileName);
        Assert.Equal(failure, Assert.Single(completed.Events.OfType<SessionImportEvent.Failed>()).Failure);
        await file.DidNotReceive().OnImported();
        await harness.SessionTelemetryWriter.DidNotReceive().PutProcessedSessionAsync(
            Arg.Any<Session>(), Arg.Any<ProcessedTelemetryPayload>(), Arg.Any<Track?>(), Arg.Any<RecordedSessionSource?>());
    }

    [Fact]
    public async Task ImportAsync_ImportsSourcePublishesStoresBeforeAcknowledgementAndReportsProgress()
    {
        var harness = new ImportWorkflowHarness();
        using var coordinator = harness.CreateCoordinator();
        var (setup, _) = harness.SeedSetupAndBike();
        var startTime = new DateTime(2025, 6, 1, 12, 34, 56, DateTimeKind.Utc);
        var telemetrySource = new TelemetryFileSource("ride-01.SST", [1, 2, 3]);
        var file = CreateTelemetryFile(
            name: "ride-01",
            description: "morning lap",
            startTime: startTime,
            shouldBeImported: true,
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
        file.OnImported().Returns(_ =>
        {
            Assert.True(sessionsPublished);
            Assert.True(sourcesPublished);
            Assert.Throws<ObjectDisposedException>(() => telemetrySource.SstBytes);
            return Task.CompletedTask;
        });
        var states = new List<SessionImportState>();
        using var observation = coordinator.Observe().Subscribe(states.Add);

        await coordinator.ImportAsync(
            [file],
            setup.Id);
        var result = Assert.IsType<SessionImportState.Completed>(coordinator.State).Result;

        var imported = Assert.Single(result.Imported);
        Assert.Empty(result.Failures);
        Assert.Equal("ride-01", imported.Name);
        Assert.Equal("morning lap", imported.Description);
        Assert.Equal(new DateTimeOffset(startTime).ToUnixTimeSeconds(), imported.Timestamp);
        await harness.SessionTelemetryWriter.Received(1).PutProcessedSessionAsync(
            Arg.Is<Session>(session =>
                session.Name == "ride-01" &&
                session.Description == "morning lap" &&
                session.Setup == setup.Id),
            Arg.Any<ProcessedTelemetryPayload>(),
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
        await file.Received(1).OnImported();
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
        var file = CreateTelemetryFile(name: "ride", shouldBeImported: true);
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
            [file],
            setup.Id);
        var result = Assert.IsType<SessionImportState.Completed>(coordinator.State).Result;

        Assert.Single(result.Imported);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(SessionImportFailureOperation.Publish, failure.Operation);
        await file.DidNotReceive().OnImported();
        Assert.Contains(
            coordinator.State.Events,
            importEvent => importEvent is SessionImportEvent.Failed { Failure: SessionImportFailure.OperationFailed { FileName: "ride", Reason: SessionImportFailureReason.Publish } });
        Assert.DoesNotContain(coordinator.State.Events, importEvent => importEvent is SessionImportEvent.Imported);
    }

    [Theory]
    [InlineData(true, ImportAction.Import)]
    [InlineData(null, ImportAction.Trash)]
    [InlineData(false, ImportAction.Ignore)]
    public async Task ImportAsync_RoutesImportTrashAndIgnoreActions(
        bool? shouldBeImported,
        ImportAction expectedAction)
    {
        var harness = new ImportWorkflowHarness();
        using var coordinator = harness.CreateCoordinator();
        var (setup, _) = harness.SeedSetupAndBike();
        var file = CreateTelemetryFile(name: "candidate", shouldBeImported: shouldBeImported);

        await coordinator.ImportAsync([file], setup.Id);
        var result = Assert.IsType<SessionImportState.Completed>(coordinator.State).Result;

        switch (expectedAction)
        {
            case ImportAction.Import:
                Assert.Single(result.Imported);
                Assert.Empty(result.Failures);
                await file.Received(1).ReadSourceAsync(Arg.Any<CancellationToken>());
                await file.Received(1).OnImported();
                await file.DidNotReceive().OnTrashed();
                break;
            case ImportAction.Trash:
                Assert.Empty(result.Imported);
                Assert.Empty(result.Failures);
                await file.Received(1).OnTrashed();
                await file.DidNotReceive().ReadSourceAsync(Arg.Any<CancellationToken>());
                await file.DidNotReceive().OnImported();
                break;
            case ImportAction.Ignore:
                Assert.Empty(result.Imported);
                Assert.Empty(result.Failures);
                await file.DidNotReceive().ReadSourceAsync(Arg.Any<CancellationToken>());
                await file.DidNotReceive().OnImported();
                await file.DidNotReceive().OnTrashed();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(expectedAction), expectedAction, null);
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
        var rejected = CreateTelemetryFile(
            name: "bad",
            shouldBeImported: true,
            malformedMessage: "invalid telemetry payload",
            source: rejectedSource);
        var importable = CreateTelemetryFile(
            name: "trimmed",
            shouldBeImported: true,
            malformedMessage: "trailing chunk was trimmed",
            canImport: true,
            source: importableSource);
        var states = new List<SessionImportState>();
        using var observation = coordinator.Observe().Subscribe(states.Add);

        await coordinator.ImportAsync(
            [rejected, importable],
            setup.Id);
        var result = Assert.IsType<SessionImportState.Completed>(coordinator.State).Result;

        Assert.Equal("trimmed", Assert.Single(result.Imported).Name);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("bad", failure.FileName);
        Assert.Equal(SessionImportFailureOperation.Import, failure.Operation);
        await rejected.Received(1).ReadSourceAsync(Arg.Any<CancellationToken>());
        await rejected.DidNotReceive().OnImported();
        await importable.Received(1).OnImported();
        await harness.Reprocessor.Received(1).ReprocessAsync(
            Arg.Is<RecordedSessionDomainSnapshot>(domain => domain.Session.Name == "trimmed"),
            Arg.Any<RecordedSessionSource>(),
            Arg.Any<CancellationToken>());
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
        var broken = CreateTelemetryFile(name: "broken", shouldBeImported: true, source: brokenSource);
        var good = CreateTelemetryFile(name: "ok", shouldBeImported: true, source: goodSource);
        if (failurePoint is ImportFailurePoint.ReadSource)
        {
            broken.ReadSourceAsync(Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("read"));
        }
        else if (failurePoint is ImportFailurePoint.Reprocess)
        {
            harness.Reprocessor
                .ReprocessAsync(
                    Arg.Any<RecordedSessionDomainSnapshot>(),
                    Arg.Any<RecordedSessionSource>(),
                    Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    var domain = callInfo.ArgAt<RecordedSessionDomainSnapshot>(0);
                    var source = callInfo.ArgAt<RecordedSessionSource>(1);
                    return source.SourceName == "broken.SST"
                        ? Task.FromException<RecordedSessionReprocessResult>(new InvalidOperationException("reprocess"))
                        : Task.FromResult(ImportWorkflowHarness.CreateReprocessResult(domain, source));
                });
        }
        else
        {
            harness.SessionTelemetryWriter
                .PutProcessedSessionAsync(
                    Arg.Is<Session>(session => session.Name == "broken"),
                    Arg.Any<ProcessedTelemetryPayload>(),
                    Arg.Any<Track?>(),
                    Arg.Any<RecordedSessionSource?>())
                .ThrowsAsync(new InvalidOperationException("persistence"));
        }

        await coordinator.ImportAsync([broken, good], setup.Id);
        var result = Assert.IsType<SessionImportState.Completed>(coordinator.State).Result;

        Assert.Equal("ok", Assert.Single(result.Imported).Name);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("broken", failure.FileName);
        Assert.Equal(SessionImportFailureOperation.Import, failure.Operation);
        await harness.SessionTelemetryWriter.Received(1).PutProcessedSessionAsync(
            Arg.Is<Session>(session => session.Name == "ok"),
            Arg.Any<ProcessedTelemetryPayload>(),
            Arg.Any<Track?>(),
            Arg.Any<RecordedSessionSource?>());
        Assert.Throws<ObjectDisposedException>(() => goodSource.SstBytes);
        if (brokenSource is not null)
        {
            Assert.Throws<ObjectDisposedException>(() => brokenSource.SstBytes);
        }
    }

    [Fact]
    public async Task ImportAsync_TrashesBatchFilesThroughOneSessionAndDisposesIt()
    {
        var harness = new ImportWorkflowHarness();
        using var coordinator = harness.CreateCoordinator();
        var (setup, _) = harness.SeedSetupAndBike();
        var endpoint = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 1557);
        var session = Substitute.For<IDaqManagementSession>();
        harness.DaqManagementService.OpenSessionAsync("10.0.0.1", 1557, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(session));
        session.TrashFileAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DaqManagementResult>(new DaqManagementResult.Ok()));
        var first = CreateNetworkFile(endpoint, id: 1);
        var second = CreateNetworkFile(endpoint, id: 2);

        await coordinator.ImportAsync([first, second], setup.Id);
        var result = Assert.IsType<SessionImportState.Completed>(coordinator.State).Result;

        Assert.Empty(result.Failures);
        await harness.DaqManagementService.Received(1)
            .OpenSessionAsync("10.0.0.1", 1557, Arg.Any<CancellationToken>());
        await session.Received(1).TrashFileAsync(1, Arg.Any<CancellationToken>());
        await session.Received(1).TrashFileAsync(2, Arg.Any<CancellationToken>());
        await session.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ImportAsync_OverlapsDownloadWithProcessing()
    {
        var harness = new ImportWorkflowHarness();
        using var coordinator = harness.CreateCoordinator();
        var (setup, _) = harness.SeedSetupAndBike();
        var first = CreateTelemetryFile(name: "first", shouldBeImported: true);
        var second = CreateTelemetryFile(name: "second", shouldBeImported: true);
        var secondReadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstProcessingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstProcessing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        second.ReadSourceAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                secondReadStarted.TrySetResult();
                return Task.FromResult(new TelemetryFileSource("second.SST", [1, 2, 3]));
            });
        harness.Reprocessor
            .ReprocessAsync(
                Arg.Any<RecordedSessionDomainSnapshot>(),
                Arg.Any<RecordedSessionSource>(),
                Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var domain = callInfo.ArgAt<RecordedSessionDomainSnapshot>(0);
                var source = callInfo.ArgAt<RecordedSessionSource>(1);
                if (source.SourceName == "first.SST")
                {
                    firstProcessingStarted.TrySetResult();
                    await releaseFirstProcessing.Task;
                }

                return ImportWorkflowHarness.CreateReprocessResult(domain, source);
            });

        var importTask = coordinator.ImportAsync([first, second], setup.Id);

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
        var file = CreateTelemetryFile("first", shouldBeImported: true);
        file.ReadSourceAsync(Arg.Any<CancellationToken>()).Returns(source.Task);
        var anotherFile = CreateTelemetryFile("second", shouldBeImported: true);

        var firstRun = coordinator.ImportAsync([file], setup.Id);
        var secondRun = coordinator.ImportAsync([anotherFile], setup.Id);
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
        await anotherFile.DidNotReceive().ReadSourceAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportAsync_PublishesFailure_AndAllowsAnotherRun()
    {
        var harness = new ImportWorkflowHarness();
        using var coordinator = harness.CreateCoordinator();
        var setupId = Guid.NewGuid();
        harness.SetupRepository.GetAsync(setupId).ThrowsAsync(new InvalidOperationException("unavailable"));

        await coordinator.ImportAsync([], setupId);

        Assert.IsType<SessionImportState.Failed>(coordinator.State);
        var (setup, _) = harness.SeedSetupAndBike();
        await coordinator.ImportAsync([CreateTelemetryFile("recovered", shouldBeImported: true)], setup.Id);

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
        harness.Reprocessor.ReprocessAsync(Arg.Any<RecordedSessionDomainSnapshot>(), Arg.Any<RecordedSessionSource>(),
            Arg.Any<CancellationToken>()).Returns(async call =>
        {
            processing.SetResult();
            await release.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
            return ImportWorkflowHarness.CreateReprocessResult(call.Arg<RecordedSessionDomainSnapshot>(), call.Arg<RecordedSessionSource>());
        });
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = CreateTelemetryFile("queued", shouldBeImported: true);
        var second = CreateTelemetryFile("reading", shouldBeImported: true);
        var third = CreateTelemetryFile("unread", shouldBeImported: true);
        second.ReadSourceAsync(Arg.Any<CancellationToken>()).Returns(call =>
        {
            reading.SetResult();
            return new TaskCompletionSource<TelemetryFileSource>().Task.WaitAsync(call.Arg<CancellationToken>());
        });
        var run = coordinator.ImportAsync([first, second, third], setup.Id);
        await Task.WhenAll(reading.Task, processing.Task).AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        var stop = coordinator.StopAsync();
        Assert.False(stop.IsCompleted);
        release.SetResult();
        await Task.WhenAll(run, stop).AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        var result = Assert.IsType<SessionImportState.Completed>(coordinator.State).Result;
        Assert.Equal("queued", Assert.Single(result.Imported).Name);
        Assert.Empty(result.Failures);
        await first.Received(1).OnImported();
        await third.DidNotReceive().ReadSourceAsync(Arg.Any<CancellationToken>());
    }

    private static NetworkTelemetryFile CreateNetworkFile(IPEndPoint endpoint, int id) =>
        new(endpoint, id, $"{id:00000}.SST",
            DateTimeOffset.FromUnixTimeSeconds(111 + id), TimeSpan.FromSeconds(6))
        { ShouldBeImported = null };

    public enum ImportAction
    {
        Import,
        Trash,
        Ignore
    }

    public enum ImportFailurePoint
    {
        ReadSource,
        Reprocess,
        Persistence
    }
}
