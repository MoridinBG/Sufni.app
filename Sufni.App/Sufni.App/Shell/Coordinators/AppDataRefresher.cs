using System.Threading;
using System.Threading.Tasks;

using Sufni.App.Bikes.Stores;
using Sufni.App.Segments.Stores;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Stores;
using Sufni.App.SyncAndPairing.Stores;
using Sufni.App.Video.Stores;
namespace Sufni.App.Shell.Coordinators;

public interface IAppDataRefresher
{
    Task RefreshAsync(CancellationToken cancellationToken = default);
}

internal sealed class AppDataRefresher(
    IBikeStoreWriter bikeStore,
    ISetupStoreWriter setupStore,
    ISessionStoreWriter sessionStore,
    IRecordedSessionSourceStoreWriter recordedSessionSourceStore,
    IPairedDeviceStoreWriter pairedDeviceStore,
    ISessionVideoStoreWriter sessionVideoStore,
    ISessionSegmentStoreWriter sessionSegmentStore,
    IRecordedSessionProcessingOptionCache processingOptionCache) : IAppDataRefresher
{
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        // Hydrate the per-session processing-option cache before populating the
        // stores so the recorded-session projection's first sweep (triggered by these
        // store refreshes) evaluates staleness with each session's real option
        // instead of the default (ordering gate).
        await processingOptionCache.HydrateAsync();

        await bikeStore.RefreshAsync(cancellationToken);
        await setupStore.RefreshAsync(cancellationToken);
        await sessionStore.RefreshAsync(cancellationToken);
        await recordedSessionSourceStore.RefreshAsync(cancellationToken);
        await pairedDeviceStore.RefreshAsync(cancellationToken);
        await sessionVideoStore.RefreshAsync(cancellationToken);
        await sessionSegmentStore.RefreshAsync(cancellationToken);
    }
}
