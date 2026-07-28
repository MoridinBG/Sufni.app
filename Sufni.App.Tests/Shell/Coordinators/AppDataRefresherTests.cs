using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.Database;

using Sufni.App.Bikes.Stores;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Stores;
using Sufni.App.Shell.Coordinators;
using Sufni.App.SyncAndPairing.Stores;
namespace Sufni.App.Tests.Shell.Coordinators;

public class AppDataRefresherTests
{
    [Fact]
    public async Task RefreshCoreStateAsync_RefreshesAllCoreState()
    {
        var bikeStore = Substitute.For<IBikeStoreWriter>();
        var setupStore = Substitute.For<ISetupStoreWriter>();
        var sessionStore = Substitute.For<ISessionStoreWriter>();
        var sourceStore = Substitute.For<IRecordedSessionSourceStoreWriter>();
        var pairedDeviceStore = Substitute.For<IPairedDeviceStoreWriter>();
        var processingOptionCache = Substitute.For<IRecordedSessionProcessingOptionCache>();
        var derivationWindowCache = Substitute.For<IRecordedSessionDerivationWindowCache>();
        var refresher = new AppDataRefresher(
            bikeStore,
            setupStore,
            sessionStore,
            sourceStore,
            pairedDeviceStore,
            processingOptionCache,
            derivationWindowCache,
            []);

        await refresher.RefreshCoreStateAsync(cancellationToken: TestContext.Current.CancellationToken);

        // The option/window caches must hydrate before stores refresh so the
        // projection's first sweep sees each session's real derivation inputs.
        await processingOptionCache.Received(1).HydrateAsync();
        await derivationWindowCache.Received(1).HydrateAsync();
        await bikeStore.Received(1).RefreshAsync(cancellationToken: TestContext.Current.CancellationToken);
        await setupStore.Received(1).RefreshAsync(cancellationToken: TestContext.Current.CancellationToken);
        await sessionStore.Received(1).RefreshAsync(cancellationToken: TestContext.Current.CancellationToken);
        await sourceStore.Received(1).RefreshAsync(cancellationToken: TestContext.Current.CancellationToken);
        await pairedDeviceStore.Received(1).RefreshAsync(cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RefreshAllStateAsync_RefreshesCoreStateThenExtensionParticipants()
    {
        var bikeStore = Substitute.For<IBikeStoreWriter>();
        var setupStore = Substitute.For<ISetupStoreWriter>();
        var sessionStore = Substitute.For<ISessionStoreWriter>();
        var sourceStore = Substitute.For<IRecordedSessionSourceStoreWriter>();
        var pairedDeviceStore = Substitute.For<IPairedDeviceStoreWriter>();
        var processingOptionCache = Substitute.For<IRecordedSessionProcessingOptionCache>();
        var derivationWindowCache = Substitute.For<IRecordedSessionDerivationWindowCache>();
        var participant = Substitute.For<IExtensionStateRefreshParticipant>();
        var refresher = new AppDataRefresher(
            bikeStore,
            setupStore,
            sessionStore,
            sourceStore,
            pairedDeviceStore,
            processingOptionCache,
            derivationWindowCache,
            [participant]);

        await refresher.RefreshAllStateAsync(cancellationToken: TestContext.Current.CancellationToken);

        await sessionStore.Received(1).RefreshAsync(cancellationToken: TestContext.Current.CancellationToken);
        await participant.Received(1).RefreshExtensionStateAsync(Arg.Any<CancellationToken>());
    }
}
