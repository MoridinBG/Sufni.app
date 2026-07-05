using SQLite;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.Telemetry;

using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Tests.TestSupport.Persistence;
namespace Sufni.App.Tests.MapsAndTracks.Services;

public class TrackRepositoryTests
{
    [Fact]
    public async Task PutAsync_Throws_WhenLiveTrackHasNoPoints()
    {
        using var tempDatabase = new TempDatabase("track-validation.db");
        var databasePath = tempDatabase.DatabasePath;

        var database = new TestPersistenceHarness(databasePath);

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.PutAsync(new Track { Points = [] }));
    }

    [Fact]
    public async Task FindTrackByTimeRangeAsync_ReturnsActiveTrackWithSameStartAndEnd()
    {
        using var tempDatabase = new TempDatabase("track-time-range.db");
        var databasePath = tempDatabase.DatabasePath;
        var trackId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        await database.PutAsync(new Track
        {
            Id = trackId,
            Points =
            [
                new TrackPoint(100, 1, 1, 10),
                new TrackPoint(101, 2, 2, 11)
            ]
        });

        var found = await database.FindTrackByTimeRangeAsync(100, 101);
        var missing = await database.FindTrackByTimeRangeAsync(100, 102);

        Assert.Equal(trackId, found);
        Assert.Null(missing);

    }

    [Fact]
    public async Task FindTrackContainingTimestampAsync_ReturnsTightestCoveringWindow()
    {
        using var tempDatabase = new TempDatabase("track-tightest-window.db");
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);

        var widestId = Guid.NewGuid();
        var wideId = Guid.NewGuid();
        var tightestId = Guid.NewGuid();

        // All three tracks cover timestamp 150; their windows are 250, 100, and 20.
        await database.PutAsync(new Track
        {
            Id = widestId,
            Points = [new TrackPoint(50, 1, 1, 10), new TrackPoint(300, 2, 2, 11)]
        });
        await database.PutAsync(new Track
        {
            Id = wideId,
            Points = [new TrackPoint(100, 1, 1, 10), new TrackPoint(200, 2, 2, 11)]
        });
        await database.PutAsync(new Track
        {
            Id = tightestId,
            Points = [new TrackPoint(140, 1, 1, 10), new TrackPoint(160, 2, 2, 11)]
        });

        var found = await database.FindTrackContainingTimestampAsync(150);

        // The tightest covering window wins, deterministically, regardless of insert order.
        Assert.Equal(tightestId, found);
    }

    [Fact]
    public async Task FindTrackContainingTimestampAsync_ReturnsNull_WhenTimestampIsMissing()
    {
        using var tempDatabase = new TempDatabase("track-no-timestamp.db");
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        _ = await database.GetInitializedConnectionAsync();

        Assert.Null(await database.FindTrackContainingTimestampAsync(null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(17)]
    public async Task GetTracksByIdsAsync_ReturnsActiveTracksForSmallInputCounts(int activeCount)
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

        var tracks = await database.GetTracksByIdsAsync(activeIds);

        Assert.Equal(activeIds.Length, tracks.Count);
        Assert.True(activeIds.ToHashSet().SetEquals(tracks.Select(track => track.Id)));
    }

    [Fact]
    public async Task GetTracksByIdsAsync_ReturnsActiveTracksAcrossChunks()
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

        var tracks = await database.GetTracksByIdsAsync([.. activeIds, deletedTrack.Id, Guid.NewGuid()]);

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

        var payload = await database.GetTrackPayloadAsync(trackId, metadata!.Updated);
        var stalePayload = await database.GetTrackPayloadAsync(trackId, metadata.Updated + 1);

        Assert.NotNull(payload);
        Assert.Equal(trackId, payload!.Id);
        Assert.Equal(metadata.Updated, payload.Updated);
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
}
