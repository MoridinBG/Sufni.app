using DynamicData;
using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.Shared.Stores;

namespace Sufni.App.Tests.MapsAndTracks.Stores;

public class TrackStoreTests
{
    [Fact]
    public async Task Publication_ReplacesMetadataAndRemovesTracks()
    {
        using var publication = new StorePublication(new InlineUiThreadDispatcher());
        using var store = new TrackStore(publication);
        using var subscription = store.Connect().Bind(out var observed).Subscribe();
        var first = new TrackSnapshot(Guid.NewGuid(), 100.25, 200.75, 1);
        var other = new TrackSnapshot(Guid.NewGuid(), 50, 60, 1);
        await store.ReplaceAllAsync([first, other]);
        var updated = first with { PointsRevision = 2, EndTime = 201.25 };

        await store.PublishTracksChangedAsync([updated]);
        await store.PublishTracksRemovedAsync([other.Id]);

        Assert.Equal(updated, Assert.Single(observed));
        Assert.Equal(updated, store.Get(first.Id));
        Assert.Null(store.Get(other.Id));
        await store.ReplaceAllAsync([other]);
        Assert.Equal(other, Assert.Single(observed));
        Assert.Null(store.Get(first.Id));
    }
}
