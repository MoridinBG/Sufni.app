using System;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Stores;

public sealed record TerrainLabelSnapshot(
    Guid Id,
    string OwnerKey,
    double StartSeconds,
    double EndSeconds,
    RoughnessClass Class)
{
    public static TerrainLabelSnapshot From(TerrainLabel label) => new(
        label.Id,
        label.OwnerKey,
        label.StartSeconds,
        label.EndSeconds,
        label.Class);
}
