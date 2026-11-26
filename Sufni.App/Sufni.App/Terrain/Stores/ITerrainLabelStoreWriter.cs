using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sufni.App.Terrain.Stores;

public interface ITerrainLabelStoreWriter : ITerrainLabelStore
{
    Task ReplaceAllAsync(IReadOnlyList<TerrainLabelSnapshot> snapshots);

    /// <summary>
    /// Publishes prepared snapshots and removals in one cache update.
    /// </summary>
    Task PublishLabelsAsync(IReadOnlyCollection<TerrainLabelSnapshot> snapshots, IReadOnlyCollection<Guid> removedIds);
}
