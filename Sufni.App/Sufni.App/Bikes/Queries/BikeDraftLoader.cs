using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Models;
using Sufni.App.SyncAndPairing.Services;

namespace Sufni.App.Bikes.Queries;

internal sealed class BikeDraftLoader(ISynchronizableRepository<Bike> bikeRepository,
    IBackgroundTaskRunner backgroundTaskRunner) : IBikeDraftLoader
{
    private static readonly ILogger logger = Log.ForContext<BikeDraftLoader>();

    public async Task<BikeDraftLoadResult> LoadDraftAsync(Guid bikeId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await backgroundTaskRunner.RunAsync<BikeDraftLoadResult>(async () =>
            {
                var bike = await bikeRepository.GetAsync(bikeId);
                return bike is null ? new BikeDraftLoadResult.Missing() : new BikeDraftLoadResult.Loaded(BikeDraft.From(bike));
            }, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            logger.Error(exception, "Bike draft could not be loaded for {BikeId}", bikeId);
            return new BikeDraftLoadResult.Failed(BikeLoadFailureReason.Read, exception.Message);
        }
    }
}
