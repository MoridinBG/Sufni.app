using System.Linq;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Stores;
using Serilog;
using Sufni.App.ExtensionHost.Services;

namespace Sufni.App.Coordinators;

/// <summary>
/// Desktop-only singleton owning the bike+setup branch of the
/// synchronization server's <c>SynchronizationDataArrived</c> event.
/// Sessions are owned by <see cref="SessionCoordinator"/>; paired
/// devices by <see cref="PairedDeviceCoordinator"/>. Each entity
/// family has exactly one inbound owner.
/// </summary>
public sealed class InboundSyncCoordinator : IInboundSyncCoordinator
{
    private static readonly ILogger logger = Log.ForContext<InboundSyncCoordinator>();

    private readonly IDatabaseService databaseService;
    private readonly IBikeStoreWriter bikeStoreWriter;
    private readonly ISetupStoreWriter setupStoreWriter;
    private readonly IUiThreadDispatcher uiThreadDispatcher;

    public InboundSyncCoordinator(
        IDatabaseService databaseService,
        IBikeStoreWriter bikeStoreWriter,
        ISetupStoreWriter setupStoreWriter,
        ISynchronizationServerService synchronizationServer,
        IUiThreadDispatcher? uiThreadDispatcher = null)
    {
        this.databaseService = databaseService;
        this.bikeStoreWriter = bikeStoreWriter;
        this.setupStoreWriter = setupStoreWriter;
        this.uiThreadDispatcher = uiThreadDispatcher ?? new AvaloniaUiThreadDispatcher();

        synchronizationServer.SynchronizationDataArrived += OnSynchronizationDataArrived;
    }

    private void OnSynchronizationDataArrived(object? sender, SynchronizationDataArrivedEventArgs e)
    {
        _ = uiThreadDispatcher.InvokeAsync(async () =>
        {
            try
            {
                var boards = await databaseService.GetAllAsync<Board>();
                var removedBikeCount = 0;
                var upsertedBikeCount = 0;
                foreach (var bike in e.Data.Bikes)
                {
                    var freshBike = await databaseService.GetAsync<Bike>(bike.Id);
                    if (freshBike is null)
                    {
                        bikeStoreWriter.Remove(bike.Id);
                        removedBikeCount++;
                    }
                    else
                    {
                        bikeStoreWriter.Upsert(BikeSnapshot.From(freshBike));
                        upsertedBikeCount++;
                    }
                }

                var removedSetupCount = 0;
                var upsertedSetupCount = 0;
                foreach (var setup in e.Data.Setups)
                {
                    var freshSetup = await databaseService.GetAsync<Setup>(setup.Id);
                    if (freshSetup is null)
                    {
                        setupStoreWriter.Remove(setup.Id);
                        removedSetupCount++;
                    }
                    else
                    {
                        var board = boards.FirstOrDefault(b => b?.SetupId == freshSetup.Id, null);
                        setupStoreWriter.Upsert(SetupSnapshot.From(freshSetup, board?.Id));
                        upsertedSetupCount++;
                    }
                }

                logger.Verbose(
                    "Applied inbound bike/setup synchronization with {RemovedBikeCount} bike removals, {UpsertedBikeCount} bike upserts, {RemovedSetupCount} setup removals, and {UpsertedSetupCount} setup upserts",
                    removedBikeCount,
                    upsertedBikeCount,
                    removedSetupCount,
                    upsertedSetupCount);
            }
            catch (System.Exception exception)
            {
                logger.Error(exception, "Failed to apply inbound bike/setup synchronization data");
            }
        });
    }
}
