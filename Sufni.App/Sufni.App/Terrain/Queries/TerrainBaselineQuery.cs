using System;
using System.Collections.Generic;
using System.Linq;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Terrain.Models;

namespace Sufni.App.Terrain.Queries;

public static class TerrainBaselineQuery
{
    public static TerrainBaselineIdentity Select(Guid openSessionId, Guid? bikeId, IEnumerable<RecordedSessionSummary> sessions) =>
        bikeId is null ? TerrainBaselineIdentity.Empty : new([.. sessions
            .Where(s => s.Id != openSessionId && s.BikeId == bikeId && s.Terrain is TerrainStatus.Current)
            .OrderBy(s => s.Id)
            .Select(s => new SessionProfileReference(s.Id, ((TerrainStatus.Current)s.Terrain).Key))]);
}
