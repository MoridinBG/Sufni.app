using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DynamicData;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Shared.Base;
using Sufni.App.Shared.Stores;

namespace Sufni.App.MapsAndTracks.Stores;

public sealed record CustomTileLayerSnapshot(Guid Id, string Name, string UrlTemplate,
    string AttributionText, string AttributionUrl, int MaxZoom, long Updated)
{
    public static CustomTileLayerSnapshot From(CustomTileLayer row) => new(row.Id, row.Name, row.UrlTemplate,
        row.AttributionText, row.AttributionUrl, row.MaxZoom, row.Updated);

    public TileLayerConfig ToConfig() => new()
    {
        Id = Id, Name = Name, UrlTemplate = UrlTemplate, AttributionText = AttributionText,
        AttributionUrl = AttributionUrl, MaxZoom = MaxZoom, IsCustom = true
    };
}

public interface ICustomTileLayerStore
{
    IObservable<IChangeSet<CustomTileLayerSnapshot, Guid>> Connect();
    CustomTileLayerSnapshot? Get(Guid id);
    IReadOnlyList<CustomTileLayerSnapshot> GetAll();
}

public interface ICustomTileLayerStoreWriter : ICustomTileLayerStore
{
    Task ReplaceAllAsync(IReadOnlyList<CustomTileLayerSnapshot> snapshots);
    Task PublishChangedAsync(IReadOnlyCollection<CustomTileLayerSnapshot> snapshots);
    Task PublishRemovedAsync(IReadOnlyCollection<Guid> ids);
}

internal sealed class CustomTileLayerStore(StorePublication publication)
    : SourceCacheStoreBase<CustomTileLayerSnapshot, Guid>(row => row.Id, publication), ICustomTileLayerStoreWriter
{
    public IReadOnlyList<CustomTileLayerSnapshot> GetAll() => Items.ToArray();
    public Task PublishChangedAsync(IReadOnlyCollection<CustomTileLayerSnapshot> snapshots) => PublishSnapshotsAsync(snapshots);
    public Task PublishRemovedAsync(IReadOnlyCollection<Guid> ids) => PublishRemovalsAsync(ids);
}
