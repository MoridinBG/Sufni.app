using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;
using Sufni.App.Sessions.Services;

namespace Sufni.App.Tests.Sessions.Services;

public class RecordedSessionSourceSyncQueryTests
{
    [Fact]
    public async Task GetSourceSyncTargetIdsAsync_ReplacesMissingDerivedSessionWithMissingSource()
    {
        var derivedSessionId = Guid.NewGuid();
        var sourceSessionId = Guid.NewGuid();
        var existingSourceId = Guid.NewGuid();
        var ordinaryMissingSessionId = Guid.NewGuid();
        var repository = Substitute.For<IRecordedSessionSourceRepository>();
        var provider = Substitute.For<IRecordedSessionDerivationWindowProvider>();
        repository.GetSessionIdsMissingRecordedSourceAsync()
            .Returns([derivedSessionId, ordinaryMissingSessionId]);
        repository.GetSourceBackedSessionIdsAsync().Returns([existingSourceId]);
        repository.GetPersistedDerivationSourceSessionIdsAsync().Returns([]);
        provider.GetWindowsAsync().Returns(new Dictionary<Guid, RecordedSessionDerivationWindow>
        {
            [derivedSessionId] = new(sourceSessionId, 1, null)
        });
        provider.GetReferencedSourceSessionIdsAsync().Returns([sourceSessionId, existingSourceId]);
        var query = new RecordedSessionSourceSyncQuery(repository, provider);

        var targetIds = await query.GetSourceSyncTargetIdsAsync();

        Assert.Contains(ordinaryMissingSessionId, targetIds);
        Assert.Contains(sourceSessionId, targetIds);
        Assert.DoesNotContain(derivedSessionId, targetIds);
        Assert.DoesNotContain(existingSourceId, targetIds);
    }

    [Fact]
    public async Task GetSourceSyncTargetIdsAsync_KeepsMissingSession_WhenWindowIsSelfOrAbsent()
    {
        var selfWindowSessionId = Guid.NewGuid();
        var absentWindowSessionId = Guid.NewGuid();
        var repository = Substitute.For<IRecordedSessionSourceRepository>();
        var provider = Substitute.For<IRecordedSessionDerivationWindowProvider>();
        repository.GetSessionIdsMissingRecordedSourceAsync()
            .Returns([selfWindowSessionId, absentWindowSessionId]);
        repository.GetSourceBackedSessionIdsAsync().Returns([]);
        repository.GetPersistedDerivationSourceSessionIdsAsync().Returns([]);
        provider.GetWindowsAsync().Returns(new Dictionary<Guid, RecordedSessionDerivationWindow>
        {
            [selfWindowSessionId] = new(selfWindowSessionId, 2, null)
        });
        provider.GetReferencedSourceSessionIdsAsync().Returns([]);
        var query = new RecordedSessionSourceSyncQuery(repository, provider);

        var targetIds = await query.GetSourceSyncTargetIdsAsync();

        Assert.Contains(selfWindowSessionId, targetIds);
        Assert.Contains(absentWindowSessionId, targetIds);
    }

    [Fact]
    public async Task GetSourceSyncTargetIdsAsync_AddsPersistedFingerprintSource_WhenProviderIsBlind()
    {
        var derivedSessionId = Guid.NewGuid();
        var sourceSessionId = Guid.NewGuid();
        var repository = Substitute.For<IRecordedSessionSourceRepository>();
        var provider = Substitute.For<IRecordedSessionDerivationWindowProvider>();
        repository.GetSessionIdsMissingRecordedSourceAsync().Returns([derivedSessionId]);
        repository.GetSourceBackedSessionIdsAsync().Returns([]);
        repository.GetPersistedDerivationSourceSessionIdsAsync().Returns([sourceSessionId]);
        provider.GetWindowsAsync().Returns(new Dictionary<Guid, RecordedSessionDerivationWindow>());
        provider.GetReferencedSourceSessionIdsAsync().Returns([]);
        var query = new RecordedSessionSourceSyncQuery(repository, provider);

        var targetIds = await query.GetSourceSyncTargetIdsAsync();

        Assert.Contains(sourceSessionId, targetIds);
    }

    [Fact]
    public async Task GetSourceSyncTargetIdsAsync_UsesProviderSet_WhenPersistedFingerprintsAddNothing()
    {
        var sourceSessionId = Guid.NewGuid();
        var repository = Substitute.For<IRecordedSessionSourceRepository>();
        var provider = Substitute.For<IRecordedSessionDerivationWindowProvider>();
        repository.GetSessionIdsMissingRecordedSourceAsync().Returns([]);
        repository.GetSourceBackedSessionIdsAsync().Returns([]);
        repository.GetPersistedDerivationSourceSessionIdsAsync().Returns([]);
        provider.GetWindowsAsync().Returns(new Dictionary<Guid, RecordedSessionDerivationWindow>());
        provider.GetReferencedSourceSessionIdsAsync().Returns([sourceSessionId]);
        var query = new RecordedSessionSourceSyncQuery(repository, provider);

        var targetIds = await query.GetSourceSyncTargetIdsAsync();

        Assert.Equal([sourceSessionId], targetIds);
    }
}
