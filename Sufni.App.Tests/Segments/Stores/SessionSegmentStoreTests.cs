using NSubstitute;
using Sufni.App.ExtensionHost.TestSupport.Async;

using Sufni.App.Segments.Models;
using Sufni.App.Segments.Services;
using Sufni.App.Segments.Stores;
namespace Sufni.App.Tests.Segments.Stores;

public class SessionSegmentStoreTests
{
    [Fact]
    public async Task PublishSegmentsAsync_RemovesDeletedSegments()
    {
        var store = new SessionSegmentStore(Substitute.For<ISessionSegmentRepository>(), new InlineUiThreadDispatcher());
        var sessionId = Guid.NewGuid();
        var kept = new SessionSegment { SessionId = sessionId, Name = "kept", StartSeconds = 0, EndSeconds = 1 };
        var deleted = new SessionSegment { SessionId = sessionId, Name = "deleted", StartSeconds = 2, EndSeconds = 3 };
        await store.PublishSegmentsAsync([kept, deleted]);

        deleted.Deleted = 10;
        await store.PublishSegmentsAsync([deleted]);

        Assert.Equal(kept.Id, Assert.Single(store.GetForSession(sessionId)).Id);
    }
}
