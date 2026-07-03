using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;
using Sufni.App.ExtensionHost.TestSupport.Async;

using Sufni.App.Sessions.Services;
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
        var provider = Substitute.For<IRecordedSessionDerivationWindowProvider>();
        var retainedId = Guid.NewGuid();
        var retainedIds = new[] { retainedId };
        provider.GetReferencedSourceSessionIdsAsync().Returns(Task.FromResult<IReadOnlyCollection<Guid>>(retainedIds));
        repository.DeleteOrphanedRecordedSessionSourcesAsync(Arg.Any<IReadOnlyCollection<Guid>>())
            .Returns(Task.FromResult(1));
        var cleanup = new RecordedSessionSourceRetentionCleanup(
            context,
            repository,
            provider,
            new InlineBackgroundTaskRunner());

        await cleanup.RunAsync();

        await provider.Received(1).GetReferencedSourceSessionIdsAsync();
        await repository.Received(1).DeleteOrphanedRecordedSessionSourcesAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(retainedIds)));
    }
}
