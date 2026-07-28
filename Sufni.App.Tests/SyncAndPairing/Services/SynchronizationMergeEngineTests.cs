using SQLite;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.Telemetry;

using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Stores;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Tests.TestSupport.Persistence;
namespace Sufni.App.Tests.SyncAndPairing.Services;

public class SynchronizationMergeEngineTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectionalCursorUpdates_InsertOrUpdateSingleRow(bool seedExistingRow)
    {
        using var tempDatabase = new TempDatabase("sync.db");
        var databasePath = tempDatabase.DatabasePath;

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetLastPushTimeAsync("https://sync.test");
        if (seedExistingRow)
        {
            using var connection = new SQLiteConnection(databasePath);
            connection.Insert(new Synchronization
            {
                ServerUrl = "https://sync.test",
                LastPushTime = 1,
                LastPullTime = 2,
            });
        }

        await database.UpdateLastPushTimeAsync("https://sync.test", 11);
        await database.UpdateLastPullTimeAsync("https://sync.test", 12);

        Assert.Equal(11, await database.GetLastPushTimeAsync("https://sync.test"));
        Assert.Equal(12, await database.GetLastPullTimeAsync("https://sync.test"));

        using var verificationConnection = new SQLiteConnection(databasePath);
        var rows = verificationConnection.Table<Synchronization>()
            .Where(s => s.ServerUrl == "https://sync.test")
            .ToList();
        Assert.Single(rows);
    }

    [Fact]
    public async Task LegacySingleCursor_IsResetForProtocolV3Replay()
    {
        using var tempDatabase = new TempDatabase("legacy-sync.db");
        using (var connection = new SQLiteConnection(tempDatabase.DatabasePath))
        {
            connection.Execute(
                "CREATE TABLE sync (server_url TEXT PRIMARY KEY, last_sync_time INTEGER NOT NULL)");
            connection.Execute(
                "INSERT INTO sync (server_url, last_sync_time) VALUES (?, ?)",
                "https://sync.test",
                123);
        }

        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);

        Assert.Equal(0, await database.GetLastPushTimeAsync("https://sync.test"));
        Assert.Equal(0, await database.GetLastPullTimeAsync("https://sync.test"));
    }

    [Fact]
    public async Task GetChangedAsync_ReplaysCursorSecondAndExcludesFutureRows()
    {
        using var tempDatabase = new TempDatabase("bounded-sync-window.db");
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        _ = await database.GetSessionsAsync();

        var boundaryTrack = new Track
        {
            Id = Guid.NewGuid(),
            Points = [new TrackPoint(100, 1, 1, 10)],
            Updated = 5,
            ClientUpdated = 5,
        };
        var futureTrack = new Track
        {
            Id = Guid.NewGuid(),
            Points = [new TrackPoint(101, 2, 2, 11)],
            Updated = 6,
            ClientUpdated = 6,
        };
        using (var connection = new SQLiteConnection(tempDatabase.DatabasePath))
        {
            connection.Insert(boundaryTrack);
            connection.Insert(futureTrack);
        }

        var sinceExclusive = SynchronizationProtocol.GetSinceExclusive(cursor: 5);
        var changed = await database.GetChangedAsync<Track>(sinceExclusive, upperInclusive: 5);

        Assert.Equal(4, sinceExclusive);
        Assert.Equal(boundaryTrack.Id, Assert.Single(changed).Id);
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

        var syncData = await database.GetSynchronizationDataAsync(100, 150);

        Assert.Equal(150, syncData.UpperBound);
        Assert.Single(syncData.Sessions);
        Assert.Single(syncData.Tracks);
        Assert.Equal(trackId, syncData.Tracks[0].Id);

    }

    [Fact]
    public async Task GetSynchronizationDataAsync_DoesNotLeakFutureRelatedTrackVersion()
    {
        using var tempDatabase = new TempDatabase("bounded-related-track.db");
        var sessionId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        _ = await database.GetSessionsAsync();

        using (var connection = new SQLiteConnection(tempDatabase.DatabasePath))
        {
            connection.Insert(new Track
            {
                Id = trackId,
                Points = [new TrackPoint(100, 1, 1, 10)],
                Updated = 151,
                ClientUpdated = 151,
            });
            connection.Insert(new Session(sessionId, "session", "desc", null, 100)
            {
                FullTrack = trackId,
                Updated = 150,
                ClientUpdated = 150,
            });
        }

        var syncData = await database.GetSynchronizationDataAsync(100, 150);

        Assert.Single(syncData.Sessions);
        Assert.Empty(syncData.Tracks);
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
    public async Task ApplyRemoteSynchronizationDataAsync_RetainsHeldProcessedGeneration()
    {
        using var tempDatabase = new TempDatabase("remote-sync.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var setupId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        byte[] originalPsst = [1, 2, 3, 4];

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Session(sessionId, "local", "local desc", null, 1234)
            {
                ProcessedData = originalPsst,
                Updated = 1,
                ClientUpdated = 1
            });
        }

        var remoteSession = new Session(sessionId, "remote", "remote desc", setupId, 1234)
        {
            FullTrack = trackId,
            GpsOffsetSeconds = 1.25,
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

        var beforeMerge = await database.GetSessionAsync(sessionId);

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
        Assert.Equal(beforeMerge!.FullTrack, session.FullTrack);
        Assert.Equal(beforeMerge.GpsOffsetSeconds, session.GpsOffsetSeconds);
        // The held-BLOB row retains every processed-generation field until the
        // matching BLOB arrives. Independent session metadata still syncs now.
        Assert.Equal(beforeMerge.ProcessingFingerprintJson, session.ProcessingFingerprintJson);
        Assert.Equal(beforeMerge.DurationSeconds, session.DurationSeconds);
        Assert.Equal(beforeMerge.DistanceMeters, session.DistanceMeters);
        Assert.Equal(beforeMerge.AscentMeters, session.AscentMeters);
        Assert.Equal(beforeMerge.DescentMeters, session.DescentMeters);
        Assert.Equal(99, session.Updated);
        Assert.Equal("50", session.FrontSpringRate);
        Assert.Equal("60", session.RearSpringRate);
        Assert.Null(sessionTrack);
        Assert.Equal(originalPsst, rawPsst);
        Assert.Equal(beforeMerge.ProcessedTelemetryRevision, session.ProcessedTelemetryRevision);
        Assert.Equal(beforeMerge.TrackProjectionRevision, session.TrackProjectionRevision);
        Assert.NotNull(fullTrack);
        Assert.Equal(2, fullTrack!.Points.Count);
        Assert.Equal(1, fullTrack.PointsRevision);

    }

    [Fact]
    public async Task MergeAllAsync_RetainsHeldProcessedGeneration()
    {
        using var tempDatabase = new TempDatabase("merge-session-sync.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var setupId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        byte[] originalPsst = [1, 2, 3, 4];

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Session(sessionId, "local", "local desc", null, 1234)
            {
                ProcessedData = originalPsst,
                Updated = 1,
                ClientUpdated = 1
            });
        }

        var beforeMerge = await database.GetSessionAsync(sessionId);

        await database.MergeAllAsync(new SynchronizationData
        {
            Sessions =
            [
                new Session(sessionId, "remote", "remote desc", setupId, 1234)
                {
                    FullTrack = trackId,
                    GpsOffsetSeconds = 1.25,
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
        var changedSession = Assert.Single(await database.GetChangedAsync<Session>(0, long.MaxValue));
        var sessionTrack = await database.GetSessionTrackAsync(sessionId);
        var rawPsst = await database.GetSessionRawPsstAsync(sessionId);

        Assert.NotNull(session);
        Assert.Equal("remote", session!.Name);
        Assert.Equal("remote desc", session.Description);
        Assert.Equal(setupId, session.Setup);
        Assert.Equal(1234, session.Timestamp);
        Assert.Equal(beforeMerge!.FullTrack, session.FullTrack);
        Assert.Equal(beforeMerge.GpsOffsetSeconds, session.GpsOffsetSeconds);
        Assert.Equal(beforeMerge.ProcessingFingerprintJson, session.ProcessingFingerprintJson);
        Assert.Equal(beforeMerge.DurationSeconds, session.DurationSeconds);
        Assert.Equal(beforeMerge.DistanceMeters, session.DistanceMeters);
        Assert.Equal(beforeMerge.AscentMeters, session.AscentMeters);
        Assert.Equal(beforeMerge.DescentMeters, session.DescentMeters);
        Assert.Equal("50", session.FrontSpringRate);
        Assert.Equal("60", session.RearSpringRate);
        Assert.True(session.HasProcessedData);
        Assert.True(changedSession.HasProcessedData);
        Assert.Null(sessionTrack);
        Assert.Equal(originalPsst, rawPsst);

    }

    [Theory]
    [InlineData(PushSwapScenario.CurrentInputs, true, false)]
    [InlineData(PushSwapScenario.DerivationWindowChanged, true, false)]
    [InlineData(PushSwapScenario.MetadataBeforeSource, true, true)]
    [InlineData(PushSwapScenario.StaleIncoming, false, false)]
    [InlineData(PushSwapScenario.DerivedStaleIncoming, false, false)]
    public async Task MergeAllAsync_RecordsPushSwapRequestOnlyForEligibleIncomingFingerprint(
        PushSwapScenario scenario,
        bool expectSwapRequest,
        bool expectRecordedSourceMissing)
    {
        using var tempDatabase = new TempDatabase("push-swap-eligibility.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var setupId = Guid.NewGuid();
        var bikeId = Guid.NewGuid();
        var targetTrackId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();
        var (heldFingerprint, incomingFingerprint) = await SeedPushSwapScenarioAsync(
            database,
            sessionId,
            setupId,
            bikeId,
            scenario);

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Session(sessionId, "hub", "desc", setupId, 100)
            {
                ProcessedData = [1, 2, 3],
                ProcessingFingerprintJson = heldFingerprint,
                Updated = 1,
                ClientUpdated = 1
            });
        }

        await database.MergeAllAsync(new SynchronizationData
        {
            Sessions =
            [
                new Session(sessionId, "hub", "desc", setupId, 100)
                {
                    ProcessingFingerprintJson = incomingFingerprint,
                    DurationSeconds = 80,
                    DistanceMeters = 20,
                    AscentMeters = 8,
                    DescentMeters = 3,
                    FullTrack = targetTrackId,
                    GpsOffsetSeconds = 1.25,
                    Track = [new TrackPoint(100, 1, 2, 3)],
                    Updated = 99,
                    ClientUpdated = 88
                }
            ]
        });

        using var verify = new SQLiteConnection(databasePath);
        if (expectRecordedSourceMissing)
        {
            Assert.Equal(0, verify.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM session_recording_source WHERE session_id = ?",
                sessionId));
        }

        Assert.Equal(
            expectSwapRequest ? 1 : 0,
            verify.ExecuteScalar<int>("SELECT COUNT(*) FROM session_blob_swap_request"));
        if (expectSwapRequest)
        {
            var session = await database.GetSessionAsync(sessionId);
            Assert.Equal(heldFingerprint, session!.ProcessingFingerprintJson);
            Assert.Equal(
                incomingFingerprint,
                verify.ExecuteScalar<string>(
                    "SELECT target_fingerprint FROM session_blob_swap_request WHERE session_id = ?",
                    sessionId));
            var targetGeneration = AppJson.Deserialize<SessionProcessedGeneration>(
                verify.ExecuteScalar<string>(
                    "SELECT target_generation FROM session_blob_swap_request WHERE session_id = ?",
                    sessionId));
            Assert.NotNull(targetGeneration);
            Assert.Equal(80, targetGeneration!.DurationSeconds);
            Assert.Equal(targetTrackId, targetGeneration.FullTrackId);
            Assert.Equal(1.25, targetGeneration.GpsOffsetSeconds);
            Assert.Equal(100, Assert.Single(targetGeneration.Track!).Time);
        }
    }

    [Fact]
    public async Task ApplyRemoteSynchronizationDataAsync_QueuesSwap_WhenDerivedSessionSourceExistsUnderWindowSource()
    {
        using var tempDatabase = new TempDatabase("derived-pull-swap.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var sourceSessionId = Guid.NewGuid();
        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        var source = PersistenceTestData.CreateRecordedSessionSource(sourceSessionId);
        var window = new RecordedSessionDerivationWindow(sourceSessionId, 1, null);
        await database.PutRecordedSessionSourceAsync(source);

        var localFingerprint = AppJson.Serialize(CreateFingerprint("local", source.SourceHash, window));
        var remoteFingerprint = AppJson.Serialize(CreateFingerprint("remote", source.SourceHash, window));
        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Session(sessionId, "local", "desc", null, 100)
            {
                ProcessedData = [1, 2, 3],
                ProcessingFingerprintJson = localFingerprint,
                Updated = 1,
                ClientUpdated = 1
            });
        }

        var swaps = await database.ApplyRemoteSynchronizationDataAndReturnSwapsAsync(new SynchronizationData
        {
            Sessions =
            [
                new Session(sessionId, "remote", "desc", null, 100)
                {
                    ProcessingFingerprintJson = remoteFingerprint,
                    DurationSeconds = 80,
                    DistanceMeters = 20,
                    AscentMeters = 8,
                    DescentMeters = 3,
                    FullTrack = Guid.NewGuid(),
                    GpsOffsetSeconds = 1.25,
                    Track = [new TrackPoint(100, 1, 2, 3)],
                    Updated = 99,
                    ClientUpdated = 88
                }
            ]
        });

        var swap = Assert.Single(swaps);
        Assert.Equal(sessionId, swap.SessionId);
        Assert.Equal(remoteFingerprint, swap.TargetFingerprint);
        Assert.NotNull(swap.TargetGeneration);
        Assert.Equal(80, swap.TargetGeneration!.DurationSeconds);
        Assert.Equal(20, swap.TargetGeneration.DistanceMeters);
        Assert.Equal(8, swap.TargetGeneration.AscentMeters);
        Assert.Equal(3, swap.TargetGeneration.DescentMeters);
        Assert.Equal(1.25, swap.TargetGeneration.GpsOffsetSeconds);
        Assert.Equal(100, Assert.Single(swap.TargetGeneration.Track!).Time);
    }

    [Fact]
    public async Task MergeAllAsync_PushPayloadRoundTrip_PreservesExistingPsst_WhenSyncStopsBeforeRepair()
    {
        using var tempDatabase = new TempDatabase("interrupted-sync.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        byte[] originalPsst = [9, 8, 7, 6];

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
        byte[] originalPsst = [4, 3, 2, 1];

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

    [Theory]
    [InlineData(true, 110, 100, 100, false, 150, 150, null, 100, true)]
    [InlineData(false, 210, 200, null, true, 150, 150, 150, null, true)]
    [InlineData(false, 110, 100, null, true, 150, 150, 150, 150, false)]
    public async Task MergeAllAsync_AppliesBoardTombstonePrecedence(
        bool localDeleted,
        long localUpdated,
        long localClientUpdated,
        int? localDeletedAt,
        bool incomingDeleted,
        long incomingUpdated,
        long incomingClientUpdated,
        int? incomingDeletedAt,
        int? expectedDeletedAt,
        bool expectLocalSetup)
    {
        using var tempDatabase = new TempDatabase("merge-delete.db");
        var databasePath = tempDatabase.DatabasePath;
        var boardId = Guid.NewGuid();
        var localSetupId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetAllAsync<Board>();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Board(boardId, localSetupId)
            {
                Updated = localUpdated,
                ClientUpdated = localClientUpdated,
                Deleted = localDeleted ? localDeletedAt : null
            });
        }

        await database.MergeAllAsync(new SynchronizationData
        {
            Boards =
            [
                new Board(boardId, incomingDeleted ? null : Guid.NewGuid())
                {
                    Updated = incomingUpdated,
                    ClientUpdated = incomingClientUpdated,
                    Deleted = incomingDeleted ? incomingDeletedAt : null
                }
            ]
        });

        using var verificationConnection = new SQLiteConnection(databasePath);
        var board = verificationConnection.Table<Board>().Single(b => b.Id == boardId);

        Assert.Equal((long?)expectedDeletedAt, board.Deleted);
        if (expectLocalSetup)
        {
            Assert.Equal(localSetupId, board.SetupId);
        }

    }

    private static async Task<(string HeldFingerprint, string IncomingFingerprint)> SeedPushSwapScenarioAsync(
        TestPersistenceHarness database,
        Guid sessionId,
        Guid setupId,
        Guid bikeId,
        PushSwapScenario scenario)
    {
        var setup = new Setup { Id = setupId, BikeId = bikeId, Name = "setup" };
        var bike = new Bike { Id = bikeId, Name = "bike", HeadAngle = 65 };
        await database.PutAsync(setup);
        await database.PutAsync(bike);

        var fingerprintService = new ProcessingFingerprintService();
        var sessionSnapshot = SessionSnapshot.From(new Session(sessionId, "session", "desc", setupId, 100));
        var setupSnapshot = SetupSnapshot.From(setup, null);
        var bikeSnapshot = BikeSnapshot.From(bike);

        if (scenario == PushSwapScenario.DerivationWindowChanged)
        {
            var oldSourceSessionId = Guid.NewGuid();
            var newSourceSessionId = Guid.NewGuid();
            var oldSource = PersistenceTestData.CreateRecordedSessionSource(oldSourceSessionId);
            var newSource = PersistenceTestData.CreateRecordedSessionSource(newSourceSessionId);
            await database.PutRecordedSessionSourceAsync(oldSource);
            await database.PutRecordedSessionSourceAsync(newSource);

            return (
                AppJson.Serialize(fingerprintService.CreateCurrentDatabaseInputs(
                    sessionSnapshot,
                    setupSnapshot,
                    bikeSnapshot,
                    RecordedSessionSourceSnapshot.From(oldSource),
                    new RecordedSessionDerivationWindow(oldSourceSessionId, 1, null))),
                AppJson.Serialize(fingerprintService.CreateCurrentDatabaseInputs(
                    sessionSnapshot,
                    setupSnapshot,
                    bikeSnapshot,
                    RecordedSessionSourceSnapshot.From(newSource),
                    new RecordedSessionDerivationWindow(newSourceSessionId, 1, null))));
        }

        var sourceSessionId = scenario == PushSwapScenario.DerivedStaleIncoming
            ? Guid.NewGuid()
            : sessionId;
        var source = PersistenceTestData.CreateRecordedSessionSource(sourceSessionId);
        if (scenario != PushSwapScenario.MetadataBeforeSource)
        {
            await database.PutRecordedSessionSourceAsync(source);
        }

        var derivationWindow = scenario == PushSwapScenario.DerivedStaleIncoming
            ? new RecordedSessionDerivationWindow(sourceSessionId, 1, null)
            : null;
        var canonical = fingerprintService.CreateCurrentDatabaseInputs(
            sessionSnapshot,
            setupSnapshot,
            bikeSnapshot,
            RecordedSessionSourceSnapshot.From(source),
            derivationWindow);

        return scenario is PushSwapScenario.StaleIncoming or PushSwapScenario.DerivedStaleIncoming
            ? (
                AppJson.Serialize(canonical with { DependencyHash = "hub-old-hash" }),
                AppJson.Serialize(canonical with { DependencyHash = "client-stale-hash" }))
            : (
                AppJson.Serialize(canonical with { DependencyHash = "stale-dependency-hash" }),
                AppJson.Serialize(canonical));
    }

    public enum PushSwapScenario
    {
        CurrentInputs,
        DerivationWindowChanged,
        MetadataBeforeSource,
        StaleIncoming,
        DerivedStaleIncoming
    }

    private static ProcessingFingerprint CreateFingerprint(
        string dependencyHash,
        string sourceHash,
        RecordedSessionDerivationWindow? window) =>
        new(
            SchemaVersion: 3,
            ProcessingVersion: TelemetryProcessingVersion.Current,
            SetupId: Guid.NewGuid(),
            BikeId: Guid.NewGuid(),
            TrackProjectionVersion: GpsTrackPointProjection.ProjectionVersion,
            DependencyHash: dependencyHash,
            SourceHash: sourceHash,
            DerivationWindow: window);
}
