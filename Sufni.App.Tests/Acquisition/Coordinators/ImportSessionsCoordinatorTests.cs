using System.IO;
using System.Net;
using System.Threading;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sufni.App.Acquisition.Coordinators;
using Sufni.App.Acquisition.Models;
using Sufni.App.Acquisition.Services;
using Sufni.App.Acquisition.Services.Management;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;
using Sufni.App.ExtensionHost.Contracts.Services;
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
    public async Task OpenAsync_OpensImportSessionsThroughTheEditorFactory()
    {
        var harness = new ImportWorkflowHarness();

        await harness.CreateCoordinator().OpenAsync();

        harness.EditorFactory.Received(1).OpenImportSessions();
    }

    [Fact]
    public async Task ImportAsync_ImportsSourcePublishesStoresBeforeAcknowledgementAndReportsProgress()
    {
        var harness = new ImportWorkflowHarness();
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
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                sessionsPublished = true;
                return Task.CompletedTask;
            });
        harness.SourceStore.PublishSourcesChangedAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
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
            Assert.Equal(0, telemetrySource.AllocatedCapacity);
            Assert.Throws<ObjectDisposedException>(() => telemetrySource.SstBytes);
            return Task.CompletedTask;
        });
        var progressEvents = new List<SessionImportEvent>();

        var result = await harness.CreateCoordinator().ImportAsync(
            [file],
            setup.Id,
            harness.CaptureImportProgress(progressEvents));

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
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(imported.Id)),
            Arg.Any<CancellationToken>());
        await harness.SourceStore.Received(1).PublishSourcesChangedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(imported.Id)),
            Arg.Any<CancellationToken>());
        await file.Received(1).OnImported();
        Assert.Contains(progressEvents, e => e is SessionImportEvent.Progress { Current: 1, Total: 1 });
        Assert.Contains(progressEvents, e => e is SessionImportEvent.Imported importedEvent && importedEvent.Snapshot.Id == imported.Id);
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
        var (setup, _) = harness.SeedSetupAndBike();
        var file = CreateTelemetryFile(name: "candidate", shouldBeImported: shouldBeImported);

        var result = await harness.CreateCoordinator().ImportAsync([file], setup.Id);

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
        var progressEvents = new List<SessionImportEvent>();

        var result = await harness.CreateCoordinator().ImportAsync(
            [rejected, importable],
            setup.Id,
            harness.CaptureImportProgress(progressEvents));

        Assert.Equal("trimmed", Assert.Single(result.Imported).Name);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("bad", failure.FileName);
        Assert.Equal(SessionImportFailureOperation.Import, failure.Operation);
        await rejected.Received(1).ReadSourceAsync(Arg.Any<CancellationToken>());
        await rejected.DidNotReceive().OnImported();
        await importable.Received(1).OnImported();
        await harness.Reprocessor.Received(1).ProcessImportedSstAsync(
            Arg.Is<RecordedSessionDomainSnapshot>(domain => domain.Session.Name == "trimmed"),
            Arg.Any<RecordedSessionSource>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<CancellationToken>());
        Assert.Equal(0, rejectedSource.AllocatedCapacity);
        Assert.Equal(0, importableSource.AllocatedCapacity);
        Assert.Contains(progressEvents, e => e is SessionImportEvent.ImportFailed failed && failed.FileName == "bad");
    }

    [Theory]
    [InlineData(ImportFailurePoint.ReadSource)]
    [InlineData(ImportFailurePoint.Reprocess)]
    [InlineData(ImportFailurePoint.Persistence)]
    public async Task ImportAsync_ContinuesAfterPerFileReadReprocessOrPersistenceFailure(ImportFailurePoint failurePoint)
    {
        var harness = new ImportWorkflowHarness();
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
                .ProcessImportedSstAsync(
                    Arg.Any<RecordedSessionDomainSnapshot>(),
                    Arg.Any<RecordedSessionSource>(),
                    Arg.Any<ReadOnlyMemory<byte>>(),
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

        var result = await harness.CreateCoordinator().ImportAsync([broken, good], setup.Id);

        Assert.Equal("ok", Assert.Single(result.Imported).Name);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("broken", failure.FileName);
        Assert.Equal(SessionImportFailureOperation.Import, failure.Operation);
        await harness.SessionTelemetryWriter.Received(1).PutProcessedSessionAsync(
            Arg.Is<Session>(session => session.Name == "ok"),
            Arg.Any<ProcessedTelemetryPayload>(),
            Arg.Any<Track?>(),
            Arg.Any<RecordedSessionSource?>());
        Assert.Equal(0, goodSource.AllocatedCapacity);
        if (brokenSource is not null)
        {
            Assert.Equal(0, brokenSource.AllocatedCapacity);
            Assert.Throws<ObjectDisposedException>(() => brokenSource.SstBytes);
        }
    }

    [Fact]
    public async Task ImportAsync_OpensOneNetworkSessionPerEndpointRoutesTrashThroughSessionAndDisposes()
    {
        var harness = new ImportWorkflowHarness();
        var (setup, _) = harness.SeedSetupAndBike();
        var endpointA = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 1557);
        var endpointB = new IPEndPoint(IPAddress.Parse("10.0.0.2"), 1557);
        var sessionA = Substitute.For<IDaqManagementSession>();
        var sessionB = Substitute.For<IDaqManagementSession>();
        harness.DaqManagementService.OpenSessionAsync("10.0.0.1", 1557, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(sessionA));
        harness.DaqManagementService.OpenSessionAsync("10.0.0.2", 1557, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(sessionB));
        sessionA.TrashFileAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DaqManagementResult>(new DaqManagementResult.Ok()));
        sessionB.TrashFileAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DaqManagementResult>(new DaqManagementResult.Ok()));
        var fileA1 = CreateNetworkFile(endpointA, id: 1);
        var fileA2 = CreateNetworkFile(endpointA, id: 2);
        var fileB = CreateNetworkFile(endpointB, id: 3);

        await harness.CreateCoordinator().ImportAsync([fileA1, fileA2, fileB], setup.Id);

        await harness.DaqManagementService.Received(1)
            .OpenSessionAsync("10.0.0.1", 1557, Arg.Any<CancellationToken>());
        await harness.DaqManagementService.Received(1)
            .OpenSessionAsync("10.0.0.2", 1557, Arg.Any<CancellationToken>());
        await sessionA.Received(1).TrashFileAsync(1, Arg.Any<CancellationToken>());
        await sessionA.Received(1).TrashFileAsync(2, Arg.Any<CancellationToken>());
        await sessionB.Received(1).TrashFileAsync(3, Arg.Any<CancellationToken>());
        await harness.DaqManagementService.DidNotReceiveWithAnyArgs()
            .TrashFileAsync(default!, default, default, default);
        await sessionA.Received(1).DisposeAsync();
        await sessionB.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ImportAsync_OverlapsDownloadWithProcessing()
    {
        var harness = new ImportWorkflowHarness();
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
            .ProcessImportedSstAsync(
                Arg.Any<RecordedSessionDomainSnapshot>(),
                Arg.Any<RecordedSessionSource>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
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

        var importTask = harness.CreateCoordinator().ImportAsync([first, second], setup.Id);

        await firstProcessingStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await secondReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        releaseFirstProcessing.SetResult();
        var result = await importTask;

        Assert.Equal(2, result.Imported.Count);
    }

    private static NetworkTelemetryFile CreateNetworkFile(IPEndPoint endpoint, int id) =>
        new(endpoint, Substitute.For<IDaqManagementService>(), id, $"{id:00000}.SST", 3,
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
