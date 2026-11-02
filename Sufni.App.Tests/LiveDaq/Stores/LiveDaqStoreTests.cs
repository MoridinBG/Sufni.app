using Sufni.App.LiveDaq.Stores;

namespace Sufni.App.Tests.LiveDaq.Stores;

public class LiveDaqStoreTests
{
    [Fact]
    public async Task ReplaceAll_QueuesImmutableSnapshotsInOrder_AndPublishesOnDispatcher()
    {
        var dispatcher = new ManualUiThreadDispatcher();
        using var store = new LiveDaqStore(dispatcher);
        var online = new LiveDaqSnapshot("board", "ride", "board", "192.168.0.10", 1557, true, null, null);
        var offline = online with { Host = null, Port = null, IsOnline = false };
        var observed = new List<LiveDaqSnapshot>();
        using var subscription = store.Connect().Subscribe(_ =>
        {
            Assert.True(dispatcher.CheckAccess());
            observed.Add(store.Get("board")!);
        });
        var input = new List<LiveDaqSnapshot> { online };

        dispatcher.Post(() => store.ReplaceAll([offline]));
        store.ReplaceAll(input);
        input.Clear();
        Assert.Null(store.Get("board"));
        Assert.Empty(observed);
        await dispatcher.DrainAsync();

        Assert.Equal(new[] { online, offline }, observed);
        Assert.Equal(offline, store.Get("board"));
    }

    [Fact]
    public async Task Dispose_PreventsQueuedPublicationAfterLifetimeEnds()
    {
        var dispatcher = new ManualUiThreadDispatcher();
        var store = new LiveDaqStore(dispatcher);
        var observed = 0;
        using var subscription = store.Connect().Subscribe(_ => observed++);
        store.ReplaceAll([new LiveDaqSnapshot("board", "ride", null, null, null, false, null, null)]);

        store.Dispose();
        await dispatcher.DrainAsync();

        Assert.Equal(0, observed);
    }
}
