using SQLite;
using Sufni.App.Sessions.Models;
using Sufni.Telemetry;

using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.Tests.TestSupport.Persistence;
namespace Sufni.App.Tests.MapsAndTracks.Services;

public class TrackRepositoryTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task DeleteIfUnselectedAsync_RetainsOnlyTracksSelectedByAnActiveSession(bool fullerTrack, bool deletedSession)
    {
        using var temp = new TempDatabase();
        var database = new TestPersistenceHarness(temp.DatabasePath);
        var candidate = new Track([new TrackPoint(100, 1, 1, 10), new TrackPoint(110, 2, 2, 11)]);
        await database.PutAsync(candidate);
        if (fullerTrack)
            await database.PutAsync(new Track([new TrackPoint(100, 1, 1, 10), new TrackPoint(120, 2, 2, 11)]));
        var session = new Session(Guid.NewGuid(), "ride", "", null, 100) { DurationSeconds = 20 };
        await database.PutSessionAsync(session);
        if (deletedSession) await database.DeleteAsync(session);

        var deleted = await database.TrackRepository.DeleteIfUnselectedAsync(candidate.Id);

        Assert.Equal(fullerTrack || deletedSession, deleted);
        Assert.Equal(deleted, await database.GetAsync<Track>(candidate.Id) is null);
        Assert.False(await database.TrackRepository.DeleteIfUnselectedAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetTrackSnapshotsAsync_ReadsFractionalBoundsAndRevisionWithoutDecodingPoints()
    {
        using var temp = new TempDatabase();
        await using var context = PersistenceTestData.CreateConnectionContext(temp.DatabasePath);
        var connection = await context.GetInitializedConnectionAsync();
        var track = new Track([new TrackPoint(100.25, 1, 2, 3), new TrackPoint(101.75, 4, 5, 6)]);
        await connection.InsertAsync(track);
        await connection.InsertAsync(new Track { Deleted = 100 });
        await connection.ExecuteAsync("UPDATE track SET points = ? WHERE id = ?", "unreadable payload", track.Id);
        var repository = new TrackRepository(context, PersistenceTestData.CreateTimeProvider(), PersistenceTestData.CreateDeviceIdentity());

        var expected = new TrackSnapshot(track.Id, 100.25, 101.75, 2);
        Assert.Equal(expected, Assert.Single(await repository.GetTrackSnapshotsAsync()));
        Assert.Equal(expected, Assert.Single(await repository.GetTrackSnapshotsAsync([track.Id])));
        Assert.Empty(await repository.GetTrackSnapshotsAsync([]));
    }

    [Fact]
    public async Task PutAsync_Throws_WhenLiveTrackHasNoPoints()
    {
        using var tempDatabase = new TempDatabase("track-validation.db");
        var databasePath = tempDatabase.DatabasePath;

        var database = new TestPersistenceHarness(databasePath);

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.PutAsync(new Track { Points = [] }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(17)]
    public async Task GetTrackSnapshotsAsync_ReturnsActiveTracksForSmallInputCounts(int activeCount)
    {
        using var tempDatabase = new TempDatabase($"track-id-small-{activeCount}.db");
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var activeIds = Enumerable.Range(0, activeCount).Select(_ => Guid.NewGuid()).ToArray();

        for (var i = 0; i < activeIds.Length; i++)
        {
            await database.PutAsync(new Track
            {
                Id = activeIds[i],
                Points =
                [
                    new TrackPoint(100 + i, 1, 1, 10),
                    new TrackPoint(101 + i, 2, 2, 11),
                ],
            });
        }

        var tracks = await database.TrackRepository.GetTrackSnapshotsAsync(activeIds);

        Assert.Equal(activeIds.Length, tracks.Count);
        Assert.True(activeIds.ToHashSet().SetEquals(tracks.Select(track => track.Id)));
    }

    [Fact]
    public async Task GetTrackSnapshotsAsync_ReturnsActiveTracksAcrossChunks()
    {
        using var tempDatabase = new TempDatabase("track-id-chunks.db");
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var activeIds = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToArray();

        for (var i = 0; i < activeIds.Length; i++)
        {
            await database.PutAsync(new Track
            {
                Id = activeIds[i],
                Points =
                [
                    new TrackPoint(100 + i, 1, 1, 10),
                    new TrackPoint(101 + i, 2, 2, 11),
                ],
            });
        }

        var deletedTrack = new Track
        {
            Id = Guid.NewGuid(),
            Points =
            [
                new TrackPoint(1000, 1, 1, 10),
                new TrackPoint(1001, 2, 2, 11),
            ],
        };
        await database.PutAsync(deletedTrack);
        await database.DeleteAsync(deletedTrack);

        var tracks = await database.TrackRepository.GetTrackSnapshotsAsync([.. activeIds, deletedTrack.Id, Guid.NewGuid()]);

        Assert.Equal(activeIds.Length, tracks.Count);
        Assert.True(activeIds.ToHashSet().SetEquals(tracks.Select(track => track.Id)));
        Assert.DoesNotContain(tracks, track => track.Id == deletedTrack.Id);
    }

    [Fact]
    public async Task GetTrackPayloadAsync_ReturnsPointsForMatchingUpdatedTimestamp()
    {
        using var tempDatabase = new TempDatabase("track-payload.db");
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var trackId = Guid.NewGuid();
        var points = new List<TrackPoint>
        {
            new(100, 1, 1, 10),
            new(101, 2, 2, 11),
        };
        await database.PutAsync(new Track
        {
            Id = trackId,
            Points = points,
        });

        var metadata = await database.GetTrackPayloadMetadataAsync(trackId);
        Assert.NotNull(metadata);

        var payload = await database.GetTrackPayloadAsync(trackId, metadata!.PointsRevision);
        var stalePayload = await database.GetTrackPayloadAsync(trackId, metadata.PointsRevision + 1);

        Assert.NotNull(payload);
        Assert.Equal(trackId, payload!.Id);
        Assert.Equal(metadata.PointsRevision, payload.PointsRevision);
        Assert.Equal(points.Select(point => point.Time), payload.Points.Select(point => point.Time));
        Assert.Null(stalePayload);
    }

    [Fact]
    public async Task GetTrackPayloadMetadataAsync_ReturnsNullForDeletedTrack()
    {
        using var tempDatabase = new TempDatabase("track-payload-deleted.db");
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var track = new Track
        {
            Id = Guid.NewGuid(),
            Points =
            [
                new TrackPoint(100, 1, 1, 10),
                new TrackPoint(101, 2, 2, 11),
            ],
        };
        await database.PutAsync(track);
        await database.DeleteAsync(track);

        Assert.Null(await database.GetTrackPayloadMetadataAsync(track.Id));
        Assert.Null(await database.GetTrackPayloadAsync(track.Id, track.Updated));
    }

    [Fact]
    public async Task GetTrackSnapshotsAsync_ReturnsOnlyActiveRequestedRows()
    {
        using var tempDatabase = new TempDatabase("track-payload-metadata-by-id.db");
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var active = new Track
        {
            Id = Guid.NewGuid(),
            Points =
            [
                new TrackPoint(100, 1, 1, 10),
                new TrackPoint(101, 2, 2, 11),
            ],
        };
        var deleted = new Track
        {
            Id = Guid.NewGuid(),
            Points =
            [
                new TrackPoint(200, 1, 1, 10),
                new TrackPoint(201, 2, 2, 11),
            ],
        };
        await database.PutAsync(active);
        await database.PutAsync(deleted);
        await database.DeleteAsync(deleted);

        var metadata = await database.TrackRepository.GetTrackSnapshotsAsync(
            [active.Id, deleted.Id, Guid.NewGuid()]);

        var item = Assert.Single(metadata);
        Assert.Equal(active.Id, item.Id);
        Assert.Equal(1, item.PointsRevision);
    }

    [Fact]
    public async Task GetTrackSnapshotsAsync_ReturnsEmptyForEmptyInput()
    {
        using var tempDatabase = new TempDatabase("track-payload-metadata-empty.db");
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);

        var metadata = await database.TrackRepository.GetTrackSnapshotsAsync([]);

        Assert.Empty(metadata);
    }
}
