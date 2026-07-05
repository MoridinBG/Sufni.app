using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;
using Sufni.App.ExtensionHost.TestSupport.Async;
using Sufni.Telemetry;

using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Tests.TestSupport.Persistence;
namespace Sufni.App.Tests.Sessions.Services;

public class RecordedSessionSourceRetentionCleanupTests
{
    [Fact]
    public async Task RunAsync_DeletesOrphanedSourcesExceptProviderRetainedIds()
    {
        using var tempDatabase = new TempDatabase("recorded-source-retention-cleanup.db");
        var context = PersistenceTestData.CreateConnectionContext(tempDatabase.DatabasePath, []);
        var repository = Substitute.For<IRecordedSessionSourceRepository>();
        var store = Substitute.For<IRecordedSessionSourceStoreWriter>();
        var provider = Substitute.For<IRecordedSessionDerivationWindowProvider>();
        var retainedId = Guid.NewGuid();
        var retainedIds = new[] { retainedId };
        provider.GetReferencedSourceSessionIdsAsync().Returns(Task.FromResult<IReadOnlyCollection<Guid>>(retainedIds));
        repository.GetPersistedDerivationSourceSessionIdsAsync().Returns(Task.FromResult(new List<Guid>()));
        var deletedId = Guid.NewGuid();
        repository.DeleteOrphanedRecordedSessionSourcesAsync(Arg.Any<IReadOnlyCollection<Guid>>())
            .Returns(Task.FromResult<IReadOnlyList<Guid>>([deletedId]));
        var cleanup = new RecordedSessionSourceRetentionCleanup(
            context,
            repository,
            store,
            provider,
            new InlineBackgroundTaskRunner());

        await cleanup.RunAsync();

        await provider.Received(1).GetReferencedSourceSessionIdsAsync();
        await repository.Received(1).DeleteOrphanedRecordedSessionSourcesAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(retainedIds)));
        await store.Received(1).PublishSourcesRemovedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { deletedId })),
            Arg.Any<CancellationToken>());
        await store.DidNotReceive().RefreshAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_RetainsPersistedFingerprintSources_AndDeletesTrueOrphans_WithNullProvider()
    {
        using var tempDatabase = new TempDatabase("recorded-source-retention-persisted-window.db");
        var context = PersistenceTestData.CreateConnectionContext(tempDatabase.DatabasePath, []);
        var repository = new RecordedSessionSourceRepository(context);
        var store = Substitute.For<IRecordedSessionSourceStoreWriter>();
        var provider = Substitute.For<IRecordedSessionDerivationWindowProvider>();
        provider.GetReferencedSourceSessionIdsAsync().Returns(Task.FromResult<IReadOnlyCollection<Guid>>([]));
        var retainedSourceId = Guid.NewGuid();
        var orphanSourceId = Guid.NewGuid();
        var derivedSessionId = Guid.NewGuid();
        await repository.PutRecordedSessionSourceAsync(PersistenceTestData.CreateRecordedSessionSource(retainedSourceId));
        await repository.PutRecordedSessionSourceAsync(PersistenceTestData.CreateRecordedSessionSource(orphanSourceId));
        var fingerprint = new ProcessingFingerprint(
            SchemaVersion: 3,
            ProcessingVersion: TelemetryProcessingVersion.Current,
            SetupId: Guid.NewGuid(),
            BikeId: Guid.NewGuid(),
            TrackProjectionVersion: GpsTrackPointProjection.ProjectionVersion,
            DependencyHash: "dependency",
            SourceHash: "source",
            DerivationWindow: new RecordedSessionDerivationWindow(retainedSourceId, 1, null));
        var connection = await context.GetInitializedConnectionAsync();
        await connection.InsertAsync(new Session(derivedSessionId, "derived", "desc", null, 100)
        {
            ProcessingFingerprintJson = AppJson.Serialize(fingerprint)
        });
        var cleanup = new RecordedSessionSourceRetentionCleanup(
            context,
            repository,
            store,
            provider,
            new InlineBackgroundTaskRunner());

        await cleanup.RunAsync();

        Assert.NotNull(await repository.GetRecordedSessionSourceAsync(retainedSourceId));
        Assert.Null(await repository.GetRecordedSessionSourceAsync(orphanSourceId));
        await store.Received(1).PublishSourcesRemovedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { orphanSourceId })),
            Arg.Any<CancellationToken>());
        await store.DidNotReceive().RefreshAsync(Arg.Any<CancellationToken>());
    }
}
