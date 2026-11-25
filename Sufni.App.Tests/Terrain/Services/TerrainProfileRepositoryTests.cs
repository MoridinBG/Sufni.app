using Sufni.App;
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
        var window = TerrainProfiles.Window(0) with { Frame = TerrainProfiles.Sensor(2) };
        var profile = TerrainProfiles.Profile([window], grade: TerrainGradeState.NoElevation, forkScale: null)
            with { FrameMatching = [new(1, 0.2, -1)] };
        Assert.True(await db.TerrainProfiles.TryCommitAsync(session.Id, key, TerrainMaterialization.Computed(profile)));
        var stored = SessionSnapshot.From(Assert.Single(await db.GetSessionsAsync()));
        var current = Assert.IsType<TerrainStatus.Current>(SessionTerrainStatusQuery.Evaluate(stored, []).Status);
        Assert.Equal(TerrainProfileCalculator.Summarize(profile), current.Summary);
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
        Assert.Equal(new TerrainStatus.Unavailable(TerrainAvailabilityReason.NoTrack),
            SessionTerrainStatusQuery.Evaluate(latest, []).Status);
    }

    [Fact]
    public async Task SummaryStoredByAnEarlierAlgorithmReadsAsPending()
    {
        using var temp = new TempDatabase();
        await using var db = new TestPersistenceHarness(temp.DatabasePath);
        var session = new Session(Guid.NewGuid(), "ride", "", null, 150) { ProcessedData = [1], DurationSeconds = 15 };
        await db.PutAsync(session);
        var key = SessionTerrainStatusQuery.Evaluate(SessionSnapshot.From((await db.GetSessionAsync(session.Id))!), []).Key;
        var connection = await db.GetInitializedConnectionAsync();
        await connection.ExecuteAsync("""
            INSERT INTO session_terrain_profile (session_id, terrain_profile_key, terrain_outcome, terrain_summary, profile)
            VALUES (?, ?, ?, ?, ?)
            """, session.Id, AppJson.Serialize(key with { AlgorithmVersion = 4 }), (int)TerrainOutcome.Computed,
            """{"ClassifiedMetres":590,"CalmShare":0.08,"MediumShare":0.39,"RoughShare":0.53,"GentleShare":null,"ModerateShare":null,"SteepShare":null,"DominantSource":"frame_only"}""",
            new byte[] { 1 });

        var stored = SessionSnapshot.From(Assert.Single(await db.GetSessionsAsync()));
        Assert.IsType<TerrainStatus.Pending>(SessionTerrainStatusQuery.Evaluate(stored, []).Status);
    }

    [Fact]
    public async Task ClassCommitWritesOnlyRowsThatHoldTheComputedProfileOfTheirClassKey()
    {
        using var temp = new TempDatabase();
        await using var db = new TestPersistenceHarness(temp.DatabasePath);
        var token = TestContext.Current.CancellationToken;
        var (current, keyOfCurrent) = await StoreAsync(db, TerrainMaterialization.Computed(Profile()));
        var (replaced, keyOfReplaced) = await StoreAsync(db, TerrainMaterialization.Computed(Profile()));
        var (unavailable, keyOfUnavailable) = await StoreAsync(db, TerrainMaterialization.WithoutProfile(TerrainOutcome.NoTrack));
        var summary = new TerrainClassSummary(400, 0.25);

        var written = await db.TerrainProfiles.CommitClassesAsync(new Dictionary<Guid, (TerrainClassKey, TerrainClassSummary)>
        {
            [current] = (ClassKey(keyOfCurrent), summary),
            [replaced] = (ClassKey(keyOfReplaced with { AlgorithmVersion = keyOfReplaced.AlgorithmVersion - 1 }), summary),
            [unavailable] = (ClassKey(keyOfUnavailable), summary)
        }, token);

        Assert.Equal([current], written);
        var stored = (await db.GetSessionsAsync()).Select(SessionSnapshot.From).ToDictionary(session => session.Id);
        Assert.Equal((ClassKey(keyOfCurrent), summary), (stored[current].TerrainClassKey, stored[current].TerrainClassSummary));
        Assert.Null(stored[replaced].TerrainClassKey);
        Assert.Null(stored[unavailable].TerrainClassSummary);
    }

    [Fact]
    public async Task ProfileCommitClearsTheStoredClasses()
    {
        using var temp = new TempDatabase();
        await using var db = new TestPersistenceHarness(temp.DatabasePath);
        var token = TestContext.Current.CancellationToken;
        var (id, key) = await StoreAsync(db, TerrainMaterialization.Computed(Profile()));
        Assert.Equal([id], await db.TerrainProfiles.CommitClassesAsync(
            new Dictionary<Guid, (TerrainClassKey, TerrainClassSummary)> { [id] = (ClassKey(key), new(400, 0.25)) }, token));

        Assert.True(await db.TerrainProfiles.TryCommitAsync(id, key, TerrainMaterialization.Computed(Profile()), token));

        var stored = SessionSnapshot.From((await db.GetSessionAsync(id))!);
        Assert.Equal((null, null), (stored.TerrainClassKey, stored.TerrainClassSummary));
    }

    private static TerrainProfile Profile() => TerrainProfiles.Profile([TerrainProfiles.Window(0)]);

    private static TerrainClassKey ClassKey(TerrainProfileKey profile) =>
        new(profile, TerrainProfiles.Scale, TerrainClassifier.Version);

    private static async Task<(Guid Id, TerrainProfileKey Key)> StoreAsync(TestPersistenceHarness db, TerrainMaterialization result)
    {
        var session = new Session(Guid.NewGuid(), "ride", "", null, 150) { ProcessedData = [1], DurationSeconds = 15 };
        await db.PutAsync(session);
        var key = SessionTerrainStatusQuery.Evaluate(SessionSnapshot.From((await db.GetSessionAsync(session.Id))!), []).Key;
        Assert.True(await db.TerrainProfiles.TryCommitAsync(session.Id, key, result, TestContext.Current.CancellationToken));
        return (session.Id, key);
    }
}
