using System;
using System.Collections.Generic;
using Serilog;
using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Stores;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Infrastructure;
namespace Sufni.App.SyncAndPairing.Coordinators;

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

    private readonly ISynchronizableRepository<Bike> bikeRepository;
    private readonly ISynchronizableRepository<Setup> setupRepository;
    private readonly IBikeStoreWriter bikeStoreWriter;
    private readonly ISetupStoreWriter setupStoreWriter;
    private readonly IUiThreadDispatcher uiThreadDispatcher;

    public InboundSyncCoordinator(
        ISynchronizableRepository<Bike> bikeRepository,
        ISynchronizableRepository<Setup> setupRepository,
        IBikeStoreWriter bikeStoreWriter,
        ISetupStoreWriter setupStoreWriter,
        ISynchronizationServerService synchronizationServer,
        IUiThreadDispatcher? uiThreadDispatcher = null)
    {
        this.bikeRepository = bikeRepository;
        this.setupRepository = setupRepository;
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
                var removedBikeIds = new List<Guid>();
                var changedBikeIds = new List<Guid>();
                var removedBikeCount = 0;
                var upsertedBikeCount = 0;
                foreach (var bike in e.Data.Bikes)
                {
                    var freshBike = await bikeRepository.GetAsync(bike.Id);
                    if (freshBike is null)
                    {
                        removedBikeIds.Add(bike.Id);
                        removedBikeCount++;
                    }
                    else
                    {
                        changedBikeIds.Add(freshBike.Id);
                        upsertedBikeCount++;
                    }
                }

                if (removedBikeIds.Count > 0)
                {
                    await bikeStoreWriter.PublishBikesRemovedAsync(removedBikeIds);
                }

                if (changedBikeIds.Count > 0)
                {
                    await bikeStoreWriter.PublishBikesChangedAsync(changedBikeIds);
                }

                var removedSetupCount = 0;
                var upsertedSetupCount = 0;
                var removedSetupIds = new List<Guid>();
                var changedSetupIds = new List<Guid>();
                foreach (var setup in e.Data.Setups)
                {
                    var freshSetup = await setupRepository.GetAsync(setup.Id);
                    if (freshSetup is null)
                    {
                        removedSetupIds.Add(setup.Id);
                        removedSetupCount++;
                    }
                    else
                    {
                        changedSetupIds.Add(freshSetup.Id);
                        upsertedSetupCount++;
                    }
                }

                if (removedSetupIds.Count > 0)
                {
                    await setupStoreWriter.PublishSetupsRemovedAsync(removedSetupIds);
                }

                if (changedSetupIds.Count > 0)
                {
                    await setupStoreWriter.PublishSetupsChangedAsync(changedSetupIds);
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
