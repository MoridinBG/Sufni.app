using DynamicData;
using Sufni.App.Shared.Stores;
using Sufni.App.Sessions.Store;
using Sufni.App.Tests.TestSupport.Persistence;

namespace Sufni.App.Tests.Sessions.Store;

public class RecordedSessionSourceStoreTests
{
    [Fact]
    public async Task Publication_UpdatesObservedMetadata_AndRemovesBySourceSessionId()
    {
        using var store = new RecordedSessionSourceStore(new StorePublication(new InlineUiThreadDispatcher()));
        using var subscription = store.Connect().Bind(out var snapshots).Subscribe();
        var source = RecordedSessionSourceSnapshot.From(PersistenceTestData.CreateRecordedSessionSource(Guid.NewGuid()));
        await store.PublishSourcesChangedAsync([source], TestContext.Current.CancellationToken);
        var updated = source with { SourceName = "renamed.SST" };

        await store.PublishSourcesChangedAsync([updated], TestContext.Current.CancellationToken);

        Assert.Equal(updated, Assert.Single(snapshots));
        Assert.Equal(updated, store.Get(source.SessionId));

        await store.PublishSourcesRemovedAsync([source.SessionId], TestContext.Current.CancellationToken);

        Assert.Empty(snapshots);
        Assert.Null(store.Get(source.SessionId));
    }

    [Fact]
    public async Task ReplaceAllAsync_ReplacesCachedMetadata()
    {
        using var store = new RecordedSessionSourceStore(new StorePublication(new InlineUiThreadDispatcher()));
        using var subscription = store.Connect().Bind(out var snapshots).Subscribe();
        var old = RecordedSessionSourceSnapshot.From(PersistenceTestData.CreateRecordedSessionSource(Guid.NewGuid()));
        var replacement = RecordedSessionSourceSnapshot.From(PersistenceTestData.CreateRecordedSessionSource(Guid.NewGuid()));
        await store.PublishSourcesChangedAsync([old], TestContext.Current.CancellationToken);

        await store.ReplaceAllAsync([replacement]);

        Assert.Equal(replacement, Assert.Single(snapshots));
        Assert.Null(store.Get(old.SessionId));
        Assert.Equal(replacement, store.Get(replacement.SessionId));
    }
}
