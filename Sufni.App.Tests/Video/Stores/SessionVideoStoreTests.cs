using Sufni.App.Video.Stores;
using Sufni.App.Shared.Stores;

namespace Sufni.App.Tests.Video.Stores;

public class SessionVideoStoreTests
{
    [Fact]
    public async Task KeepsMissingAndUnreferencedClipsForFutureSessionWindows()
    {
        using var store = new SessionVideoStore(new StorePublication(new InlineUiThreadDispatcher()));
        var present = new SessionVideoSnapshot(Guid.NewGuid(), "unreferenced-source", "/video/present.mp4", 0, 60, 0, 1, true);
        var missing = present with { Id = Guid.NewGuid(), Path = "/video/missing.mp4", FileExists = false };

        await store.ReplaceAllAsync([present, missing]);
        await store.PublishClipsAsync([present with { AlignmentOffsetSeconds = 5 }]);

        Assert.Equal(2, store.GetAll().Count);
        Assert.Equal(missing, store.Get(missing.Id));
        Assert.Equal(5, store.Get(present.Id)!.AlignmentOffsetSeconds);
    }
}
