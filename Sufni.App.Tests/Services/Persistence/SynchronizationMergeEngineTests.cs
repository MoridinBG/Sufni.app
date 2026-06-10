using SQLite;
using Sufni.App.ExtensionHost.Database;
using Sufni.App.ExtensionHost.Models;
using Sufni.App.ExtensionHost.SessionDetails;
using Sufni.App.ExtensionHosting.Database;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Tests.Infrastructure;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Services.Persistence;

public class SynchronizationMergeEngineTests
{
    [Fact]
    public async Task UpdateLastSyncTimeAsync_InsertsRow_WhenMissing()
    {
        using var tempDatabase = new TempDatabase("sync.db");
        var databasePath = tempDatabase.DatabasePath;

        var database = new TestPersistenceHarness(databasePath);

        await database.UpdateLastSyncTimeAsync("https://sync.test");

        var lastSyncTime = await database.GetLastSyncTimeAsync("https://sync.test");
        Assert.True(lastSyncTime > 0);

        using var connection = new SQLiteConnection(databasePath);
        var rows = connection.Table<Synchronization>()
            .Where(s => s.ServerUrl == "https://sync.test")
            .ToList();
        Assert.Single(rows);
    }

    [Fact]
    public async Task UpdateLastSyncTimeAsync_UpdatesExistingRow_WithoutDuplicatingIt()
    {
        using var tempDatabase = new TempDatabase("sync.db");
        var databasePath = tempDatabase.DatabasePath;

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetLastSyncTimeAsync("https://sync.test");

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Synchronization
            {
                ServerUrl = "https://sync.test",
                LastSyncTime = 1
            });
        }

        await database.UpdateLastSyncTimeAsync("https://sync.test");

        var lastSyncTime = await database.GetLastSyncTimeAsync("https://sync.test");
        Assert.True(lastSyncTime > 1);

        using var verificationConnection = new SQLiteConnection(databasePath);
        var rows = verificationConnection.Table<Synchronization>()
            .Where(s => s.ServerUrl == "https://sync.test")
            .ToList();
        Assert.Single(rows);
    }

    [Fact]
    public async Task GetSynchronizationDataAsync_IncludesTrackReferencedByChangedSession()
    {
        using var tempDatabase = new TempDatabase("sync-data.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var trackId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Track
            {
                Id = trackId,
                Points =
                [
                    new TrackPoint(100, 1, 1, 10),
                    new TrackPoint(101, 2, 2, 11)
                ],
                Updated = 50,
                ClientUpdated = 50
            });

            connection.Insert(new Session(sessionId, "session", "desc", null, 100)
            {
                FullTrack = trackId,
                Updated = 150,
                ClientUpdated = 150
            });
        }

        var syncData = await database.GetSynchronizationDataAsync(100);

        Assert.Single(syncData.Sessions);
        Assert.Single(syncData.Tracks);
        Assert.Equal(trackId, syncData.Tracks[0].Id);

    }

    [Fact]
    public async Task ApplyRemoteSynchronizationDataAsync_AllowsDeletedTrackWithoutPoints()
    {
        using var tempDatabase = new TempDatabase("deleted-track-sync.db");
        var databasePath = tempDatabase.DatabasePath;
        var trackId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);

        await database.ApplyRemoteSynchronizationDataAsync(new SynchronizationData
        {
            Tracks =
            [
                new Track
                {
                    Id = trackId,
                    Points = [],
                    Deleted = 100,
                    Updated = 100,
                    ClientUpdated = 100
                }
            ]
        });

        using var verificationConnection = new SQLiteConnection(databasePath);
        var track = verificationConnection.Table<Track>().Single(t => t.Id == trackId);

        Assert.Equal(100, track.Deleted);
        Assert.False(track.HasPoints);

    }

    [Fact]
    public async Task ApplyRemoteSynchronizationDataAsync_UpdatesSessionSyncFields_WithoutClearingPsst()
    {
        using var tempDatabase = new TempDatabase("remote-sync.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var setupId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        var originalPsst = new byte[] { 1, 2, 3, 4 };

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Session(sessionId, "local", "local desc", null, 50)
            {
                ProcessedData = originalPsst,
                Updated = 1,
                ClientUpdated = 1
            });
        }

        var remoteSession = new Session(sessionId, "remote", "remote desc", setupId, 1234)
        {
            FullTrack = trackId,
            Track =
            [
                new TrackPoint(1234, 1, 1, 100),
                new TrackPoint(1235, 2, 2, 101)
            ],
            ProcessingFingerprintJson = """{"remote":true}""",
            DurationSeconds = 65,
            DistanceMeters = 10,
            AscentMeters = 4,
            DescentMeters = 2,
            FrontSpringRate = "50",
            RearSpringRate = "60",
            Updated = 99,
            ClientUpdated = 88
        };

        var remoteTrack = new Track
        {
            Id = trackId,
            Points =
            [
                new TrackPoint(1234, 10, 10, 100),
                new TrackPoint(1235, 20, 20, 101)
            ],
            Updated = 99,
            ClientUpdated = 88
        };

        await database.ApplyRemoteSynchronizationDataAsync(new SynchronizationData
        {
            Sessions = [remoteSession],
            Tracks = [remoteTrack]
        });

        var session = await database.GetSessionAsync(sessionId);
        var sessionTrack = await database.GetSessionTrackAsync(sessionId);
        var rawPsst = await database.GetSessionRawPsstAsync(sessionId);
        var fullTrack = await database.GetAsync<Track>(trackId);

        Assert.NotNull(session);
        Assert.Equal("remote", session!.Name);
        Assert.Equal("remote desc", session.Description);
        Assert.Equal(setupId, session.Setup);
        Assert.Equal(1234, session.Timestamp);
        Assert.Equal(trackId, session.FullTrack);
        Assert.Equal("""{"remote":true}""", session.ProcessingFingerprintJson);
        Assert.Equal(65, session.DurationSeconds);
        Assert.Equal(10, session.DistanceMeters);
        Assert.Equal(4, session.AscentMeters);
        Assert.Equal(2, session.DescentMeters);
        Assert.Equal(99, session.Updated);
        Assert.Equal("50", session.FrontSpringRate);
        Assert.Equal("60", session.RearSpringRate);
        Assert.NotNull(sessionTrack);
        Assert.Equal(2, sessionTrack!.Count);
        Assert.Equal(originalPsst, rawPsst);
        Assert.NotNull(fullTrack);
        Assert.Equal(2, fullTrack!.Points.Count);

    }

    [Fact]
    public async Task MergeAllAsync_UpdatesSessionSyncFields_WithoutClearingPsst()
    {
        using var tempDatabase = new TempDatabase("merge-session-sync.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var setupId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        var originalPsst = new byte[] { 1, 2, 3, 4 };

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Session(sessionId, "local", "local desc", null, 50)
            {
                ProcessedData = originalPsst,
                Updated = 1,
                ClientUpdated = 1
            });
        }

        await database.MergeAllAsync(new SynchronizationData
        {
            Sessions =
            [
                new Session(sessionId, "remote", "remote desc", setupId, 1234)
                {
                    FullTrack = trackId,
                    Track =
                    [
                        new TrackPoint(1234, 1, 1, 100),
                        new TrackPoint(1235, 2, 2, 101)
                    ],
                    ProcessingFingerprintJson = """{"remote":true}""",
                    DurationSeconds = 65,
                    DistanceMeters = 10,
                    AscentMeters = 4,
                    DescentMeters = 2,
                    FrontSpringRate = "50",
                    RearSpringRate = "60",
                    Updated = 99,
                    ClientUpdated = 88
                }
            ]
        });

        var session = await database.GetSessionAsync(sessionId);
        var changedSession = Assert.Single(await database.GetChangedAsync<Session>(0));
        var sessionTrack = await database.GetSessionTrackAsync(sessionId);
        var rawPsst = await database.GetSessionRawPsstAsync(sessionId);

        Assert.NotNull(session);
        Assert.Equal("remote", session!.Name);
        Assert.Equal("remote desc", session.Description);
        Assert.Equal(setupId, session.Setup);
        Assert.Equal(1234, session.Timestamp);
        Assert.Equal(trackId, session.FullTrack);
        Assert.Equal("""{"remote":true}""", session.ProcessingFingerprintJson);
        Assert.Equal(65, session.DurationSeconds);
        Assert.Equal(10, session.DistanceMeters);
        Assert.Equal(4, session.AscentMeters);
        Assert.Equal(2, session.DescentMeters);
        Assert.Equal("50", session.FrontSpringRate);
        Assert.Equal("60", session.RearSpringRate);
        Assert.True(session.HasProcessedData);
        Assert.True(changedSession.HasProcessedData);
        Assert.NotNull(sessionTrack);
        Assert.Equal(2, sessionTrack!.Count);
        Assert.Equal(originalPsst, rawPsst);

    }

    [Fact]
    public async Task MergeAllAsync_PushPayloadRoundTrip_PreservesExistingPsst_WhenSyncStopsBeforeRepair()
    {
        using var tempDatabase = new TempDatabase("interrupted-sync.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var originalPsst = new byte[] { 9, 8, 7, 6 };

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Session(sessionId, "server", "server desc", null, 50)
            {
                ProcessedData = originalPsst,
                Updated = 1,
                ClientUpdated = 1
            });
        }

        var pushPayload = AppJson.Deserialize<SynchronizationData>(AppJson.Serialize(new SynchronizationData
        {
            Sessions =
            [
                new Session(sessionId, "client", "client desc", null, 1234)
                {
                    ProcessedData = [1, 2, 3],
                    Updated = 99,
                    ClientUpdated = 88
                }
            ]
        }));

        Assert.NotNull(pushPayload);
        Assert.Single(pushPayload!.Sessions);
        Assert.Null(pushPayload.Sessions[0].ProcessedData);
        Assert.False(pushPayload.Sessions[0].HasProcessedData);

        await database.MergeAllAsync(pushPayload);

        Assert.Equal(originalPsst, await database.GetSessionRawPsstAsync(sessionId));
        Assert.Empty(await database.GetIncompleteSessionIdsAsync());

    }

    [Fact]
    public async Task MergeAllAsync_DoesNotMutateIncomingBoardTimestamps_WhenInsertOrUpdateAccepted()
    {
        using var tempDatabase = new TempDatabase("merge-board-nonmutating.db");
        var databasePath = tempDatabase.DatabasePath;
        var existingBoardId = Guid.NewGuid();
        var insertedBoardId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetAllAsync<Board>();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Board(existingBoardId, null)
            {
                Updated = 1,
                ClientUpdated = 1
            });
        }

        var updatedBoard = new Board(existingBoardId, Guid.NewGuid())
        {
            Updated = 99,
            ClientUpdated = 88
        };
        var insertedBoard = new Board(insertedBoardId, Guid.NewGuid())
        {
            Updated = 199,
            ClientUpdated = 177
        };

        await database.MergeAllAsync(new SynchronizationData
        {
            Boards = [updatedBoard, insertedBoard]
        });

        Assert.Equal(99, updatedBoard.Updated);
        Assert.Equal(88, updatedBoard.ClientUpdated);
        Assert.Equal(199, insertedBoard.Updated);
        Assert.Equal(177, insertedBoard.ClientUpdated);

        var storedUpdatedBoard = await database.GetAsync<Board>(existingBoardId);
        var storedInsertedBoard = await database.GetAsync<Board>(insertedBoardId);

        Assert.NotNull(storedUpdatedBoard);
        Assert.NotNull(storedInsertedBoard);
        Assert.Equal(99, storedUpdatedBoard!.ClientUpdated);
        Assert.Equal(199, storedInsertedBoard!.ClientUpdated);

    }

    [Fact]
    public async Task MergeAllAsync_DoesNotMutateIncomingSessionTimestamps_WhenInsertOrUpdateAccepted()
    {
        using var tempDatabase = new TempDatabase("merge-session-nonmutating.db");
        var databasePath = tempDatabase.DatabasePath;
        var existingSessionId = Guid.NewGuid();
        var insertedSessionId = Guid.NewGuid();
        var originalPsst = new byte[] { 4, 3, 2, 1 };

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Session(existingSessionId, "local", "local desc", null, 50)
            {
                ProcessedData = originalPsst,
                Updated = 1,
                ClientUpdated = 1
            });
        }

        var updatedSession = new Session(existingSessionId, "remote", "remote desc", null, 1234)
        {
            Updated = 99,
            ClientUpdated = 88
        };
        var insertedSession = new Session(insertedSessionId, "new", "new desc", null, 2345)
        {
            Updated = 199,
            ClientUpdated = 177
        };

        await database.MergeAllAsync(new SynchronizationData
        {
            Sessions = [updatedSession, insertedSession]
        });

        Assert.Equal(99, updatedSession.Updated);
        Assert.Equal(88, updatedSession.ClientUpdated);
        Assert.Equal(199, insertedSession.Updated);
        Assert.Equal(177, insertedSession.ClientUpdated);

        using var verificationConnection = new SQLiteConnection(databasePath);
        var storedUpdatedSession = verificationConnection.Table<Session>().Single(s => s.Id == existingSessionId);
        var storedInsertedSession = verificationConnection.Table<Session>().Single(s => s.Id == insertedSessionId);

        Assert.NotNull(storedUpdatedSession);
        Assert.NotNull(storedInsertedSession);
        Assert.Equal(99, storedUpdatedSession.ClientUpdated);
        Assert.Equal(199, storedInsertedSession.ClientUpdated);
        Assert.Equal(originalPsst, await database.GetSessionRawPsstAsync(existingSessionId));
        Assert.Null(await database.GetSessionRawPsstAsync(insertedSessionId));

    }

    [Fact]
    public async Task MergeAllAsync_DoesNotResurrectDeletedBoard_WhenIncomingUpdateIsNotDeleted()
    {
        using var tempDatabase = new TempDatabase("merge-delete.db");
        var databasePath = tempDatabase.DatabasePath;
        var boardId = Guid.NewGuid();
        var deletedSetupId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetAllAsync<Board>();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Board(boardId, deletedSetupId)
            {
                Updated = 110,
                ClientUpdated = 100,
                Deleted = 100
            });
        }

        await database.MergeAllAsync(new SynchronizationData
        {
            Boards =
            [
                new Board(boardId, Guid.NewGuid())
                {
                    Updated = 150,
                    ClientUpdated = 150,
                    Deleted = null
                }
            ]
        });

        using var verificationConnection = new SQLiteConnection(databasePath);
        var board = verificationConnection.Table<Board>().Single(b => b.Id == boardId);

        Assert.Equal(deletedSetupId, board.SetupId);
        Assert.Equal(100, board.Deleted);

    }

    [Fact]
    public async Task MergeAllAsync_DoesNotApplyStaleDeleteOverNewerLiveBoard()
    {
        using var tempDatabase = new TempDatabase("merge-stale-delete.db");
        var databasePath = tempDatabase.DatabasePath;
        var boardId = Guid.NewGuid();
        var liveSetupId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetAllAsync<Board>();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Board(boardId, liveSetupId)
            {
                Updated = 210,
                ClientUpdated = 200
            });
        }

        await database.MergeAllAsync(new SynchronizationData
        {
            Boards =
            [
                new Board(boardId, null)
                {
                    Updated = 150,
                    ClientUpdated = 150,
                    Deleted = 150
                }
            ]
        });

        using var verificationConnection = new SQLiteConnection(databasePath);
        var board = verificationConnection.Table<Board>().Single(b => b.Id == boardId);

        Assert.Equal(liveSetupId, board.SetupId);
        Assert.Null(board.Deleted);

    }

    [Fact]
    public async Task MergeAllAsync_AppliesNewerDeleteOverOlderLiveBoard()
    {
        using var tempDatabase = new TempDatabase("merge-new-delete.db");
        var databasePath = tempDatabase.DatabasePath;
        var boardId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetAllAsync<Board>();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Board(boardId, Guid.NewGuid())
            {
                Updated = 110,
                ClientUpdated = 100
            });
        }

        await database.MergeAllAsync(new SynchronizationData
        {
            Boards =
            [
                new Board(boardId, null)
                {
                    Updated = 150,
                    ClientUpdated = 150,
                    Deleted = 150
                }
            ]
        });

        using var verificationConnection = new SQLiteConnection(databasePath);
        var board = verificationConnection.Table<Board>().Single(b => b.Id == boardId);

        Assert.Equal(150, board.Deleted);

    }
}
