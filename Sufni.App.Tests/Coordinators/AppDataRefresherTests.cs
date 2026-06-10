using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Stores;

namespace Sufni.App.Tests.Coordinators;

public class AppDataRefresherTests
{
    [Fact]
    public async Task RefreshAsync_RefreshesAllStoreWriters()
    {
        var bikeStore = Substitute.For<IBikeStoreWriter>();
        var setupStore = Substitute.For<ISetupStoreWriter>();
        var sessionStore = Substitute.For<ISessionStoreWriter>();
        var sourceStore = Substitute.For<IRecordedSessionSourceStoreWriter>();
        var pairedDeviceStore = Substitute.For<IPairedDeviceStoreWriter>();
        var refresher = new AppDataRefresher(
            bikeStore,
            setupStore,
            sessionStore,
            sourceStore,
            pairedDeviceStore);

        await refresher.RefreshAsync();

        await bikeStore.Received(1).RefreshAsync();
        await setupStore.Received(1).RefreshAsync();
        await sessionStore.Received(1).RefreshAsync();
        await sourceStore.Received(1).RefreshAsync();
        await pairedDeviceStore.Received(1).RefreshAsync();
    }
}
