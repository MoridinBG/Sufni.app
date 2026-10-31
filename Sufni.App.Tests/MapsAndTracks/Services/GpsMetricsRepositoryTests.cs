using System.Text.Json;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Queries;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Tests.TestSupport.Persistence;

namespace Sufni.App.Tests.MapsAndTracks.Services;

public class GpsMetricsRepositoryTests
{
    [Fact]
    public async Task TryCommitGpsMetricsAsync_PublishesLocalValuesWithoutChangingAuthoredClockOrWirePayload()
    {
        using var temp = new TempDatabase();
        await using var context = PersistenceTestData.CreateConnectionContext(temp.DatabasePath);
        var connection = await context.GetInitializedConnectionAsync();
        var session = new Session(Guid.NewGuid(), "ride", "notes", null, 100) { DurationSeconds = 2, Updated = 42 };
        await connection.InsertAsync(session);
        await connection.InsertAsync(new Track(PersistenceTestData.CreateFullTrack().Points));
        var tracks = new TrackRepository(context);
        var sessions = new SessionRepository(context, PersistenceTestData.CreateTimeProvider());
        var key = SessionGpsMetricsQuery.Evaluate(100, 2, 0, 0, await tracks.GetTrackSnapshotsAsync()).Key;

        Assert.True(await tracks.TryCommitGpsMetricsAsync(session.Id, key, new GpsMetrics(5, 4, 3), TestContext.Current.CancellationToken));
        await sessions.PutSessionAsync(new Session(session.Id, "edited", "new notes", null));
        var edited = (await sessions.GetSessionAsync(session.Id))!;
        var editedClock = edited.Updated;
        Assert.True(await tracks.TryCommitGpsMetricsAsync(session.Id, key, new GpsMetrics(6, 7, 8), TestContext.Current.CancellationToken));
        var current = (await sessions.GetSessionAsync(session.Id))!;

        Assert.Equal(editedClock, current.Updated);
        Assert.Equal("edited", current.Name);
        Assert.Equal(key, current.GpsMetricsKey);
        Assert.Equal(6, current.DistanceMeters);
        Assert.Equal(7, current.AscentMeters);
        Assert.Equal(8, current.DescentMeters);
        using var document = JsonDocument.Parse(AppJson.Serialize(current));
        Assert.False(document.RootElement.TryGetProperty("distance_meters", out _));
        Assert.False(document.RootElement.TryGetProperty("ascent_meters", out _));
        Assert.False(document.RootElement.TryGetProperty("descent_meters", out _));
        Assert.False(document.RootElement.TryGetProperty("GpsMetricsKey", out _));
    }

    [Theory]
    [InlineData("offset")]
    [InlineData("duration")]
    [InlineData("origin")]
    [InlineData("track")]
    [InlineData("deletion")]
    public async Task TryCommitGpsMetricsAsync_RejectsChangedInputs_AndRetainsPreviousMaterialization(string change)
    {
        using var temp = new TempDatabase();
        await using var context = PersistenceTestData.CreateConnectionContext(temp.DatabasePath);
        var connection = await context.GetInitializedConnectionAsync();
        var id = Guid.NewGuid();
        await connection.InsertAsync(new Session(id, "ride", "", null, 100) { DurationSeconds = 2, Updated = 42 });
        await connection.InsertAsync(new Track(PersistenceTestData.CreateFullTrack().Points));
        var repository = new TrackRepository(context);
        var key = SessionGpsMetricsQuery.Evaluate(100, 2, 0, 0, await repository.GetTrackSnapshotsAsync()).Key;
        Assert.True(await repository.TryCommitGpsMetricsAsync(id, key, new GpsMetrics(5, 4, 3), TestContext.Current.CancellationToken));
        switch (change)
        {
            case "offset": await connection.ExecuteAsync("UPDATE session SET gps_offset_seconds=1 WHERE id=?", id); break;
            case "duration": await connection.ExecuteAsync("UPDATE session SET duration_seconds=3 WHERE id=?", id); break;
            case "origin": await connection.ExecuteAsync("UPDATE session SET window_start_seconds=0.25 WHERE id=?", id); break;
            case "deletion": await connection.ExecuteAsync("UPDATE session SET deleted=1 WHERE id=?", id); break;
            case "track": await connection.InsertAsync(new Track([new TrackPoint(100, 0, 0, 0), new TrackPoint(103, 5, 5, 5)])); break;
        }

        Assert.False(await repository.TryCommitGpsMetricsAsync(id, key, new GpsMetrics(99, 99, 99), TestContext.Current.CancellationToken));
        Assert.Equal(5, await connection.ExecuteScalarAsync<double>("SELECT distance_meters FROM session_gps_metrics WHERE session_id=?", id));
        Assert.Equal(AppJson.Serialize(key), await connection.ExecuteScalarAsync<string>("SELECT gps_metrics_key FROM session_gps_metrics WHERE session_id=?", id));
        Assert.Equal(42, await connection.ExecuteScalarAsync<long>("SELECT updated FROM session WHERE id=?", id));
    }
}
