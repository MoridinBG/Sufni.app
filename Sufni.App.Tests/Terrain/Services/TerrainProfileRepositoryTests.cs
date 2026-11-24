using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Store;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Queries;
using Sufni.App.Tests.TestSupport.Io;
using Sufni.App.Tests.TestSupport.Persistence;
using Sufni.Telemetry;
using Sufni.Telemetry.Tests;

namespace Sufni.App.Tests.Terrain.Services;

public class TerrainProfileRepositoryTests
{
    [Fact]
    public async Task OffsetChangeRejectsOldWritesAndExactReadsOnlyLoadComputedWindows()
    {
        using var temp = new TempDatabase();
        await using var db = new TestPersistenceHarness(temp.DatabasePath);
        var session = new Session(Guid.NewGuid(), "ride", "", null, 150) { ProcessedData = [1], DurationSeconds = 15 };
        await db.PutAsync(session);
        var key = SessionTerrainStatusQuery.Evaluate(SessionSnapshot.From((await db.GetSessionAsync(session.Id))!), []).Key;
        var window = TerrainProfiles.Window(0) with { Roughness = RoughnessClass.Rough, Source = RoughnessSource.FrameOnly };
        var profile = TerrainProfiles.Profile([window], steepness: TerrainSteepnessState.NoElevation, lowersScale: null)
            with { FrameMatching = [new(1, 0.2, -1)] };
        Assert.True(await db.TerrainProfiles.TryCommitAsync(session.Id, key, TerrainMaterialization.Computed(profile)));
        var stored = SessionSnapshot.From(Assert.Single(await db.GetSessionsAsync()));
        var current = Assert.IsType<TerrainStatus.Current>(SessionTerrainStatusQuery.Evaluate(stored, []).Status);
        Assert.Equal(10, current.Summary.ClassifiedMetres);
        Assert.Equal(1, current.Summary.RoughShare);
        var loaded = Assert.Single(await db.TerrainProfiles.ReadAsync(new Dictionary<Guid, TerrainProfileKey> { [session.Id] = key })).Value;
        Assert.Equal(window, Assert.Single(loaded.Windows));
        Assert.Equal(profile.FrameMatching[0], Assert.Single(loaded.FrameMatching));

        await db.SessionRepository.UpdateGpsAlignmentAsync(session.Id, 3);
        var unavailable = TerrainMaterialization.WithoutProfile(TerrainOutcome.NoTrack);
        Assert.False(await db.TerrainProfiles.TryCommitAsync(session.Id, key, unavailable));
        var edited = SessionSnapshot.From((await db.GetSessionAsync(session.Id))!);
        var nextKey = SessionTerrainStatusQuery.Evaluate(edited, []).Key;
        Assert.Empty(await db.TerrainProfiles.ReadAsync(new Dictionary<Guid, TerrainProfileKey> { [session.Id] = nextKey }));
        Assert.True(await db.TerrainProfiles.TryCommitAsync(session.Id, nextKey, unavailable));
        Assert.Empty(await db.TerrainProfiles.ReadAsync(new Dictionary<Guid, TerrainProfileKey> { [session.Id] = key }));
        Assert.Empty(await db.TerrainProfiles.ReadAsync(new Dictionary<Guid, TerrainProfileKey> { [session.Id] = nextKey }));
        var latest = SessionSnapshot.From((await db.GetSessionAsync(session.Id))!);
        Assert.Null(latest.TerrainSummary);
        Assert.Equal(new TerrainStatus.Unavailable(TerrainAvailabilityReason.NoTrack),
            SessionTerrainStatusQuery.Evaluate(latest, []).Status);
    }
}
