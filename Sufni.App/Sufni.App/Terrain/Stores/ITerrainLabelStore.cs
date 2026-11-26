using System;
using System.Collections.Generic;
using DynamicData;

namespace Sufni.App.Terrain.Stores;

/// <summary>
/// Read-only view of the live terrain labels. Writes go through <see cref="ITerrainLabelStoreWriter"/>,
/// reserved for coordinators.
/// </summary>
public interface ITerrainLabelStore
{
    IObservable<IChangeSet<TerrainLabelSnapshot, Guid>> Connect();

    TerrainLabelSnapshot? Get(Guid labelId);

    IReadOnlyList<TerrainLabelSnapshot> GetForOwner(string ownerKey);
}
