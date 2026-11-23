using System.Collections.Generic;
using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Store;
using Sufni.App.Terrain.Models;

namespace Sufni.App.Terrain.Queries;

public static class SessionTerrainStatusQuery
{
    public static (TerrainProfileKey Key, TerrainStatus Status) Evaluate(SessionSnapshot session,
        IReadOnlyList<TrackSnapshot> tracks) => Evaluate(session, tracks, out _);

    internal static (TerrainProfileKey Key, TerrainStatus Status) Evaluate(SessionSnapshot session,
        IReadOnlyList<TrackSnapshot> tracks, out TrackSnapshot? selected)
    {
        var key = TerrainProfileKey.From(SessionContentToken.Create(session, tracks, out selected));
        if (!session.HasProcessedData)
            return (key, new TerrainStatus.Unavailable(TerrainAvailabilityReason.NoTelemetry));
        if (session.TerrainProfileKey != key)
            return (key, new TerrainStatus.Pending());

        TerrainStatus status = session.TerrainOutcome switch
        {
            TerrainOutcome.Computed when session.TerrainSummary is { } summary => new TerrainStatus.Current(key, summary),
            TerrainOutcome.NoTrack => new TerrainStatus.Unavailable(TerrainAvailabilityReason.NoTrack),
            TerrainOutcome.NoMovement => new TerrainStatus.Unavailable(TerrainAvailabilityReason.NoMovement),
            TerrainOutcome.ContentMissing => new TerrainStatus.Unavailable(TerrainAvailabilityReason.ContentMissing),
            TerrainOutcome.Failed => new TerrainStatus.Failed(),
            _ => new TerrainStatus.Pending()
        };
        return (key, status);
    }
}
