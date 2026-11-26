using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sufni.App.Shared.Base;
using Sufni.App.Shared.Stores;

namespace Sufni.App.Terrain.Stores;

internal sealed class TerrainLabelStore(
    StorePublication publication)
    : SourceCacheStoreBase<TerrainLabelSnapshot, Guid>(snapshot => snapshot.Id, publication), ITerrainLabelStoreWriter
{
    public IReadOnlyList<TerrainLabelSnapshot> GetForOwner(string ownerKey) =>
        Items.Where(label => label.OwnerKey == ownerKey).ToList();

    public Task PublishLabelsAsync(IReadOnlyCollection<TerrainLabelSnapshot> snapshots, IReadOnlyCollection<Guid> removedIds) =>
        PublishChangesAsync(snapshots, removedIds);
}
