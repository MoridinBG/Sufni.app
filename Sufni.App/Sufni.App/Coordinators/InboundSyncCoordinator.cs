using System.Linq;
using Avalonia.Threading;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Stores;

namespace Sufni.App.Coordinators;

/// <summary>
/// Desktop-only singleton owning the bike+setup branch of the
/// synchronization server's <c>SynchronizationDataArrived</c> event.
/// Sessions are owned by <see cref="ISessionCoordinator"/>; paired
/// devices by <see cref="IPairedDeviceCoordinator"/>. Each entity
/// family has exactly one inbound owner.
/// </summary>
public sealed class InboundSyncCoordinator : IInboundSyncCoordinator
{
    private readonly IDatabaseService databaseService;
    private readonly IBikeStoreWriter bikeStoreWriter;
    private readonly ISetupStoreWriter setupStoreWriter;

    public InboundSyncCoordinator(
        IDatabaseService databaseService,
        IBikeStoreWriter bikeStoreWriter,
        ISetupStoreWriter setupStoreWriter,
        ISynchronizationServerService synchronizationServer)
    {
        this.databaseService = databaseService;
        this.bikeStoreWriter = bikeStoreWriter;
        this.setupStoreWriter = setupStoreWriter;

        synchronizationServer.SynchronizationDataArrived += OnSynchronizationDataArrived;
    }

    private void OnSynchronizationDataArrived(object? sender, SynchronizationDataArrivedEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            foreach (var bike in e.Data.Bikes)
            {
                if (bike.Deleted is not null)
                {
                    bikeStoreWriter.Remove(bike.Id);
                }
                else
                {
                    bikeStoreWriter.Upsert(BikeSnapshot.From(bike));
                }
            }

            var boards = await databaseService.GetAllAsync<Board>();
            foreach (var setup in e.Data.Setups)
            {
                if (setup.Deleted is not null)
                {
                    setupStoreWriter.Remove(setup.Id);
                }
                else
                {
                    var board = boards.FirstOrDefault(b => b?.SetupId == setup.Id, null);
                    setupStoreWriter.Upsert(SetupSnapshot.From(setup, board?.Id));
                }
            }
        });
    }
}
