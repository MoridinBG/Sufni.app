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
    public async Task AssociateSessionWithTrackAsync_UpdatesAssociationAndBumpsSessionUpdated()
    {
        using var tempDatabase = new TempDatabase("session-track-association.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var trackId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Session(sessionId, "session", "desc", null, 100)
            {
                Updated = 1,
                ClientUpdated = 1
            });
        }

        await database.PutAsync(new Track
        {
            Id = trackId,
            Points =
            [
                new TrackPoint(90, 1, 1, 0),
                new TrackPoint(110, 2, 2, 0)
            ]
        });

        var before = await database.GetSessionAsync(sessionId);

        var associatedTrackId = await database.AssociateSessionWithTrackAsync(sessionId);

        var after = await database.GetSessionAsync(sessionId);

        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.Equal(trackId, associatedTrackId);
        Assert.Equal(trackId, after!.FullTrack);
        Assert.True(after.Updated > before!.Updated);

    }
}
