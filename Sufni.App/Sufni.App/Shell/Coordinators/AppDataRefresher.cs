using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.Database;

using Sufni.App.Bikes.Stores;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Stores;
using Sufni.App.SyncAndPairing.Stores;
namespace Sufni.App.Shell.Coordinators;

public interface IAppDataRefresher
{
    Task RefreshAsync();
}

public interface IAppStateRefreshOrchestrator
{
    Task RefreshCoreStateAsync(CancellationToken cancellationToken = default);
    Task RefreshAllStateAsync(CancellationToken cancellationToken = default);
}

internal sealed class AppDataRefresher(
    IBikeStoreWriter bikeStore,
    ISetupStoreWriter setupStore,
    ISessionStoreWriter sessionStore,
    IRecordedSessionSourceStoreWriter recordedSessionSourceStore,
    IPairedDeviceStoreWriter pairedDeviceStore,
    IRecordedSessionProcessingOptionCache processingOptionCache,
    IRecordedSessionDerivationWindowCache derivationWindowCache,
    IEnumerable<IExtensionStateRefreshParticipant> extensionStateRefreshParticipants) : IAppDataRefresher, IAppStateRefreshOrchestrator
{
    public Task RefreshAsync() => RefreshCoreStateAsync();

    public async Task RefreshCoreStateAsync(CancellationToken cancellationToken = default)
    {
        // Hydrate the per-session processing-option/window caches before populating the
        // stores so the recorded-session projection's first sweep (triggered by these
        // store refreshes) evaluates staleness with each session's real option and
        // derivation window instead of defaults (ordering gate).
        await processingOptionCache.HydrateAsync();
        await derivationWindowCache.HydrateAsync();

        await bikeStore.RefreshAsync(cancellationToken);
        await setupStore.RefreshAsync(cancellationToken);
        await sessionStore.RefreshAsync(cancellationToken);
        await recordedSessionSourceStore.RefreshAsync(cancellationToken);
        await pairedDeviceStore.RefreshAsync(cancellationToken);
    }

    public async Task RefreshAllStateAsync(CancellationToken cancellationToken = default)
    {
        await RefreshCoreStateAsync(cancellationToken);
        foreach (var participant in extensionStateRefreshParticipants)
        {
            await participant.RefreshExtensionStateAsync(cancellationToken);
        }
    }
}
