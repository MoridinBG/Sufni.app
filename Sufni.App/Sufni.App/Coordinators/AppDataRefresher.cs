using System.Threading.Tasks;
using Sufni.App.Stores;

namespace Sufni.App.Coordinators;

public interface IAppDataRefresher
{
    Task RefreshAsync();
}

internal sealed class AppDataRefresher(
    IBikeStoreWriter bikeStore,
    ISetupStoreWriter setupStore,
    ISessionStoreWriter sessionStore,
    IRecordedSessionSourceStoreWriter recordedSessionSourceStore,
    IPairedDeviceStoreWriter pairedDeviceStore) : IAppDataRefresher
{
    public async Task RefreshAsync()
    {
        await bikeStore.RefreshAsync();
        await setupStore.RefreshAsync();
        await sessionStore.RefreshAsync();
        await recordedSessionSourceStore.RefreshAsync();
        await pairedDeviceStore.RefreshAsync();
    }
}
