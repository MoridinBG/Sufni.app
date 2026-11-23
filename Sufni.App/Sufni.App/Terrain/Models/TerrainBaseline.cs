using System;
using System.Collections.Immutable;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Models;

public readonly record struct SessionProfileReference(Guid SessionId, TerrainProfileKey Key);

public sealed record TerrainBaselineIdentity(ImmutableArray<SessionProfileReference> Profiles)
{
    public static TerrainBaselineIdentity Empty { get; } = new([]);
    public bool Equals(TerrainBaselineIdentity? other) => other is not null && Profiles.AsSpan().SequenceEqual(other.Profiles.AsSpan());
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var profile in Profiles) hash.Add(profile);
        return hash.ToHashCode();
    }
}

public closed record TerrainBaseline(TerrainBaselineIdentity Identity)
{
    public sealed record NotLoaded(TerrainBaselineIdentity Identity) : TerrainBaseline(Identity);
    public sealed record Failed(TerrainBaselineIdentity Identity) : TerrainBaseline(Identity);
    public sealed record Fitted(TerrainBaselineIdentity Identity, SuspensionResponseModel Model) : TerrainBaseline(Identity);
    public sealed record Insufficient(TerrainBaselineIdentity Identity, SuspensionResponseModel Model) : TerrainBaseline(Identity);
}
