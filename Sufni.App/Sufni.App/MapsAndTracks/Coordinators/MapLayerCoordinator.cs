using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Shared.Models;

namespace Sufni.App.MapsAndTracks.Coordinators;

/// <summary>Owns custom map-layer writes and the device's map selection.</summary>
public sealed class MapLayerCoordinator(IAppPreferences preferences, IBackgroundTaskRunner backgroundTaskRunner)
    : IMapLayerCoordinator, IAsyncDisposable
{
    private static readonly ILogger logger = Log.ForContext<MapLayerCoordinator>();
    private readonly SemaphoreSlim writes = new(1, 1);

    public Task<MapLayerWriteResult> AddAndSelectAsync(TileLayerConfig config) =>
        WriteAsync(config.Id, config.AsCustom());

    public Task<MapLayerWriteResult> SelectAsync(Guid layerId) => WriteAsync(layerId, null);

    private async Task<MapLayerWriteResult> WriteAsync(Guid layerId, TileLayerConfig? custom)
    {
        await writes.WaitAsync();
        var reason = custom is null ? MapLayerFailureReason.Select : MapLayerFailureReason.Add;
        try
        {
            await backgroundTaskRunner.RunAsync(async () =>
            {
                if (custom is not null)
                {
                    var layers = await preferences.Map.GetCustomLayersAsync();
                    await preferences.Map.SetCustomLayersAsync(layers.Append(custom).ToArray());
                }
                reason = MapLayerFailureReason.Select;
                if (await preferences.Map.GetSelectedLayerIdAsync() != layerId)
                    await preferences.Map.SetSelectedLayerIdAsync(layerId);
            });
            return new MapLayerWriteResult.Saved();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.Error(e, "Map layer {Operation} failed for {LayerId}", reason, layerId);
            return new MapLayerWriteResult.Failed(reason, e.Message);
        }
        finally
        {
            writes.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await writes.WaitAsync();
        writes.Dispose();
    }
}
