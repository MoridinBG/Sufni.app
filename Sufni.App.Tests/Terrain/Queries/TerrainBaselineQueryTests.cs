using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Queries;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Terrain.Queries;

public class TerrainBaselineQueryTests
{
    [Fact]
    public void IdentityContainsOnlyOtherCurrentRidesOfTheBikeAndIgnoresCatalogOrdering()
    {
        var bike = Guid.NewGuid();
        var session = TestSnapshots.Session(hasProcessedData: true);
        var key = SessionTerrainStatusQuery.Evaluate(session, []).Key;
        var row = new RecordedSessionSummary(session.Id, 1, "ride", "", 100, true, new SessionStaleness.Current(), BikeId: bike)
        { Terrain = new TerrainStatus.Current(key, new TerrainSummary(600, null)) };
        var first = row with { Id = Guid.NewGuid() };
        var second = row with { Id = Guid.NewGuid() };
        RecordedSessionSummary[] catalog = [row, first, second, row with { Id = Guid.NewGuid(), BikeId = Guid.NewGuid() },
            row with { Id = Guid.NewGuid(), Terrain = new TerrainStatus.Pending() },
            row with { Id = Guid.NewGuid(), Terrain = new TerrainStatus.Failed() },
            row with { Id = Guid.NewGuid(), Terrain = new TerrainStatus.Unavailable(TerrainAvailabilityReason.NoTrack) }];
        var identity = TerrainBaselineQuery.Select(row.Id, bike, catalog);
        Assert.Equal(new[] { first.Id, second.Id }.Order(), identity.Profiles.Select(p => p.SessionId));
        Assert.Equal(identity, TerrainBaselineQuery.Select(row.Id, bike, catalog.Reverse()));
        Assert.Empty(TerrainBaselineQuery.Select(row.Id, null, catalog).Profiles);
    }
}
