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
    public async Task RunAsync_DeletesOrphanedSourcesExceptReferencedIds()
    {
        using var tempDatabase = new TempDatabase("recorded-source-retention-cleanup.db");
        var context = PersistenceTestData.CreateConnectionContext(tempDatabase.DatabasePath, []);
        var repository = Substitute.For<IRecordedSessionSourceRepository>();
        var store = Substitute.For<IRecordedSessionSourceStoreWriter>();
        var retainedId = Guid.NewGuid();
        var retainedIds = new[] { retainedId };
        repository.GetReferencedSourceSessionIdsAsync().Returns(Task.FromResult(retainedIds.ToList()));
        var deletedId = Guid.NewGuid();
        repository.DeleteOrphanedRecordedSessionSourcesAsync(Arg.Any<IReadOnlyCollection<Guid>>())
            .Returns(Task.FromResult<IReadOnlyList<Guid>>([deletedId]));
        var cleanup = new RecordedSessionSourceRetentionCleanup(
            context,
            repository,
            store,
            new InlineBackgroundTaskRunner());

        await cleanup.RunAsync();

        await repository.Received(1).DeleteOrphanedRecordedSessionSourcesAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(retainedIds)));
        await store.Received(1).PublishSourcesRemovedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { deletedId })),
            Arg.Any<CancellationToken>());
        await store.DidNotReceive().RefreshAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_RetainsSourcesReferencedByDerivedSessions()
    {
        using var tempDatabase = new TempDatabase("recorded-source-retention-persisted-window.db");
        var context = PersistenceTestData.CreateConnectionContext(tempDatabase.DatabasePath, []);
        var repository = new RecordedSessionSourceRepository(context);
        var store = Substitute.For<IRecordedSessionSourceStoreWriter>();
        var retainedSourceId = Guid.NewGuid();
        var orphanSourceId = Guid.NewGuid();
        var derivedSessionId = Guid.NewGuid();
        await repository.PutRecordedSessionSourceAsync(PersistenceTestData.CreateRecordedSessionSource(retainedSourceId));
        await repository.PutRecordedSessionSourceAsync(PersistenceTestData.CreateRecordedSessionSource(orphanSourceId));
        var connection = await context.GetInitializedConnectionAsync(cancellationToken: TestContext.Current.CancellationToken);
        await connection.InsertAsync(new Session(derivedSessionId, "derived", "desc", null, 100)
        {
            SourceSessionId = retainedSourceId,
            WindowStartSeconds = 1
        });
        var cleanup = new RecordedSessionSourceRetentionCleanup(
            context,
            repository,
            store,
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
