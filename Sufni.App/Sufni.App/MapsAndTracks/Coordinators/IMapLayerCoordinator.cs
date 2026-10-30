using System;
using System.Threading.Tasks;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Shared.Models;

namespace Sufni.App.MapsAndTracks.Coordinators;

public interface IMapLayerCoordinator
{
    Task<MapLayerWriteResult> AddAndSelectAsync(TileLayerConfig config);
    Task<MapLayerWriteResult> SelectAsync(Guid layerId);
}

public abstract record MapLayerWriteResult
{
    private MapLayerWriteResult() { }
    public sealed record Saved : MapLayerWriteResult;
    public sealed record Failed(MapLayerFailureReason Reason, string? Details = null) : MapLayerWriteResult;
}
