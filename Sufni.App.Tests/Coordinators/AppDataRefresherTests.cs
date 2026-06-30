using NSubstitute;

using Sufni.App.Bikes.Stores;
using Sufni.App.Sessions.Processing.SessionGraph;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Stores;
using Sufni.App.Shell.Coordinators;
using Sufni.App.SyncAndPairing.Stores;
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
        var processingOptionCache = Substitute.For<IRecordedSessionProcessingOptionCache>();
        var refresher = new AppDataRefresher(
            bikeStore,
            setupStore,
            sessionStore,
            sourceStore,
            pairedDeviceStore,
            processingOptionCache);

        await refresher.RefreshAsync();

        // The option cache must hydrate before stores refresh so the graph's
        // first sweep sees each session's real option.
        await processingOptionCache.Received(1).HydrateAsync();
        await bikeStore.Received(1).RefreshAsync();
        await setupStore.Received(1).RefreshAsync();
        await sessionStore.Received(1).RefreshAsync();
        await sourceStore.Received(1).RefreshAsync();
        await pairedDeviceStore.Received(1).RefreshAsync();
    }
}
