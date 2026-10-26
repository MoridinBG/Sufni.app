using NSubstitute;

using Sufni.App.Bikes.Stores;
using Sufni.App.Segments.Stores;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Stores;
using Sufni.App.Shell.Coordinators;
using Sufni.App.SyncAndPairing.Stores;
using Sufni.App.Video.Stores;
namespace Sufni.App.Tests.Shell.Coordinators;

public class AppDataRefresherTests
{
    [Fact]
    public async Task RefreshAsync_RefreshesAllState()
    {
        var bikeStore = Substitute.For<IBikeStoreWriter>();
        var setupStore = Substitute.For<ISetupStoreWriter>();
        var sessionStore = Substitute.For<ISessionStoreWriter>();
        var sourceStore = Substitute.For<IRecordedSessionSourceStoreWriter>();
        var pairedDeviceStore = Substitute.For<IPairedDeviceStoreWriter>();
        var sessionVideoStore = Substitute.For<ISessionVideoStoreWriter>();
        var sessionSegmentStore = Substitute.For<ISessionSegmentStoreWriter>();
        var processingOptionCache = Substitute.For<IRecordedSessionProcessingOptionCache>();
        var refresher = new AppDataRefresher(
            bikeStore,
            setupStore,
            sessionStore,
            sourceStore,
            pairedDeviceStore,
            sessionVideoStore,
            sessionSegmentStore,
            processingOptionCache);

        await refresher.RefreshAsync(cancellationToken: TestContext.Current.CancellationToken);

        // The option cache must hydrate before stores refresh so the
        // projection's first sweep sees each session's real processing options.
        await processingOptionCache.Received(1).HydrateAsync();
        await bikeStore.Received(1).RefreshAsync(cancellationToken: TestContext.Current.CancellationToken);
        await setupStore.Received(1).RefreshAsync(cancellationToken: TestContext.Current.CancellationToken);
        await sessionStore.Received(1).RefreshAsync(cancellationToken: TestContext.Current.CancellationToken);
        await sourceStore.Received(1).RefreshAsync(cancellationToken: TestContext.Current.CancellationToken);
        await pairedDeviceStore.Received(1).RefreshAsync(cancellationToken: TestContext.Current.CancellationToken);
        await sessionVideoStore.Received(1).RefreshAsync(cancellationToken: TestContext.Current.CancellationToken);
        await sessionSegmentStore.Received(1).RefreshAsync(cancellationToken: TestContext.Current.CancellationToken);
    }
}
