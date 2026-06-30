using System.Threading.Tasks;

using Sufni.App.Bikes.Stores;
using Sufni.App.Sessions.Processing.SessionGraph;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Stores;
using Sufni.App.SyncAndPairing.Stores;
namespace Sufni.App.Shell.Coordinators;

public interface IAppDataRefresher
{
    Task RefreshAsync();
}

internal sealed class AppDataRefresher(
    IBikeStoreWriter bikeStore,
    ISetupStoreWriter setupStore,
    ISessionStoreWriter sessionStore,
    IRecordedSessionSourceStoreWriter recordedSessionSourceStore,
    IPairedDeviceStoreWriter pairedDeviceStore,
    IRecordedSessionProcessingOptionCache processingOptionCache) : IAppDataRefresher
{
    public async Task RefreshAsync()
    {
        // Hydrate the per-session processing-option cache before populating the
        // stores so the recorded-session graph's first sweep (triggered by these
        // store refreshes) evaluates staleness with each session's real option
        // instead of the 25 ms default (ordering gate).
        await processingOptionCache.HydrateAsync();

        await bikeStore.RefreshAsync();
        await setupStore.RefreshAsync();
        await sessionStore.RefreshAsync();
        await recordedSessionSourceStore.RefreshAsync();
        await pairedDeviceStore.RefreshAsync();
    }
}
