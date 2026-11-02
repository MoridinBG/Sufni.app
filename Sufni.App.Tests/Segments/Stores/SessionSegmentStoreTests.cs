using System.Reactive.Linq;
using Sufni.App.Shared.Stores;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.Stores;

namespace Sufni.App.Tests.Segments.Stores;

public class SessionSegmentStoreTests
{
    [Fact]
    public async Task Publication_RetainsEverySourceWithoutRequiringASession()
    {
        using var store = new SessionSegmentStore(new StorePublication(new InlineUiThreadDispatcher()));
        var first = SessionSegmentSnapshot.From(new SessionSegment { OwnerKey = "source-a", Name = "A", StartSeconds = 0, EndSeconds = 1 });
        var second = SessionSegmentSnapshot.From(new SessionSegment { OwnerKey = "source-b", Name = "B", StartSeconds = 2, EndSeconds = 3 });

        await store.PublishSegmentsAsync([first, second], []);

        Assert.Equal(first, Assert.Single(store.GetForOwner("source-a")));
        Assert.Equal(second, Assert.Single(store.GetForOwner("source-b")));
    }

    [Fact]
    public async Task Publication_ChangesAndRemovesSegments_InOneObservedUpdate()
    {
        using var store = new SessionSegmentStore(new StorePublication(new InlineUiThreadDispatcher()));
        const string ownerKey = "source-hash";
        var kept = SessionSegmentSnapshot.From(new SessionSegment { OwnerKey = ownerKey, Name = "kept", StartSeconds = 0, EndSeconds = 1 });
        var removed = SessionSegmentSnapshot.From(new SessionSegment { OwnerKey = ownerKey, Name = "removed", StartSeconds = 2, EndSeconds = 3 });
        await store.PublishSegmentsAsync([kept, removed], []);
        var observed = new List<IReadOnlyList<SessionSegmentSnapshot>>();
        using var changes = store.Connect().Skip(1).Subscribe(_ => observed.Add(store.GetForOwner(ownerKey)));
        var renamed = kept with { Name = "renamed" };

        await store.PublishSegmentsAsync([renamed], [removed.Id]);

        Assert.Equal(renamed, Assert.Single(Assert.Single(observed)));
        Assert.Null(store.Get(removed.Id));
    }
}
