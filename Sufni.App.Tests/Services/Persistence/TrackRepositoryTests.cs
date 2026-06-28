using SQLite;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHosting.Database;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Tests.Infrastructure;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Services.Persistence;

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

        Assert.Null(await database.FindTrackContainingTimestampAsync(null));
    }
}
